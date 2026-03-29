"""
Agent task engine — manages task lifecycle, workspace, and execution.
Adapted from the original OpenClaw engine.py with per-request API key support.
"""

import os
import re
import threading
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Optional

from core.anthropic_client import call_claude_with_retry
from core.prompts import SYSTEM_PROMPT
from utils.file_manager import (
    create_workspace,
    validate_output_files,
    select_output_files,
)

ANTHROPIC_MODEL = os.environ.get("ANTHROPIC_MODEL", "claude-sonnet-4-20250514")
MAX_ITERATIONS = int(os.environ.get("MAX_ITERATIONS", "15"))
_SESSION_TIMEOUT_MINUTES = int(os.environ.get("AGENT_SESSION_TIMEOUT_MINUTES", "10"))
TIMEOUT_SECONDS = _SESSION_TIMEOUT_MINUTES * 60
MAX_CONCURRENT_TASKS = int(os.environ.get("MAX_CONCURRENT_TASKS", "2"))

_WEB_SEARCH_HINTS = re.compile(
    r"\b(znajdź|wyszukaj|sprawdź|aktualne?|obecne?|dzisiaj|teraz|latest|current|search|find|today|news|cena|price|kurs)\b",
    re.IGNORECASE,
)


class TaskStatus(str, Enum):
    QUEUED = "queued"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


@dataclass
class AgentTask:
    task_id: str
    prompt: str
    document_content: Optional[str] = None
    model: str = ANTHROPIC_MODEL
    max_iterations: int = MAX_ITERATIONS
    timeout_seconds: int = TIMEOUT_SECONDS
    anthropic_api_key: Optional[str] = None
    status: TaskStatus = TaskStatus.QUEUED
    message: Optional[str] = None
    error: Optional[str] = None
    output_files: list = field(default_factory=list)
    direct_response: Optional[str] = None
    created_at: datetime = field(default_factory=datetime.utcnow)
    started_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None
    cancelled: bool = False


# ── In-memory task store ──────────────────────────────────

_tasks: dict[str, AgentTask] = {}
_task_lock = threading.Lock()
_task_semaphore = threading.Semaphore(MAX_CONCURRENT_TASKS)


def get_task(task_id: str) -> Optional[AgentTask]:
    with _task_lock:
        return _tasks.get(task_id)


def update_task(task: AgentTask) -> None:
    with _task_lock:
        _tasks[task.task_id] = task


def is_task_active(task_id: str) -> bool:
    task = get_task(task_id)
    return task is not None and task.status in (TaskStatus.RUNNING, TaskStatus.QUEUED)


def _needs_web_search(prompt: str) -> bool:
    return bool(_WEB_SEARCH_HINTS.search(prompt))


# ── Agent execution ──────────────────────────────────────

def execute_agent_task(task: AgentTask) -> None:
    """
    Agent based on native tool_use.
    Model uses tools (write_file, run_bash, read_file, list_dir, mark_output,
    optionally web_search).
    
    Output file priority:
    1. Files explicitly marked via mark_output tool
    2. Files selected by validate_output_files + select_output_files heuristic
    """
    workspace = create_workspace(task.task_id)
    task.status = TaskStatus.RUNNING
    task.started_at = datetime.utcnow()
    update_task(task)

    try:
        prompt = task.prompt
        if task.document_content:
            prompt = f"{prompt}\n\n--- ATTACHED DOCUMENT ---\n{task.document_content}"

        enable_web_search = _needs_web_search(prompt)

        direct_response, marked_outputs = call_claude_with_retry(
            prompt=prompt,
            workspace=workspace,
            model=task.model,
            max_tool_rounds=task.max_iterations,
            api_key=task.anthropic_api_key,
            enable_web_search=enable_web_search,
        )

        task.direct_response = direct_response

        if marked_outputs:
            valid = validate_output_files(marked_outputs, workspace)
            task.output_files = [
                {"name": f.name, "path": str(f), "size": f.stat().st_size}
                for f in valid
            ]
        else:
            selected = select_output_files(workspace)
            task.output_files = [
                {"name": f.name, "path": str(f), "size": f.stat().st_size}
                for f in selected
            ]

        task.status = TaskStatus.COMPLETED
        task.message = f"Completed with {len(task.output_files)} output file(s)"

    except Exception as e:
        task.status = TaskStatus.FAILED
        task.error = str(e)
        print(f"[Agent] Task {task.task_id} failed: {e}", flush=True)

    finally:
        task.completed_at = datetime.utcnow()
        update_task(task)


def run_task_async(task: AgentTask) -> None:
    """Run task in a background thread with semaphore-based concurrency control."""
    def _run():
        acquired = _task_semaphore.acquire(timeout=task.timeout_seconds)
        if not acquired:
            task.status = TaskStatus.FAILED
            task.error = "Queue full — max concurrent tasks reached"
            task.completed_at = datetime.utcnow()
            update_task(task)
            return
        try:
            execute_agent_task(task)
        finally:
            _task_semaphore.release()

    thread = threading.Thread(target=_run, daemon=True)
    thread.start()
