"""
YouTube handlers for extracting video transcripts and channel video lists.

Supports:
- Individual video URLs (youtube.com/watch, youtu.be)
- Channel URLs (youtube.com/channel, youtube.com/@username)
- Playlist URLs (youtube.com/playlist) [future]

Uses youtube-transcript-api for transcript extraction.
Uses yt-dlp or httpx for channel/playlist video listing.
"""

import logging
import re
import xml.etree.ElementTree as ET
from html import unescape
from typing import Optional, List, Dict, Any, Tuple
from urllib.parse import urlparse, parse_qs

from .base import UrlHandler, FetchedContent

logger = logging.getLogger(__name__)

# Optional imports
try:
    from youtube_transcript_api import YouTubeTranscriptApi, TranscriptsDisabled, NoTranscriptFound
    YOUTUBE_TRANSCRIPT_AVAILABLE = True
except ImportError:
    YOUTUBE_TRANSCRIPT_AVAILABLE = False
    logger.info("youtube-transcript-api not installed - YouTube transcript extraction disabled")

try:
    import httpx
    HTTPX_AVAILABLE = True
except ImportError:
    HTTPX_AVAILABLE = False


class YouTubeVideoHandler(UrlHandler):
    """
    Handler for individual YouTube video URLs.
    
    Extracts video transcripts (captions/subtitles) as text content.
    
    Supported URL patterns:
    - https://www.youtube.com/watch?v=VIDEO_ID
    - https://www.youtube.com/watch/VIDEO_ID
    - https://youtu.be/VIDEO_ID
    - https://www.youtube.com/embed/VIDEO_ID
    - https://www.youtube.com/shorts/VIDEO_ID
    """
    
    # Regex patterns for video ID extraction
    YOUTUBE_VIDEO_PATTERNS = [
        r"(?:youtube\.com\/watch\?v=)([a-zA-Z0-9_-]{11})",
        r"(?:youtube\.com\/watch\/)([a-zA-Z0-9_-]{11})",
        r"(?:youtu\.be\/)([a-zA-Z0-9_-]{11})",
        r"(?:youtube\.com\/embed\/)([a-zA-Z0-9_-]{11})",
        r"(?:youtube\.com\/v\/)([a-zA-Z0-9_-]{11})",
        r"(?:youtube\.com\/shorts\/)([a-zA-Z0-9_-]{11})",
    ]
    
    def can_handle(self, url: str) -> bool:
        """Check if URL is a YouTube video URL"""
        return self._extract_video_id(url) is not None
    
    def _extract_video_id(self, url: str) -> Optional[str]:
        """Extract video ID from various YouTube URL formats"""
        for pattern in self.YOUTUBE_VIDEO_PATTERNS:
            match = re.search(pattern, url)
            if match:
                return match.group(1)
        
        # Try parsing query string
        parsed = urlparse(url)
        if "youtube.com" in parsed.netloc:
            query_params = parse_qs(parsed.query)
            if "v" in query_params:
                return query_params["v"][0]
        
        return None
    
    async def fetch(self, url: str, data_path: str) -> FetchedContent:
        """
        Fetch video transcript from YouTube.
        """
        video_id = self._extract_video_id(url)
        if not video_id:
            raise ValueError(f"Could not extract video ID from URL: {url}")
        
        if not YOUTUBE_TRANSCRIPT_AVAILABLE:
            raise RuntimeError("youtube-transcript-api is required for YouTube transcript extraction")
        
        try:
            transcript_api = YouTubeTranscriptApi() if callable(YouTubeTranscriptApi) else YouTubeTranscriptApi

            # Try to get transcript in English first, then any available
            if hasattr(transcript_api, "list"):
                transcript_list = transcript_api.list(video_id)
            else:
                transcript_list = transcript_api.list_transcripts(video_id)
            
            # Prefer manually created over auto-generated
            transcript = None
            language_code = None
            is_generated = False
            
            try:
                transcript = transcript_list.find_manually_created_transcript(['en', 'en-US', 'en-GB'])
                language_code = 'en (manual)'
                is_generated = False
            except Exception:
                try:
                    transcript = transcript_list.find_generated_transcript(['en', 'en-US', 'en-GB'])
                    language_code = 'en (auto-generated)'
                    is_generated = True
                except Exception:
                    # Get any available transcript
                    for t in transcript_list:
                        transcript = t
                        language_code = t.language_code
                        is_generated = t.is_generated
                        break
            
            if transcript is None:
                raise RuntimeError(f"No transcript available for YouTube video {video_id}")
            
            # Fetch transcript text
            transcript_data = transcript.fetch()
            if hasattr(transcript_data, "to_raw_data"):
                transcript_data = transcript_data.to_raw_data()
            
            # Combine transcript segments into text
            text_parts = []
            for segment in transcript_data:
                if hasattr(segment, "text"):
                    text_parts.append(segment.text)
                else:
                    text_parts.append(segment.get("text", ""))
            
            full_text = " ".join(text_parts)
            
            # Clean up transcript text
            full_text = self._clean_transcript(full_text)
            
            return FetchedContent(
                text=full_text,
                content_type="youtube_transcript",
                metadata={
                    "video_id": video_id,
                    "url": url,
                    "language": language_code,
                    "is_auto_generated": is_generated,
                    "segment_count": len(transcript_data),
                    "character_count": len(full_text)
                }
            )
            
        except TranscriptsDisabled:
            raise RuntimeError(f"Transcripts are disabled for YouTube video {video_id}")
        except NoTranscriptFound:
            raise RuntimeError(f"No transcript found for YouTube video {video_id}")
        except Exception as e:
            logger.error(f"Error fetching YouTube transcript for {video_id}: {e}")
            raise RuntimeError(f"Error fetching transcript for YouTube video {video_id}: {e}") from e
    
    def _clean_transcript(self, text: str) -> str:
        """Clean up auto-generated transcript text"""
        # Remove [Music], [Applause], etc.
        text = re.sub(r"\[.*?\]", "", text)
        
        # Normalize whitespace
        text = re.sub(r"\s+", " ", text)
        
        return text.strip()
    
    @property
    def handler_name(self) -> str:
        return "youtube_video"
    
    @property
    def priority(self) -> int:
        return 200  # High priority - check before generic webpage


class YouTubeChannelHandler(UrlHandler):
    """
    Handler for YouTube channel URLs.
    
    Returns a list of video URLs from the channel for individual ingestion.
    Does not fetch transcripts directly - each video URL becomes a child
    that gets processed by YouTubeVideoHandler.
    
    Supported URL patterns:
    - https://www.youtube.com/channel/CHANNEL_ID
    - https://www.youtube.com/@username
    - https://www.youtube.com/c/customname
    """
    
    # Patterns that indicate a channel (not a video)
    CHANNEL_PATTERNS = [
        r"youtube\.com\/channel\/([a-zA-Z0-9_-]+)",
        r"youtube\.com\/@([a-zA-Z0-9_-]+)",
        r"youtube\.com\/c\/([a-zA-Z0-9_-]+)",
        r"youtube\.com\/user\/([a-zA-Z0-9_-]+)",
    ]
    CHANNEL_ID_PATTERNS = [
        r'"externalId":"(UC[a-zA-Z0-9_-]{22})"',
        r'"channelId":"(UC[a-zA-Z0-9_-]{22})"',
        r'"browseId":"(UC[a-zA-Z0-9_-]{22})"',
        r"youtube\.com/channel/(UC[a-zA-Z0-9_-]{22})",
    ]
    CHANNEL_TITLE_PATTERNS = [
        r'"channelMetadataRenderer":\{"title":"([^"]+)"',
        r"<title>([^<]+?)</title>",
    ]
    VIDEO_ID_PATTERNS = [
        r'"videoId":"([a-zA-Z0-9_-]{11})"',
        r'/watch\?v=([a-zA-Z0-9_-]{11})',
    ]
    YOUTUBE_HEADERS = {
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/135.0.0.0 Safari/537.36"
        ),
        "Accept-Language": "en-US,en;q=0.9",
    }
    YOUTUBE_COOKIES = {
        "SOCS": "CAI",
        "CONSENT": "YES+cb.20210328-17-p0.en+FX+471",
    }
    
    def __init__(self, max_videos: int = 50):
        """
        Args:
            max_videos: Maximum number of videos to return from a channel
        """
        self.max_videos = max_videos
    
    def can_handle(self, url: str) -> bool:
        """Check if URL is a YouTube channel URL"""
        for pattern in self.CHANNEL_PATTERNS:
            if re.search(pattern, url):
                return True
        return False
    
    def _extract_channel_id(self, url: str) -> Optional[str]:
        """Extract channel identifier from URL"""
        for pattern in self.CHANNEL_PATTERNS:
            match = re.search(pattern, url)
            if match:
                return match.group(1)
        return None

    def _extract_resolved_channel_id(self, url: str, html_content: str) -> Optional[str]:
        """Extract the canonical UC... channel ID from URL or page HTML."""
        parsed = urlparse(url)
        channel_path = parsed.path.rstrip("/").split("/")
        if len(channel_path) >= 3 and channel_path[1] == "channel":
            candidate = channel_path[2]
            if re.fullmatch(r"UC[a-zA-Z0-9_-]{22}", candidate):
                return candidate

        for pattern in self.CHANNEL_ID_PATTERNS:
            match = re.search(pattern, html_content)
            if match:
                return match.group(1)

        return None

    def _extract_channel_title(self, html_content: str) -> Optional[str]:
        """Extract the channel title from page HTML when available."""
        for pattern in self.CHANNEL_TITLE_PATTERNS:
            match = re.search(pattern, html_content)
            if match:
                return unescape(match.group(1)).replace(" - YouTube", "").strip()
        return None

    def _normalize_channel_videos_url(self, channel_url: str) -> str:
        """Normalize a channel URL to the videos tab without query or fragment noise."""
        parsed = urlparse(channel_url)
        path = parsed.path.rstrip("/")
        if not path.endswith("/videos"):
            path = f"{path}/videos"
        return parsed._replace(path=path, query="", fragment="").geturl()

    def _looks_like_consent_page(self, response) -> bool:
        """Detect YouTube consent interstitials that block page parsing."""
        response_url = str(response.url).lower()
        if "consent.youtube.com" in response_url:
            return True

        html_content = response.text.lower()
        return "before you continue to youtube" in html_content or "consent.youtube.com" in html_content[:500]

    def _create_http_client(self):
        """Create an HTTP client configured for public YouTube page access."""
        return httpx.AsyncClient(
            follow_redirects=True,
            timeout=30.0,
            headers=self.YOUTUBE_HEADERS,
            cookies=self.YOUTUBE_COOKIES,
        )

    async def _fetch_channel_page(self, client, channel_url: str):
        """Fetch a YouTube channel page and retry once if YouTube serves a consent page."""
        response = await client.get(channel_url)
        response.raise_for_status()

        if not self._looks_like_consent_page(response):
            return response

        continue_url = parse_qs(urlparse(str(response.url)).query).get("continue", [channel_url])[0]
        logger.info("Retrying YouTube channel fetch after consent interstitial for %s", channel_url)
        response = await client.get(continue_url)
        response.raise_for_status()
        return response

    async def _fetch_feed_video_urls(self, client, channel_id: str) -> Tuple[List[str], Optional[str]]:
        """Fetch recent channel videos from the public YouTube RSS feed."""
        feed_url = f"https://www.youtube.com/feeds/videos.xml?channel_id={channel_id}"
        response = await client.get(feed_url)
        response.raise_for_status()

        try:
            root = ET.fromstring(response.text)
        except ET.ParseError as exc:
            logger.warning("Failed to parse YouTube feed for channel %s: %s", channel_id, exc)
            return [], None

        namespaces = {
            "atom": "http://www.w3.org/2005/Atom",
            "yt": "http://www.youtube.com/xml/schemas/2015",
        }

        title = root.findtext("atom:title", default=None, namespaces=namespaces)
        video_urls: list[str] = []
        seen_urls: set[str] = set()

        for entry in root.findall("atom:entry", namespaces):
            video_id = entry.findtext("yt:videoId", default=None, namespaces=namespaces)
            if video_id:
                video_url = f"https://www.youtube.com/watch?v={video_id}"
            else:
                link = entry.find("atom:link[@rel='alternate']", namespaces)
                video_url = link.get("href") if link is not None else None

            if video_url and video_url not in seen_urls:
                seen_urls.add(video_url)
                video_urls.append(video_url)

        return video_urls, title

    def _extract_video_urls_from_html(self, html_content: str) -> List[str]:
        """Extract video URLs directly from channel HTML as a fallback/supplement."""
        video_urls: list[str] = []
        seen_urls: set[str] = set()

        for pattern in self.VIDEO_ID_PATTERNS:
            for match in re.findall(pattern, html_content):
                video_url = f"https://www.youtube.com/watch?v={match}"
                if video_url not in seen_urls:
                    seen_urls.add(video_url)
                    video_urls.append(video_url)

        return video_urls
    
    async def fetch(self, url: str, data_path: str) -> FetchedContent:
        """
        Fetch list of video URLs from YouTube channel.
        
        Returns child_urls for each video to be ingested separately.
        """
        channel_reference = self._extract_channel_id(url) or url
        
        if not HTTPX_AVAILABLE:
            raise RuntimeError("httpx is required for YouTube channel expansion")
        
        try:
            video_urls, resolved_channel_id, channel_title = await self._fetch_channel_videos(url)

            if not resolved_channel_id and not video_urls:
                raise RuntimeError(f"Could not resolve YouTube channel {channel_reference}")

            if not video_urls:
                raise RuntimeError(f"No videos found on YouTube channel {channel_reference}")

            # Limit to max_videos
            video_urls = video_urls[:self.max_videos]

            metadata: Dict[str, Any] = {
                "channel_id": resolved_channel_id or channel_reference,
                "channel_reference": channel_reference,
                "url": url,
                "video_count": len(video_urls),
            }
            if channel_title:
                metadata["title"] = channel_title

            return FetchedContent(
                text=f"YouTube channel with {len(video_urls)} videos queued for processing.",
                content_type="youtube_channel",
                metadata=metadata,
                child_urls=video_urls
            )

        except RuntimeError:
            raise
        except Exception as e:
            logger.error(f"Error fetching YouTube channel {channel_reference}: {e}")
            raise RuntimeError(f"Error fetching YouTube channel {channel_reference}: {e}") from e
    
    async def _fetch_channel_videos(self, channel_url: str) -> Tuple[List[str], Optional[str], Optional[str]]:
        """
        Fetch video URLs from a YouTube channel page.
        
        Uses the public RSS feed when the canonical channel ID is available,
        then supplements from page HTML to stay resilient to modern YouTube
        page shapes without requiring an API key.
        """
        videos_url = self._normalize_channel_videos_url(channel_url)

        async with self._create_http_client() as client:
            response = await self._fetch_channel_page(client, videos_url)

            if self._looks_like_consent_page(response):
                raise RuntimeError("YouTube consent interstitial prevented channel resolution")

            html_content = response.text
            resolved_channel_id = self._extract_resolved_channel_id(channel_url, html_content) or self._extract_resolved_channel_id(
                str(response.url), html_content
            )

            channel_title = self._extract_channel_title(html_content)
            video_urls: list[str] = []
            seen_urls: set[str] = set()

            if resolved_channel_id:
                feed_video_urls, feed_title = await self._fetch_feed_video_urls(client, resolved_channel_id)
                if feed_title and not channel_title:
                    channel_title = feed_title
                for video_url in feed_video_urls:
                    if video_url not in seen_urls:
                        seen_urls.add(video_url)
                        video_urls.append(video_url)

            for video_url in self._extract_video_urls_from_html(html_content):
                if video_url not in seen_urls:
                    seen_urls.add(video_url)
                    video_urls.append(video_url)

            return video_urls, resolved_channel_id, channel_title
    
    @property
    def handler_name(self) -> str:
        return "youtube_channel"
    
    @property
    def priority(self) -> int:
        return 210  # Higher than video handler to catch channel URLs first
