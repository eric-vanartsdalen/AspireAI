# AspireAI Tasks

Working task breakdown for the [Plan](../plan.md). Tracks what's been accomplished and what remains.

Note: This will be a living document.

**Last Updated:** 2026-02-27
**Active Branch:** `task/documents-cleanup`

---

## Completed Work ?

### P0 — Pipeline Contract Alignment (Done) ✅

- [x] Fix `save_document_page` invocation/signature mismatch — aligned caller kwargs with service signature (`e9d90ea`)
- [x] Align `document_pages` FK column — C# `[Column("file_id")]`, Python `file_id`, DB `file_id INTEGER NOT NULL` (`6e5b34b`, `77db074`)
- [x] Fix Python router/service contract mismatches — added 9 backward-compatibility wrappers in DatabaseService (`a8e1b71`)
- [x] Normalize upload status casing — FileUploadController writes `"uploaded"` (lowercase) consistently (`62ee545`)
- [x] Validate uploaded rows are locatable by Python prior to processing
- [x] Verify Docker volume mapping exposes uploaded files to Python container at runtime

### Phase 0–2 (Done) ✅

- [x] Solution/project structure and Aspire AppHost orchestration
- [x] Blazor chat UI with message history, user/assistant bubbles, auto-scroll
- [x] Backend LLM integration (Ollama via Semantic Kernel)
- [x] Speech-to-text and text-to-speech (Web Speech API interop)
- [x] File upload UI component with SQLite metadata persistence
- [x] Timestamped file storage with `original_file_name` / `file_name` distinction

---

## Active Tasks

### Upload Path Normalization (P0)

- [ ] Resolve full physical file path as `file_path` (directory) + `file_name` (stored timestamped filename) in Python
- [ ] Add guardrails for Windows-style DB paths ? container runtime paths

**Files:**
- `src/AspireApp.PythonServices/app/services/database_service.py` — add/verify helper to resolve physical path from `file_path` + `file_name`
- `src/AspireApp.PythonServices/app/services/docling_service.py` — confirm docling input uses resolved full path

### Python Footprint Minimization (P0)

- [ ] Remove non-essential SQLite usage patterns and legacy schema dependencies
- [ ] Minimize API endpoints to required upload ? process ? retrieve lifecycle
- [ ] Document the retained endpoint/database contract surface

### Processing Pipeline Stabilization (P1)

- [ ] Process uploaded records through Docling and persist page content in `document_pages`
- [ ] Persist processing timestamps and error details consistently
- [ ] Add retry behavior for failed processing records
- [ ] Ensure processing status transitions use canonical values (`uploaded` ? `processing` ? `processed` / `error`)

**Files:**
- `src/AspireApp.PythonServices/app/routers/processing.py` — ensure processing consumes resolved full file path; use canonical status transitions
- `src/AspireApp.PythonServices/app/services/database_service.py` — handle case variance in `get_unprocessed_files` filter

### Docling ? LightRAG Ingestion (P1)

- [ ] Export Docling free-text output in a form accepted by LightRAG ingest path
- [ ] Confirm LightRAG uses Neo4j backend and ingested records are queryable
- [ ] Keep orchestration through Python retrieval APIs (no parallel retrieval path)

### Chat Retrieval + Citations (P2)

- [ ] Update chat flow to call Python `/rag` retrieval path
- [ ] Render source references in chat UI (file + page + snippet)

---

## Testing & CI Tasks

### Test Infrastructure Bootstrap (P1)

- [ ] Verify/configure `AspireApp.WebTest` project (xUnit) — reference [TagzApp pattern](https://github.com/FritzAndFriends/TagzApp/tree/main/src/TagzApp.WebTest) for Aspire integration testing
- [ ] Add pytest framework with `conftest.py` for Python tests
- [ ] Update CI pipeline (`squad-ci.yml`) to run `dotnet build` + `dotnet test` + `pytest`
- [ ] Gate PR merges on build + test pass

### High-Risk Coverage (P1)

- [ ] Cross-service contract tests (C# JSON serialization ? Python models)
- [ ] Upload-to-processing happy path test
- [ ] Error paths for processing and DB operations
- [ ] Python router unit tests (mocked DatabaseService)

### Integration Tests (P2)

- [ ] Upload smoke test: create/list/delete file; validate filesystem + DB state
- [ ] Python integration: uploaded record ? process ? `document_pages` rows
- [ ] RAG integration: retrieval returns source-bearing results
- [ ] E2E manual smoke: upload ? process ? chat question ? citation appears

---

## Orchestration & Config Tasks

### Config Alignment (P2)

- [ ] Resolve AI model config key mismatch — standardize `AI-Model` across AppHost and Web services
  - `src/AspireApp.AppHost/AppHost.cs` — update environment variable name
  - `src/AspireApp.Web/Components/Shared/AiInfoStateService.cs` — verify reads correct key
  - `src/AspireApp.Web/Components/Pages/HomeConfigurations.cs` — verify reads correct key

### LightRAG Startup (P2)

- [ ] Remove `.WaitFor(lightrag)` from webfrontend until integration code exists
- [ ] Add LightRAG health check endpoint if/when integration is ready
- [ ] Document LightRAG status in AppHost comments

### ApiService Decision (P2)

- [ ] Decide: remove vestigial ApiService (only weather stub) or keep for future API gateway
- [ ] If removing: delete project, remove from `AppHost.cs` and solution

---

## Code Quality Tasks (P3)

### Logging

- [ ] Replace `Console.WriteLine` with `ILogger<T>` in high-impact files:
  - `Chat.razor.cs` (35+ instances)
  - `FileUploadController.cs`
  - `OllamaWarmupService.cs`
  - `AiInfoStateService.cs`
  - `Program.cs` (database init)

### Cleanup

- [ ] Consolidate duplicate `ServiceDiscoveryUtilities` classes (root vs Pages namespace) into single shared class
- [ ] Remove legacy EF entity classes (`Document`, `ProcessedDocument`) that reference non-existent tables
- [ ] Fix `OllamaWarmupService` — inject `IHttpClientFactory` instead of raw `new HttpClient()`
- [ ] Remove redundant `IConfiguration` registration in `Web/Program.cs`
- [ ] Update `ServiceDefaults/Extensions.cs` — map health checks in all environments, not just Development

### Dependencies

- [ ] Pin Python dependency versions in `requirements.txt`
- [ ] Align SemanticKernel package versions (SK Core vs Ollama connector)

### Performance

- [ ] Optimize Neo4j batch writes — move page/relationship creation to `UNWIND` patterns

---

## Milestone Gates

| Gate | Criteria | Status |
|------|----------|--------|
| A | Upload record has valid path + status; file exists on disk | ? |
| E | Volume-mounted files visible to Python container | ? |
| B1 | Processing accepts current upload-row shape (`file_path` dir + timestamped `file_name`) | ? |
| B2 | Processing selection handles `Uploaded` status values without missing records | ? |
| B | Processing writes `document_pages` rows successfully | ? |
| F | Docling output ingested into LightRAG and queryable | ? |
| G | Python SQLite/API footprint reduced and documented | ? |
| C | RAG search returns references from processed content | ? |
| D | Chat displays citation references from retrieval | ? |

---

## Verification Checklist (Gate B1/B2)

- [ ] Upload via Web UI ? confirm row shape: directory in `file_path`, timestamped name in `file_name`
- [ ] Trigger Python processing ? verify row is selected even if status arrives as `Uploaded`
- [ ] Verify processing reads file from mapped volume and writes `document_pages` rows
- [ ] Verify status transitions end at `processed` (or `error` with `processing_error` populated)
