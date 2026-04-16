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
from typing import Optional, List, Dict, Any
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
            # Try to get transcript in English first, then any available
            transcript_list = YouTubeTranscriptApi.list_transcripts(video_id)
            
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
            
            # Combine transcript segments into text
            text_parts = []
            for segment in transcript_data:
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
    
    async def fetch(self, url: str, data_path: str) -> FetchedContent:
        """
        Fetch list of video URLs from YouTube channel.
        
        Returns child_urls for each video to be ingested separately.
        """
        channel_id = self._extract_channel_id(url)
        
        if not HTTPX_AVAILABLE:
            raise RuntimeError("httpx is required for YouTube channel expansion")
        
        try:
            # Fetch channel page to find video links
            video_urls = await self._fetch_channel_videos(url)
            
            if not video_urls:
                raise RuntimeError(f"No videos found on YouTube channel {channel_id}")
            
            # Limit to max_videos
            video_urls = video_urls[:self.max_videos]
            
            return FetchedContent(
                text=f"YouTube channel with {len(video_urls)} videos queued for processing.",
                content_type="youtube_channel",
                metadata={
                    "channel_id": channel_id,
                    "url": url,
                    "video_count": len(video_urls)
                },
                child_urls=video_urls
            )
            
        except Exception as e:
            logger.error(f"Error fetching YouTube channel {channel_id}: {e}")
            raise RuntimeError(f"Error fetching YouTube channel {channel_id}: {e}") from e
    
    async def _fetch_channel_videos(self, channel_url: str) -> List[str]:
        """
        Fetch video URLs from a YouTube channel page.
        
        This is a simplified implementation that parses the channel page HTML.
        For production use, consider using the YouTube Data API or yt-dlp.
        """
        async with httpx.AsyncClient(follow_redirects=True, timeout=30.0) as client:
            # Append /videos to get the videos page
            if not channel_url.endswith("/videos"):
                videos_url = channel_url.rstrip("/") + "/videos"
            else:
                videos_url = channel_url
            
            response = await client.get(videos_url)
            response.raise_for_status()
            
            html_content = response.text
            
            # Extract video IDs from the page
            # YouTube embeds video data in JSON within the page
            video_ids: list[str] = []
            seen_ids: set[str] = set()
            
            # Pattern for video IDs in various contexts
            patterns = [
                r'"videoId":"([a-zA-Z0-9_-]{11})"',
                r'/watch\?v=([a-zA-Z0-9_-]{11})',
            ]
            
            for pattern in patterns:
                matches = re.findall(pattern, html_content)
                for match in matches:
                    if match not in seen_ids:
                        seen_ids.add(match)
                        video_ids.append(match)
            
            # Convert to full URLs
            video_urls = [f"https://www.youtube.com/watch?v={vid}" for vid in video_ids]
            
            return video_urls
    
    @property
    def handler_name(self) -> str:
        return "youtube_channel"
    
    @property
    def priority(self) -> int:
        return 210  # Higher than video handler to catch channel URLs first
