# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2025-11-02):** Merged 5 inbox decisions from file-hash schema bug fix (Jarvis, Buster). Archived 2026-03-21 and earlier (9 decisions, ~8 KB) to `decisions-archive.md` to maintain ~20 KB target. Inbox cleared.

<!-- Decisions are appended below. Each entry starts with ### -->

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

**#1: Endpoint Reachability**
```csharp
var serviceInfoResponse = await pythonClient.GetAsync("/processing/service-info");
Assert.Equal(200, serviceInfoResponse.StatusCode);
```
Verifies Python service online, routes registered.

**#2: POST Accepts Real Work**
```csharp
var processResponse = await pythonClient.PostAsync($"/processing/process-document/{documentId}", null);
Assert.Equal(200, processResponse.StatusCode);
var responseBody = await processResponse.Content.ReadAsStringAsync();
Assert.Contains("Processing started", responseBody);
```
Proves POST endpoint callable and accepts document ID.

**#3: Status Reflects Processing Progress**
```csharp
var statusResponse = await pythonClient.GetAsync($"/processing/status/{documentId}");
var status = JsonSerializer.Deserialize<ProcessingStatus>(await statusResponse.Content.ReadAsStringAsync());
Assert.Equal("processing", status.Status, ignoreCase: true);

// Poll until terminal state (10 iterations, 1s delay each)
for (int i = 0; i < 10; i++) {
    await Task.Delay(1000);
    statusResponse = await pythonClient.GetAsync($"/processing/status/{documentId}");
    status = JsonSerializer.Deserialize<ProcessingStatus>(await statusResponse.Content.ReadAsStringAsync());
    if (status.Status == "processed" || status.Status == "error") break;
}

Assert.True(status.Status == "processed" || status.Status == "error", 
    $"Processing did not complete; final status: {status.Status}, error: {status.ErrorMessage}");
if (status.Status == "processed") {
    Assert.True(status.TotalPages > 0, "Processed document should have extracted pages");
}
```
Proves background task runs, status transitions, database persists.

**#4: Loud Failure on Contract/Work Break**
Contract breaks (`JsonSerializationException`), missing endpoints (HTTP 404), background failures (status `error` with message), database crashes (HTTP 500) all fail test explicitly.

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

### Implementation Notes
1. **HttpClient Setup:** Reuse `TestFixture.AppHostMapping.PythonServiceUri`
2. **Document ID Extraction:** Parse from UI interaction or file table after upload
3. **Polling:** 10 iterations × 1s = 10s total timeout (sufficient for test PDF)
4. **Error Context:** Include final `status.ErrorMessage` in assertion failure for debugging
5. **Async:** Proper `await` for all HTTP calls and delays

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
3. Platform-specific defaults:
   - **Container:** `/app/docs-database/` → `/app/database/` → repo/database → cwd/database
   - **Local (Windows):** repo/database → cwd/database → `/app/docs-database/` → `/app/database/`

Service tries each candidate in order until initialization succeeds. Path source is logged and stored in `db_path_source` attribute.

#### Startup Error Diagnostics
When database initialization fails, `_format_initialization_failure()` generates comprehensive diagnostic message:
- Database path attempted
- Path source (e.g., "ASPIRE_DB_PATH", "repository", "cwd")
- Exception type and message
- Schema diagnostics via `_collect_schema_diagnostics()`: existing tables, `files` column names, missing canonical columns, "incompatible legacy schema" label

#### "Legacy Schema" Concept
- No separate "legacy path" detection—all paths treated equally
- "Legacy" refers to schema shape, not file location
- `_collect_schema_diagnostics()` reports "incompatible legacy schema" when required columns missing from `files` table
- Self-healing via `_ensure_required_columns()` adds missing columns at startup

### Decision
**Affirm test scenario:** `test_legacy_schema_startup_failure_reports_path_and_cause` validates edge case diagnostics when self-healing is unavailable. Test should remain active to ensure startup failures provide actionable debugging information (path, source, schema details, SQLite error).

### Rationale
- Production code self-heals missing columns in normal operation
- Test validates fallback diagnostic path when self-healing fails
- Comprehensive error reporting enables faster debugging of schema incompatibilities
- Database path and schema details are essential for multi-environment troubleshooting

### Impact
- Test remains in test suite as regression protection for startup diagnostics ✅
- No code changes required to DatabaseService ✅
- Buster can assess test coverage confidence knowing current behavior ✅

---

## Legacy Schema Test Update — Buster — 2026-03-27

**Scope:** Python `DatabaseStartupPathAuditTests.test_legacy_schema_startup_failure_reports_path_and_cause`

### Context
Test was failing after multi-candidate database initialization refactor. Service works correctly in manual testing, but test needed assessment to determine if it should be updated or removed.

### Root Cause
Multi-candidate database initialization refactor changed exception chaining depth:

**Before refactor:**
```
_ensure_database_schema raises RuntimeError from sqlite3.OperationalError
  → Exception chain: RuntimeError → OperationalError
```

**After refactor:**
```
_initialize_database catches exception from _ensure_database_schema, then raises RuntimeError
  → Exception chain: RuntimeError → RuntimeError → OperationalError
```

The behavior being tested (legacy schema detection and detailed error reporting) **still exists and works correctly**. Only the depth of exception chaining changed.

### Decision
**UPDATE THE TEST** to traverse the exception chain rather than checking only the immediate cause.

**Changed from:**
```python
self.assertIsInstance(context.exception.__cause__, sqlite3.OperationalError)
```

**Changed to:**
```python
# Walk the exception chain to find OperationalError at root
root_cause = context.exception.__cause__
while root_cause and not isinstance(root_cause, sqlite3.OperationalError):
    root_cause = root_cause.__cause__
self.assertIsInstance(root_cause, sqlite3.OperationalError,
                      "Expected sqlite3.OperationalError in exception chain")
```

### Rationale
- The scenario being tested (legacy schema startup failure with detailed diagnostics) remains valid
- The error reporting behavior works correctly (verified in test output)
- The multi-candidate retry pattern is a deliberate architectural improvement
- Walking the exception chain is more robust than assuming single-level chaining
- Alternative (removing test) would lose valuable regression coverage for schema diagnostics

### Impact
- ✅ Test now passes and correctly verifies legacy schema detection
- ✅ Test validates all expected error message content (path, column names, diagnostics)
- ✅ Test validates that root cause is still OperationalError
- ✅ All 10 tests in test_p0_contract_audit.py pass
- ✅ All 30 Python tests pass
- ✅ More resilient to future exception handling refactors

### Verification
```powershell
cd src\AspireApp.PythonServices
python -m pytest tests/test_p0_contract_audit.py -v
# Result: 10 passed

python -m pytest tests/ -v
# Result: 30 passed
```

---

## Optional Docling Smoke Coverage — Jarvis — 2026-03-28

**Scope:** Python smoke tests for document processing initialization

### Context
`src/AspireApp.PythonServices\requirements.txt` intentionally omits the heavyweight `docling` package, while `src/AspireApp.PythonServices\Dockerfile` installs it only for the full image. The old smoke test imported `app.services.docling_service` directly, so lightweight/dev environments reported the absence of `docling` instead of validating the supported fallback path.

### Decision
Smoke tests should validate `app.services.service_factory` and the selected `DoclingService` implementation, not direct `docling` package availability. The test should pass when the factory selects either the full processor or `docling_service_fallback`, and only fail when neither supported processing path can initialize.

### Rationale
- Matches the runtime contract used by `processing.py` and FastAPI health reporting
- Preserves lightweight developer environments without forcing the heavy `docling` install
- Still surfaces real regressions by asserting which implementation the factory selected

### Impact
- `test_services.py` stays meaningful in both full and lightweight environments ✅
- Future changes to optional dependency handling have a clear test target ✅
- Avoids unnecessary dependency bloat in `requirements.txt` ✅

---

## Docling Smoke Gate Alignment — Buster — 2026-03-28

**Scope:** Python service smoke validation for Docling-capable and fallback-capable environments

### Context
The failing smoke signal was `Optional dependency 'docling' is not installed: No module named 'docling'` from `src/AspireApp.PythonServices/test_services.py`. Audit showed this was reproducible in the project `.venv`, because `setup_dev_env.py` installs `requirements.txt`, and `requirements.txt` intentionally omits the top-level `docling` package while lightweight/fallback processing remains a supported development mode.

### Decision
Treat `app.services.service_factory` as the smoke-test contract for document processing. The smoke gate should pass when the current environment can initialize either:

1. the full Docling service, or
2. the fallback processor selected by the factory.

Direct import of `app.services.docling_service` is not the default smoke gate because that incorrectly fails supported lightweight setups.

### Rationale
- The product contract already supports fallback processing through `service_factory.py` and `docling_service_fallback.py`
- `BUILD_CONFIGURATION.md` and `README.md` document lightweight development as valid, so the smoke test must reflect that supported runtime
- A smoke test that only passes with the optional full package installed produces false negatives in the standard local dev environment

### Impact
- Default local `.venv` smoke validation now passes without requiring a heavyweight `docling` install ✅
- Full Docling environments still pass and are detected as `service_type = full` ✅
- Regression coverage now proves the factory selects the implementation that matches the installed dependency set ✅

---
