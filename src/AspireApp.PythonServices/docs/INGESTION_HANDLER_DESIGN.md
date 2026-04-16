# Extensible Ingestion Handler Architecture

**Owner:** Jarvis (Python/Data Dev)  
**Status:** Design → Implementation  
**Date:** 2025-01-19

---

## Problem Statement

Current ingestion flow only supports physical file uploads (PDF, DOCX via Blazor). We need to:

1. Support additional file types: `.txt`, `.md`, `.json`
2. Enable URL ingestion for web pages
3. Handle YouTube URLs (video transcripts + channel expansion)
4. Keep the design extensible for future content sources (podcasts, RSS, APIs)

**Current Bottleneck:** All ingestion assumes a physical file exists on disk in the `/app/data` directory.

---

## Current State Analysis

### Supported File Types (via fallback)
- ✅ **PDF** — PyPDF2 extraction (fallback) or Docling (full)
- ✅ **DOCX** — python-docx extraction (fallback)
- ⚠️ **TXT/MD** — Text fallback exists but not exposed through Web upload UI (`.txt`, `.md` blocked in `FileUploadController._allowedExtensions`)
- ❌ **JSON** — Not supported (no structured ingestion handler)

### Current Upload Flow
```
[Blazor UI] 
  → FileUploadController (validates .pdf/.docx/.txt/.md)
  → FileStorageService.AddFileAsync() → writes to /app/data + creates `files` row
  → Python /processing/process-document/{id}
  → DoclingService.process_document()
    → fallback._extract_pages_text() for unrecognized extensions
  → DatabaseService.save_document_page()
  → Neo4jService.create_document_node()
```

### Gap for URL Ingestion
- No URL validation or fetch logic
- No mechanism to download remote content to local storage
- No YouTube-specific handling (transcripts, channel expansion)
- No metadata extraction from remote sources (publish date, author, domain)

---

## Proposed Architecture

### 1. Ingestion Handler Interface

Define a protocol for pluggable content handlers:

```python
# app/services/ingestion/base_handler.py
from typing import Protocol, Dict, Any, List
from pathlib import Path
from ..models.models import Document, PageContent

class IngestionSource(Protocol):
    """Source descriptor for ingestion request"""
    source_type: str  # "file", "url", "youtube-video", "youtube-channel"
    source_url: str | None
    file_path: Path | None
    metadata: Dict[str, Any]

class IngestionHandler(Protocol):
    """Handler for a specific content source type"""
    
    def can_handle(self, source: IngestionSource) -> bool:
        """Return True if this handler can process the source"""
        ...
    
    def fetch_content(self, source: IngestionSource) -> Path:
        """Download/prepare content, return local file path"""
        ...
    
    def extract_pages(self, file_path: Path, metadata: Dict[str, Any]) -> List[PageContent]:
        """Extract page content from the fetched file"""
        ...
    
    def get_source_confidence(self, source: IngestionSource) -> float:
        """Return confidence score for this source type (0.0-1.0)"""
        ...
```

### 2. Handler Registry

Central dispatcher to route ingestion requests:

```python
# app/services/ingestion/handler_registry.py
class IngestionHandlerRegistry:
    def __init__(self):
        self.handlers: List[IngestionHandler] = []
    
    def register(self, handler: IngestionHandler):
        self.handlers.append(handler)
    
    def get_handler(self, source: IngestionSource) -> IngestionHandler:
        for handler in self.handlers:
            if handler.can_handle(source):
                return handler
        raise ValueError(f"No handler for source type: {source.source_type}")
```

### 3. Concrete Handlers

#### a. File Upload Handler (existing flow)
```python
# app/services/ingestion/handlers/file_handler.py
class FileUploadHandler:
    def can_handle(self, source: IngestionSource) -> bool:
        return source.source_type == "upload" and source.file_path is not None
    
    def fetch_content(self, source: IngestionSource) -> Path:
        # Already on disk, validate and return
        return source.file_path
    
    def extract_pages(self, file_path: Path, metadata: Dict) -> List[PageContent]:
        # Delegate to existing DoclingService or fallback
        extension = file_path.suffix.lower()
        if extension == '.pdf':
            return self._extract_pdf(file_path)
        elif extension == '.docx':
            return self._extract_docx(file_path)
        elif extension in ['.txt', '.md']:
            return self._extract_text(file_path)
        elif extension == '.json':
            return self._extract_json(file_path)
```

#### b. Generic Web Page Handler
```python
# app/services/ingestion/handlers/web_handler.py
import requests
from bs4 import BeautifulSoup

class WebPageHandler:
    def can_handle(self, source: IngestionSource) -> bool:
        return (
            source.source_type == "url" and 
            source.source_url and 
            not self._is_youtube_url(source.source_url)
        )
    
    def fetch_content(self, source: IngestionSource) -> Path:
        # Download HTML, extract main content, save as markdown
        response = requests.get(source.source_url, timeout=30)
        response.raise_for_status()
        
        soup = BeautifulSoup(response.content, 'html.parser')
        main_content = self._extract_main_content(soup)
        
        # Save to temp file
        temp_path = Path(f"/app/data/temp/{uuid4()}.md")
        temp_path.parent.mkdir(exist_ok=True)
        temp_path.write_text(main_content, encoding='utf-8')
        return temp_path
    
    def _extract_main_content(self, soup: BeautifulSoup) -> str:
        # Remove script/style tags
        for tag in soup(['script', 'style', 'nav', 'footer']):
            tag.decompose()
        
        # Extract text from main content areas
        main = soup.find('main') or soup.find('article') or soup.find('body')
        return main.get_text(separator='\n', strip=True) if main else ""
    
    def get_source_confidence(self, source: IngestionSource) -> float:
        # Web pages have medium confidence (0.5)
        return 0.5
```

#### c. YouTube Video Handler
```python
# app/services/ingestion/handlers/youtube_video_handler.py
from youtube_transcript_api import YouTubeTranscriptApi

class YouTubeVideoHandler:
    def can_handle(self, source: IngestionSource) -> bool:
        return (
            source.source_type in ["url", "youtube-video"] and
            source.source_url and
            self._is_youtube_video_url(source.source_url)
        )
    
    def fetch_content(self, source: IngestionSource) -> Path:
        video_id = self._extract_video_id(source.source_url)
        
        # Fetch transcript
        try:
            transcript = YouTubeTranscriptApi.get_transcript(video_id)
            full_text = "\n".join([entry['text'] for entry in transcript])
        except Exception as e:
            raise ValueError(f"Failed to fetch YouTube transcript: {e}")
        
        # Save as markdown with metadata
        temp_path = Path(f"/app/data/temp/youtube_{video_id}.md")
        temp_path.parent.mkdir(exist_ok=True)
        
        content = f"# YouTube Video: {source.metadata.get('title', video_id)}\n\n"
        content += f"**Source:** {source.source_url}\n\n"
        content += f"## Transcript\n\n{full_text}"
        
        temp_path.write_text(content, encoding='utf-8')
        return temp_path
    
    def get_source_confidence(self, source: IngestionSource) -> float:
        # YouTube transcripts are auto-generated (medium-low confidence)
        return 0.4
```

#### d. YouTube Channel Handler
```python
# app/services/ingestion/handlers/youtube_channel_handler.py
from googleapiclient.discovery import build

class YouTubeChannelHandler:
    def can_handle(self, source: IngestionSource) -> bool:
        return (
            source.source_type in ["url", "youtube-channel"] and
            source.source_url and
            self._is_youtube_channel_url(source.source_url)
        )
    
    def fetch_content(self, source: IngestionSource) -> List[IngestionSource]:
        """
        Returns list of video sources instead of single file.
        Requires special handling in orchestration layer.
        """
        channel_id = self._extract_channel_id(source.source_url)
        
        # Fetch video list from YouTube API
        youtube = build('youtube', 'v3', developerKey=os.getenv('YOUTUBE_API_KEY'))
        videos = youtube.search().list(
            channelId=channel_id,
            maxResults=50,
            type='video'
        ).execute()
        
        # Convert to individual video sources
        video_sources = []
        for item in videos.get('items', []):
            video_id = item['id']['videoId']
            video_sources.append(IngestionSource(
                source_type="youtube-video",
                source_url=f"https://www.youtube.com/watch?v={video_id}",
                metadata={
                    'title': item['snippet']['title'],
                    'channel': source.metadata.get('channel_name', ''),
                    'published_at': item['snippet']['publishedAt']
                }
            ))
        
        return video_sources
```

#### e. JSON Handler
```python
# app/services/ingestion/handlers/json_handler.py
import json

class JsonHandler:
    def can_handle(self, source: IngestionSource) -> bool:
        return source.file_path and source.file_path.suffix.lower() == '.json'
    
    def extract_pages(self, file_path: Path, metadata: Dict) -> List[PageContent]:
        # Parse JSON and flatten to text pages
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        # Strategy 1: If array of objects, one page per object
        if isinstance(data, list):
            pages = []
            for i, item in enumerate(data, 1):
                pages.append(PageContent(
                    page_number=i,
                    content=json.dumps(item, indent=2),
                    metadata={'json_index': i - 1}
                ))
            return pages
        
        # Strategy 2: Single object, convert to formatted text
        else:
            return [PageContent(
                page_number=1,
                content=json.dumps(data, indent=2),
                metadata={'json_type': 'object'}
            )]
    
    def get_source_confidence(self, source: IngestionSource) -> float:
        return 0.8  # Structured data has high confidence
```

---

## Integration Points

### 1. New API Endpoint: URL Ingestion

```python
# app/routers/ingestion.py
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, HttpUrl

class UrlIngestionRequest(BaseModel):
    url: HttpUrl
    source_type: str = "url"  # auto-detect or explicit
    tenant_id: str = "default"

@router.post("/ingest-url")
async def ingest_url(request: UrlIngestionRequest):
    """
    Ingest content from a URL.
    - Web pages → markdown extraction
    - YouTube videos → transcript ingestion
    - YouTube channels → expand to individual videos
    """
    registry = get_handler_registry()
    
    source = IngestionSource(
        source_type=request.source_type,
        source_url=str(request.url),
        metadata={'tenant_id': request.tenant_id}
    )
    
    handler = registry.get_handler(source)
    
    # Special case: Channel expansion returns multiple sources
    if isinstance(handler, YouTubeChannelHandler):
        video_sources = handler.fetch_content(source)
        return {
            'status': 'expanded',
            'video_count': len(video_sources),
            'message': f'Channel expanded to {len(video_sources)} videos'
        }
    
    # Normal flow: fetch + create file record + queue processing
    local_path = handler.fetch_content(source)
    
    # Create database record
    db = DatabaseService()
    file_id = db.create_file_record(
        file_name=local_path.name,
        original_file_name=str(request.url),
        file_path=str(local_path.parent),
        source_type=request.source_type,
        source_url=str(request.url),
        source_confidence=handler.get_source_confidence(source),
        tenant_id=request.tenant_id
    )
    
    return {
        'status': 'queued',
        'file_id': file_id,
        'message': 'URL content queued for processing'
    }
```

### 2. Update File Upload Controller (C#)

```csharp
// Allow .json files
private readonly string[] _allowedExtensions = [".pdf", ".docx", ".txt", ".md", ".json"];
```

---

## Dependency Risks

### New Libraries Required

```txt
# Web scraping
beautifulsoup4==4.12.*
requests==2.32.*
html2text==2024.12.*  # Better HTML → Markdown conversion

# YouTube support
youtube-transcript-api==0.6.*
google-api-python-client==2.160.*  # For channel expansion (requires API key)

# Optional: Better content extraction
readability-lxml==0.8.*  # Improved main content detection
```

### Risk Assessment

| Dependency | Risk | Mitigation |
|------------|------|------------|
| `beautifulsoup4` | Low — stable library | Use `lxml` parser for speed |
| `youtube-transcript-api` | Medium — relies on YouTube undocumented API | May break if YouTube changes. Fallback: surface error to user |
| `google-api-python-client` | Medium — requires API key + quota | Make optional; channel expansion disabled without key |
| `readability-lxml` | Low | Optional enhancement; fallback to basic extraction |

### Environment Variables

```bash
YOUTUBE_API_KEY=<optional-for-channel-expansion>
INGESTION_TIMEOUT_SECONDS=30
INGESTION_MAX_URL_SIZE_MB=50
```

---

## Processing Expectations

### Performance Impact

| Source Type | Fetch Time | Processing Time | Total |
|-------------|------------|-----------------|-------|
| File Upload | 0s (already local) | 2-10s (Docling/fallback) | 2-10s |
| Web Page | 1-5s (HTTP + parse) | 1s (text only) | 2-6s |
| YouTube Video | 2-10s (transcript API) | 1s (text only) | 3-11s |
| YouTube Channel | 5-30s (API + expand) | N/A (queues multiple videos) | 5-30s |

### Storage Impact

- **Temp files:** `/app/data/temp/` for fetched content (cleaned after processing)
- **Processed files:** Same as current flow (`/app/data/processed/documents/{id}/`)
- **JSON:** Could be large (multi-MB arrays); consider pagination or chunking for huge files

---

## Extensibility Examples

### Future Handler: Podcast RSS

```python
class PodcastRssHandler:
    def can_handle(self, source):
        return source.source_url and 'rss' in source.metadata.get('feed_type', '')
    
    def fetch_content(self, source):
        # Parse RSS, download audio, transcribe with Whisper
        ...
```

### Future Handler: API Data

```python
class ApiDataHandler:
    def can_handle(self, source):
        return source.source_type == 'api' and source.metadata.get('api_endpoint')
    
    def fetch_content(self, source):
        # Call API, paginate results, serialize to JSON
        ...
```

---

## Decision Points for Eric

1. **YouTube API Key:** Required for channel expansion. Without it, channels are not supported. Acceptable?
2. **Temp File Cleanup:** Should we auto-delete temp files after processing, or retain for debugging?
3. **JSON Strategy:** Array of objects → one page per object, or chunk large arrays?
4. **Source Confidence Defaults:** Current proposal:
   - Upload: 0.7
   - Web page: 0.5
   - YouTube transcript: 0.4
   - JSON: 0.8
5. **URL Size Limits:** Propose 50MB max for fetched content. Override needed?

---

## Implementation Plan

### Phase 1: File Type Support (Low Risk)
1. ✅ Update `FileUploadController._allowedExtensions` to include `.json`
2. ✅ Implement `JsonHandler` for structured data extraction
3. ✅ Test with `city-locations-pops.json` from DataExample

### Phase 2: URL Ingestion Foundation (Medium Risk)
1. ✅ Create handler interface + registry
2. ✅ Implement `WebPageHandler` with BeautifulSoup
3. ✅ Add `/ingest-url` endpoint
4. ✅ Test with public URLs (documentation sites, blogs)

### Phase 3: YouTube Support (High Risk)
1. ✅ Implement `YouTubeVideoHandler` with transcript API
2. ✅ Test with sample videos
3. 🔄 Implement `YouTubeChannelHandler` (requires API key decision)
4. 🔄 Add error handling for missing transcripts

### Phase 4: Polish & Production
1. Add proper logging for each handler
2. Implement temp file cleanup job
3. Add retry logic for transient network failures
4. Document C# contracts for new source types

---

## Open Questions

- Should we support authentication for private URLs (basic auth, OAuth)?
- Rate limiting for URL ingestion to avoid abuse?
- Support for multi-page PDFs from URLs (download → process like upload)?
- Webhook support for external content notifications?

---

**Next Steps:** Await Eric's approval on decision points, then proceed with Phase 1 implementation.
