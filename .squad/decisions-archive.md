# Decisions Archive

> Archived entries from .squad/decisions.md exceeding ~20 KB threshold.
> These are kept for reference but not actively reviewed by agents.
> **Archive date:** 2026-04-05  
> **Latest archive:** 9 entries from 2025-11-02 to 2026-03-28 (SQLite startup, FastAPI proof, docling smoke coverage), ~7 KB. These now go below after separator.
> **Previous archive:** 2025-11-02 entry with Dashboard Playwright Testing and earlier entries.

--- ARCHIVE ENTRIES (2025-11-02 to 2026-03-28) ---

## SQLite Startup Schema Self-Repair — Jarvis — 2025-11-02

**Scope:** Python SQLite startup compatibility for persisted developer databases

### Context
Python service can start against existing shared SQLite database that predates latest canonical schema. Observed failure: database lacked `file_hash` column, causing startup to fail at index creation with `sqlite3.OperationalError: no such column: file_hash`.

### Decision
`DatabaseService._ensure_database_schema()` must self-heal persisted SQLite tables by adding missing canonical columns before creating indexes. Smoke coverage in `test_services.py` should surface database initialization failures directly.

### Rationale
Repo intentionally shares persisted SQLite file across C# and Python workflows, so stale schemas are normal upgrade condition rather than edge case. Self-healing enables local developer databases to upgrade in place during Python startup without manual cleanup.

### Impact
- Existing developer databases upgrade in place during Python startup ✅
- Missing-column failures surface as real smoke-test failures instead of console noise ✅
- Regression coverage protects `file_hash` column upgrade path ✅

---

## SQLite Startup QA Gate — Buster — 2025-11-02

**Scope:** Python `DatabaseService()` startup on local Windows + regression coverage

### Decision: Accept startup fix only if QA conditions hold

1. **Local default path selection prefers repo/cwd database over `/app/...` fallbacks** when not in container and `ASPIRE_DB_PATH` unset
2. **Startup diagnostics preserve real failure** by naming database path, SQLite error type/cause, and schema mismatch
3. **Regression tests exercise real path-ordering code and actual startup-failure path** — patching candidate list directly insufficient coverage
4. **`test_services.py` remains usable smoke harness** by calling current APIs and skipping optional dependencies

### Rationale
Defect was environment-specific: curated tests passed while manual local run picked wrong SQLite file and crashed on startup. Regression test bypassing path-ordering logic would miss exact bug we need to prevent.

### Verification Status
- ✅ Local default path resolution tests pass
- ✅ Startup diagnostics include database path and error details
- ✅ Regression tests cover real path ordering and startup failure scenarios
- ✅ `test_services.py` smoke harness validates real startup path

---

## Processing Endpoint Proof Surface — Jarvis — 2025-11-02

**Scope:** FastAPI processing endpoints used by `FlowEndToEnd` and WebTest polling

### Decision
Treat `POST /processing/process-document/{id}` and `POST /processing/process-all` as queue APIs that persist `status='processing'` before returning. Treat `GET /processing/status/{id}` as canonical polling endpoint exposing durable progress data (`processed_pages`, `total_pages`) from SQLite with explicit Swagger response models.

### Rationale
Without upfront status write, caller can successfully trigger processing and immediately poll the same record yet still observe `uploaded` until background task starts — race condition makes end-to-end proof flaky. Persisted page counts give WebTest stronger HTTP-only proof that document pages were written.

### Impact
- Jeff/Buster can trigger `POST /processing/process-document/{id}` and poll `GET /processing/status/{id}` immediately without queue-time race ✅
- Swagger/OpenAPI now documents processing trigger and polling shapes explicitly ✅
- Recommended WebTest assertions: trigger returns 200 + `message`, poll until `status` is `processed`/`error`, assert `total_pages > 0` and `processed_pages > 0` on success ✅

---

## FastAPI Proof Gate — Buster — 2025-11-02

**Scope:** Minimum assertions to credibly prove FastAPI processing endpoints work without regression

### Context
Eric asked: "Can we add FastAPI processing calls to FlowEndToEnd to prove they work?" Audit revealed endpoints exist and handle errors, but test never invokes them—uploading file succeeds but processing pipeline never verified.

### Decision: Add Four Assertions to FlowEndToEnd

**#1: Endpoint Reachability** — Verifies Python service online, routes registered.

**#2: POST Accepts Real Work** — Proves POST endpoint callable and accepts document ID.

**#3: Status Reflects Processing Progress** — Proves background task runs, status transitions, database persists.

**#4: Loud Failure on Contract/Work Break** — Contract breaks, missing endpoints, background failures, database crashes all fail test explicitly.

### Expected Behavior

**Successful Flow:**
```
POST /processing/process-document/1 → 200 {"message": "Processing started for document 1"}
GET /processing/status/1 → 200 {"document_id": 1, "status": "processing", ...}
[wait] → 200 {"document_id": 1, "status": "processed", "total_pages": 12, ...}
```

**Failure Scenarios:**
```
POST invalid ID → 404
POST already processing → 409
GET returns 500 → test fails on status code
Status stuck > 10s → poll timeout, test fails on state check
```

### Impact
- Test moves from UI-only verification to full pipeline proof ✅
- Any break in POST, status queries, or background work caught ✅
- Developers see which stage failed: endpoint, status query, processing, database ✅
- Eric's concern directly addressed ✅

---

## FlowEndToEnd Uses API-Backed Upload State — Jeff — 2025-11-02

**Scope:** End-to-end test upload/processing architecture

### Context
`UploadData.razor.cs` uploads via `IHttpClientFactory` from Blazor Server code, so browser never issues `/api/FileUpload` POST directly. Playwright cannot capture browser network response for upload.

### Decision
Resolve uploaded document from API-backed Web state after UI upload instead of waiting on Playwright response. Call Python processing endpoint directly with resolved document ID and poll Python status endpoint for completion.

### Rationale
- Matches actual runtime architecture (browser upload → Blazor → HTTP → API)
- Gives deterministic document-id capture for follow-up processing calls
- Surfaces real cross-service failures (e.g., Python returning 404 for uploaded ID) instead of hiding behind UI-only pass

### Impact
- `BasicAspireAppHostTests.FlowEndToEnd` can now prove whether FastAPI processing works from harness ✅
- Live validation exposed Python integration bug: Web API sees uploaded row but `POST /processing/process-document/{id}` returns 404 ✅
- Test now exercises full upload → trigger → process → retrieve pipeline ✅

---

## Python Service Startup Path Resolution — Jarvis — 2026-03-27

**Scope:** Python SQLite database path resolution and startup diagnostics

### Findings

#### Database Path Resolution Strategy
Python service uses ordered candidate list:
1. Explicit `db_path` parameter (if provided)
2. `ASPIRE_DB_PATH` environment variable (if set)
3. Platform-specific defaults

#### Startup Error Diagnostics
When database initialization fails, comprehensive diagnostic message generated with path source, exception type, and schema diagnostics.

#### "Legacy Schema" Concept
- No separate "legacy path" detection—all paths treated equally
- "Legacy" refers to schema shape, not file location
- Self-healing via `_ensure_required_columns()` adds missing columns at startup

### Decision
**Affirm test scenario:** `test_legacy_schema_startup_failure_reports_path_and_cause` validates edge case diagnostics when self-healing is unavailable. Test should remain active.

### Rationale
- Production code self-heals missing columns in normal operation
- Test validates fallback diagnostic path when self-healing fails
- Comprehensive error reporting enables faster debugging of schema incompatibilities

### Impact
- Test remains in test suite as regression protection for startup diagnostics ✅
- No code changes required to DatabaseService ✅

---

## Legacy Schema Test Update — Buster — 2026-03-27

**Scope:** Python `DatabaseStartupPathAuditTests.test_legacy_schema_startup_failure_reports_path_and_cause`

### Context
Test was failing after multi-candidate database initialization refactor. Service works correctly in manual testing, but test needed assessment.

### Root Cause
Multi-candidate database initialization refactor changed exception chaining depth, but behavior being tested (legacy schema detection and error reporting) still exists and works correctly.

### Decision
**UPDATE THE TEST** to traverse the exception chain rather than checking only the immediate cause.

### Rationale
- The scenario being tested remains valid
- The error reporting behavior works correctly
- The multi-candidate retry pattern is a deliberate architectural improvement
- Walking the exception chain is more robust than assuming single-level chaining

### Impact
- ✅ Test now passes and correctly verifies legacy schema detection
- ✅ All 10 tests in test_p0_contract_audit.py pass
- ✅ All 30 Python tests pass
- ✅ More resilient to future exception handling refactors

---

## Optional Docling Smoke Coverage — Jarvis — 2026-03-28

**Scope:** Python smoke tests for document processing initialization

### Context
`requirements.txt` intentionally omits the heavyweight `docling` package, while `Dockerfile` installs it only for the full image. Lightweight/dev environments reported the absence of `docling` instead of validating the supported fallback path.

### Decision
Smoke tests should validate `app.services.service_factory` and the selected `DoclingService` implementation, not direct `docling` package availability.

### Rationale
- Matches the runtime contract used by `processing.py` and FastAPI health reporting
- Preserves lightweight developer environments without forcing heavy `docling` install
- Still surfaces real regressions by asserting which implementation the factory selected

### Impact
- `test_services.py` stays meaningful in both full and lightweight environments ✅
- Future changes to optional dependency handling have a clear test target ✅
- Avoids unnecessary dependency bloat in `requirements.txt` ✅

---

## Docling Smoke Gate Alignment — Buster — 2026-03-28

**Scope:** Python service smoke validation for Docling-capable and fallback-capable environments

### Context
Failing smoke signal was `Optional dependency 'docling' is not installed: No module named 'docling'` from test. Audit showed this was reproducible in the project `.venv` because requirements intentionally omit top-level `docling` while lightweight/fallback processing remains a supported development mode.

### Decision
Treat `app.services.service_factory` as the smoke-test contract. The smoke gate should pass when the current environment can initialize either the full Docling service or the fallback processor.

### Rationale
- The product contract already supports fallback processing
- Lightweight development is valid per documentation
- A smoke test that only passes with optional full package installed produces false negatives

### Impact
- Default local `.venv` smoke validation now passes without requiring a heavyweight `docling` install ✅
- Full Docling environments still pass and are detected as `service_type = full` ✅
- Regression coverage proves the factory selects the implementation that matches the installed dependency set ✅

---

--- PREVIOUS ARCHIVED ENTRIES (Before 2025-11-02) ---

## Dashboard Playwright Testing — Jeff, Buster, Bob — 2026-03-21

**Scope:** Aspire Dashboard authenticated navigation in WebTest suite

### Aspire Dashboard Resource Snapshot Capture (Jeff Decision)

**Context:** `AspireApp.WebTest` needed the Aspire dashboard URL and browser token during fixture startup so Playwright can open the authenticated dashboard page reliably.

**Decision:** In Aspire integration tests, wait for the `aspire-dashboard` resource to become healthy and read `DASHBOARD__FRONTEND__PUBLICURL` plus `DASHBOARD__FRONTEND__BROWSERTOKEN` from `dashboardState.Snapshot.EnvironmentVariables`. Use `app.GetEndpoint("aspire-dashboard", "http")` only as fallback for the base dashboard URL.

**Rationale:** The console log line is human-facing and should not be the test contract. The resource snapshot is runtime state owned by the running `DistributedApplication`, so it gives the same values programmatically and avoids log scraping.

**Testing note:** The dashboard title is app-name specific (`AspireApp resources` in this repo), so UI assertions should verify the authenticated redirect leaves `/login` and that the title contains `resources`, not an exact `Aspire Resources` string.

**Affected paths:** `src/AspireApp.WebTest/Fixtures/TestFixture.cs`, `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`, `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs`

**Impact:** Infrastructure sound for dashboard test harness. Foundation work complete.

### Aspire Dashboard Playwright Tests Must Wait for Auth Redirect (Bob Decision)

**Context:** Jeff's dashboard test artifact navigated to `/login?t=TOKEN` and immediately polled `document.title`. Buster rejected it because the page title was empty at assertion time. The Blazor-driven redirect flow (token validation → cookie set → `NavigateTo("/", forceLoad: true)`) involves a race: between `GotoAsync` returning and the redirect completing, there's a window where `document.title` reflects the login page, then goes empty on the new page before Blazor hydrates `<PageTitle>`.

**Decision:** All Playwright tests that go through the Aspire Dashboard `/login?t=TOKEN` auth flow must:

1. **Gate on redirect completion**: `WaitForURLAsync(url => !url.Contains("/login"))` after `GotoAsync`.
2. **Poll the title with explicit timeout**: `WaitForFunctionAsync` must specify at least 60s timeout — the default 30s is insufficient for Blazor Server cold-start.
3. **Assert flexibly on title content**: Use `Contains("resources")` (case-insensitive), not an exact match like `"Aspire Resources"`, since the title varies across Aspire versions.

**Rationale:** `WaitForURLAsync` closes the redirect race; 60s timeout accommodates Blazor hydration latency; flexible title assertions avoid version/configuration fragility.

**Implementation Status:**
- `dotnet build AspireApp.sln` ✅
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj` ✅
- Targeted test `BasicAspireAppHostTests.AspireDashboardLoads` ✅

**Impact:**
- **Buster:** Accept the revised test as passing the QA gate.
- **Jeff:** Adopt this pattern for any new Aspire Dashboard Playwright tests.
- **All:** The `TestFixture.cs` token/URL capture infrastructure is confirmed sound — only the assertion strategy needed fixing.

---


# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-03-25):** Archived pre-2026-02-27 entries (6 decisions, ~12 KB) to `decisions-archive.md` due to file size (30.75 KB → 18.5 KB target). Merged 2 inbox decisions: bob-roadmap-tracking-2026-03-25.md, copilot-directive-2026-03-25T14-07-58Z.md. Inbox cleared.


<!-- Decisions are appended below. Each entry starts with ### -->


## Architecture Review — Bob (Lead/Architect) — 2026-02-21

**Scope:** Comprehensive architecture review of AspireAI solution

### CRITICAL: Python Router↔DatabaseService Contract Misalignment

Python Pydantic models and routers are out of sync with the DatabaseService class. The routers call ~10 methods (`get_document()`, `get_unprocessed_documents()`, `get_processed_document()`, `save_processed_document()`, etc.) that **do not exist** on the current DatabaseService. This causes `AttributeError` at runtime on most document and processing endpoints.

**Decision:** Either add the missing wrapper methods to DatabaseService or rewrite routers to call the current API (`get_file_by_id()`, `get_unprocessed_files()`, `get_all_files()`). Option (b) is cleaner.

**Impact:** Processing pipeline (Gate B1/B2) is completely blocked until fixed. ~1 day effort.

### HIGH: Status Casing Mismatch

FileUploadController.cs line 123 writes `"Uploaded"` (capital U) to the status field, but Python's `get_unprocessed_files()` queries `WHERE status = 'uploaded'` (lowercase). **Files uploaded via the C# Web UI will never be found by Python for processing.**

**Decision:** Normalize to lowercase `"uploaded"` in FileUploadController.cs. One-line fix unblocks entire pipeline.

**Impact:** Fixes file discovery. ~30 minutes.

### HIGH: ApiService is Vestigial

AspireApp.ApiService contains only a weather forecast stub. The entire project is boilerplate with no integration into the document pipeline. Web frontend communicates directly with Python service and SQLite. ApiService adds startup latency and orchestration complexity for zero value.

**Decision:** Three options: (a) keep as facade/proxy, (b) merge into Web, (c) remove entirely. Recommend (c) now. Add real API gateway later if needed.

**Impact:** Simplifies startup, reduces orchestration bloat. Medium-term effort for removal.

### MEDIUM: SQLite Concurrent Access Risk

Two services (Web + Python) share one SQLite file via bind mount with dual access patterns: EF Core in C# and raw SQL in Python. SQLite with WAL mode handles this reasonably, but there's no coordination on schema migrations. Cold-start race condition possible.

**Decision:** WaitFor(pythonServices) in AppHost mitigates. Add explicit schema version check at startup if needed. Monitor in production.

**Impact:** Low immediate risk given WaitFor ordering. Document as potential issue.

### MEDIUM: LightRAG Integration is Wired But Unverified

LightRAG container is registered with full configuration in AppHost but has no health check or integration test. Web frontend `WaitFor(lightrag)` blocks on it. No Python or C# code references LightRAG APIs.

**Decision:** Either add `.WithHttpHealthCheck()` for LightRAG or remove `WaitFor(lightrag)` from webfrontend until integration code exists. Improves startup time during development.

**Impact:** Fixes startup blocking. ~1 hour.

### MEDIUM: global.json Targets .NET 10 Preview

`global.json` specifies SDK 10.0.0 with `allowPrerelease: true`. All `.csproj` files target `net10.0`. This is fine for learning but creates dependency on preview SDK availability. README still references ".NET 9" (stale).

**Decision:** Document .NET 10 requirement clearly in README. Consider pinning to stable SDK release when available.

**Impact:** Maintenance task. Update docs immediately.

### LOW: Legacy Entity Dead Weight

`DocumentEntities.cs` carries deprecated `Document` and `ProcessedDocument` EF entities mapped to non-existent `documents` and `processed_documents` tables. These legacy tables are created by EF Core alongside the canonical `files` + `document_pages` tables, cluttering the schema.

**Decision:** Remove deprecated entities and migrations after verifying no remaining code references them.

**Impact:** Reduces confusion. ~2 hours effort.

### LOW: No Automated Tests

No test projects exist in the solution. `test_all_builds.py` and `test_database_schema.py` are utility scripts, not CI-ready test suites. Roadmap's "Testing Baseline" task is unstarted.

**Decision:** Establish test infrastructure (CI build + test projects) before closing schema migration. Create C# xUnit project and Python pytest suite per Buster's recommendations.

**Impact:** Enables safe refactoring. Foundation work required.

---

## .NET Deep Analysis — Jeff (.NET Dev) — 2026-02-21

**Scope:** .NET projects deep dive, build health, package dependency alignment

### Config Key Mismatch: AI-Chat-Model vs AI-Model

AppHost.cs line 129 passes `AI-Chat-Model` as environment variable, but Web project's `AiInfoStateService` and `HomeConfigurations` look for `AI-Model`. The model name may not propagate correctly through Aspire-injected env vars.

**Decision:** Align environment variable naming. Update either AppHost to use `AI-Model` or Web services to check `AI-Chat-Model`. Prefer consistent naming across all Aspire services.

**Impact:** Fixes AI model propagation. ~30 minutes.

### LightRAG and Ollama Have No Health Checks

LightRAG is registered with `AddContainer()` but has no `WithHttpHealthCheck()`. Ollama has no explicit health check configured. The webfrontend `WaitFor()` will wait indefinitely if either fails to start properly.

**Decision:** Add `.WithHttpHealthCheck()` for both LightRAG (port 9621) and Ollama (port 11434). If health check endpoints don't exist, remove from WaitFor chain until integration code ready.

**Impact:** Fixes startup blocking, improves debugging. ~1 hour.

### SemanticKernel Version Mismatch

`Microsoft.SemanticKernel` is at 1.71.0 but `Microsoft.SemanticKernel.Connectors.Ollama` is at 1.68.0-alpha. These should be kept in sync to avoid runtime compatibility issues.

**Decision:** Update Connectors.Ollama to match core SK version (1.71.0).

**Impact:** Fixes dependency skew. ~30 minutes.

### Duplicate ServiceDiscoveryUtilities Class

Two classes with the same name exist in different namespaces:
- `AspireApp.Web.ServiceDiscoveryUtilities` (root namespace)
- `AspireApp.Web.Components.Pages.ServiceDiscoveryUtilities` (Pages namespace)

They have different method signatures and behavior. `HomeConfigurations` uses Pages version, `AiInfoStateService` uses root version. Maintenance hazard.

**Decision:** Consolidate into single class in shared namespace. Verify both call sites work correctly after merge.

**Impact:** Reduces confusion. ~1-2 hours.

### OllamaWarmupService Creates Raw HttpClient

Line 88 creates `new HttpClient()` instead of using `IHttpClientFactory` from DI. This bypasses resilience policies and proper lifecycle management.

**Decision:** Inject `IHttpClientFactory` into OllamaWarmupService constructor and use it to create HttpClient.

**Impact:** Follows .NET guidance. ~30 minutes.

### Console.WriteLine Used Extensively

Both `Chat.razor.cs` (35+ instances) and other services use `Console.WriteLine` for debug output instead of `ILogger<T>`. This bypasses structured logging and won't appear in Aspire telemetry.

**Decision:** Replace all `Console.WriteLine` with injected `ILogger<T>`. Scope to high-impact files (Chat, FileUploadController) initially.

**Impact:** Improves observability. Medium-term cleanup.

### ApiService /health Endpoint Only Mapped in Development

`ServiceDefaults.MapDefaultEndpoints()` line 115 has `if (app.Environment.IsDevelopment())`. Health endpoints won't exist in production. AppHost registers `WithHttpHealthCheck("/health")` for apiservice, creating a mismatch.

**Decision:** Either map `/health` unconditionally or adjust AppHost expectations. For now, document as dev-only during Aspire runs.

**Impact:** Fixes health check for production deployments. ~1 hour.

### Redundant IConfiguration Registration

`Program.cs` line 53: `builder.Services.AddSingleton<IConfiguration>(builder.Configuration)` is unnecessary. `IConfiguration` is already registered by the host builder.

**Decision:** Remove redundant registration.

**Impact:** Cleanup. ~5 minutes.

---

## Python Services & Neo4j Deep Analysis — Jarvis (Python/Data) — 2026-02-21

**Scope:** Python service architecture, API endpoints, contract alignment, Neo4j schema validation

### CRITICAL: ~10 Missing DatabaseService Methods

The routers call methods that don't exist on the current `DatabaseService` class:
- `get_document()`, `get_unprocessed_documents()`, `get_documents_by_status()`
- `get_processed_document()`, `save_processed_document()`
- `get_statistics()`, `get_active_services()`, `get_file_document_sync_status()`, `force_sync_files_and_documents()`

These cause `AttributeError` at runtime on most document, processing, and health check endpoints.

**Decision:** Implement missing methods as thin wrappers around the current `get_file_by_id()` / `get_unprocessed_files()` API, or rewrite routers to call existing methods directly. Option (b) is cleaner and aligns with "minimal Python footprint" goal.

**Implemented by Jarvis (2025-11-02):** Added 9 backward-compatibility wrapper methods to `DatabaseService`. Wrapper methods delegate to existing file-based methods + model conversion. Preserves router API contract unchanged; reuses proven internal methods; consistent with existing pattern. Commit: (from inbox decision).

**Impact:** Unblocks processing pipeline. ~1 day effort → complete.

### CRITICAL: save_document_page() Signature Mismatch

`processing.py` line 75 calls `db.save_document_page(page_record)` passing a DocumentPage object, but the actual signature is `save_document_page(self, file_id, page_number, content, metadata, neo4j_node_id)` expecting individual arguments.

**Decision:** Update call to pass individual arguments: `db.save_document_page(file_id, page_number, content, metadata, node_id)`.

**Implemented by Jarvis (2025-11-02):** Fixed method invocation in `processing.py`. Commit `e9d90ea`. P0 Item 2 complete.

**Impact:** Fixes document processing crash. ~1 hour → complete.

### HIGH: Status Casing Mismatch ("Uploaded" vs "uploaded")

C# FileUploadController writes `"Uploaded"` (capital U) but Python queries for `"uploaded"` (lowercase). Files uploaded via Web UI will never be found by Python.

**Decision:** Change C# to write lowercase `"uploaded"` to match Python expectations (also matches other status values: processing, processed, error).

**Implemented by Jeff (2025-11-02):** Normalized FileUploadController.cs line 123 `"Uploaded"` → `"uploaded"`. Commit `62ee545`. P0 Item 4 complete.

**Impact:** Enables file discovery. ~30 minutes → complete.

### HIGH: FK Column Name Mismatch on document_pages

| Side | Column Name |
|------|-------------|
| **Python** (CREATE TABLE) | `file_id` |
| **C#** (EF Core [Column] attribute) | `document_id` |

Whichever service creates the table first determines the actual column name. The other will fail or behave incorrectly.

**Decision:** Decide on canonical name (recommend `file_id` for consistency with foreign key semantics). Update C# [Column] attribute to match Python CREATE TABLE statement. Verify both sides agree before cold-start.

**Implemented by Jeff & Jarvis (2025-11-02):** Aligned to canonical `file_id`. C# [Column] attribute updated; Python schema unchanged. Commits: Jeff `6e5b34b`, Jarvis `77db074`. P0 Item 2 complete.

**Impact:** Fixes data integrity risk. ~2 hours → complete.

### HIGH: Legacy C# Entities Reference Non-Existent Tables

`DocumentEntities.cs` has `Document` mapped to `documents` table and `ProcessedDocument` mapped to `processed_documents` table. Neither table exists in Python schema. This dead code could cause confusion or conflict during migrations.

**Decision:** Remove deprecated entities after verifying no remaining code references them.

**Impact:** Reduces confusion. ~1-2 hours.

### MEDIUM: requirements.txt Has No Version Pinning

All dependencies are unpinned (`fastapi`, `uvicorn`, `neo4j`, `docling-core`, etc.). Builds are non-reproducible. `docling` especially is heavy; upgrades could break processing pipeline.

**Decision:** Pin all dependencies with version constraints. Use `pip freeze` to generate reproducible requirements. Example:
```
fastapi==0.104.1
uvicorn==0.24.0
neo4j==5.14.0

## P0: Upload Path Normalization & Python Footprint Minimization — Bob, Jarvis, Jeff, Buster — 2026-03-20

### BLOCKING: Upload Path Normalization

**Problem:** DoclingService path construction used wrong base (`/app/data/uploads` instead of `/app/data`) and wrong field (`file_path` directory instead of `file_name` filename). Result: guaranteed `FileNotFoundError`.

**Decision:** Container-relative path resolution rule: `{DATA_PATH}/{file_name}` where `DATA_PATH=/app/data`. Remove `self.uploads_path` concept entirely.

**Implemented by Jarvis:** Path resolver in `DoclingService.process_document()` now correctly constructs `data_mount / document.filename`. Supports both container-style and Windows-style database values.

**Validation:** `test_p0_contract_audit.py` assertions converted from `expectedFailure` to live regression coverage.

**Impact:** Unblocks Gate B1. Files uploaded via C# now discoverable and processable.

### Python Endpoint Surface Rationalization

**Decision:** Removed 7 dead endpoints:
- `GET /documents/health/concurrent-access` (no-op pool stats)
- `GET /documents/health/schema-sync` (always "healthy")
- `POST /documents/admin/force-sync` (dead endpoint)
- `GET /documents/stats/performance` (unused dashboard stats)
- `GET /documents/health/database` (redundant with `/health`)
- `GET /processing/status/{document_id}` (duplicate of `/documents/{document_id}/status`)
- `GET /processing/processed-documents` (reimplements `/documents/status/completed`)

**Retained (13 core + health):** Upload/process/retrieve lifecycle endpoints plus search and context retrieval.

**Implemented by Jarvis & Jeff:** Endpoints removed; routers updated to canonical schema.

**Impact:** Smaller attack surface, clearer contract documentation.

### Python DatabaseService Footprint Minimization

**Decision:** Removed 5 dead methods (only used by deleted endpoints):
- `get_statistics()`, `get_active_services()`, `get_file_document_sync_status()`, `force_sync_files_and_documents()`, `save_document()`

**Retained:** 8 core pipeline methods + 7 legacy compatibility wrappers (justified: router rewrite is P2, not P0).

**Implemented by Jeff:** Sync shims and retired-schema support artifacts removed. Routers now project directly from canonical `files` and `document_pages` tables.

**Impact:** Footprint minimized; compatibility layer maintained for smooth migration.

### Cross-Service Contract Documentation

**Deliverable:** `docs/CROSS_SERVICE_CONTRACT.md` updated with:
1. Shared DB schema (files, document_pages) — canonical column names, types, constraints
2. Status lifecycle: `uploaded` → `processing` → `processed` | `error`
3. Path resolution rule: `file_path` (host) + `file_name` (container)
4. Volume mounts: host `./data` → `/app/data` in both containers
5. Retained API surface (16 endpoints)
6. Ownership: C# owns upload/insert, Python owns processing/status

**Impact:** Single source of truth for contract; reduces cross-service bugs.

### QA Gating

**Buster Review Phases:**
1. Initial: Rejected due to incomplete test gate + contract misalignment
2. Post-Bob revision: Approved Upload Path Normalization; footprint remains open
3. Post-Jeff cleanup: Approved Python Footprint Minimization

**Final Status:** Both P0 items approved; validation gates live; ready for production deployment.

---

## Decision Summary (2026-03-20 Merge)

**Inbox files merged and deleted:**
- `bob-python-footprint-p0.md` → merged to decisions
- `jarvis-python-contract-trim.md` → merged to decisions
- `buster-p0-qa-gate.md` → merged to decisions (context only)
- `buster-p0-footprint-gate.md` → merged to decisions (archived in log)
- `buster-p0-python-footprint-approval.md` → merged to decisions
- `jeff-python-footprint-minimization.md` → merged to decisions

**Deduplication:** No exact duplicates found. Decisions form coherent audit trail from initial blocker → implementation → validation → approval.

**Cross-agent propagation:** Updates appended to Bob, Jarvis, Jeff, Buster history.md files.
docling-core==1.2.0
```

**Impact:** Enables reproducible builds. ~1 hour.

### MEDIUM: Neo4j Operations Not Batched

Pages and relationships are created one-by-one in loops instead of batched with `UNWIND`. This is slow at scale.

**Decision:** Refactor page and relationship creation to use batch `UNWIND` queries. Example:
```cypher
UNWIND $pages as page
CREATE (p:Page {id: page.id, document_id: page.document_id, ...})
```

**Impact:** Improves processing performance. ~4 hours.

### MEDIUM: No Full-Text or Vector Index

Neo4j search uses string `CONTAINS` (very slow at scale). Vector index is commented out in neo4j.conf. GDS and APOC plugins installed but unused.

**Decision:** Create full-text index for text search. Enable vector index for semantic search once embeddings are added. Example:
```cypher
CREATE FULLTEXT INDEX ft_page_content FOR (p:Page) ON EACH [p.content]
```

**Impact:** Enables scalable search. ~2 days for vector integration.

### LOW: LightRAG Container Has Zero Python Integration

LightRAG is wired in AppHost as separate container with Ollama connection and Neo4j access. **No Python code calls LightRAG APIs.** Web frontend waits for it but doesn't use it. Completely standalone.

**Decision:** Clarify LightRAG role: Is it replacing the custom Python RAG pipeline or supplementing it? Document decision and either (a) wire Python endpoints to call LightRAG, or (b) remove from AppHost/startup until integration code ready.

**Impact:** Clarifies architecture. Depends on product decision.

---

## P0 Completion & Roadmap Update — Bob (Lead/Architect) — 2026-03-20

**Scope:** Mark P0 completions in roadmap; close out Upload Path Normalization and Python Footprint Minimization

### Roadmap Status: P0 Items Complete

Two P0 efforts have been successfully completed and approved:

1. **Upload Path Normalization (P0)** ✅ — Files uploaded via C# Web UI are now discoverable and processable by Python services
2. **Python Footprint Minimization (P0)** ✅ — API surface rationalized, dead endpoints removed, DatabaseService methods cleaned up

**Impact:** Clears milestone Gates A, B1, B2, E, G. Enables P1 (Processing Pipeline Stabilization) to proceed without path/schema concerns.

**Decision:** Record P0 completions in `roadmap/Tasks.md`. Move Upload Path Normalization and Python Footprint Minimization items into Completed Work section. Update milestone gates table. Active blockers (Gates B, F, C, D) remain pending downstream P1/P2 work.

**Metadata Update:** Last Updated corrected to 2026-03-20; Active Branch updated to `task/p0-python-tasks` to reflect current working baseline.

---

---

## Quality Audit — Buster (QA) — 2026-02-21

**Scope:** Automated test inventory, CI/CD health, code quality patterns

### CRITICAL: Zero Automated Tests

The solution has no test projects and no automated test suite:
- **C#:** 0 test projects (no xUnit/NUnit/MSTest)
- **Python:** 6 "test" files, but NONE are actual tests. All are manual diagnostic scripts with no assertions, no pytest runner, no conftest.py, no pytest.ini.

The files `test_all_builds.py`, `test_database_schema.py`, `test_services.py`, and `test_concurrent_access.py` are utility/benchmark scripts, not pytest-integrated tests.

**Decision:** Establish test infrastructure before closing schema migration:
1. Create `AspireApp.UnitTests.csproj` (xUnit)
2. Add `pytest` to requirements.txt
3. Create `conftest.py` and `pytest.ini`
4. Create integration test suites per Buster's Phase 2-3 roadmap

**Impact:** Enables safe refactoring of P0/P1 fixes. Blocks PR merge until CI passes.

### CRITICAL: CI/CD Pipeline is Non-Functional

`squad-ci.yml` is placeholder: `echo "No build commands configured"`. No build verification, no tests run, PRs merge unchecked.

**Decision:** Update CI workflow to:
1. Run `dotnet build` (with Aspire stopped to avoid file locks)
2. Run `dotnet test` once test projects exist
3. Run `pytest` on Python services once test suite created
4. Block PR merge until all checks pass

**Impact:** Prevents regression. Foundation work.

### HIGH RISK: Logging Uses Console.WriteLine

7+ files use `Console.WriteLine` instead of `ILogger<T>`:
- `Chat.razor.cs` (35+ instances)
- `Program.cs`, `HomeConfigurations.cs`, `ServiceDiscoveryUtilities.cs`, `AiInfoStateService.cs`, `SpeechService.cs`

This bypasses structured logging and OpenTelemetry integration. Debug output won't appear in Aspire dashboard logs.

**Decision:** Replace with `ILogger<T>`. Inject logger into services/components. Prioritize high-impact files (Chat, Controllers).

**Impact:** Improves observability. Medium-term refactoring.

### HIGH RISK: No Cross-Service Contract Tests

C#↔Python communication has no validation tests. If C# changes field names or types, Python models silently diverge. This is a primary vector for runtime failures.

**Decision:** Create contract test suite that verifies:
1. C# records serialize to JSON matching Python Pydantic model field names
2. Python models deserialize C# JSON correctly
3. Enum values and status strings match
4. DateTime formats are compatible

Run these tests in CI on every build.

**Impact:** Prevents contract drift. ~1-2 days effort.

### MEDIUM: Broad catch(Exception) Everywhere

27+ catch(Exception) blocks across C# and Python swallow errors or re-expose generically. Error context is lost, making debugging hard.

**Decision:** Prioritize specific exception catches:
- Catch `FileNotFoundException`, `InvalidOperationException` individually
- Log with context (document ID, operation)
- Re-throw with context or return structured error response

**Impact:** Improves debuggability. Medium-term refactoring.

### MEDIUM: Python Dependencies Unpinned

`requirements.txt` has no version pins. Builds are non-reproducible. Docling is especially volatile (heavy ML dependencies).

**Decision:** Pin all versions. Use `pip freeze` to generate reproducible requirements.

**Impact:** Enables reproducible builds. ~1 hour.

### Test Coverage Gap Matrix Priority

| Feature | Risk | Recommended Test Type |
|---------|------|----------------------|
| Chat feature | 🔴 HIGH | Unit (service logic) + Integration (E2E with Ollama mock) |
| File Upload | 🔴 HIGH | Unit (validation) + Integration (controller → storage) |
| Processing pipeline | 🔴 HIGH | Integration (Python DatabaseService → Neo4j) |
| Cross-service contracts | 🔴 HIGH | Contract tests (JSON serialization) |
| Python routes | 🔴 HIGH | Unit (TestClient) + mocked dependencies |
| Neo4j queries | Medium | Unit (mocked driver) + integration (real Neo4j) |

---

## Instructions Consolidation — Bob (Lead/Architect) — 2026-02-27

**Scope:** Merge project-specific context with Squad boilerplate into unified root instructions file

### Consolidate copilot-instructions.md

`.github/copilot-instructions.md` was replaced with 47-line Squad boilerplate, losing all project-specific context (architecture overview, day-one setup, troubleshooting, instruction lookup, repo map). Consolidated both versions into a single 167-line file that:

1. **Opens with team personas** — Bob, Jeff, Jarvis, Buster described as domain owners with distinct voices
2. **Restores operational context** — Quick Overview, Day-One Checklist, Build/Run/Test, Validation Before PR, Troubleshooting Cheatsheet, Repo Map
3. **Retains Squad conventions** — team context, capability self-check, branch naming, PR guidelines, decision inbox
4. **Updates all references** — .NET 10 SDK (from global.json), all 15 instruction files, all 12 prompt files

**Principles:** Personas first (set ownership/tone), reference not replicate (keep root file scannable), correct versions (synced with project reality), unified voice.

**Impact:** All squad members read updated file for current conventions. No instruction files modified; only root consolidation.

---

## DocumentPage FK Column Name Alignment — Jeff & Jarvis — 2025-11-02

**Scope:** P0 Item 2 — Resolve FK column name mismatch on `document_pages` table

### RESOLVED: DocumentPage FK Column Alignment

The `document_pages` table had conflicting column names across language boundaries:
- **Python (source of truth):** `file_id` INTEGER NOT NULL (referencing `files(id)`)
- **C# (EF Core):** `[Column("document_id")]` on `FileId` property

This created a data integrity risk: C# and Python would disagree on the actual column name in the database.

**Decision:** Aligned to canonical `file_id` (Python-defined, semantically correct).

**Implementation:**
- **Jeff (C#):** Updated `DocumentEntities.cs` `[Column("document_id")]` → `[Column("file_id")]`. Updated `UploadDbContext.cs` index name. Build verified clean. Commit: 6e5b34b.
- **Jarvis (Python):** Updated `DocumentPage` Pydantic model, `fix_database.py`, `diagnose_database.py`, `README.md` schema docs. Commit: 77db074.

**Impact:** Fixed schema alignment. P0 Item 2 closed. No more C#↔Python column name conflicts on `document_pages`.

---

## Processing Pipeline Retry & Lifecycle Canonicalization — Jarvis & Buster — 2026-03-25

**Scope:** P1 Item 1 — Stabilize canonical uploaded to processing to processed/error lifecycle; prevent duplicate processing

### Processing Retry Reset (Jarvis Decision)

When a file enters processing, the Python service now resets stale processing artifacts in the same lifecycle step. That includes clearing completion/error fields, clearing docling/Neo4j output columns, and deleting any existing document_pages rows for the file.

**Rationale:** Retries were not safe if a previous attempt partially persisted output before failing. The canonical schema enforces UNIQUE(file_id, page_number), so leaving old page rows behind turns recovery attempts into duplicate-write failures instead of a clean retry.

**Impact:**
- Keeps lifecycle transitions canonical: uploaded to processing to processed / error
- Allows failed rows to re-enter batch processing without manual cleanup
- Preserves processed rows unless an explicit new processing attempt is started

### Processing Retries Stay Canonical (Buster Decision)

Failed files rows must be retry-eligible for the Python processing pipeline. The next processing transition must clear stale failure markers before work resumes.

**QA Expectations:**
- Canonical lifecycle stays uploaded to processing to processed / error
- Failed rows remain discoverable by list_unprocessed_documents()
- Retrying a failed row clears stale processing_error and processing_completed_at
- process_document_task() is covered for both success and failure status updates

---

## Document Ingestion Trigger Strategy — Bob, Jeff, Jarvis, Buster — 2026-03-26

**Scope:** P1 gap closure — Web upload does not trigger Python processing pipeline

### Architecture Decision

**Adopt two-phase approach:** UI button (Phase 1) now, auto-trigger (Phase 2) next.

**Phase 1 — Immediate (Jeff):** Add "Process" action per-row or batch "Process All" button on Upload Documents page.

**Phase 2 — Next sprint (Jeff):** After upload succeeds, fire-and-forget POST to Python service.

---

## LightRAG Integration Architecture — Bob, Jarvis, Buster — 2026-03-25

**Scope:** P1 spike review — clarify LightRAG auto-pickup assumption and integration boundary

### Architecture Decision

**Correct boundary is explicit Python → LightRAG API ingestion, not directory-watching.**

The Python processing pipeline should:
1. Export Docling output as markdown ✅
2. Copy that markdown into shared LightRAG input directory  
3. Explicitly trigger LightRAG ingestion via POST /documents/scan
4. If scan call fails, keep canonical document processing successful and record LightRAG handoff failure in metadata

---

## FlowEndToEnd Test — Regression Vector Requiring Immediate Rewrite — Buster & Team — 2026-03-25

**Scope:** P1 testing — End-to-end ingestion test coverage

The BasicAspireAppHostTests.FlowEndToEnd test passes but proves nothing about ingestion pipeline. It is a false-positive confidence issue.

**Solution:** Rewrite test to include processing trigger, status polling, and assertions on pages/Neo4j/markdown staging.

---

## Roadmap Status Tracking & Challenge Log — Bob — 2025-03-25

**What Changed:** Updated oadmap/Tasks.md to enforce status tracking and surface challenges

**Process Rule:** Roadmap edits should happen during/immediately after task completion, not retroactively.

---

--- ARCHIVE ENTRIES (2026-04-05 Postgres cutover + related) ---
## Postgres Cutover — Operational Data Migration — Bob — 2026-07-26

**Author:** Bob (Lead / Architect)  
**Status:** APPROVED — Ready for execution  
**Scope:** Replace SQLite shared-file pattern with Postgres for `files` and `document_pages` tables

### Context

The Web UI (C#/Blazor) and Python processing service currently share a single SQLite file (`data-resources.db`) via Docker bind mounts. This works but has caused recurring operational pain:

- WAL vs DELETE journal-mode conflicts across the Windows host / Linux container boundary
- Stale-read workarounds in Python (`_should_prefer_fresh_reads`, fresh connection fallbacks)
- Multi-candidate path resolution logic (8+ code paths to find the right `.db` file)
- `DeleteJournalModeInterceptor` hack in C# to force journal mode on every connection
- SQLite `CheckpointDatabaseAsync` calls after every write in FileStorageService
- Bind-mount file visibility issues between services

**Postgres is already provisioned in AppHost** (`builder.AddPostgres("postgres")` with `appdb` database, pgWeb, bind mount, user/pass parameters). Both services already `WaitFor(postgres)` and receive `POSTGRES_USER`/`POSTGRES_PASSWORD` environment variables. Neither service actually connects to Postgres yet.

### Decision

#### 1. Keep the same `files` + `document_pages` schema in Postgres

The schema is stable and well-documented in `docs/CROSS_SERVICE_CONTRACT.md`. Both sides agree on column names, types, and writer/reader ownership. No structural redesign needed.

**DDL changes (SQLite → Postgres):**

| SQLite | Postgres |
|--------|----------|
| `INTEGER PRIMARY KEY AUTOINCREMENT` | `SERIAL PRIMARY KEY` (or `GENERATED ALWAYS AS IDENTITY`) |
| `DATETIME` | `TIMESTAMPTZ` |
| `DEFAULT CURRENT_TIMESTAMP` | `DEFAULT NOW()` |
| `TEXT` (for JSON columns) | `JSONB` for `page_metadata`; `TEXT` for everything else |
| Placeholder `?` | Placeholder `%s` (psycopg2) |

**Indexes and constraints transfer directly.** The `UNIQUE(file_id, page_number)` and FK cascade behavior are standard SQL.

#### 2. C# Web Changes (Jeff owns)

| What | Action |
|------|--------|
| **NuGet packages** | Remove `Microsoft.EntityFrameworkCore.Sqlite`. Add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` |
| **Program.cs** | Replace `AddDbContext<UploadDbContext>(options.UseSqlite(...))` with `builder.AddNpgsqlDbContext<UploadDbContext>("appdb")`. Remove `ResolveSqliteConnectionString`, `GetSqliteDataSource`, `ShouldResolveAgainstContentRoot` helpers. Remove `DeleteJournalModeInterceptor` class entirely |
| **FileStorageService** | Delete `CheckpointDatabaseAsync()` and all calls to it. Remove `Microsoft.Data.Sqlite` import |
| **UploadDbContext** | Replace `HasDefaultValueSql("CURRENT_TIMESTAMP")` with `HasDefaultValueSql("NOW()")` in legacy entity config. Primary table config is attribute-driven and works cross-provider |
| **DocumentEntities.cs** | No changes needed — `[Column]` attributes are provider-agnostic |
| **AppHost.cs (webfrontend)** | Add `.WithReference(postgres)` to webfrontend. Remove `ConnectionStrings__DefaultConnection` env var (Aspire injects it via `WithReference`) |

#### 3. Python Changes (Jarvis owns)

| What | Action |
|------|--------|
| **requirements.txt** | Add `psycopg2-binary` (sync) or `psycopg[binary]` (async-capable). Remove: nothing (sqlite3 is stdlib) |
| **Dockerfile** | No change needed — `psycopg2-binary` has no native build deps |
| **DatabaseService class** | Replace `sqlite3` connection pool with `psycopg2.pool.ThreadedConnectionPool`. Remove `ConnectionPool` class. Remove all SQLite pragma logic. Remove multi-candidate path resolution. Remove fresh-connection workaround methods. SQL: `?` → `%s`, `AUTOINCREMENT` → `SERIAL`, add `RETURNING id` to inserts |
| **Connection config** | Read `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` from env. AppHost must pass these (see below) |
| **Schema init** | `_ensure_database_schema()` keeps `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS` — standard Postgres DDL. Remove `_ensure_required_columns` ALTER TABLE migration logic (fresh Postgres, no legacy schemas to heal) |

#### 4. AppHost.cs Changes (Jeff owns, but affects both)

**Add to Python service env vars:**
```
.WithEnvironment("POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
.WithEnvironment("POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
.WithEnvironment("POSTGRES_DB", "appdb")
```

Or, simpler: `.WithReference(postgres)` and read the Aspire-injected connection string. For a Dockerfile-based service the explicit env vars are cleaner since Python won't use Aspire service discovery natively.

**Remove from AppHost:**
- SQLite file setup block (lines 17-31): `sharedDatabaseFileName`, `sharedDatabaseFile`, `sharedDatabaseConnectionString`, `Directory.CreateDirectory`, `File.Create`
- `ASPIRE_DB_PATH` env var from Python service
- `/app/docs-database` bind mount from Python service (keep `/app/data` mount for file storage)
- `ConnectionStrings__DefaultConnection` env var from webfrontend

**Keep:** `sharedDatabasePath` directory creation and bind mount for the postgres data directory (already wired).

#### 5. Cross-Service Contract Update

`docs/CROSS_SERVICE_CONTRACT.md` section "Shared Database (SQLite)" becomes "Shared Database (PostgreSQL)". The table schema, status lifecycle, writer/reader ownership, and processing trigger contract all remain unchanged. Remove the journal-mode paragraph and path-resolution section.

### What This Eliminates

- `ConnectionPool` class (150+ lines of SQLite workarounds)
- `DeleteJournalModeInterceptor` class
- `CheckpointDatabaseAsync` method
- `_should_prefer_delete_journal` / `_should_prefer_fresh_reads` / `_fetch_*_from_fresh_connection` methods
- Multi-candidate database path resolution (~100 lines)
- SQLite pragma tuning (WAL, synchronous, mmap, cache_size, busy_timeout)
- All stale-read workarounds
- Journal-mode conflicts between host and container
- SQLite file creation at AppHost startup

**Net reduction:** ~400+ lines of SQLite-specific complexity across both services.

### Rationale

Postgres eliminates reliability issues inherent to bind-mounted SQLite files. Modern relational database handles concurrency, journal modes, and persistence correctly. Existing Aspire infrastructure already supports it.

### Impact

- Operational reliability: No more journal-mode conflicts or stale-read workarounds ✅
- Architectural clarity: Web and Python have separate, proper database connections via connection pooling ✅
- Code reduction: ~400+ lines eliminated across both services ✅
- Test infrastructure can now use proper test databases instead of in-memory SQLite ✅

---


--- ARCHIVE (2026-04-05) Postgres cutover + BRAIN pivot sections ---
## Postgres Cutover — Operational Data Migration — Bob — 2026-07-26

<!-- Decisions are appended below. Each entry starts with ### -->
## Web upload store Postgres cutover — Jeff — 2026-04-05

**Owner:** Jeff  
**Scope:** AspireApp.AppHost, AspireApp.Web, AspireApp.WebTest

### Decision

The Web operational upload store now uses the Aspire-managed PostgreSQL database resource exposed as `DefaultConnection`. AppHost injects that connection via `.WithReference(postgres)`, and the Web app resolves it through the existing `GetConnectionString("appdb")` path.

### Rationale

This keeps the Web-side cutover surgical: the upload API, EF context, and file-storage service keep their existing shape while the backing store switches from SQLite to Postgres. For this phase we intentionally kept Python's legacy configuration in AppHost so the current Python service startup path is not broken while Jarvis finishes the Python-side Postgres adoption.

### Impact

- Upload metadata is now written to Postgres instead of the shared SQLite file ✅
- SQLite-only Web concerns (path resolution, journal-mode interceptor, WAL checkpointing) are removed ✅
- WebTest now has a focused regression that uploads through the Web API and verifies the `files` row lands in Postgres ✅

---

## Python Postgres Upload Store Cutover — Jarvis — 2026-04-05

**Scope:** Python FastAPI operational document store (`files` + `document_pages`) shared with the Web upload flow

### Decision

The Python service should treat PostgreSQL as the source of truth for upload lifecycle state and extracted page rows. It should preserve the existing table and column contract used by the Web project instead of introducing Python-specific schema variants.

### Implementation notes

1. Resolve the operational store from connection-string-first configuration (`ASPIRE_DB_CONNECTION_STRING`, `POSTGRES_CONNECTION_STRING`, `DATABASE_URL`) with `POSTGRES_*` environment variables as the fallback contract.
2. Keep Python writes on the same canonical tables and columns:
   - `files` for lifecycle + processing metadata
   - `document_pages` for extracted page content
3. Keep `document_pages` uniqueness on `(file_id, page_number)` so retries and upserts stay deterministic.
4. Validate locally with fake pooled Postgres connections in Python tests rather than relying on a live database for every regression run.

### Rationale

The Web UI uploads documents that Python later processes, so both services need one shared operational schema. Holding the line on the existing `files` / `document_pages` contract reduces cross-service churn while allowing the runtime storage engine to move from SQLite to PostgreSQL.

### Impact

- Python no longer depends on SQLite journaling, file paths, or PRAGMA-based startup repair ✅
- Aspire now passes explicit Postgres connection settings to the Python service ✅
- Follow-up work on the Web side can switch providers without changing the Python-side table contract again ✅

---

## Shared Postgres Contract Audit Uses AppHost-Derived Store Name — Jarvis — 2026-04-05

**Scope:** Python regression coverage for the shared Postgres upload store

### Context

Eric updated `src/AspireApp.AppHost/AppHost.cs` so Aspire now loads with the correct Postgres connection-string wiring. Python manual verification still worked, but `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` was failing because it hardcoded an older database name literal (`DefaultConnection`) instead of validating the active shared-store contract.

### Decision

Cross-service Postgres contract audits should derive the upload-store database name from AppHost source and then verify that:

1. AppHost references that store from dependent services,
2. Python receives the same name through `POSTGRES_DATABASE`, and
3. Web resolves the same name through `GetConnectionString(...)`.

### Rationale

The durable contract is "all three surfaces point at the same named Postgres upload store," not a specific historical connection-string name. Deriving the name from AppHost keeps the test sensitive to real drift while avoiding false failures when the store is legitimately renamed during infrastructure fixes.

### Impact

- Python contract tests now flag real contract mismatches instead of stale literals ✅
- AppHost naming fixes remain verifiable without touching Python runtime code ✅
- The shared `files` / `document_pages` schema audit remains the primary Python-side proof surface ✅

---

## Buster Regression Verdict — 2026-04-05

**Scope:** Postgres upload-store regression checks spanning AppHost, Web, WebTest, and Python contract audit

### Context

Eric updated the AppHost/Web connection behavior so the app would start and manual upload + Python document API checks worked again. The automated gate then failed, so QA needed to determine whether the break was in product code, the harness, or stale assumptions left behind by the Postgres cutover.

### Decision

Treat this as a **test/harness regression**, not a product rollback:

1. The live runtime contract uses the Aspire-managed Postgres database name `appdb`.
2. Regression tests must assert **alignment** across AppHost, Web, and Python, not a hardcoded legacy name like `DefaultConnection`.
3. WebTest fixture validation must read `ConnectionStrings__appdb` and require `POSTGRES_DATABASE=appdb`.
4. A locked `AspireApp.WebTest.exe` process is an execution-environment failure mode, not application evidence; clear the stale process before rerunning WebTest.

### Rationale

Manual behavior and source inspection both pointed to a consistent runtime: AppHost registers `appdb`, Web reads `GetConnectionString("appdb")`, and Python receives `POSTGRES_DATABASE=appdb`. The red tests were rejecting that valid state because they encoded the old database name directly.

### Impact

- Python contract audit reflects the current Postgres contract again ✅
- WebTest fixture no longer blocks a correct AppHost/Web/Python startup because of a stale connection-string key ✅
- Future renames stay testable if the contract test continues deriving the DB name from AppHost rather than hardcoding it ✅

---

## BRAIN Pivot — Kujan Architecture Review — 2026-07-15

**Agent:** Kujan (Adversarial Architect Reviewer)  
**Scope:** BRAIN pivot viability — all six roadmap/planning documents vs. actual implementation  
**Date:** 2026-07-15

### Executive Summary

The AspireAI codebase is a well-orchestrated document processing pipeline masquerading as an agentic AI platform in its planning documents. The gap between the BRAIN specification (6 independent service layers with structured contracts, multi-agent reasoning, confidence scoring, and domain extensibility) and the actual implementation (a Blazor chat UI + a Python monolith that writes document pages to Neo4j) is **structural, not incremental**. The current architecture can serve as infrastructure scaffolding (Aspire orchestration, Dockerized services, Neo4j/Ollama containers), but the service boundaries, data model, and inter-service communication patterns all need to be redesigned. The most critical finding: three of the six BRAIN layers (Validation, Reasoning, Application) have zero implementation and zero infrastructure to support them. The LightRAG integration, which consumed significant effort, is architecturally opposed to BRAIN's requirement for transparent, controllable knowledge construction.

### Key Findings

**BRAIN layers with zero implementation:**
1. **Validation Layer (Truth Engine)** — No claim extraction, confidence scoring, evidence references, or contradiction detection
2. **Reasoning Layer (Agent System)** — No agent infrastructure; Semantic Kernel only used for basic chat
3. **Application Layer** — No domain abstraction; hardcoded to one workflow (upload → parse → store)

**What can be reused:**
- Aspire AppHost orchestration (solid, extensible)
- Dockerized Neo4j + Ollama container infrastructure
- Docling parsing (core for Ingestion Layer)
- SQLite operational schema (for pipeline state, not knowledge store)
- Health check patterns

**What is wasted or misaligned:**
- LightRAG integration is architecturally opposed to BRAIN (opaque, uncontrollable knowledge construction)
- Neo4j Document→Page graph doesn't serve BRAIN's entity/claim/concept model
- Phase 4-5 (Flat RAG, LightRAG/GraphRAG) are superseded by BRAIN layers

### Critical Gaps

| BRAIN Layer | Current Implementation | Coverage |
|-------------|----------------------|----------|
| **Ingestion** | Docling parsing + markdown export | ~40% — file-based ingestion works, but no connector architecture |
| **Knowledge** | Neo4j Document→Page graph + LightRAG | ~25% — graph store exists but schema is wrong for BRAIN |
| **Validation** | None | **0%** |
| **Reasoning** | None | **0%** |
| **Application** | None | **0%** |
| **Interface** | Blazor chat + FastAPI endpoints | ~30% — chat UI exists but no API gateway, no response contracts |

### Recommendations

**Do Now (Before Writing New Code):**
1. Define BRAIN core contracts — Create `CanonicalDocument`, `Claim`, `ValidatedDocument`, `KnowledgeResult`, `ReasoningStep`
2. Repurpose ApiService as Interface Service — Delete weather stub, wire as BRAIN API gateway
3. Choose and add a vector store — Add Qdrant or similar to AppHost

**Do Next (First Vertical Slice):**
4. Extract Knowledge Service — Move Neo4j/RAG logic into dedicated service
5. Build minimal Validation Service — LLM-based claim extraction + confidence scoring
6. Wire Semantic Kernel for agent orchestration

**Stop:**
7. LightRAG integration work — Not aligned with BRAIN's need for transparent knowledge construction
8. Phase 4/5/6 as currently scoped — These are superseded by BRAIN layer architecture

### Critical Questions for Eric

1. Agent framework choice (Semantic Kernel vs. LangGraph)?
2. LightRAG disposition (keeper, fallback, or deprecated)?
3. Vector store selection (Qdrant, Chroma, Neo4j indexes, pgvector)?
4. Multi-tenant timeline (Phase 1 or later)?
5. Python service decomposition (split now or keep monolith)?
6. Confidence scoring strategy (LLM-based, heuristic, or cross-reference)?
7. Which domain for first vertical slice (QA intelligence recommended)?

### Decision

**BRAIN pivot is viable but requires architectural redesign, not incremental feature addition.** The Aspire orchestration layer, container infrastructure, and Docling parsing are reusable foundations. The Python monolith needs decomposition, the Neo4j schema needs extension (not replacement), a vector store must be added, and three entirely new service layers (Validation, Reasoning, Application) must be built from scratch. LightRAG should be deprecated as a primary integration path because it's architecturally opposed to BRAIN's requirement for transparent knowledge construction with validation interception.

### Impact

- Roadmap (`Plan.md`) needs rewrite — current Phases 4-8 are superseded ✅
- LightRAG integration effort is sunk cost — keep for reference, don't extend ✅
- ApiService gets repurposed — no longer vestigial ✅
- New contracts directory needed before implementation begins ✅
- Three new services needed — Validation, Reasoning, Application ✅

---

## Tenant Context UI Slice — Data Layer and API Contract — Bob, Jarvis, Kujan, Buster — 2026-04-05

**Authors:** Bob (Lead/Architect), Jarvis (Python/Data Dev), Kujan (Adversarial Architect), Buster (QA/Tester)  
**Status:** APPROVED — Data layer complete, ready for UI phase  
**Scope:** Multi-tenant foundation for BRAIN Phase 1; establish tenant_id in schema, API, and service layer

### Context

The BRAIN roadmap (Plan.md line 97) requires all contracts to include `tenant_id` for multi-tenant isolation. Jeff's initial tenant-context implementation was rejected because FileUploadController signatures changed without matching FileStorageService updates, creating a coherence gap that broke the build. Subsequent revisions addressed schema synchronization, contract validation, and operational testing. Final approval by Buster closes the data layer and API contract for the next UI phase.

### Decision

#### 1. Tenant Schema Pattern (Bob, Jarvis)

**Column definition (both C# and Python):**
```sql
tenant_id TEXT NOT NULL DEFAULT 'default'
```

**Indexes:**
- `idx_files_tenant` (single column) for tenant-scoped full queries
- `idx_files_tenant_status` (composite on tenant_id, status) for filtered queries

**Rationale:** Allows multi-tenant file isolation without schema redesign. DEFAULT 'default' provides backward compatibility for existing clients. Composite index optimizes common query pattern (files by tenant and processing status).

#### 2. API Contract (Bob)

**Header-based tenant selection:**
```
X-Tenant-Id: <tenant_id>
```

**Extraction logic (FileUploadController.GetTenantId()):**
- Read `X-Tenant-Id` header from request
- Default to `"default"` if not provided
- Pass to FileStorageService methods

**Service signatures:**
- `AddFileAsync(string filename, Stream content, string tenantId)`
- `AddUrlAsync(string url, string tenantId)`
- `GetAllFilesAsync(string? tenantId)` — returns all if tenant is null (backward compatible)

**Rationale:** Header-based selection keeps Web/Python separation clean. Both services read the same header and persist/query by tenant_id. Optional parameter allows backward-compatible null filtering.

#### 3. Python Schema Alignment (Jarvis, Kujan)

**Changes to DatabaseService:**
- Added `tenant_id` to `_files_column_definitions` (line 88)
- Added `tenant_id` to CREATE TABLE statement (line 235)
- Added `tenant_id` to INSERT placeholders in `create_file_record()`
- Added `tenant_id` to all SELECT projections (`_fetch_file_row`, `_fetch_all_file_rows`, `_fetch_unprocessed_file_rows`)
- Added two indexes in `_ensure_database_schema()` (lines 263-264)
- Updated `_row_to_file_dict()` tuple mapping to include tenant_id at correct ordinal

**Rationale:** Explicit round-trip testing (write tenant_id, read it back) closes the contract audit gap where the column existed but was not actually persisted/retrieved.

#### 4. Contract Audit Validation (Kujan, Buster)

**Python test coverage:**
- `test_database_service_initializes_canonical_schema_and_indexes` — verifies tenant_id column and indexes exist
- `test_web_file_metadata_columns_match_python_projection` — asserts tenant_id alignment across Web/Python boundary
- Explicit round-trip: `create_file_record(tenant_id="test-tenant")` → `get_file_by_id()` → assertion on tenant_id value

**C# test coverage:**
- `OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres` — SELECT includes tenant_id column, assertion validates default tenant_id value

**Verification:** 8/8 Python contract tests pass. 1/1 C# operational test passes.

### Intentional Deferrals (Next UI Phase)

The following are explicitly **not** implemented in this slice (test scaffolding provided):

1. **Tenant selector UI** — NavMenu component for user tenant selection
2. **Session state** — Store selected tenant_id in browser session/cookie
3. **Frontend header propagation** — UploadData and Chat components must attach X-Tenant-Id header to API calls
4. **Multi-tenant duplicate detection** — Current file hash uniqueness is global; should scope to (tenant_id, file_hash)
5. **Tenant-aware delete** — Verify delete operations respect tenant boundary
6. **Python query filtering** — `get_unprocessed_files()` reads all files; should accept optional tenant_id parameter

**Scaffolding location:** `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs` lines 157-258 contain commented test templates showing expected coverage when UI components are implemented.

### Verdict

✅ **APPROVED** — Data layer and API contract are coherent and protected by validation. All tests pass. The missing UI selector is the expected next phase, not a blocking defect in this slice.

**What this slice accomplishes:**
- Schema stability: tenant_id column with NOT NULL + default constraint
- Index support: Query optimization for tenant-scoped and composite queries
- API contract: Header extraction with "default" fallback
- Service layer: Both C# and Python accept and persist tenant_id
- Query filtering: GetAllFilesAsync(tenantId) scopes results
- Cross-service validation: Contract audit confirms C#/Python alignment

**What remains for UI phase:**
- Tenant selection UI component
- Session state management
- Frontend header attachment
- Multi-tenant duplicate detection
- Tenant-aware delete operations
- Python processing pipeline tenant awareness

### Trade-Offs

**Chosen:** Minimal scope — only add tenant_id to schema and service layer without enforcing global filtering or tenant isolation in processing pipelines.

**Alternative (rejected):** Full tenant filtering across all queries. Rejected because broader scope (requires deciding on filtering strategy, row-level security policies) and can be layered incrementally.

---

## BRAIN Pivot — Key Decisions — Eric — 2026-07-15

**Status:** APPROVED

### Decisions

1. **Product Direction:** Pivot from chat-oriented RAG application to BRAIN — a domain-agnostic agentic knowledge assistant with proactive Jarvis-like behavior
2. **First Domain Slice:** Domain-agnostic knowledge coalescing engine with source-aware confidence scoring and proactive assistant personality (not a specific domain module)
3. **LightRAG Disposition:** Keep behind `IKnowledgeRetriever` abstraction. BRAIN contracts are primary. LightRAG is a pluggable retrieval backend, not the system of record
4. **Agent Framework Location:** Python for Reasoning/Validation/Knowledge layers. C# with Microsoft.Extensions.AI for Interface/Gateway/Aspire
5. **Multi-Tenancy Timing:** Design for tenancy from day 1 (tenant_id in all contracts). Implement isolation enforcement in Phase 6
6. **Vector Store:** Neo4j vector indexes (Neo4j 5.x). Swap to Qdrant later if needed, behind `IKnowledgeRetriever` abstraction
7. **Python Decomposition:** Internal packages first (`app/brain/ingestion/`, `app/brain/knowledge/`, etc.). Extract to separate Aspire services when contracts stabilize
8. **Breaking Changes Strategy:** Feature branch (`brain-pivot`). Merge to main when first agentic slice works end-to-end
9. **Proactive Behavior:** Core to MVP. Jarvis-like personality (suggesting, inferring, offering context) ships in Phase 3, not deferred

### Superseded Plans

- Plan.md Phases 4-8 (Flat Vector RAG, LightRAG/GraphRAG, Plugin Ecosystem, Testing/Deployment, Advanced Features)
- ApiService as vestigial weather stub (now: BRAIN API Gateway)
- Direct Blazor → Ollama chat path (now: Blazor → Gateway → Reasoning → Knowledge)

---

## BRAIN Pivot — Strategic Product Review — Verbal — 2026-07-15

**Scope:** Vision-roadmap alignment, scope risk assessment, MVP definition, prioritization

### Executive Summary

The current roadmap is still optimizing a document-chat product, not building BRAIN. `Plan.md` defines the product as a "configurable, modular Blazor-based chat assistant," while the BRAIN specs define a domain-agnostic cognition layer with explicit ingestion, knowledge, validation, reasoning, application, and interface layers. Those are not the same product, and the roadmap never resolves that conflict.

### Vision-Roadmap Misalignment

1. **Product definition inconsistency:** Plan.md frames as Blazor chat assistant; Architecture.md frames as retrieval-augmented chat; BRAIN specs frame as reusable cognition layer. These are different products.
2. **Phases don't lead to BRAIN:** Current sequence (Phase 3: ingestion → Phase 4: flat RAG → Phase 5: LightRAG → Phase 6: plugins) optimizes RAG application, not BRAIN layers. No dedicated phase for Validation, Reasoning, Application, or unified interface/gateway.
3. **Task breakdown confirms pre-BRAIN state:** No automatic upload → processing trigger, no chat retrieval + citations wired, LightRAG stability remains shaky.
4. **Architecture.md mixes incompatible ambition:** Tries to hold both pragmatic current system (upload → Docling → Neo4j → chat) and ambitious target system (tenants, claims, evidence, concepts, contradiction detection). No bridge between schemas.

### Scope Risk Assessment

| Risk | Severity | Mitigation |
|---|---|---|
| Product identity drift (chat app vs cognition layer) | Critical | Rewrite roadmap around BRAIN contracts and one domain slice |
| No first domain selected | Critical | Pick one slice now; QA intelligence is strongest |
| Platform-first overbuild | Critical | Build one orchestrated flow before additional layers |
| Multi-tenancy too early | High | Defer until after MVP proof |
| Direct UI → Ollama architecture persists | High | Insert BRAIN gateway/reason endpoint early |

### Recommended MVP: "Minimum Viable Agentic"

**Definition:** BRAIN is minimally viable when it can take a user goal, retrieve relevant evidence from ingested sources, produce a structured recommendation or plan, and explain both confidence and evidence.

**Concrete interaction flow:**
1. User uploads document or adds website URL
2. BRAIN normalizes both into canonical ingestion contract
3. User asks goal-oriented question
4. BRAIN runs single orchestrated flow: retrieve evidence → synthesize answer → run critic/self-check → return structured output
5. UI shows recommendation, evidence, confidence, unresolved questions

**Acceptance criteria:**
1. Two source types, one contract (file upload + URL ingestion)
2. One reasoning endpoint (`/reason`)
3. Evidence is mandatory on all responses
4. Confidence is explicit using transparent heuristic
5. One domain workflow works end to end
6. System honestly says "insufficient evidence" instead of improvising
7. One automated proof path exists end-to-end

### Recommended New Phase Sequence

**Phase 0 — Reframe the Product**
- Declare BRAIN as product core
- Declare chat as one interface, not the architecture
- Choose first domain slice
- Define non-goals for MVP

**Phase 1 — Define Core Contracts**
- CanonicalDocument, KnowledgeResult, ReasonResponse
- Evidence and confidence schema
- Correlation/trace contract

**Phase 2 — Unify Ingestion + Knowledge Baseline**
- File + URL through one ingestion contract
- Explicit processing trigger
- Stable retrieval backend behind one interface
- Status visibility and failure visibility

**Phase 3 — Ship Minimum Viable Agentic Slice**
- Implement `/reason`
- Support `answer` + `plan` modes
- Retrieve → synthesize → critic/self-check
- Return evidence, confidence, unresolved questions
- Single-tenant only

**Phase 4 — Evaluate and Harden**
- Automated end-to-end proof
- Latency and quality baselines
- Honest insufficient-evidence behavior
- Operational observability

**Phase 5 — Prove Reusability**
- Add second connector OR second domain module
- Keep same contracts
- Decide if graph-specific enrichment materially improves outcomes

**Phase 6 — Scale Deliberately**
- Multi-tenancy, auth/access control
- Deployment hardening, plugin ecosystem
- Advanced graph reasoning, long-term memory

### What Should Be Cut From Near-Term Plan

- **Cut** the old product framing: "Blazor-based chat assistant"
- **Cut** separate product phases for Flat RAG then GraphRAG
- **Cut** plugin ecosystem as a near-term phase
- **Cut** multi-tenant RAG from the pivot-critical path
- **Cut** any assumption that chat polish proves BRAIN

### Impact

- Roadmap clarity: BRAIN is the product, not RAG optimization ✅
- Scope discipline: MVP focuses on one evidence-backed agentic loop ✅
- First domain selection unlocks concrete success criteria ✅
- Multi-tenancy deferred until product thesis is proven ✅
- Vertical slice approach prevents platform-first overbuild ✅

---

## Python Test Discovery & Smoke Gate — Buster — 2026-04-05

**Author:** Buster (QA / Tester)
**Status:** APPROVED
**Date:** 2026-04-05

# Buster — Python test discovery and smoke gate alignment — 2026-04-05

## Context

Visual Studio's Python workflow for `src\AspireApp.PythonServices` is driven by `AspireApp.PythonServices.pyproj`, not just by filesystem pytest discovery. That left `tests\test_p0_contract_audit.py` and `tests\test_processing_pipeline_regression.py` outside the VS test container even though `python -m pytest` from the project directory collected them normally. There was also a misleading utility script, `test_build_config.py`, exposing a `test_*` function that could trigger Docker builds during automated discovery, and bootstrap paths creating `.venv` needed the Postgres client bits used by the smoke gate.

## Decision

1. Treat `AspireApp.PythonServices.pyproj` as part of the Python QA harness and keep regression tests explicitly listed there when they must run in Visual Studio.
2. Keep utility scripts out of automated discovery by avoiding `test_*` function names unless the file is a real automated test.
3. Ensure local `.venv` bootstrap installs the packages required by the smoke gate, including `psycopg[binary]`, `psycopg-pool`, and `pytest`.

## Rationale

This keeps CLI pytest and Visual Studio Test Explorer aligned on what the real regression gate is. It also prevents false negatives from half-bootstrapped interpreters and false positives from utility scripts masquerading as tests.

## Impact

- Visual Studio can now see the contract audit and processing regression tests.
- The DatabaseService smoke gate has the dependencies it expects in the common local bootstrap path.
- Docker build diagnostics stay opt-in instead of executing as part of automated test discovery.



