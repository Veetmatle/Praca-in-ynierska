"""
UniScraper — FastAPI microservice for scraping university websites.
Replaces the original PolitechnikaService.cs with a parameterized, extensible Python service.
Supports multiple universities via a registry pattern.
"""

import os
from datetime import datetime

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from scrapers.pk_scraper import PolitechnikaKrakowskaScraper
from scrapers.base import ScraperRegistry

app = FastAPI(
    title="UniScraper API",
    version="1.0.0",
    description="Parameterized university website scraper for student assistants.",
)

CACHE_TTL_MINUTES = int(os.environ.get("CACHE_TTL_MINUTES", "60"))

# ── Register scrapers ────────────────────────────────────
registry = ScraperRegistry()
registry.register("Politechnika Krakowska", PolitechnikaKrakowskaScraper(cache_ttl_minutes=CACHE_TTL_MINUTES))


# ── Models ────────────────────────────────────────────────

class HealthResponse(BaseModel):
    status: str = "healthy"
    timestamp: str
    registered_universities: list[str]


class ScrapeRequest(BaseModel):
    query: str
    university: str = "Politechnika Krakowska"
    faculty: str | None = None
    field_of_study: str | None = None
    academic_year: str | None = None
    study_year: int | None = None
    dean_group: str | None = None


class ScrapeResponse(BaseModel):
    context: str
    sources: list[dict] = Field(default_factory=list)
    university: str
    cached: bool = False


class LinksResponse(BaseModel):
    university: str
    links: list[dict]
    total: int


# ── Endpoints ─────────────────────────────────────────────

@app.get("/health")
async def health():
    return HealthResponse(
        timestamp=datetime.utcnow().isoformat(),
        registered_universities=registry.list_universities(),
    )


@app.post("/api/scrape")
async def scrape(req: ScrapeRequest) -> ScrapeResponse:
    """
    Scrape university website with parameterized filters.
    Returns relevant context text for LLM consumption.
    """
    scraper = registry.get(req.university)
    if scraper is None:
        available = registry.list_universities()
        raise HTTPException(
            status_code=400,
            detail=f"Uczelnia '{req.university}' nie jest obsługiwana. Dostępne: {available}",
        )

    try:
        result = await scraper.scrape(
            query=req.query,
            faculty=req.faculty,
            field_of_study=req.field_of_study,
            academic_year=req.academic_year,
            study_year=req.study_year,
            dean_group=req.dean_group,
        )

        return ScrapeResponse(
            context=result["context"],
            sources=result.get("sources", []),
            university=req.university,
            cached=result.get("cached", False),
        )

    except Exception as e:
        raise HTTPException(
            status_code=500,
            detail=f"Błąd scrapowania: {str(e)[:300]}",
        )


@app.get("/api/links/{university}")
async def get_links(university: str) -> LinksResponse:
    """Get all scraped links for a university (cached)."""
    scraper = registry.get(university)
    if scraper is None:
        raise HTTPException(status_code=404, detail=f"Uczelnia '{university}' nie znaleziona")

    links = await scraper.get_all_links()
    return LinksResponse(
        university=university,
        links=links,
        total=len(links),
    )
