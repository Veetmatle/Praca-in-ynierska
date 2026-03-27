"""
Workspace lifecycle, output file selection, and cleanup.
Adapted from the original OpenClaw file_manager.py.
"""

import os
import shutil
import threading
import time
from pathlib import Path
from typing import Callable, Optional

WORKSPACE_DIR = Path(os.environ.get("WORKSPACE_DIR", "/workspace"))

FAILURE_INDICATORS = {
    ".pyc", "__pycache__", ".git", ".mypy_cache",
    ".pytest_cache", "node_modules", ".env",
}

OUTPUT_EXTENSIONS = {
    ".pdf", ".png", ".jpg", ".jpeg", ".svg", ".gif",
    ".csv", ".xlsx", ".xls", ".json", ".xml",
    ".html", ".md", ".txt", ".docx", ".pptx",
    ".py", ".cs", ".js", ".ts", ".zip", ".tar",
}


def create_workspace(task_id: str) -> Path:
    """Create an isolated workspace directory for a task."""
    ws = WORKSPACE_DIR / task_id
    ws.mkdir(parents=True, exist_ok=True)
    return ws


def validate_output_files(paths: list[Path], workspace: Path) -> list[Path]:
    """Validate that marked output files exist and are within the workspace."""
    valid = []
    for p in paths:
        resolved = p if p.is_absolute() else workspace / p
        if resolved.exists() and resolved.is_file():
            try:
                resolved.relative_to(workspace)
                valid.append(resolved)
            except ValueError:
                print(f"[file_manager] Skipping file outside workspace: {p}", flush=True)
    return valid


def select_output_files(workspace: Path, max_files: int = 10) -> list[Path]:
    """Heuristic: select the most likely output files from workspace."""
    candidates = []
    for f in workspace.rglob("*"):
        if not f.is_file():
            continue
        if any(indicator in str(f) for indicator in FAILURE_INDICATORS):
            continue
        if f.suffix.lower() in OUTPUT_EXTENSIONS:
            candidates.append(f)

    candidates.sort(key=lambda f: f.stat().st_mtime, reverse=True)
    return candidates[:max_files]


def cleanup_workspace(task_id: str) -> None:
    """Remove a task's workspace directory."""
    ws = WORKSPACE_DIR / task_id
    if ws.exists():
        shutil.rmtree(ws, ignore_errors=True)
        print(f"[cleanup] Removed workspace: {task_id}", flush=True)


def cleanup_old_workspaces(
    max_age_hours: int = 24,
    is_task_active_fn: Optional[Callable[[str], bool]] = None,
) -> None:
    """Remove workspaces older than max_age_hours, skipping active tasks."""
    if not WORKSPACE_DIR.exists():
        return

    cutoff = time.time() - (max_age_hours * 3600)
    for entry in WORKSPACE_DIR.iterdir():
        if not entry.is_dir():
            continue
        if is_task_active_fn and is_task_active_fn(entry.name):
            continue
        try:
            mtime = entry.stat().st_mtime
            if mtime < cutoff:
                shutil.rmtree(entry, ignore_errors=True)
                print(f"[cleanup] Removed old workspace: {entry.name}", flush=True)
        except OSError:
            pass


def start_cleanup_scheduler(
    interval_hours: int = 1,
    is_task_active_fn: Optional[Callable[[str], bool]] = None,
) -> None:
    """Start a background thread for periodic workspace cleanup."""
    def _loop():
        while True:
            time.sleep(interval_hours * 3600)
            try:
                cleanup_old_workspaces(is_task_active_fn=is_task_active_fn)
            except Exception as e:
                print(f"[cleanup] Error: {e}", flush=True)

    thread = threading.Thread(target=_loop, daemon=True)
    thread.start()
