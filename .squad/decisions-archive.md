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
