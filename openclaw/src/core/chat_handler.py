"""
Chat handler for streaming conversational AI responses.
Uses Claude API with streaming for real-time text delivery.
"""

import json
from typing import AsyncGenerator

import anthropic

from core.prompts import CHAT_SYSTEM_PROMPT


async def handle_chat_stream(
    api_key: str,
    model: str,
    prompt: str,
    history: list[tuple[str, str]],
) -> AsyncGenerator[str, None]:
    """
    Stream chat response as SSE events.
    Yields 'data: {"text": "..."}\\n\\n' formatted chunks.
    """
    client = anthropic.Anthropic(api_key=api_key)

    messages = []
    for role, content in history:
        mapped_role = "user" if role.lower() == "user" else "assistant"
        messages.append({"role": mapped_role, "content": content})

    messages.append({"role": "user", "content": prompt})

    try:
        with client.messages.stream(
            model=model,
            max_tokens=4096,
            system=CHAT_SYSTEM_PROMPT,
            messages=messages,
        ) as stream:
            for text in stream.text_stream:
                chunk = json.dumps({"text": text})
                yield f"data: {chunk}\n\n"

        yield "data: [DONE]\n\n"

    except anthropic.RateLimitError:
        error = json.dumps({"text": "[Błąd: Limit zapytań API został przekroczony. Spróbuj za chwilę.]"})
        yield f"data: {error}\n\n"
        yield "data: [DONE]\n\n"

    except anthropic.AuthenticationError:
        error = json.dumps({"text": "[Błąd: Nieprawidłowy klucz API Anthropic. Sprawdź konfigurację.]"})
        yield f"data: {error}\n\n"
        yield "data: [DONE]\n\n"

    except Exception as e:
        error = json.dumps({"text": f"[Błąd: {str(e)[:200]}]"})
        yield f"data: {error}\n\n"
        yield "data: [DONE]\n\n"
