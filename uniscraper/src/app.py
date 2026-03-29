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


# ── REST endpoints (for C# backend fast-path RAG) ─────────
async def handle_fetch_file(request: Request) -> JSONResponse:
    """Download a file from URL and return as base64."""
    import base64
    import httpx as httpx_client

    try:
        body = await request.json()
    except Exception:
        return JSONResponse({"error": "Invalid JSON"}, status_code=400)

    url = body.get("url")
    if not url:
        return JSONResponse({"error": "Field 'url' is required"}, status_code=400)

    max_size = 15 * 1024 * 1024  # 15 MB
    try:
        async with httpx_client.AsyncClient(timeout=30, follow_redirects=True) as client:
            resp = await client.get(url, headers={
                "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
            })
            resp.raise_for_status()

            if len(resp.content) > max_size:
                return JSONResponse({"error": f"File too large ({len(resp.content)} bytes)"}, status_code=413)

            content_type = resp.headers.get("content-type", "application/octet-stream")
            filename = url.split("/")[-1].split("?")[0] or "document"

            return JSONResponse({
                "fileName": filename,
                "mimeType": content_type.split(";")[0].strip(),
                "base64Data": base64.b64encode(resp.content).decode(),
                "sizeBytes": len(resp.content),
            })
    except httpx_client.HTTPError as e:
        return JSONResponse({"error": f"Download failed: {str(e)[:200]}"}, status_code=502)
    except Exception as e:
        return JSONResponse({"error": f"Error: {str(e)[:200]}"}, status_code=500)


async def handle_scrape(request: Request) -> JSONResponse:
    try:
        body = await request.json()
    except Exception:
        return JSONResponse({"error": "Invalid JSON body"}, status_code=400)

    university = body.get("university", "Politechnika Krakowska")
    scraper = registry.get(university)
    if scraper is None:
        return JSONResponse(
            {"error": f"University '{university}' not supported. Available: {registry.list_universities()}"},
            status_code=404,
        )

    query = body.get("query", "")
    if not query:
        return JSONResponse({"error": "Field 'query' is required"}, status_code=400)

    try:
        result = await scraper.scrape(
            query=query,
            faculty=body.get("faculty"),
            field_of_study=body.get("field_of_study"),
            academic_year=body.get("academic_year"),
            study_year=body.get("study_year"),
            dean_group=body.get("dean_group"),
        )
    except Exception as e:
        return JSONResponse({"error": f"Scraping failed: {str(e)[:300]}"}, status_code=500)

    return JSONResponse({
        "context": result.get("context", ""),
        "sources": result.get("sources", []),
        "best_match_files": result.get("best_match_files", []),
        "university": university,
        "cached": result.get("cached", False),
    })


async def handle_links(request: Request) -> JSONResponse:
    university = request.path_params.get("university", "Politechnika Krakowska")
    scraper = registry.get(university)
    if scraper is None:
        return JSONResponse(
            {"error": f"University '{university}' not supported. Available: {registry.list_universities()}"},
            status_code=404,
        )

    query = request.query_params.get("query", "")
    try:
        result = await scraper.scrape(
            query=query or "linki dokumenty",
            faculty=request.query_params.get("faculty"),
            field_of_study=request.query_params.get("field_of_study"),
        )
    except Exception as e:
        return JSONResponse({"error": f"Scraping failed: {str(e)[:300]}"}, status_code=500)

    return JSONResponse({
        "sources": result.get("sources", []),
        "university": university,
        "cached": result.get("cached", False),
    })


# ── Starlette app combining /health + REST + MCP SSE ──────
app = Starlette(
    routes=[
        Route("/health", health_endpoint),
        Route("/api/scrape", handle_scrape, methods=["POST"]),
        Route("/api/fetch-file", handle_fetch_file, methods=["POST"]),
        Route("/api/links/{university}", handle_links),
        Route("/sse", handle_sse),
        Mount("/messages/", app=sse.handle_post_message),
    ]
)

if __name__ == "__main__":
    port = int(os.environ.get("PORT", "8001"))
    uvicorn.run(app, host="0.0.0.0", port=port, log_level="info")
