"""
Base classes for URL content handlers.

Defines the extensibility seam for adding new URL types (YouTube, PDFs at URLs,
RSS feeds, etc.) without modifying the core processing pipeline.
"""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Optional, List, Dict, Any


@dataclass
class FetchedContent:
    """
    Result of fetching and extracting content from a URL.
    
    Attributes:
        text: The extracted text content to be processed as a document
        content_type: Describes the source type (e.g., "video_transcript", "webpage", "document")
        metadata: Source-specific metadata for provenance tracking
        child_urls: For aggregation sources (channels, playlists): URLs to also ingest
        file_path: If content was saved to disk (e.g., downloaded PDF), the path
    """
    text: str
    content_type: str
    metadata: Dict[str, Any] = field(default_factory=dict)
    child_urls: Optional[List[str]] = None
    file_path: Optional[str] = None
    
    @property
    def has_children(self) -> bool:
        """True if this result contains child URLs to be ingested separately"""
        return self.child_urls is not None and len(self.child_urls) > 0


class UrlHandler(ABC):
    """
    Abstract base class for URL content handlers.
    
    Each handler is responsible for:
    1. Detecting if it can handle a given URL (can_handle)
    2. Fetching and extracting text content from the URL (fetch)
    
    Handlers are checked in priority order - more specific handlers should
    have higher priority to override generic handlers.
    """
    
    @abstractmethod
    def can_handle(self, url: str) -> bool:
        """
        Return True if this handler can process the given URL.
        
        Should be a fast check based on URL pattern matching,
        not network requests.
        """
        pass
    
    @abstractmethod
    async def fetch(self, url: str, data_path: str) -> FetchedContent:
        """
        Fetch content from the URL and extract text.
        
        Args:
            url: The URL to fetch content from
            data_path: Base path for storing downloaded files if needed
            
        Returns:
            FetchedContent with extracted text and metadata
            
        Raises:
            Exception: If fetching or extraction fails
        """
        pass
    
    @property
    @abstractmethod
    def handler_name(self) -> str:
        """Unique identifier for this handler type"""
        pass
    
    @property
    def priority(self) -> int:
        """
        Handler priority (higher = checked first).
        
        Specific handlers (YouTube, GitHub) should have higher priority
        than generic handlers (webpage).
        
        Default priority is 100. GenericWebPageHandler uses 0 as fallback.
        """
        return 100
