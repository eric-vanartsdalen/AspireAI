"""
Generic web page handler for extracting text from HTML pages.

This is the fallback handler for URLs that don't match more specific
handlers (YouTube, GitHub, etc.). It extracts the main text content
from web pages using trafilatura or BeautifulSoup.
"""

import logging
import re
from typing import Optional

from .base import UrlHandler, FetchedContent

logger = logging.getLogger(__name__)

# Optional imports - graceful fallback if not installed
try:
    import httpx
    HTTPX_AVAILABLE = True
except ImportError:
    HTTPX_AVAILABLE = False
    logger.warning("httpx not installed - GenericWebPageHandler will not work")

try:
    import trafilatura
    TRAFILATURA_AVAILABLE = True
except ImportError:
    TRAFILATURA_AVAILABLE = False
    logger.info("trafilatura not installed - using basic HTML extraction")

try:
    from bs4 import BeautifulSoup
    BS4_AVAILABLE = True
except ImportError:
    BS4_AVAILABLE = False
    logger.info("beautifulsoup4 not installed - HTML extraction limited")


class GenericWebPageHandler(UrlHandler):
    """
    Default handler for web pages - extracts visible text content.
    
    Uses trafilatura (preferred) or BeautifulSoup for text extraction.
    Handles redirects and common content types.
    """
    
    def __init__(self, timeout: float = 30.0, max_content_length: int = 10_000_000):
        self.timeout = timeout
        self.max_content_length = max_content_length
    
    def can_handle(self, url: str) -> bool:
        """
        Returns True for any HTTP/HTTPS URL.
        
        This is the fallback handler, so it accepts everything.
        More specific handlers (YouTube, etc.) have higher priority
        and will be checked first.
        """
        return url.startswith("http://") or url.startswith("https://")
    
    async def fetch(self, url: str, data_path: str) -> FetchedContent:
        """
        Fetch the web page and extract main text content.
        """
        if not HTTPX_AVAILABLE:
            raise RuntimeError("httpx is required for GenericWebPageHandler. Install with: pip install httpx")
        
        async with httpx.AsyncClient(follow_redirects=True, timeout=self.timeout) as client:
            response = await client.get(url)
            response.raise_for_status()
            
            content_type = response.headers.get("content-type", "")
            
            # Handle binary content types
            if "application/pdf" in content_type:
                # For PDFs, we'd need to save and process separately
                # For now, return a placeholder
                return FetchedContent(
                    text=f"[PDF document at {url}]",
                    content_type="pdf_url",
                    metadata={
                        "url": url,
                        "content_type": content_type,
                        "note": "PDF content extraction not yet implemented"
                    }
                )
            
            # Get text content
            html_content = response.text
            
            # Extract main text
            text = self._extract_text(html_content, url)
            if not text.strip():
                raise RuntimeError(f"No readable text could be extracted from {url}")
            
            # Extract page title
            title = self._extract_title(html_content)
            
            return FetchedContent(
                text=text,
                content_type="webpage",
                metadata={
                    "url": url,
                    "title": title,
                    "content_length": len(text),
                    "response_status": response.status_code,
                    "extractor": "trafilatura" if TRAFILATURA_AVAILABLE else "beautifulsoup" if BS4_AVAILABLE else "basic"
                }
            )
    
    def _extract_text(self, html_content: str, url: str) -> str:
        """Extract main text content from HTML"""
        
        # Try trafilatura first (best quality)
        if TRAFILATURA_AVAILABLE:
            text = trafilatura.extract(html_content, include_comments=False, include_tables=True)
            if text:
                return text
        
        # Fallback to BeautifulSoup
        if BS4_AVAILABLE:
            soup = BeautifulSoup(html_content, "html.parser")
            
            # Remove script and style elements
            for element in soup(["script", "style", "nav", "footer", "header"]):
                element.decompose()
            
            # Get text
            text = soup.get_text(separator="\n", strip=True)
            
            # Clean up whitespace
            lines = [line.strip() for line in text.splitlines() if line.strip()]
            return "\n".join(lines)
        
        # Basic fallback - strip HTML tags with regex
        text = re.sub(r"<[^>]+>", " ", html_content)
        text = re.sub(r"\s+", " ", text)
        return text.strip()
    
    def _extract_title(self, html_content: str) -> Optional[str]:
        """Extract page title from HTML"""
        if BS4_AVAILABLE:
            soup = BeautifulSoup(html_content, "html.parser")
            title_tag = soup.find("title")
            if title_tag:
                return title_tag.get_text(strip=True)
        
        # Regex fallback
        match = re.search(r"<title[^>]*>([^<]+)</title>", html_content, re.IGNORECASE)
        if match:
            return match.group(1).strip()
        
        return None
    
    @property
    def handler_name(self) -> str:
        return "generic_webpage"
    
    @property
    def priority(self) -> int:
        """Lowest priority - acts as fallback for unmatched URLs"""
        return 0
