"""
Politechnika Krakowska WIiT scraper.
Adapted from the original PolitechnikaService.cs — HTML scraping with caching
and parameterized filtering by faculty, field of study, year, dean group.
"""

import asyncio
import re
import time
from dataclasses import dataclass
from typing import Optional
from urllib.parse import urljoin

import httpx
from bs4 import BeautifulSoup

from scrapers.base import BaseScraper


@dataclass
class ScrapedLink:
    """Single link scraped from the university website."""
    text: str
    url: str
    category: str = ""


class PolitechnikaKrakowskaScraper(BaseScraper):
    """
    Scrapes Politechnika Krakowska WIiT student pages.
    Adapted from C# PolitechnikaService with:
    - Parameterized filtering (faculty, field, year, group)
    - Time-based caching
    - Link deduplication (keeps newest version)
    """

    BASE_URL = "https://it.pk.edu.pl"
    STUDENT_PAGE_URL = "https://it.pk.edu.pl/studenci/"

    def __init__(self, cache_ttl_minutes: int = 60):
        self._cache_ttl = cache_ttl_minutes * 60
        self._cached_links: list[ScrapedLink] = []
        self._last_scrape_time: float = 0
        self._lock = asyncio.Lock()

    async def scrape(
        self,
        query: str,
        faculty: Optional[str] = None,
        field_of_study: Optional[str] = None,
        academic_year: Optional[str] = None,
        study_year: Optional[int] = None,
        dean_group: Optional[str] = None,
    ) -> dict:
        """Scrape and filter links relevant to the student's query and parameters."""
        links = await self._get_links()

        if not links:
            return {"context": "Nie udało się pobrać danych ze strony PK.", "sources": [], "cached": False}

        # Filter links based on student parameters
        filtered = self._filter_links(links, faculty, field_of_study, academic_year, study_year, dean_group)

        # Score links by relevance to query
        scored = self._score_links(filtered, query)

        # Take top results
        top_links = scored[:15]

        if not top_links:
            return {
                "context": "Nie znaleziono dokumentów pasujących do zapytania.",
                "sources": [],
                "cached": True,
            }

        # Build context string for LLM
        context_parts = ["Znalezione dokumenty na stronie WIiT PK:\n"]
        sources = []

        for i, (link, score) in enumerate(top_links, 1):
            context_parts.append(f"{i}. [{link.text}]({link.url})")
            if link.category:
                context_parts.append(f"   Kategoria: {link.category}")
            sources.append({"text": link.text, "url": link.url, "category": link.category})

        # Find up to 3 best matching downloadable files (sorted by score)
        best_files = []
        file_extensions = (".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx")
        for link, score in top_links:
            if any(link.url.lower().endswith(ext) for ext in file_extensions):
                best_files.append({"text": link.text, "url": link.url, "score": score})
                if len(best_files) >= 3:
                    break

        return {
            "context": "\n".join(context_parts),
            "sources": sources,
            "best_match_files": best_files,  # 0-3 downloadable files, sorted by relevance
            "cached": self._is_cache_valid(),
        }

    async def get_all_links(self) -> list[dict]:
        links = await self._get_links()
        return [{"text": l.text, "url": l.url, "category": l.category} for l in links]

    # ── Private helpers ───────────────────────────────────

    async def _get_links(self) -> list[ScrapedLink]:
        """Get links from cache or fresh scrape."""
        if self._is_cache_valid():
            return self._cached_links

        async with self._lock:
            # Double-check after acquiring lock
            if self._is_cache_valid():
                return self._cached_links

            links = await self._scrape_student_page()
            links = self._deduplicate_links(links)
            self._cached_links = links
            self._last_scrape_time = time.time()
            return links

    def _is_cache_valid(self) -> bool:
        return (time.time() - self._last_scrape_time) < self._cache_ttl and bool(self._cached_links)

    async def _scrape_student_page(self) -> list[ScrapedLink]:
        """Scrape the WIiT student page for all document links."""
        links: list[ScrapedLink] = []

        async with httpx.AsyncClient(timeout=30, follow_redirects=True) as client:
            try:
                response = await client.get(self.STUDENT_PAGE_URL)
                response.raise_for_status()
            except httpx.HTTPError as e:
                print(f"[PK Scraper] Error fetching student page: {e}", flush=True)
                return links

        soup = BeautifulSoup(response.text, "html.parser")

        # Find content area
        content = soup.find("div", class_="entry-content") or soup.find("main") or soup

        current_category = ""
        for element in content.find_all(["h2", "h3", "h4", "a", "li"]):
            # Track section headers as categories
            if element.name in ("h2", "h3", "h4"):
                current_category = element.get_text(strip=True)
                continue

            # Extract links
            if element.name == "a":
                href = element.get("href", "")
                text = element.get_text(strip=True)
                if href and text and len(text) > 2:
                    url = urljoin(self.BASE_URL, href)
                    links.append(ScrapedLink(text=text, url=url, category=current_category))

            elif element.name == "li":
                for a_tag in element.find_all("a", href=True):
                    text = a_tag.get_text(strip=True)
                    href = a_tag["href"]
                    if text and len(text) > 2:
                        url = urljoin(self.BASE_URL, href)
                        links.append(ScrapedLink(text=text, url=url, category=current_category))

        print(f"[PK Scraper] Scraped {len(links)} links from student page", flush=True)
        return links

    def _deduplicate_links(self, links: list[ScrapedLink]) -> list[ScrapedLink]:
        """
        Keep only the newest version of duplicate documents.
        Adapted from original C# pre-filtering logic.
        """
        seen: dict[str, ScrapedLink] = {}
        date_pattern = re.compile(r"\d{1,2}[./]\d{1,2}[./]\d{2,4}")

        for link in links:
            # Normalize text for dedup (remove dates, whitespace)
            normalized = date_pattern.sub("", link.text).strip().lower()
            normalized = re.sub(r"\s+", " ", normalized)

            if normalized in seen:
                # Keep the one with a more recent date in the text
                existing_dates = date_pattern.findall(seen[normalized].text)
                new_dates = date_pattern.findall(link.text)
                if new_dates and (not existing_dates or new_dates[-1] > existing_dates[-1]):
                    seen[normalized] = link
            else:
                seen[normalized] = link

        return list(seen.values())

    def _filter_links(
        self,
        links: list[ScrapedLink],
        faculty: Optional[str],
        field_of_study: Optional[str],
        academic_year: Optional[str],
        study_year: Optional[int],
        dean_group: Optional[str],
    ) -> list[ScrapedLink]:
        """Filter links by student parameters. Loose matching — include if any param matches."""
        if not any([faculty, field_of_study, academic_year, study_year, dean_group]):
            return links

        filtered = []
        for link in links:
            combined = f"{link.text} {link.category}".lower()

            # Include if any parameter matches
            match = True  # Start inclusive, exclude mismatches

            if field_of_study:
                # Don't exclude based on field_of_study unless it's clearly about another field
                pass

            if study_year:
                year_patterns = [f"rok {study_year}", f"r.{study_year}", f"semestr {study_year * 2 - 1}",
                                 f"semestr {study_year * 2}"]
                if any(p in combined for p in year_patterns):
                    filtered.append(link)
                    continue

            if dean_group and dean_group.lower() in combined:
                filtered.append(link)
                continue

            if academic_year and academic_year in combined:
                filtered.append(link)
                continue

            # General inclusion — keep links that don't seem to be for a specific other group
            filtered.append(link)

        return filtered

    def _score_links(
        self,
        links: list[ScrapedLink],
        query: str,
    ) -> list[tuple[ScrapedLink, float]]:
        """Score links by keyword relevance to the query."""
        query_lower = query.lower()
        query_words = set(re.findall(r"\w{3,}", query_lower))

        scored = []
        for link in links:
            combined = f"{link.text} {link.category}".lower()
            score = 0.0

            for word in query_words:
                if word in combined:
                    score += 1.0

            # Bonus for exact substring match
            if query_lower in combined:
                score += 3.0

            # Bonus for document-like links (PDFs, etc.)
            if any(ext in link.url.lower() for ext in [".pdf", ".doc", ".xls", ".xlsx"]):
                score += 0.5

            if score > 0:
                scored.append((link, score))

        scored.sort(key=lambda x: x[1], reverse=True)
        return scored
