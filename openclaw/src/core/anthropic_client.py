"""
Anthropic Claude API client with native tool_use.
Adapted from the original anthropic_client.py — now accepts API key per-request.
"""

from __future__ import annotations

import asyncio
import os
import time
from pathlib import Path
from typing import TYPE_CHECKING, Any, Optional

import anthropic

if TYPE_CHECKING:
    from core.engine import AgentTask

UNISCRAPER_MCP_URL = os.environ.get("UNISCRAPER_MCP_URL", "")

CLAUDE_RETRY_MAX_ATTEMPTS = 5
CLAUDE_RETRY_BASE_DELAY_S = 2.0
CLAUDE_RETRY_MAX_DELAY_S = 60.0
MAX_TOOL_ROUNDS = int(os.environ.get("MAX_TOOL_ROUNDS", "20"))
MAX_TOKENS = int(os.environ.get("MAX_TOKENS", "4096"))

# ── Tool definitions ──────────────────────────────────────

TOOLS_BASE = [
    {
        "name": "run_bash",
        "description": "Run a bash command in the task workspace. Returns stdout, stderr and exit code.",
        "input_schema": {
            "type": "object",
            "properties": {
                "command": {"type": "string", "description": "Bash command to execute"},
                "timeout": {"type": "integer", "description": "Timeout in seconds (default 120)", "default": 120},
            },
            "required": ["command"],
        },
    },
    {
        "name": "write_file",
        "description": "Write content to a file in the workspace.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative file path"},
                "content": {"type": "string", "description": "File content to write"},
            },
            "required": ["path", "content"],
        },
    },
    {
        "name": "read_file",
        "description": "Read a file from the workspace.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative file path to read"},
            },
            "required": ["path"],
        },
    },
    {
        "name": "list_dir",
        "description": "List files and directories in the workspace.",
        "input_schema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Relative path (default: '.')", "default": "."},
            },
            "required": [],
        },
    },
    {
        "name": "mark_output",
        "description": "Mark files as final output of this task.",
        "input_schema": {
            "type": "object",
            "properties": {
                "files": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "List of relative file paths to mark as output",
                },
            },
            "required": ["files"],
        },
    },
    {
        "name": "query_university",
        "description": (
            "Pobiera informacje ze strony uczelni (Politechnika Krakowska): plany zajęć, "
            "dokumenty, zarządzenia, aktualności. Używaj gdy student pyta o harmonogramy, "
            "linki uczelniane lub ogłoszenia."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "tool": {
                    "type": "string",
                    "enum": ["get_university_links", "get_schedule_info", "get_university_news"],
                    "description": (
                        "Narzędzie MCP do wywołania: "
                        "get_university_links — ogólne wyszukiwanie dokumentów i linków; "
                        "get_schedule_info — plany zajęć z filtrem roku akademickiego; "
                        "get_university_news — aktualne ogłoszenia."
                    ),
                },
                "query": {
                    "type": "string",
                    "description": "Zapytanie lub słowa kluczowe.",
                },
                "faculty": {
                    "type": "string",
                    "description": "Wydział, np. 'WIiT' (opcjonalnie).",
                },
                "field_of_study": {
                    "type": "string",
                    "description": "Kierunek studiów, np. 'Informatyka' (opcjonalnie).",
                },
                "academic_year": {
                    "type": "string",
                    "description": "Rok akademicki, np. '2024/2025' (opcjonalnie).",
                },
                "study_year": {
                    "type": "integer",
                    "description": "Rok studiów, np. 2 (opcjonalnie).",
                },
                "dean_group": {
                    "type": "string",
                    "description": "Grupa dziekańska, np. 'ID3' (opcjonalnie).",
                },
            },
            "required": ["tool", "query"],
        },
    },
]


def _call_uniscraper_mcp(tool_name: str, arguments: dict[str, Any]) -> str:
    """Call a tool on the UniScraper MCP server via SSE client. Runs in a fresh event loop."""
    from mcp.client.sse import sse_client
    from mcp.client.session import ClientSession

    async def _invoke() -> str:
        async with sse_client(UNISCRAPER_MCP_URL) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                result = await session.call_tool(tool_name, arguments)
                if result.content:
                    item = result.content[0]
                    return item.text if hasattr(item, "text") else str(item)
                return "Brak wyników."

    try:
        return asyncio.run(_invoke())
    except Exception as e:
        return f"Błąd połączenia z UniScraper MCP ({UNISCRAPER_MCP_URL}): {e}"


def _get_client(api_key: Optional[str] = None) -> anthropic.Anthropic:
    """Create Anthropic client with provided or env-based API key."""
    key = api_key or os.environ.get("ANTHROPIC_API_KEY", "")
    if not key:
        raise ValueError("No Anthropic API key provided")
    return anthropic.Anthropic(api_key=key)


def call_claude_with_retry(
    prompt: str,
    workspace: Path,
    model: str,
    max_tool_rounds: int = MAX_TOOL_ROUNDS,
    api_key: Optional[str] = None,
    enable_web_search: bool = False,
) -> tuple[str | None, list[Path]]:
    """
    Call Claude with tool_use loop. Returns (direct_response, marked_output_paths).
    """
    from core.prompts import SYSTEM_PROMPT

    client = _get_client(api_key)
    tools = list(TOOLS_BASE)

    if enable_web_search:
        tools.append({
            "type": "web_search_20250305",
            "name": "web_search",
        })

    working_messages = [{"role": "user", "content": prompt}]
    marked_outputs: list[Path] = []
    tool_rounds = 0

    while tool_rounds < max_tool_rounds:
        for attempt in range(CLAUDE_RETRY_MAX_ATTEMPTS):
            try:
                response = client.messages.create(
                    model=model,
                    max_tokens=MAX_TOKENS,
                    system=SYSTEM_PROMPT,
                    messages=working_messages,
                    tools=tools,
                )
                break
            except (anthropic.RateLimitError, anthropic.APIConnectionError) as e:
                delay = min(CLAUDE_RETRY_BASE_DELAY_S * (2 ** attempt), CLAUDE_RETRY_MAX_DELAY_S)
                print(f"[Claude] Retry {attempt + 1}/{CLAUDE_RETRY_MAX_ATTEMPTS} after {delay}s: {e}", flush=True)
                time.sleep(delay)
        else:
            raise RuntimeError("Claude API unavailable after retries")

        # Extract text response
        text_parts = [b.text for b in response.content if hasattr(b, "text")]

        if response.stop_reason == "end_turn":
            return "\n".join(text_parts) if text_parts else None, marked_outputs

        if response.stop_reason == "tool_use":
            # Append assistant turn
            working_messages.append({"role": "assistant", "content": response.content})

            # Execute tools
            tool_results = []
            for block in response.content:
                if not hasattr(block, "type") or block.type != "tool_use":
                    continue

                result = _execute_tool(block.name, block.input, workspace, marked_outputs)
                tool_results.append({
                    "type": "tool_result",
                    "tool_use_id": block.id,
                    "content": result,
                })

            working_messages.append({"role": "user", "content": tool_results})
            tool_rounds += 1
        else:
            return "\n".join(text_parts) if text_parts else None, marked_outputs

    return "Max tool rounds reached.", marked_outputs


def _execute_tool(
    name: str,
    inputs: dict[str, Any],
    workspace: Path,
    marked_outputs: list[Path],
) -> str:
    """Execute a single tool call. marked_outputs is mutated in place."""
    from utils.shell_executor import run_command

    try:
        if name == "run_bash":
            command = inputs["command"]
            timeout = inputs.get("timeout", 120)
            print(f"[tool] run_bash: {command[:80]}", flush=True)
            returncode, stdout, stderr = run_command(
                ["bash", "-c", command], cwd=workspace, timeout=timeout
            )
            parts = [f"exit_code: {returncode}"]
            if stdout:
                parts.append(f"stdout:\n{stdout[:3000]}")
            if stderr:
                parts.append(f"stderr:\n{stderr[:1000]}")
            return "\n".join(parts)

        elif name == "write_file":
            path = inputs["path"]
            content = inputs["content"]
            file_path = workspace / path
            file_path.parent.mkdir(parents=True, exist_ok=True)
            file_path.write_text(content, encoding="utf-8")
            print(f"[tool] write_file: {path} ({len(content.splitlines())} lines)", flush=True)
            return f"OK: wrote {len(content)} chars to {path}"

        elif name == "read_file":
            path = inputs["path"]
            file_path = workspace / path
            if not file_path.exists():
                return f"ERROR: file not found: {path}"
            content = file_path.read_text(encoding="utf-8", errors="replace")
            print(f"[tool] read_file: {path}", flush=True)
            return content[:5000]

        elif name == "list_dir":
            path = inputs.get("path", ".")
            target = workspace / path
            if not target.exists():
                return f"ERROR: directory not found: {path}"
            entries = sorted(target.iterdir())
            lines = []
            for e in entries[:100]:
                prefix = "d " if e.is_dir() else "f "
                lines.append(f"{prefix}{e.name}")
            return "\n".join(lines) if lines else "(empty directory)"

        elif name == "mark_output":
            files = inputs.get("files", [])
            resolved = []
            for f in files:
                p = workspace / f
                if p.exists():
                    resolved.append(p)
                    print(f"[tool] mark_output: {f}", flush=True)
                else:
                    print(f"[tool] mark_output: SKIP (not found): {f}", flush=True)
            marked_outputs.extend(resolved)
            return f"Marked {len(resolved)} file(s) as output"

        elif name == "query_university":
            if not UNISCRAPER_MCP_URL:
                return "ERROR: UNISCRAPER_MCP_URL not configured — UniScraper MCP unavailable."
            mcp_tool = inputs["tool"]
            mcp_args = {k: v for k, v in inputs.items() if k != "tool" and v is not None}
            print(f"[tool] query_university → MCP:{mcp_tool} args={list(mcp_args.keys())}", flush=True)
            return _call_uniscraper_mcp(mcp_tool, mcp_args)

        else:
            return f"ERROR: unknown tool: {name}"

    except Exception as e:
        return f"ERROR: {name} failed: {e}"
