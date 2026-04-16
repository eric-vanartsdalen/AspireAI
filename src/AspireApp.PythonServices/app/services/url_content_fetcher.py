"""
URL Content Fetcher Service

Orchestrates URL content extraction using pluggable handlers.
Provides the main entry point for the processing pipeline to
fetch content from URLs before routing to docling.
"""

import logging
from typing import List, Optional

from .url_handlers.base import UrlHandler, FetchedContent
from .url_handlers.webpage import GenericWebPageHandler
from .url_handlers.youtube import YouTubeVideoHandler, YouTubeChannelHandler

logger = logging.getLogger(__name__)


class UrlContentFetcher:
    """
    Service for fetching and extracting content from URLs.
    
    Maintains a registry of URL handlers, sorted by priority.
    When fetching a URL, tries handlers in priority order until
    one can handle it.
    
    Usage:
        fetcher = UrlContentFetcher()
        content = await fetcher.fetch("https://youtube.com/watch?v=...")
        
        # content.text contains extracted text
        # content.child_urls contains URLs to also ingest (for channels)
    """
    
    def __init__(self, data_path: str = "/app/data"):
        """
        Initialize the fetcher with default handlers.
        
        Args:
            data_path: Base path for storing downloaded content
        """
        self.data_path = data_path
        self._handlers: List[UrlHandler] = []
        
        # Register default handlers
        self._register_default_handlers()
    
    def _register_default_handlers(self):
        """Register the built-in URL handlers"""
        # YouTube handlers (high priority)
        self.register_handler(YouTubeChannelHandler(max_videos=50))
        self.register_handler(YouTubeVideoHandler())
        
        # Generic webpage handler (low priority fallback)
        self.register_handler(GenericWebPageHandler())
        
        logger.info(f"Registered {len(self._handlers)} URL handlers")
    
    def register_handler(self, handler: UrlHandler) -> None:
        """
        Register a URL handler.
        
        Handlers are sorted by priority (highest first) so that
        specific handlers (YouTube, GitHub) take precedence over
        generic handlers (webpage).
        """
        self._handlers.append(handler)
        # Sort by priority (descending)
        self._handlers.sort(key=lambda h: h.priority, reverse=True)
        logger.debug(f"Registered handler: {handler.handler_name} (priority {handler.priority})")
    
    def get_handler(self, url: str) -> Optional[UrlHandler]:
        """
        Find the first handler that can process the given URL.
        
        Returns None if no handler matches.
        """
        for handler in self._handlers:
            if handler.can_handle(url):
                return handler
        return None
    
    async def fetch(self, url: str) -> FetchedContent:
        """
        Fetch and extract content from a URL.
        
        Finds the appropriate handler and delegates fetching to it.
        
        Args:
            url: The URL to fetch content from
            
        Returns:
            FetchedContent with extracted text and metadata
            
        Raises:
            ValueError: If no handler can process the URL
            Exception: If fetching fails
        """
        handler = self.get_handler(url)
        
        if handler is None:
            raise ValueError(f"No handler available for URL: {url}")
        
        logger.info(f"Fetching URL with {handler.handler_name}: {url}")
        
        try:
            content = await handler.fetch(url, self.data_path)
            
            # Add handler info to metadata
            content.metadata["handler"] = handler.handler_name
            content.metadata["source_type"] = "url" if handler.handler_name == "webpage" else handler.handler_name
            
            logger.info(
                f"Fetched {len(content.text)} chars from {url} "
                f"(type: {content.content_type}, children: {len(content.child_urls or [])})"
            )
            
            return content
            
        except Exception as e:
            logger.error(f"Error fetching {url} with {handler.handler_name}: {e}")
            raise
    
    def list_handlers(self) -> List[dict]:
        """Return information about registered handlers"""
        return [
            {
                "name": h.handler_name,
                "priority": h.priority,
                "type": type(h).__name__
            }
            for h in self._handlers
        ]


# Singleton instance for easy import
_default_fetcher: Optional[UrlContentFetcher] = None


def get_url_content_fetcher(data_path: str = "/app/data") -> UrlContentFetcher:
    """
    Get or create the default URL content fetcher instance.
    
    Uses a singleton pattern for efficiency (handlers don't need
    to be recreated on each request).
    """
    global _default_fetcher
    if _default_fetcher is None:
        _default_fetcher = UrlContentFetcher(data_path=data_path)
    return _default_fetcher
