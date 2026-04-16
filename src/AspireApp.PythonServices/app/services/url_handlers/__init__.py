"""
URL content handlers for fetching and extracting text from various URL types.

Architecture:
- UrlHandler: Abstract base class defining the handler interface
- FetchedContent: Dataclass for handler output
- Each concrete handler implements detection and fetching for specific URL patterns

Extensibility:
- Add new handlers by creating a class that extends UrlHandler
- Register handlers in url_content_fetcher.py registry
- Handlers are checked in priority order (most specific first)
"""

from .base import UrlHandler, FetchedContent
from .webpage import GenericWebPageHandler
from .youtube import YouTubeVideoHandler, YouTubeChannelHandler

__all__ = [
    "UrlHandler",
    "FetchedContent",
    "GenericWebPageHandler",
    "YouTubeVideoHandler",
    "YouTubeChannelHandler",
]
