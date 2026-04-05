# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-05 — Web Upload Store Postgres Cutover

**Status:** Complete (Jeff)

**Key paths:**
- `src/AspireApp.AppHost/AppHost.cs` — the operational upload store now comes from Aspire Postgres via `AddDatabase("DefaultConnection")`, and the Web frontend consumes it through `WithReference(uploadStore)`.
- `src/AspireApp.Web/Program.cs` — `UploadDbContext` now uses `UseNpgsql(...)`; the SQLite path resolution and journal-mode interceptor were removed.
- `src/AspireApp.Web/Shared/FileStorageService.cs` — SQLite WAL checkpoint logic was removed; upload metadata writes are provider-agnostic again.
- `src/AspireApp.Web/appsettings.json` — local default connection string now points at PostgreSQL instead of the old SQLite file.
- `src/AspireApp.WebTest/Fixtures/TestFixture.cs`, `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs` — test fixture captures the injected upload-store connection string and the new integration test proves the upload API persists rows into Postgres.

**Patterns learned:**
- In this repo, the clean Aspire cutover pattern is: name the database resource `DefaultConnection`, inject it with `.WithReference(...)`, and let the Web project keep using `GetConnectionString("DefaultConnection")`.
- For this migration phase, keep Python’s legacy `ASPIRE_DB_PATH` wiring alive in AppHost while the Web project moves first; that preserves current Python startup behavior while making the Web upload store cut over cleanly.
- The most direct regression proof for the .NET half is an upload API test that verifies both the stored file on disk and the corresponding `files` row in Postgres through an `NpgsqlConnection`.

### 2025-02-21 — Deep .NET Analysis

**Build:** Clean, 0 warnings. Target `net10.0` preview, SDK 10.0.200-preview via `global.json`.

**Key Paths:**
- Orchestration: `src/AspireApp.AppHost/AppHost.cs` — 6 services (apiservice, ollama, graph-db, python-service, lightrag, webfrontend)
- Blazor app: `src/AspireApp.Web/Program.cs` — EF Core SQLite, SemanticKernel, MVC controllers mixed in
- API: `src/AspireApp.ApiService/Program.cs` — Only weatherforecast demo endpoint
- Shared: `src/AspireApp.ServiceDefaults/Extensions.cs` — OTel, health, resilience, service discovery
- DB entities: `src/AspireApp.Web/Data/DocumentEntities.cs` — FileMetadata + legacy Document/ProcessedDocument
- DB context: `src/AspireApp.Web/Shared/UploadDbContext.cs`
- File storage: `src/AspireApp.Web/Shared/FileStorageService.cs`
- Upload controller: `src/AspireApp.Web/Controllers/FileUploadController.cs`
- Chat AI: `src/AspireApp.Web/Components/Pages/Chat.razor.cs` — SemanticKernel streaming via Ollama
- Speech: `src/AspireApp.Web/Components/Shared/SpeechService.cs` — JS interop
- Warmup: `src/AspireApp.Web/Services/OllamaWarmupService.cs` — Background model keep-alive
- Config: `src/AspireApp.Web/Components/Pages/HomeConfigurations.cs` — Static env-var config
- AI state: `src/AspireApp.Web/Components/Shared/AiInfoStateService.cs` — Singleton DI-based

**Configuration Keys:**
- AppHost injects `AI-Chat-Model` and `AI-Endpoint` as env vars to webfrontend
- Web reads `AI-Model` (not `AI-Chat-Model`) in AiInfoStateService — key mismatch
- HomeConfigurations reads Aspire connection strings: `ConnectionStrings__ollama`, `ConnectionStrings__chat`
- SQLite at `../../database/data-resources.db` relative to Web project

**Package Versions (as of analysis):**
- Aspire SDK: 13.1.0, Ollama hosting: 13.1.1
- SemanticKernel: 1.71.0, SK.Connectors.Ollama: 1.68.0-alpha (mismatched)
- EF Core Sqlite: 10.0.3, OpenTelemetry: 1.15.0

**Known Issues:**
- Two `ServiceDiscoveryUtilities` classes in different namespaces (root vs Pages)
- ApiService /health only mapped in Development mode via MapDefaultEndpoints()
- LightRAG and Ollama have no health checks but are in WaitFor chains
- Console.WriteLine used instead of ILogger in several places
- Redundant IConfiguration singleton registration in Web/Program.cs
- Legacy entities (Document, ProcessedDocument) still in DbContext with [Obsolete]

### 2026-02-21 — Cross-Agent Findings

**From Bob:**
- Processing pipeline blocked by ~10 missing DatabaseService methods in Python
- Status casing bug ("Uploaded" vs "uploaded") prevents file discovery
- ApiService vestigial, should be removed or given real purpose

**From Jarvis:**
- Save_document_page() signature mismatch will crash during processing
- FK column name conflict (file_id vs document_id) creates data integrity risk
- Requirements.txt unpinned — non-reproducible builds

**From Buster:**
- Zero automated tests — high regression risk on schema changes
- Console.WriteLine used 35+ times in Chat.razor.cs alone
- Cross-service contract tests critical to prevent JSON field name drift

### 2026-02-22 — Squad Orchestration Complete

**Status:** All four agents completed independent reviews; findings merged into shared decisions.md.

**Jeff's Action Items (Ready to Execute):**
1. Status casing fix: FileUploadController line 123 `"Uploaded"` → `"uploaded"` (30 min, P0) — **BLOCKER for Jarvis P0.2 validation**
2. Config key align: AppHost `AI-Chat-Model` → `AI-Model` (30 min, P0)
3. LightRAG health check or remove from WaitFor (1 hr, P0)
4. ApiService removal decision: Remove entire project (1 day, P1)
5. SemanticKernel version sync: Update Connectors.Ollama to 1.71.0 (30 min, P1)
6. Duplicate ServiceDiscoveryUtilities consolidation (1-2 hrs, P1)
7. Console.WriteLine → ILogger replacement (high-impact files first)

**Dependencies:**
- Status casing fix must land before Python tests can pass
- ApiService removal needs full grep verification of references
- All P0 items gate Sprint 1 completion

### 2026-02-27 — Cross-Agent Update: Jarvis P0.2 Lands

**Status:** Jarvis completed P0.2 (save_document_page fix) at commit `e9d90ea`

**Impact on Jeff:**
- P0.2 is now ✅ COMPLETE. Method invocation corrected, FK value fixed.
- **P0 Item 1 (status casing fix) is still BLOCKING** — files won't be discovered until Jeff's change lands
- **Coordination:** Once Jeff lands status casing fix, full file discovery pipeline can be validated end-to-end with Jarvis's fix in place
- Next step for Jeff: Prioritize P0.3 (status casing) to unblock integration testing

### 2026-02-21 — Jeff Full .NET Codebase Review

**Status:** Build clean (0 warnings), Aspire orchestration solid, 3 critical blockers, 7 code quality issues

**Critical Blockers (P0):**
1. Status casing: C# writes "Uploaded", Python queries "uploaded" → file discovery broken
2. AI model env var: AppHost passes "AI-Chat-Model", Web reads "AI-Model" → config mismatch
3. LightRAG: Registered with no health check, webfrontend waits for it → startup can hang

**Code Quality (P1):**
- Duplicate ServiceDiscoveryUtilities class (2 namespaces, 1 logic)
- Console.WriteLine instead of ILogger in 7 files (Chat.razor.cs: 35+ instances)
- OllamaWarmupService creates raw HttpClient (bypasses IHttpClientFactory)
- SemanticKernel version skew (1.71.0 core vs 1.68.0-alpha connectors)

**Tech Debt (P2):**
- Redundant IConfiguration registration in Program.cs
- ApiService health check dev-only (production readiness issue)
- README outdated (.NET 9 ref, should say .NET 10)

**Strategic (P3):**
- LightRAG functional role unclear (replace or supplement custom RAG?)
- ApiService decision pending (keep/remove/merge)
- Test infrastructure foundation (3-5 days when ready)

**Deliverable:** DOTNET_REVIEW.md with 90+ priority actions and validation checklist

### 2026-02-27 — DocumentPage FK Column Fix

**Change:** Fixed `[Column("document_id")]` → `[Column("file_id")]` on `DocumentPage.FileId` property in `DocumentEntities.cs` line 183. Updated corresponding comment and index name in `UploadDbContext.cs` (`idx_pages_document_id` → `idx_pages_file_id`).

**Why:** Python `database_service.py` creates the `document_pages` table with a column named `file_id` (FK to `files(id)`). The C# EF Core model had `[Column("document_id")]` which would map to a non-existent column, causing data integrity failures when both services access the same SQLite database. The C# property name `FileId` was already correct — only the column mapping attribute was wrong.

**Scope:** 2 files, 3 lines changed. `ProcessedDocument.FileId` (line 259, separate table) was intentionally left unchanged.

**Commit:** `6e5b34b` on `feature/doc-upload`

### 2025-11-02 — P0 Item 2 Complete: DocumentPage FK Column Final Alignment

**Status:** Complete (parallel work with Jarvis)  
**Commits:** Jeff: `6e5b34b` | Jarvis: `77db074`

**Jeff's Scope (C#):**
- Changed `[Column("document_id")]` → `[Column("file_id")]` on `DocumentPage.FileId` property in `DocumentEntities.cs` line 183
- Updated `UploadDbContext.cs` index name: `idx_pages_document_id` → `idx_pages_file_id`
- Build verified clean (0 errors, 0 warnings)

**Jarvis's Parallel Scope (Python):**
- Updated `DocumentPage` Pydantic model: `processed_document_id` → `file_id`
- Updated `fix_database.py` and `diagnose_database.py` CREATE TABLE statements
- Updated `README.md` schema documentation

**Result:** C#↔Python schema alignment complete. Both services now agree on FK column name `file_id` referencing `files(id)`. P0 Item 2 closed.

### 2025-11-02 — P0 Item 4 Complete: Upload Status Casing Normalization

**Status:** Complete (Coordinator-assisted, lightweight mode)  
**Commit:** `62ee545`

**Jeff's Scope (C#):**
- Changed `"Uploaded"` → `"uploaded"` on line 123 of `FileUploadController.cs`
- Build verified clean (0 errors, 0 warnings)
- No schema changes required; no cross-service contract updates needed

**Result:** FileUploadController now writes lowercase `"uploaded"` status, matching Python's file discovery queries (`WHERE status = 'uploaded'`) and other status values (processing, processed, error). File discovery pipeline unblocked. P0 Item 4 closed.

### 2026-03-20 — Python Footprint Minimization Follow-Through

**Status:** Complete (Jeff revision)

**Key paths:**
- `src/AspireApp.PythonServices/app/services/database_service.py` — removed legacy sync/document wrappers, added canonical `files`-based projections and status lookup
- `src/AspireApp.PythonServices/app/routers/documents.py` — document endpoints now project directly from `files`
- `src/AspireApp.PythonServices/app/routers/processing.py` — processing endpoints now use canonical file/status methods
- `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` — regression gate now asserts the canonical surface and absence of removed sync shims
- `src/AspireApp.PythonServices/README.md`, `docs/CROSS_SERVICE_CONTRACT.md`, `docs/MIGRATION_GUIDE.md` — docs updated to describe only the live `files` + `document_pages` footprint
- `migrate_database.py`, `test_database_schema.py`, `src/AspireApp.PythonServices/diagnose_database.py`, `src/AspireApp.PythonServices/fix_database.py`, `src/AspireApp.PythonServices/scripts/fix_schema.py`, `src/AspireApp.PythonServices/scripts/test_concurrent_access.py` — support helpers rewritten around the canonical schema

**Patterns learned:**
- Keep the SQLite source of truth in `files`; if the API still wants `Document`/`ProcessingStatus`, project those models from `files` rather than maintaining bridge/sync methods.
- Status values in the Python surface should stay canonical (`uploaded`, `processing`, `processed`, `error`) even when exposed through legacy-shaped response models.
- Stdlib validation is enough to guard this footprint: `python src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` plus `python test_database_schema.py` caught the contract cleanup without needing pytest installed.

### 2026-03-20 — P0 Decision Merge Complete

**Status:** All P0 work merged into shared decisions.md and approved by squad.

**Work Summary Across Squad:**
- **Jeff (this agent):** Finished Python footprint cleanup by removing sync shims and updating canonical contract methods. Fixed status casing + FK column alignment in earlier phases.
- **Jarvis:** Implemented upload path fix + endpoint/method pruning.
- **Bob:** Post-QA revision work. Converted audit tests from `expectedFailure` to live regression. Aligned CROSS_SERVICE_CONTRACT.md.
- **Buster:** QA gates (3 phases). Initial rejection, then approvals post-Bob and post-Jeff.

**Inbox → Decisions.md:** 6 files merged. Jeff's final footprint decision now part of permanent squad record.

**Orchestration Log Created:** Scribe created one per agent documenting spawn phases and context for successors.

**Session Log Created:** Scribe created brief summary of P0 completion status.

**Next Phase:** Continue with P1 items (version pinning, Neo4j batching, etc.) or new features. Jeff to maintain canonical Python contract surface methods and docs going forward.

### 2026-03-21 — Aspire Dashboard Test Auth Capture

**Status:** Complete (Jeff)

**Key paths:**
- `src/AspireApp.WebTest/Fixtures/TestFixture.cs` — enables the Aspire dashboard in test runs, waits for the `aspire-dashboard` resource, and reads dashboard auth data from the resource snapshot environment variables.
- `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs` — stores the dashboard base URL, browser token, and computed authenticated login URL.
- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` — navigates with the authenticated login URL and asserts the redirect leaves `/login` and lands on the resources dashboard.

**Patterns learned:**
- For Aspire dashboard UI tests, use `app.ResourceNotifications.WaitForResourceHealthyAsync("aspire-dashboard")` and read `DASHBOARD__FRONTEND__PUBLICURL` plus `DASHBOARD__FRONTEND__BROWSERTOKEN` from `Snapshot.EnvironmentVariables` instead of scraping the console log output.
- `app.GetEndpoint("aspire-dashboard", "http")` is a good fallback for the dashboard base URL, but the browser token only shows up in the dashboard resource snapshot.
- The dashboard title is runtime-specific (`AspireApp resources` here), so tests should assert the authenticated redirect and a `"resources"` title substring rather than an exact `"Aspire Resources"` string.

### 2026-03-21 — Aspire Dashboard Test Redirect/Title Poll Revision (Bob → Rejection → Fix)

**Status:** Complete (Bob revision after Buster rejection)

**Context:** Jeff's initial artifact captured dashboard URL and token correctly and populated `AppHostMappingModel.AspireDashboardLoginUri`, but Buster rejected the test because the assertion on title failed: after navigating to the login URI, `document.title` was empty when polled.

**Root Cause Race Condition:**
1. `await page.GotoAsync(aspireDashboardLoginUri)` returns after landing on login page
2. Dashboard auth handler validates token and triggers Blazor-driven redirect: `NavigateTo("/", forceLoad: true)`
3. Between redirect start and page fully load, the document transitions: login page → new page (empty title) → Blazor hydration (title set by `<PageTitle>` component)
4. Original test immediately polled `document.title` on the new page before `<PageTitle>` component ran

**Fix Applied (Bob):**
1. After `GotoAsync`, added explicit gate: `await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 120_000 });`
2. Added explicit timeout to title poll: `await page.WaitForFunctionAsync("() => document.title", new PageWaitForFunctionOptions { Timeout = 60_000 });`
3. Changed title assertion from exact match to substring: `Contains("resources", StringComparison.OrdinalIgnoreCase)`

**Validation:**
- `dotnet build AspireApp.sln` ✅ — 0 warnings, 0 errors
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter BasicAspireAppHostTests.AspireDashboardLoads` ✅ — 1 passed, ~40s wall clock

**Pattern Learning:** When testing Blazor Server UI redirects via Playwright, always:
1. Gate subsequent assertions on the URL settling (no "/login" in URL)
2. Use explicit long timeouts (60s+) for title polls to account for SignalR cold-start
3. Assert on flexible conditions (substring/contains) rather than exact matches

**Approved:** Buster accepted revised artifact. Dashboard test harness pattern documented in decisions.md for team adoption.

### 2025-11-02 — Deep Trace: Upload Flow & Ingestion Pipeline Integration Points

**Status:** Complete (read-only review, no code changes)

**Context:** User asked: "FlowEndToEnd test gets file uploaded and in table. Where does Docling parsing trigger? What is the clear ingestion process?"

**Answer — The Gap Identified:**

1. **Web UI Upload Flow (Lines 160–276 in UploadData.razor.cs):**
   - Blazor component reads file from browser
   - POSTs multipart data to `/api/FileUpload` endpoint
   - FileUploadController writes bytes to `./data/{timestamp}_{uuid}.pdf` on host filesystem
   - Writes FileMetadata row to SQLite with status="uploaded"
   - Returns 200 OK to UI, UI refreshes file table
   - **Test stops here** — it sees the file row and assumes ingestion continues

2. **What .NET Actually Does (FileUploadController.cs lines 110–123):**
   - `File.CopyToAsync(stream)` writes bytes to disk (line 113)
   - `FileStorageService.AddFileAsync()` inserts one row into `files` table (line 117)
   - Returns response (line 131)
   - **No background job, no HTTP call, no trigger of any kind**

3. **Aspire Bind Mount Visibility (AppHost.cs lines 73–74):**
   - `./data` ↔ `/app/data` (bidirectional)
   - `./database` ↔ `/app/database` (bidirectional)
   - File written by .NET is **immediately visible** to Python container
   - SQLite database shared in real-time

4. **What Python Service **Expects** (processing.py lines 128–203):**
   - Endpoint: `POST /processing/process-all`
   - Queries `files WHERE status='uploaded'`
   - Launches background tasks per file
   - Calls `docling.process_document()`, creates Neo4j nodes, updates status → "processed"
   - **But this endpoint is never called by .NET**

5. **The Missing Piece:**
   - **No orchestrated trigger** connects upload completion to processing initiation
   - Current architecture is entirely manual: user must either:
     - Call `/processing/process-all` via curl/Postman after upload
     - UI must add a "Process Now" button
     - Background polling service must exist (does not)
   - Test validation cannot go beyond "file appears in table" without manually calling Python endpoint

**Patterns & Decisions:**

- **Status quo:** Ingestion is **pull-based** (Python polls or waits for manual trigger), not **push-based** (Web triggers processing)
- **Next step for testing:** After upload, test must explicitly call Python `/processing/process-all` endpoint and poll file status until it reaches "processed"
- **Next step for UX:** Add "Process Now" button to UI or implement background polling service in .NET
- **Cross-service contract:** File status enum is canonical: "uploaded" (initial), "processing", "processed", "error"

**Documented in:** `.squad/decisions/inbox/jeff-ingestion-trigger-gap.md` for team review

### 2026-03-26 — FlowEndToEnd FastAPI Proof Harness

**Status:** Harness updated; live validation exposed a Python-side integration bug.

**What changed:**
- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` now clears stale copies of the sample document through the Web API, uploads through the UI, resolves the new file row from API-backed state, and then calls the Python trigger/status endpoints directly.
- The test now fails with readable diagnostics for trigger failures, status contract problems, processing errors, or polling timeouts.
- `BasicAspireAppHostTests.PythonServiceOpenAPILoads` still passes, so this work separates "Swagger loads" from "processing actually works."

**Key learning:**
- In this repo, the upload POST is executed by Blazor Server code (`UploadData.razor.cs` via `IHttpClientFactory`), not by browser JavaScript, so Playwright cannot wait on a browser `/api/FileUpload` response to capture the document ID.
- The reliable pattern is: upload via UI → resolve the new row from Web API state → call Python `/processing/process-document/{id}` → poll Python `/processing/status/{id}`.

**Live finding for Jarvis:**
- The revised FlowEndToEnd test currently fails because the uploaded file row exists in the Web API state, but Python returns `404 {"detail":"Document not found"}` for `POST /processing/process-document/{id}`.
- That points to a Python-side shared database visibility/path issue rather than a Swagger/OpenAPI issue.

---

### 2026-04-05 — Postgres Cutover Coordination & BRAIN Pivot Context

**Status:** Postgres cutover complete. Joined BRAIN pivot decision consolidation session.

**What Happened:**
1. **Postgres Upload Store Cutover (completed in parallel with Jarvis):**
   - Web now uses uilder.AddNpgsqlDbContext<UploadDbContext>("appdb") instead of SQLite
   - AppHost injects via .WithReference(postgres); Web reads GetConnectionString("appdb")
   - Removed DeleteJournalModeInterceptor, CheckpointDatabaseAsync, connection-string resolution helpers (~100 lines eliminated)
   - Manual AppHost tuning by Eric ensured connection string wiring works correctly
   
2. **Regression Detection & Coordination:**
   - WebTest failed due to stale fixture expectations (hardcoded DefaultConnection instead of ppdb)
   - Buster diagnosed this as test/harness regression, not product issue
   - Coordination: All three surfaces (AppHost, Web, Python) now use canonical ppdb name
   - Pattern: Future contract tests must derive DB names from AppHost source, not hardcode literals

3. **BRAIN Pivot Context:**
   - Kujan review: BRAIN requires service decomposition, new Validation/Reasoning layers, clear contracts
   - Verbal strategy: MVP should focus on one evidence-backed agentic slice (QA intelligence recommended)
   - Eric decision: Pivot approved. BRAIN is the product; chat is one interface, not architecture
   - Timeline: New phase sequence proposed (Phase 0: Reframe, Phase 1: Contracts, Phase 2-3: First slice)
   - ApiService repurposed: No longer vestigial weather stub; becomes BRAIN Interface Service / API Gateway

**Key Decisions for Web/C# Work Going Forward:**
- Postgres is now canonical for Web operational store (no more SQLite workarounds)
- Multi-service architecture is stabilizing; Aspire orchestration pattern proven sound
- Next BRAIN phase requires Interface Service (C# Minimal API) as API gateway + interface layer
- Consider Semantic Kernel agents or Microsoft.Extensions.AI for BRAIN Reasoning integration (currently only used for chat)

**Contract Alignment:**
- Postgres ppdb is the shared upload store name across all services
- docs/CROSS_SERVICE_CONTRACT.md needs update: "Shared Database" section SQLite → PostgreSQL
- Web's iles table schema unchanged; Python now writes to same Postgres tables

**Related Agent Work:**
- **Jarvis:** Python Postgres cutover completed in parallel; derives contract from AppHost
- **Buster:** Updated WebTest fixture expectations; established pattern for future contract tests
- **Kujan:** Architecture review points to Python service decomposition as next major work
- **Verbal:** Strategy review recommends deferring multi-tenancy until MVP proof

**Orchestration Log:** Created for session context at 20260405T143735Z-jeff.md

---
