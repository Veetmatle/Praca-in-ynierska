"""
OpenClaw Agent Server — FastAPI HTTP API for task execution with Claude (Anthropic).
Adapted from the original Flask-based OpenClaw agent.
Now accepts per-request API keys (passed from C# backend after AES-GCM decryption).
"""

import os
import uuid
from datetime import datetime

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from core.engine import (
    AgentTask,
    TaskStatus,
    ANTHROPIC_MODEL,
    MAX_ITERATIONS,
    TIMEOUT_SECONDS,
    get_task,
    update_task,
    run_task_async,
    is_task_active,
)
from core.chat_handler import handle_chat_stream
from utils.file_manager import (
    WORKSPACE_DIR,
    cleanup_old_workspaces,
    start_cleanup_scheduler,
)

app = FastAPI(
    title="OpenClaw Agent API",
    version="2.0.0",
    description="AI Agent with Claude (Anthropic) — tool_use based task execution and chat streaming.",
)

WORKSPACE_DIR.mkdir(parents=True, exist_ok=True)
cleanup_old_workspaces(is_task_active_fn=is_task_active)
start_cleanup_scheduler(is_task_active_fn=is_task_active)

MAX_INLINE_FILE_BYTES = int(os.environ.get("MAX_INLINE_FILE_BYTES", str(10 * 1024 * 1024)))


# ── Models ────────────────────────────────────────────────

class HealthResponse(BaseModel):
    status: str = "healthy"
    timestamp: str
    model: str


class TaskSubmitRequest(BaseModel):
    prompt: str
    task_id: str | None = None
    document_content: str | None = None
    max_iterations: int = MAX_ITERATIONS
    timeout_seconds: int = TIMEOUT_SECONDS
    anthropic_api_key: str | None = None
    model: str | None = None


class ChatMessage(BaseModel):
    role: str
    content: str


class ChatRequest(BaseModel):
    prompt: str
    history: list[ChatMessage] = Field(default_factory=list)
    anthropic_api_key: str
    model: str = ANTHROPIC_MODEL
    stream: bool = True


# ── Endpoints ─────────────────────────────────────────────

@app.get("/health")
async def health():
    return HealthResponse(
        timestamp=datetime.utcnow().isoformat(),
        model=ANTHROPIC_MODEL,
    )


@app.post("/tasks")
async def submit_task(req: TaskSubmitRequest):
    """Submit a long-running agent task (code execution, file generation)."""
    task_id = req.task_id or str(uuid.uuid4())[:8]

    task = AgentTask(
        task_id=task_id,
        prompt=req.prompt,
        document_content=req.document_content,
        model=req.model or ANTHROPIC_MODEL,
        max_iterations=req.max_iterations,
        timeout_seconds=req.timeout_seconds,
        anthropic_api_key=req.anthropic_api_key,
    )

    update_task(task)
    run_task_async(task)

    print(f"[Agent] Task {task_id} submitted with model: {task.model}", flush=True)

    return {
        "TaskId": task.task_id,
        "Status": task.status.value,
        "CreatedAt": task.created_at.isoformat(),
    }


@app.get("/tasks/{task_id}")
async def get_task_status(task_id: str):
    """Poll task status."""
    task = get_task(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail=f"Task {task_id} not found")

    return {
        "TaskId": task.task_id,
        "Status": task.status.value,
        "Message": task.message,
        "Error": task.error,
        "DirectResponse": task.direct_response,
        "OutputFiles": [
            {"FileName": f["name"], "SizeBytes": f.get("size", 0)}
            for f in task.output_files
        ],
        "CreatedAt": task.created_at.isoformat(),
        "StartedAt": task.started_at.isoformat() if task.started_at else None,
        "CompletedAt": task.completed_at.isoformat() if task.completed_at else None,
    }


@app.get("/tasks/{task_id}/files")
async def get_task_files(task_id: str):
    """Download output files as base64 JSON."""
    import base64

    task = get_task(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail=f"Task {task_id} not found")

    if task.status not in (TaskStatus.COMPLETED, TaskStatus.FAILED):
        raise HTTPException(status_code=400, detail="Task not yet finished")

    files_out = []
    for f_info in task.output_files:
        path = f_info.get("path")
        if not path or not os.path.exists(path):
            continue

        size = os.path.getsize(path)
        if size > MAX_INLINE_FILE_BYTES:
            files_out.append({
                "FileName": f_info["name"],
                "ContentBase64": "",
                "SizeBytes": size,
                "TooLarge": True,
            })
            continue

        with open(path, "rb") as fh:
            content = base64.b64encode(fh.read()).decode()

        files_out.append({
            "FileName": f_info["name"],
            "ContentBase64": content,
            "SizeBytes": size,
            "TooLarge": False,
        })

    return {"TaskId": task_id, "Files": files_out}


@app.delete("/tasks/{task_id}")
async def cancel_task(task_id: str):
    """Cancel a running task."""
    task = get_task(task_id)
    if task is None:
        raise HTTPException(status_code=404, detail=f"Task {task_id} not found")

    task.cancelled = True
    task.status = TaskStatus.CANCELLED
    task.completed_at = datetime.utcnow()
    update_task(task)

    return {"TaskId": task_id, "Status": "cancelled"}


@app.post("/api/chat")
async def chat_stream(req: ChatRequest):
    """
    Streaming chat endpoint for conversational AI.
    Returns SSE stream of text chunks.
    API key is accepted per-request from the C# backend.
    """
    from fastapi.responses import StreamingResponse

    if not req.anthropic_api_key:
        raise HTTPException(status_code=400, detail="anthropic_api_key is required")

    return StreamingResponse(
        handle_chat_stream(
            api_key=req.anthropic_api_key,
            model=req.model,
            prompt=req.prompt,
            history=[(m.role, m.content) for m in req.history],
        ),
        media_type="text/event-stream",
    )
