"""
UniScraper — MCP server for scraping university websites.
Exposes tools via Model Context Protocol (MCP) over SSE transport.
Agents (OpenClaw) connect via /sse; /health remains plain HTTP for Docker healthchecks.
"""

import os
from datetime import datetime
from typing import Any

import uvicorn
from mcp.server import Server
from mcp.server.sse import SseServerTransport
from mcp import types
from starlette.applications import Starlette
from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.routing import Mount, Route

from scrapers.pk_scraper import PolitechnikaKrakowskaScraper
from scrapers.base import ScraperRegistry

CACHE_TTL_MINUTES = int(os.environ.get("CACHE_TTL_MINUTES", "60"))

# ── Registry ──────────────────────────────────────────────
registry = ScraperRegistry()
registry.register("Politechnika Krakowska", PolitechnikaKrakowskaScraper(cache_ttl_minutes=CACHE_TTL_MINUTES))

# ── MCP Server ────────────────────────────────────────────
server = Server("uniscraper")


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return [
        types.Tool(
            name="get_university_links",
            description=(
                "Pobiera i filtruje linki ze strony uczelni (dokumenty, zarządzenia, harmonogramy). "
                "Zwraca przefiltrowaną listę zasobów pasujących do parametrów studenta i zapytania."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Zapytanie studenta — słowa kluczowe do przeszukania strony uczelni.",
                    },
                    "university": {
                        "type": "string",
                        "description": "Nazwa uczelni (domyślnie: 'Politechnika Krakowska').",
                        "default": "Politechnika Krakowska",
                    },
                    "faculty": {
                        "type": "string",
                        "description": "Wydział studenta, np. 'WIiT' (opcjonalnie).",
                    },
                    "field_of_study": {
                        "type": "string",
                        "description": "Kierunek studiów, np. 'Informatyka' (opcjonalnie).",
                    },
                    "study_year": {
                        "type": "integer",
                        "description": "Rok studiów, np. 1, 2, 3 (opcjonalnie).",
                    },
                    "dean_group": {
                        "type": "string",
                        "description": "Grupa dziekańska, np. 'ID3' (opcjonalnie).",
                    },
                },
                "required": ["query"],
            },
        ),
        types.Tool(
            name="get_schedule_info",
            description=(
                "Dedykowane wyszukiwanie planów zajęć na stronie uczelni. "
                "Zwraca linki do planów zajęć przefiltrowane według roku akademickiego i wydziału."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Zapytanie o plan zajęć, np. 'plan zajęć informatyka rok 2'.",
                    },
                    "university": {
                        "type": "string",
                        "description": "Nazwa uczelni (domyślnie: 'Politechnika Krakowska').",
                        "default": "Politechnika Krakowska",
                    },
                    "faculty": {
                        "type": "string",
                        "description": "Wydział, np. 'WIiT' (opcjonalnie).",
                    },
                    "academic_year": {
                        "type": "string",
                        "description": "Rok akademicki, np. '2024/2025' (opcjonalnie).",
                    },
                },
                "required": ["query"],
            },
        ),
        types.Tool(
            name="get_university_news",
            description=(
                "Pobiera aktualne ogłoszenia i komunikaty ze strony uczelni. "
                "Zwraca listę najnowszych wiadomości i dokumentów z sekcji aktualności."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "university": {
                        "type": "string",
                        "description": "Nazwa uczelni (domyślnie: 'Politechnika Krakowska').",
                        "default": "Politechnika Krakowska",
                    },
                },
                "required": [],
            },
        ),
    ]


@server.call_tool()
async def call_tool(name: str, arguments: dict[str, Any] | None) -> list[types.TextContent]:
    args = arguments or {}
    university = args.get("university", "Politechnika Krakowska")

    scraper = registry.get(university)
    if scraper is None:
        available = registry.list_universities()
        return [types.TextContent(
            type="text",
            text=f"Uczelnia '{university}' nie jest obsługiwana. Dostępne: {available}",
        )]

    try:
        if name == "get_university_links":
            result = await scraper.scrape(
                query=args["query"],
                faculty=args.get("faculty"),
                field_of_study=args.get("field_of_study"),
                study_year=args.get("study_year"),
                dean_group=args.get("dean_group"),
            )

        elif name == "get_schedule_info":
            result = await scraper.scrape(
                query=f"plan zajęć {args['query']}",
                faculty=args.get("faculty"),
                academic_year=args.get("academic_year"),
            )

        elif name == "get_university_news":
            result = await scraper.scrape(
                query="aktualności ogłoszenia komunikaty",
            )

        else:
            return [types.TextContent(type="text", text=f"Nieznane narzędzie: {name}")]

        return [types.TextContent(type="text", text=result["context"])]

    except Exception as e:
        return [types.TextContent(type="text", text=f"Błąd scrapowania: {str(e)[:300]}")]


# ── SSE Transport ─────────────────────────────────────────
sse = SseServerTransport("/messages/")


async def handle_sse(request: Request) -> None:
    async with sse.connect_sse(
        request.scope, request.receive, request._send
    ) as streams:
        await server.run(
            streams[0],
            streams[1],
            server.create_initialization_options(),
        )


# ── Health endpoint (plain HTTP for Docker healthcheck) ───
async def health_endpoint(request: Request) -> JSONResponse:
    return JSONResponse({
        "status": "healthy",
        "timestamp": datetime.utcnow().isoformat(),
        "registered_universities": registry.list_universities(),
    })


# ── Starlette app combining /health + MCP SSE ─────────────
app = Starlette(
    routes=[
        Route("/health", health_endpoint),
        Route("/sse", handle_sse),
        Mount("/messages/", app=sse.handle_post_message),
    ]
)

if __name__ == "__main__":
    port = int(os.environ.get("PORT", "8001"))
    uvicorn.run(app, host="0.0.0.0", port=port, log_level="info")
