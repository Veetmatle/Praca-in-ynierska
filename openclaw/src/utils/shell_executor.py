"""
Subprocess wrapper with timeout — adapted from original shell_executor.py.
"""

import subprocess
from pathlib import Path


def run_command(
    args: list[str],
    cwd: Path | None = None,
    timeout: int = 120,
) -> tuple[int, str, str]:
    """
    Run a command, capture output, enforce timeout.
    Returns (returncode, stdout, stderr).
    """
    try:
        result = subprocess.run(
            args,
            cwd=str(cwd) if cwd else None,
            capture_output=True,
            text=True,
            timeout=timeout,
        )
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return -1, "", f"Command timed out after {timeout}s"
    except FileNotFoundError as e:
        return -1, "", f"Command not found: {e}"
    except Exception as e:
        return -1, "", f"Execution error: {e}"
