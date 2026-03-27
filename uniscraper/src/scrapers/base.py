"""
Base scraper interface and registry for extensible university support.
New universities are added by implementing BaseScraper and registering in the registry.
"""

from abc import ABC, abstractmethod
from typing import Optional


class BaseScraper(ABC):
    """Abstract base for university website scrapers."""

    @abstractmethod
    async def scrape(
        self,
        query: str,
        faculty: Optional[str] = None,
        field_of_study: Optional[str] = None,
        academic_year: Optional[str] = None,
        study_year: Optional[int] = None,
        dean_group: Optional[str] = None,
    ) -> dict:
        """
        Scrape university website with parameterized filters.
        Returns dict with keys: context (str), sources (list[dict]), cached (bool).
        """
        ...

    @abstractmethod
    async def get_all_links(self) -> list[dict]:
        """Return all scraped links from cache or fresh scrape."""
        ...


class ScraperRegistry:
    """Registry for university scrapers — maps university name to scraper instance."""

    def __init__(self):
        self._scrapers: dict[str, BaseScraper] = {}

    def register(self, university_name: str, scraper: BaseScraper) -> None:
        self._scrapers[university_name.lower()] = scraper

    def get(self, university_name: str) -> Optional[BaseScraper]:
        return self._scrapers.get(university_name.lower())

    def list_universities(self) -> list[str]:
        return list(self._scrapers.keys())
