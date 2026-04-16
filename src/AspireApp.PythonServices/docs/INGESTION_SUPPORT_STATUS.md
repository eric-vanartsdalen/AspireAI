# Current Ingestion Support Status

**Last Updated:** 2025-01-19  
**Analyst:** Jarvis

---

## ✅ Currently Supported

### File Types (via Blazor Upload)
| Type | Extension | Handler | Status |
|------|-----------|---------|--------|
| PDF | `.pdf` | Docling (full) or PyPDF2 (fallback) | ✅ Working |
| DOCX | `.docx` | Docling (full) or python-docx (fallback) | ✅ Working |
| TXT | `.txt` | Blocked in C# controller | ⚠️ Backend ready, UI blocked |
| MD | `.md` | Blocked in C# controller | ⚠️ Backend ready, UI blocked |

### Processing Flow
```
User uploads file via Blazor
  → FileUploadController validates extension
  → FileStorageService saves to /app/data + creates files row
  → Background task queues processing
  → Python /processing/process-document/{id}
  → DoclingService or fallback extracts pages
  → DatabaseService saves to document_pages
  → Neo4jService creates graph nodes
```

---

## ❌ Not Yet Supported

### File Types
- **JSON** (`.json`) — No handler implemented, C# controller blocks
  - **What's Needed:** 
    - Add `.json` to C# `_allowedExtensions`
    - Implement JsonHandler in Python to parse structure
    - Decide strategy: array → one page per item, or object → single page

### URL Ingestion
- **Web Pages** — No fetch/download logic
  - **What's Needed:**
    - BeautifulSoup for HTML parsing
    - Temp file storage for downloaded content
    - Main content extraction (strip nav/footer)
    - New API endpoint: `POST /ingest-url`

- **YouTube Videos** — No transcript support
  - **What's Needed:**
    - youtube-transcript-api library
    - Video ID extraction from URLs
    - Error handling for missing transcripts
    - Source confidence: 0.4 (auto-generated transcripts)

- **YouTube Channels** — No expansion logic
  - **What's Needed:**
    - YouTube Data API v3 client
    - API key configuration (YOUTUBE_API_KEY env var)
    - Channel ID extraction
    - Bulk video ingestion workflow (queue multiple videos)

---

## 🔧 What's Already in Place

### Database Schema
- ✅ `files.source_url` field exists (nullable)
- ✅ `files.source_type` field exists (default: "upload")
- ✅ `files.source_confidence` field exists (nullable)

### Python Backend
- ✅ Fallback text extraction in `docling_service_fallback.py::_extract_pages_text()`
- ✅ `DatabaseService.create_file_record()` accepts source_url, source_type, source_confidence
- ✅ Canonicalization logic in `app/brain/ingestion/canonicalization.py` handles source metadata

### C# Frontend
- ⚠️ `FileUploadController._allowedExtensions` restricts `.txt`, `.md`, `.json`
- ❌ No URL input field in Blazor UI
- ❌ No YouTube URL handling

---

## 📋 Implementation Checklist

### Phase 1: JSON Support (Immediate, Low Risk)
- [ ] Update `FileUploadController._allowedExtensions` to include `.json`
- [ ] Implement `JsonHandler` in `app/services/ingestion/handlers/json_handler.py`
- [ ] Add unit tests for JSON parsing (array vs object)
- [ ] Test with `DataExample/city-locations-pops.json`
- [ ] Document JSON extraction strategy in `INGESTION_HANDLER_DESIGN.md`

### Phase 2: URL Foundation (1-2 days, Medium Risk)
- [ ] Add dependencies: `beautifulsoup4`, `requests`, `html2text` to requirements.txt
- [ ] Create handler interface in `app/services/ingestion/base_handler.py`
- [ ] Create handler registry in `app/services/ingestion/handler_registry.py`
- [ ] Implement `WebPageHandler` in `app/services/ingestion/handlers/web_handler.py`
- [ ] Create `/ingest-url` endpoint in `app/routers/ingestion.py`
- [ ] Add temp file cleanup logic
- [ ] Test with stable URLs (Wikipedia, documentation sites)
- [ ] Add integration tests

### Phase 3: YouTube Support (2-3 days, High Risk)
- [ ] Add dependencies: `youtube-transcript-api`, `google-api-python-client` to requirements.txt
- [ ] Implement `YouTubeVideoHandler` in `app/services/ingestion/handlers/youtube_video_handler.py`
- [ ] Test with known YouTube videos (educational content with transcripts)
- [ ] Implement `YouTubeChannelHandler` (conditional on API key availability)
- [ ] Add YOUTUBE_API_KEY environment variable to Aspire configuration
- [ ] Test channel expansion workflow
- [ ] Add error handling for missing transcripts, API quota limits

### Phase 4: UI Integration (C# side, coordinate with Jeff)
- [ ] Add URL input field to Blazor upload page
- [ ] Add source type selector (upload, url, youtube-video, youtube-channel)
- [ ] Wire URL ingestion to Python `/ingest-url` endpoint
- [ ] Show processing status for URL ingestion
- [ ] Display source_type and source_url in document list

---

## 🚨 Dependency Risks

| Library | Risk Level | Issue | Mitigation |
|---------|------------|-------|------------|
| `youtube-transcript-api` | **HIGH** | Relies on undocumented YouTube API; breaks if YouTube changes | Surface clear error to user; add retry logic |
| `google-api-python-client` | **MEDIUM** | Requires API key + quota management | Make optional; disable channel expansion without key |
| `beautifulsoup4` | **LOW** | Stable, widely used | Use `lxml` parser for speed |
| `requests` | **LOW** | Industry standard | Set timeouts (30s) to prevent hanging |

---

## 🎯 Recommended Priority Order

1. **JSON support** (Immediate value, zero new dependencies)
2. **Web page ingestion** (Broad applicability, moderate risk)
3. **YouTube video ingestion** (High user interest, manageable risk)
4. **YouTube channel expansion** (Nice-to-have, requires API key decision)

---

## 📝 Open Questions for Eric

1. **YouTube API Key:**
   - Do we want to support channel expansion, or just single videos?
   - If yes, where do we store the API key? (Environment variable, Azure Key Vault, config file?)
   - What's our quota budget? (Free tier: 10,000 units/day, list request = ~100 units)

2. **JSON Strategy:**
   - Large arrays (>10k items): one page per item, or chunk into N items per page?
   - Objects with nested structure: flatten to text, or preserve JSON structure?

3. **Temp File Cleanup:**
   - Auto-delete temp files after processing, or keep for debugging?
   - Retention period if kept (e.g., 7 days)?

4. **URL Ingestion Limits:**
   - Max URL size: 50MB acceptable?
   - Rate limiting per tenant (e.g., 10 URLs/minute)?
   - Authentication support for private URLs (defer to future)?

5. **Source Confidence Defaults:**
   - Upload: 0.7
   - Web page: 0.5
   - YouTube transcript: 0.4
   - JSON: 0.8
   - Acceptable, or override?

---

## 📂 Key File Paths

### Design Documents
- `src/AspireApp.PythonServices/docs/INGESTION_HANDLER_DESIGN.md`
- `.squad/decisions/inbox/jarvis-url-ingestion-architecture.md`
- `.squad/skills/pluggable-ingestion-handlers/SKILL.md`

### Python Implementation
- `app/services/docling_service_fallback.py` — current fallback handlers
- `app/services/database_service.py` — file record creation
- `app/brain/ingestion/canonicalization.py` — source confidence resolution
- `app/routers/processing.py` — processing orchestration

### C# Frontend
- `src/AspireApp.Web/Controllers/FileUploadController.cs` — upload validation
- `src/AspireApp.Web/Shared/FileStorageService.cs` — file persistence
- `src/AspireApp.ApiService/Contracts/BrainContractModels.cs` — cross-service contracts

### Test Data
- `src/AspireApp.WebTest/DataExample/GettysburgAddress.txt` — plain text
- `src/AspireApp.WebTest/DataExample/dotnet-readme.md` — markdown
- `src/AspireApp.WebTest/DataExample/city-locations-pops.json` — JSON array

---

**Status:** Ready for Phase 1 implementation after Eric's approval on decision points.
