# AspireAI Architecture Review & Stabilization Plan

**Status:** CRITICAL — Multiple blockers prevent Phase 3 stabilization. Processing pipeline broken by contract misalignment.  
**Last Updated:** 2026-02-21  
**Owner:** Keaton (Lead/Architect) + Squad  
**Coordination:** Testing plan in PLAN.md (owned by Hockney); this document is architecture + code fixes.

---

## Executive Summary

AspireAI has a **sound orchestration layer** (Aspire AppHost) and **production-ready UI** (Blazor chat), but **critical blockers prevent document processing pipeline from working:**

1. **~10 missing DatabaseService methods** in Python routers cause `AttributeError` on most endpoints
2. **Status casing mismatch** (`"Uploaded"` vs `"uploaded"`) prevents file discovery  
3. **Method signature mismatch** on `save_document_page()` crashes processing
4. **FK column name conflict** on `document_pages` creates data integrity risk
5. **Zero automated tests** make refactoring unsafe

**Unblocking Gate B1/B2 requires ~2 days of coordinated code fixes + 2 days of test infrastructure bootstrap.** High-priority infrastructure tasks (ApiService removal, LightRAG dependency cleanup) add ~1.5 days.

---

## Architecture Assessment

### Strengths
- **Aspire Orchestration:** AppHost.cs properly wires 6 services with health checks, WaitFor ordering, environment variable propagation
- **Canonical Schema:** `files` + `document_pages` tables well-defined and shared between C# (EF Core) and Python (raw SQL)
- **Volume Strategy:** Bind mounts expose uploaded files to Python container at runtime for processing
- **UI Foundation:** Blazor chat component with speech I/O is production-ready and properly integrated
- **Configuration Management:** Proper use of Aspire parameters, typed options, environment variables

### Critical Gaps & Risks

| Layer | Issue | Risk | Owner | Fix Time |
|-------|-------|------|-------|----------|
| **Python Contracts** | ~10 missing DatabaseService methods called by routers | 🔴 CRITICAL | McManus | 1 day |
| **Status Casing** | Web writes `"Uploaded"`, Python queries `"uploaded"` — files won't be discovered | 🔴 CRITICAL | Fenster+McManus | 30 min |
| **Method Signature** | `save_document_page(page_obj)` vs `save_document_page(file_id, ...)` | 🔴 CRITICAL | McManus | 1 hour |
| **FK Column Conflict** | Python: `file_id` vs C#: `document_id` on `document_pages` | 🔴 CRITICAL | Fenster+McManus | 2 hours |
| **Testing** | Zero automated tests; CI non-functional | 🔴 CRITICAL | Hockney | 2 days (bootstrap) |
| **ApiService Vestigial** | Only weather stub; adds 500ms startup latency for zero value | 🟡 HIGH | Fenster | 1 day (removal) |
| **LightRAG Blocker** | Wired but unused; no code calls APIs; `WaitFor()` blocks startup | 🟡 HIGH | Keaton | 1 hour |
| **Config Key Mismatch** | AppHost: `AI-Chat-Model` vs Web services: `AI-Model` | 🟡 HIGH | Fenster | 30 min |
| **Logging** | 35+ `Console.WriteLine` instead of `ILogger<T>` | 🟡 MEDIUM | Fenster | 3 hours |
| **Python Deps Unpinned** | `requirements.txt` has no version pins; non-reproducible builds | 🟡 MEDIUM | McManus | 1 hour |
| **SK Version Skew** | Core 1.71.0 vs Ollama connector 1.68.0-alpha | 🟠 MEDIUM | Fenster | 30 min |
| **Legacy Entities** | Dead C# EF classes reference non-existent tables | 🟠 LOW | Fenster | 2 hours |
| **Duplicate Classes** | Two `ServiceDiscoveryUtilities` classes, different behavior | 🟠 LOW | Fenster | 1 hour |

---

## Gate B1/B2 Blockers — Unblock Processing Pipeline (1-2 Days)

### ✅ Must Complete in Order

#### 1. Fix Python Router → DatabaseService Contract Mismatch
**Impact:** Processing pipeline completely broken; most Python endpoints fail at runtime with `AttributeError`  
**Owner:** McManus  
**Files:** 
- `src/AspireApp.PythonServices/app/routers/` (documents.py, processing.py, rag.py, health.py)
- `src/AspireApp.PythonServices/app/services/database_service.py`

**Current state:**
Routers call methods that don't exist:
- `get_document()`, `get_unprocessed_documents()`, `get_documents_by_status()`
- `get_processed_document()`, `save_processed_document()`
- `get_statistics()`, `get_active_services()`, `get_file_document_sync_status()`, `force_sync_files_and_documents()`

**Decision:** Rewrite routers to call existing DatabaseService methods (Option A — cleaner, aligns with "minimal Python footprint")

**Changes:**
1. Replace `db.get_document(id)` → `db.get_file_by_id(id)` (return File instead of Document)
2. Replace `db.get_unprocessed_documents()` → `db.get_unprocessed_files()`
3. Update response models to use File/DocumentPage terminology
4. Grep all routers to verify only existing methods called

**Acceptance:** All Python endpoints callable without `AttributeError`; health check returns 200/OK

**Related:** Testing task T2 in PLAN.md validates this fix

---

#### 2. Fix Status Casing Mismatch (`"Uploaded"` vs `"uploaded"`)
**Impact:** Files uploaded via Web UI never discovered by Python processing; entire pipeline stalls  
**Owner:** Fenster + McManus  
**Files:** 
- `src/AspireApp.Web/Controllers/FileUploadController.cs` (line 123)
- `src/AspireApp.PythonServices/app/services/database_service.py` (query)

**Current state:**
- Web writes: `"Uploaded"` (capital U)
- Python queries: `WHERE status = 'uploaded'` (lowercase)

**Changes:**
1. **Web:** Change line 123 from `"Uploaded"` → `"uploaded"`
2. **Python (defensive):** Update query to `WHERE lower(status) = 'uploaded'` to handle old data gracefully
3. Verify lifecycle values normalized: `uploaded` → `processing` → `processed` (or `error`)

**Acceptance:** Web UI upload creates row with `status='uploaded'` (lowercase); Python query finds it

**Related:** Testing task T4 in PLAN.md validates this fix

---

#### 3. Fix `save_document_page()` Signature Mismatch
**Impact:** Processing crashes when trying to persist extracted pages to `document_pages`  
**Owner:** McManus  
**Files:**
- `src/AspireApp.PythonServices/app/routers/processing.py` (line 75+)
- `src/AspireApp.PythonServices/app/services/database_service.py`

**Current state:**
```python
# Actual signature
def save_document_page(self, file_id, page_number, content, metadata, neo4j_node_id)

# Called as (WRONG)
db.save_document_page(page_record)  # Passing DocumentPage object
```

**Changes:** Update processing.py to pass individual arguments:
```python
db.save_document_page(file_id, page_number, content, metadata, neo4j_node_id)
```

**Acceptance:** Processing persists page rows without exception

---

#### 4. Fix FK Column Name Conflict on `document_pages`
**Impact:** Whichever service creates table first determines column name; the other reads wrong column or fails  
**Owner:** Fenster + McManus  
**Files:**
- `src/AspireApp.Web/Data/DocumentEntities.cs`
- `src/AspireApp.PythonServices/app/services/database_service.py`

**Current conflict:**
| Component | Column Name |
|-----------|-------------|
| Python CREATE TABLE | `file_id` |
| C# EF [Column] attribute | `document_id` |

**Decision:** Canonicalize to `file_id` (matches FK semantics: foreign key to `files` table)

**Changes:**
1. Update C# DocumentPage entity: Change `[Column("document_id")]` → `[Column("file_id")]`
2. Verify Python CREATE TABLE uses `file_id`
3. Audit both C# and Python queries use correct column name
4. Run migration to update existing tables if any

**Acceptance:** Both services can read/write `document_pages` without column mapping errors

**Related:** Integration tests in PLAN.md Phase 3 validate data integrity

---

### ✅ Supporting Fixes (Same Sprint)

#### 5. Pin Python Dependencies
**Impact:** Non-reproducible builds; upgrades (especially docling) can break processing pipeline  
**Owner:** McManus  
**Files:** `src/AspireApp.PythonServices/requirements.txt`

**Action:**
1. Generate pins from current environment: `pip freeze > requirements.txt`
2. Manually review and accept versions (or use lock file)

**Recommended pins (as of 2026-02):**
```
fastapi==0.104.1
uvicorn==0.24.0
neo4j==5.14.0
docling-core==1.2.0
docling-ibm-models==1.2.0
pydantic==2.5.0
```

**Acceptance:** Reproducible Python builds; CI runs with pinned versions

---

#### 6. Align SemanticKernel Package Versions
**Impact:** Version skew between core (1.71.0) and Ollama connector (1.68.0-alpha) risks runtime errors  
**Owner:** Fenster  
**Files:** `src/AspireApp.Web/AspireApp.Web.csproj`

**Change:** Update Ollama connector to match core: `1.71.0`

**Acceptance:** All SK packages on same minor version; no compatibility warnings

---

## HIGH Priority — Stabilize Orchestration (1-2 Days)

### 7. Decide & Implement ApiService Fate
**Impact:** 500ms startup latency; orchestration complexity for zero business value  
**Owner:** Keaton + Fenster  
**Files:** `src/AspireApp.ApiService/`, `src/AspireApp.AppHost/AppHost.cs` (lines 19-20), `AspireApp.sln`

**Current state:** ApiService contains only weather forecast stub; no integration into document pipeline; Web talks directly to Python

**Decision:** **Remove entirely** (unblocks Phase 3, simplifies orchestration)

**Changes:**
1. Delete `src/AspireApp.ApiService/` folder
2. Remove from `AspireApp.sln` project list
3. Remove from `AppHost.cs` lines 19-20: `var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")`
4. Remove from any Web startup dependencies
5. Update README to document Web → Python direct communication

**If future API gateway needed:** Can be added as fresh service later, integrated into orchestration

**Acceptance:** 
- Solution builds after removal
- Aspire startup time improves by ~500ms
- No remaining code references removed project

---

### 8. Fix LightRAG Startup Blocking
**Impact:** Web frontend `WaitFor(lightrag)` blocks startup if LightRAG unhealthy; dev startup time suffers  
**Owner:** Keaton + Fenster  
**Files:** `src/AspireApp.AppHost/AppHost.cs` (LightRAG registration + webfrontend WaitFor)

**Current state:** LightRAG container registered, but **zero Python code calls LightRAG APIs**; wired but unconsumed

**Decision:** Remove from WaitFor chain until integration code exists (Python endpoints call LightRAG)

**Changes:**
1. Remove `.WaitFor(lightrag)` from webfrontend registration in AppHost
2. Keep LightRAG container registration but make it optional
3. Document LightRAG status in AppHost comments: "Pending Phase 5 integration"
4. Add task to backlog: "Wire Python → LightRAG APIs" (Phase 5)

**Acceptance:** 
- Web starts in <10s without LightRAG blocker
- LightRAG container still available if manually tested
- CI/CD startup time improves

---

### 9. Fix Config Key Mismatch: `AI-Chat-Model` vs `AI-Model`
**Impact:** Model name may not propagate correctly from AppHost to Web services  
**Owner:** Fenster  
**Files:** 
- `src/AspireApp.AppHost/AppHost.cs` (lines 14, 23)
- Web service configuration readers

**Current state:** AppHost passes `"AI-Chat-Model"` but Web services read `"AI-Model"`

**Decision:** Normalize to `AI-Model` across all services

**Changes:**
1. AppHost: Change `"AI-Chat-Model"` → `"AI-Model"` (lines 14, 23)
2. Verify Web services (HomeConfigurations.cs, AiInfoStateService.cs) read `"AI-Model"`
3. Update `appsettings.json` key if needed

**Acceptance:** Model name propagates through environment variables; chat uses correct model

---

## MEDIUM Priority — Improve Observability (2-3 Days)

### 10. Replace Console.WriteLine with ILogger<T>
**Impact:** Debug output bypasses structured logging; won't appear in Aspire dashboard  
**Owner:** Fenster  
**Scope:** Prioritize high-impact files first

**Files (high-impact):**
- `Chat.razor.cs` (35+ instances)
- `FileUploadController.cs`
- `OllamaWarmupService.cs`

**Action:**
1. Inject `ILogger<T>` into services/components
2. Replace `Console.WriteLine` with `logger.LogInformation/LogError/LogWarning`
3. Use structured logging templates: `logger.LogInformation("Processing {DocumentId}", docId);`

**Acceptance:** Aspire dashboard logs show application diagnostics; no Console.WriteLine in hot paths

**Related:** PLAN.md testing task T10 validates logging improvements

---

### 11. Clean Up Legacy Code
**Impact:** Dead code clutters schema understanding; maintenance hazard  
**Owner:** Fenster  
**Files:** `src/AspireApp.Web/Data/DocumentEntities.cs`

**Dead Entities:**
- `Document` class (mapped to non-existent `documents` table)
- `ProcessedDocument` class (mapped to non-existent `processed_documents` table)

**Action:**
1. Grep entire codebase for remaining references
2. Remove class definitions and associated EF migrations
3. Update DbContext if needed
4. Verify only canonical `File` and `DocumentPage` entities remain

**Acceptance:** Only canonical entities in schema; no confusion from dead code

---

### 12. Consolidate Duplicate ServiceDiscoveryUtilities
**Impact:** Maintenance hazard; two classes with same name, different behavior  
**Owner:** Fenster  
**Files:** 
- `src/AspireApp.Web/ServiceDiscoveryUtilities.cs`
- `src/AspireApp.Web/Components/Pages/ServiceDiscoveryUtilities.cs`

**Action:**
1. Merge into single class in shared namespace
2. Verify both call sites (HomeConfigurations, AiInfoStateService) work after merge
3. Delete duplicate

**Acceptance:** Single source of truth for service discovery logic; no namespace confusion

---

## Testing Strategy (See Detailed PLAN.md Testing Section)

Hockney owns comprehensive testing plan with phases:

**Phase 1 (Week 1):** Test infrastructure foundation
- Create `AspireApp.UnitTests.csproj` (xUnit)
- Add pytest framework with conftest.py
- Update CI pipeline to run tests
- **Deliverable:** CI gated on test passing

**Phase 2 (Week 2):** High-risk path coverage
- Cross-service contract tests (JSON serialization)
- Python router unit tests (mocked DatabaseService)
- File upload controller tests (status casing, validation)
- **Deliverable:** 20+ tests, 60%+ coverage on critical paths

**Phase 3 (Week 3):** Integration validation
- End-to-end upload → processing → retrieval tests
- Python DatabaseService integration tests
- C#↔Python contract validation
- **Deliverable:** 40%+ overall coverage; processing pipeline validated

**Phase 4 (Week 4+):** Edge cases & stress
- Concurrent uploads
- Large file handling (50+ MB)
- Timeout scenarios
- **Deliverable:** Production-ready resilience

---

## Execution Roadmap

### **Sprint 1: Gate B1/B2 Unblock** (~2 days)
**PR:** `feature/gate-b1-b2-unblock`

1. **Day 1 morning:** Fix Python contracts (#1) + method signature (#3)
2. **Day 1 afternoon:** Fix status casing (#2), test pipeline
3. **Day 2 morning:** Pin deps (#5), align SK versions (#6)
4. **Day 2 afternoon:** Validate all gates pass; code review + merge

**Success:** Processing pipeline works end-to-end without errors

---

### **Sprint 1.5: Stabilize Orchestration** (~1.5 days)
**PR:** `feature/stabilize-orchestration`

1. **Morning:** Fix config key mismatch (#9), FK column conflict (#4)
2. **Afternoon:** Remove ApiService (#7), fix LightRAG blocker (#8)

**Success:** Orchestration startup <10s; all services healthy in dashboard

---

### **Sprint 2: Test Infrastructure** (~2 days)
**PR:** `feature/test-infrastructure` (coordinated with Hockney)

1. **Day 1:** Create C# test project, write contract tests (PLAN.md T1, T3)
2. **Day 2:** Create Python pytest suite, update CI (PLAN.md T1, T6)

**Success:** CI gates PRs on test passing; baseline smoke tests passing

---

### **Sprint 2.5: Observability & Cleanup** (~2 days, parallel)
**PR:** `feature/logging-and-cleanup`

1. Logging refactor (#10)
2. Remove dead entities (#11)
3. Consolidate duplicates (#12)
4. Documentation updates

**Success:** Aspire logs show diagnostics; codebase cleaner

---

## Success Criteria

**Gate B1/B2 Unblock complete when:**
- ✅ All Python router endpoints callable without `AttributeError`
- ✅ Web file upload creates row with `status='uploaded'` (lowercase)
- ✅ Processing reads uploaded row and persists `document_pages`
- ✅ `document_pages` FK column consistent (both services use `file_id`)
- ✅ Manual e2e test: upload → process → verify DB rows

**Stabilization complete when:**
- ✅ Orchestration startup <10s
- ✅ All services healthy in Aspire dashboard
- ✅ CI pipeline functional; PRs gated on test passing
- ✅ Baseline test suite (Phase 1-2) passing
- ✅ No vestigial services in orchestration chain

**Phase 3 (Document Upload) ready when:**
- ✅ Processing pipeline stable and tested
- ✅ Chat retrieval wired to Python `/rag` endpoint
- ✅ File paths correctly normalized between Web and Python
- ✅ Integration tests cover full upload → process → retrieve flow

---

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| **Breaking changes during contract fix** | Feature branch + comprehensive integration tests before merge (Hockney Phase 3) |
| **Status casing breaks old data** | Defensive query (`lower(status)`) handles mixed-case data; logging tracks normalization |
| **ApiService removal breaks something** | Grep entire codebase first; verify no remaining references before delete |
| **Test infrastructure over-engineered** | Start minimal (smoke + contract tests); expand incrementally (Phase 2-4) |
| **FK column migration corrupts data** | Dry-run migration on test database first; coordinate C# and Python simultaneous deployment |
| **LightRAG removal blocks future work** | Document decision; add Phase 5 task to integrate when ready |

---

## Decision Record

**Keaton (Lead/Architect) — 2026-02-21**

### Decisions Made

1. **Python Router Contracts:** Rewrite routers to call existing DatabaseService methods → simpler, aligns with "minimal Python footprint"
2. **Status Normalization:** Lowercase `"uploaded"` in Web; Python queries defensively → one-line fix, massive impact
3. **ApiService:** Remove entirely → zero business value, simplifies orchestration
4. **LightRAG:** Remove from WaitFor chain until integration ready → improves dev startup time
5. **Testing:** xUnit (C#) + pytest (Python) with CI gating, phased from smoke → integration → edge cases
6. **Logging:** Full `ILogger<T>` replacement (not gradual) → high-impact files first, then rest

### Rationale

- **Gate B1/B2 fixes** are all **data contract alignment issues** — once resolved, pipeline works end-to-end
- **Test infrastructure** is prerequisite for safe refactoring on items 7-12
- **Orchestration cleanup** (ApiService, LightRAG) improves developer experience immediately
- **Logging refactor** enables production debugging; depends on test infrastructure

---

## Out of Scope (Future Phases)

- Neo4j query performance tuning (batch UNWIND, full-text index) — Phase 4+
- Vector embeddings & semantic search — Phase 5+
- LightRAG ↔ Python integration — Phase 5+ (after contracts stable)
- Plugin ecosystem — Phase 6+
- Advanced error handling refactor — Post-stabilization
- Mobile/PWA UI — Phase 8+

---

## References

- **Roadmap:** `roadmap/Roadmap.md` (current: Phase 3 stabilization track)
- **Tasks:** `roadmap/Tasks.md` (Gate B1/B2 checklist aligned)
- **Decisions Log:** `.squad/decisions.md` (cross-agent findings from 2026-02-21 review)
- **Testing Plan:** This file (PLAN.md testing section) owned by Hockney
- **Key files:**
  - Orchestration: `src/AspireApp.AppHost/AppHost.cs`
  - Python DB: `src/AspireApp.PythonServices/app/services/database_service.py`
  - Python routes: `src/AspireApp.PythonServices/app/routers/`
  - Web upload: `src/AspireApp.Web/Controllers/FileUploadController.cs`

---

**Last updated:** 2026-02-21  
**Status:** Ready for Squad alignment & sprint planning  
**Next step:** Prioritize Sprint 1 (Gate B1/B2) and begin coordinated code fixes

### Inventory
- **C# Test Projects:** 0 (zero xUnit/NUnit/MSTest projects)
- **Python Test Suites:** 0 (no pytest infrastructure)
- **Test Files:** 6 diagnostic/benchmark scripts (not tests):
  - `test_all_builds.py` — Docker build performance benchmarking
  - `test_database_schema.py` — Manual schema verification
  - `test_services.py`, `test_build_config.py`, `test_app_main.py` — No assertions, no pytest integration
  - `test_concurrent_access.py` — Performance test, not safety test

### CI/CD Status
- `squad-ci.yml` is **non-functional**: `echo "No build commands configured"`
- No build verification, no test execution, no PR gating
- PRs merge unchecked

### Coverage Reality
- **Happy path only:** File upload, chat, processing all untested
- **Error paths:** No exception handling validation
- **Cross-service contracts:** C#↔Python JSON serialization has NO tests
- **Edge cases:** Null/empty inputs, concurrent access, resource limits — untested
- **Database:** SQLite schema assumed correct; no migration tests
- **Dependencies:** All critical services (Neo4j, Ollama, Python) have no integration tests

---

## 🔴 Critical Quality Gaps

### 1. **Cross-Service Contract Misalignment (BLOCKING)**
- C# `FileMetadata` (EF Core column mappings) ↔ Python `Document` Pydantic model
- No validation that C# JSON serialization matches Python Pydantic deserialization
- **Risk:** Any field rename or type change in C# silently breaks Python at runtime
- **Example:** If C# field `"upload_date"` renamed to `"uploadedAt"`, Python endpoints crash with `ValidationError`

### 2. **Python Routes Call Non-Existent DatabaseService Methods (BLOCKING)**
- Routers call: `get_document()`, `get_unprocessed_documents()`, `get_processed_document()`, `save_processed_document()`, etc.
- `DatabaseService` only has: `get_file_by_id()`, `get_unprocessed_files()`, `get_all_files()`
- **Risk:** Most Python endpoints throw `AttributeError` on first request

### 3. **Status Casing Bug Prevents File Discovery (HIGH)**
- C# writes status `"Uploaded"` (capital U)
- Python queries `WHERE status = 'uploaded'` (lowercase)
- **Risk:** Files uploaded via Blazor UI never found by Python for processing

### 4. **No Logging Integration Tests (HIGH)**
- `Console.WriteLine` used in 7+ files instead of `ILogger<T>`
- Aspire dashboard logs are missing critical debug info
- **Risk:** Production debugging impossible

### 5. **Broad Exception Catching Everywhere (HIGH)**
- 27+ `catch(Exception)` in C#, 18+ `except Exception` in Python
- Most return generic error strings, losing context
- **Risk:** Silent failures, hard to debug

### 6. **Unpinned Python Dependencies (MEDIUM)**
- `requirements.txt` has no version constraints
- `docling-core`, `neo4j`, `fastapi` all unpinned
- **Risk:** Non-reproducible builds, breaking changes silently

---

## ✅ Testing Strategy (Phase-Based)

### Phase 1: Foundation (Week 1) — Enable CI/CD
**Goal:** Make it impossible to merge broken code.

**Deliverables:**
- [ ] Create `AspireApp.UnitTests.csproj` (xUnit) — stub only
- [ ] Add `pytest`, `pytest-asyncio`, `pytest-cov` to `requirements.txt`
- [ ] Create Python `conftest.py` with shared fixtures
- [ ] Update `squad-ci.yml` to run `dotnet build`, `dotnet test`, `pytest`
- [ ] Pin Python dependencies with version constraints

**Definition of Done:**
- `dotnet build` runs on CI and reports warnings/errors
- CI blocks PRs if build fails
- CI workflow complete in < 3 minutes

---

### Phase 2: High-Risk Coverage (Week 2) — P0 Blockers
**Goal:** Prevent silent failures on critical paths.

**Deliverables:**

#### C# Contract Tests
```
AspireApp.UnitTests/
├── Contracts/
│   ├── DocumentSerializationTests.cs
│   ├── FileMetadataTests.cs
│   └── StatusEnumTests.cs
```
- Verify C# models serialize to JSON matching Python field names
- Verify datetime format compatibility (ISO 8601)
- Verify enum casing consistency (e.g., `"uploaded"` vs `"Uploaded"`)

#### Python Router Unit Tests
```
src/AspireApp.PythonServices/tests/
├── conftest.py
├── unit/
│   ├── test_documents_router.py (TestClient mocks)
│   ├── test_processing_router.py
│   └── test_health_router.py
├── integration/
│   └── test_database_service.py (real SQLite)
```
- Mock DatabaseService calls on routers
- Test status casing ("uploaded" lowercase)
- Verify error responses don't leak exceptions

#### C# File Upload Tests
```
AspireApp.UnitTests/
├── Controllers/
│   └── FileUploadControllerTests.cs
```
- Validate file size limits (< 100 MB)
- Validate MIME types (PDF, DOCX only)
- Test status casing ("uploaded" lowercase)
- Test duplicate file rejection

**Definition of Done:**
- ≥ 20 unit tests written and passing
- All P0 code paths exercised (happy path + main error branches)
- 60%+ coverage on controllers and routers

---

### Phase 3: Integration Suite (Week 3) — Pipeline Validation
**Goal:** Confirm file upload → processing → retrieval works end-to-end.

**Deliverables:**

#### Python Integration Tests (Real DatabaseService)
```
src/AspireApp.PythonServices/tests/integration/
├── test_database_service.py (in-memory SQLite)
├── test_processing_pipeline.py
└── test_neo4j_integration.py (if Neo4j running)
```
- Create temporary file → save to database → query
- Process document → save pages → verify Neo4j nodes created
- Batch operations (20+ files concurrently)

#### C# Integration Tests (Real Web + FileStorage)
```
AspireApp.UnitTests/Integration/
├── FileUploadIntegrationTests.cs
└── PythonServiceIntegrationTests.cs
```
- Upload file via FileUploadController → verify stored
- Call Python `/documents/` endpoint → verify JSON contract
- Upload → Python processes → status updates → retrieve

#### Cross-Service Contract Validation
```
AspireApp.UnitTests/
├── CrossService/
│   └── C2PyContractTests.cs
```
- C# uploads JSON → Python deserializes successfully
- Python returns JSON → C# deserializes successfully
- Field names match exactly
- Null/optional fields handled correctly

**Definition of Done:**
- ≥ 15 integration tests
- All major workflows tested
- 40%+ overall code coverage

---

### Phase 4: Edge Cases & Performance (Week 4+)
**Goal:** Handle real-world scenarios (large files, concurrent uploads, failures).

**Priority Tests:**
1. **Concurrent File Uploads** — 10 uploads simultaneously
2. **Large File Handling** — 50 MB PDF
3. **Database Cleanup** — Temp files + Neo4j nodes deleted on failure
4. **Timeout Handling** — Python service slow → C# timeout behavior
5. **Disk Full** — Storage exhausted → graceful error
6. **Memory Leaks** — Long-running service doesn't leak resources

---

## 📋 Quality Tasks (Prioritized by Risk)

### 🔴 P0: BLOCKING (Must fix before stable release)

| Task | Owner | Est. Time | Dependencies |
|------|-------|-----------|--------------|
| **[T1] Create test infrastructure (Phase 1)** | Hockney | 4h | None |
| **[T2] Fix Python DatabaseService method misalignment** | McManus | 8h | (Must happen in code first) |
| **[T3] Write cross-service contract tests** | Hockney | 8h | Code fixed (T2) |
| **[T4] Test status casing fix ("Uploaded" → "uploaded")** | Hockney + Fenster | 2h | Code fixed |
| **[T5] Pin Python dependencies + test reproducibility** | McManus | 2h | requirements.txt updated |
| **[T6] Update CI pipeline (squad-ci.yml)** | Hockney | 2h | Test infrastructure (T1) |

### 🟡 P1: HIGH RISK (Prevent regressions)

| Task | Owner | Est. Time | Dependencies |
|------|-------|-----------|--------------|
| **[T7] Python router unit tests (Phase 2)** | Hockney | 8h | T1 complete |
| **[T8] C# File upload controller tests (Phase 2)** | Hockney | 6h | T1 complete |
| **[T9] FileStorageService tests** | Hockney | 4h | T1 complete |
| **[T10] Logging refactor + tests** | Fenster | 8h | Code refactored |
| **[T11] Exception handling audit + specific catches** | Hockney | 6h | Code audited |

### 🟢 P2: MEDIUM (Improve maintainability)

| Task | Owner | Est. Time | Dependencies |
|------|-------|-----------|--------------|
| **[T12] Integration tests (Phase 3)** | Hockney + McManus | 12h | T3-T8 complete |
| **[T13] Concurrent upload stress tests** | Hockney | 4h | T12 complete |
| **[T14] Neo4j integration tests** | McManus | 6h | Phase 3 framework |
| **[T15] Code coverage reporting** | Hockney | 2h | T12 complete |

---

## 🎯 Acceptance Criteria

### CI/CD Passes (Phase 1)
- [ ] `dotnet build` succeeds on Ubuntu CI
- [ ] `pytest` runs on Python services
- [ ] CI workflow completes in < 3 minutes
- [ ] PR merge blocked if CI fails

### High-Risk Paths Tested (Phase 2)
- [ ] 20+ unit tests written, all passing
- [ ] Cross-service contract tests verify JSON serialization
- [ ] File upload controller tests validate inputs and status casing
- [ ] Python routers tested with mocked DatabaseService

### End-to-End Pipeline Validated (Phase 3)
- [ ] File upload → Python processing → retrieval works in test
- [ ] 15+ integration tests, all passing
- [ ] 40%+ code coverage across C# and Python

### Edge Cases Handled (Phase 4)
- [ ] Concurrent uploads stress test passes
- [ ] Large file handling tested (50+ MB)
- [ ] Timeout scenarios tested
- [ ] Database cleanup verified on errors

---

## 🧪 Test File Organization

```
AspireApp/
├── tests/
│   ├── AspireApp.UnitTests/
│   │   ├── Contracts/
│   │   │   ├── DocumentSerializationTests.cs
│   │   │   └── FileMetadataTests.cs
│   │   ├── Controllers/
│   │   │   └── FileUploadControllerTests.cs
│   │   ├── Services/
│   │   │   └── FileStorageServiceTests.cs
│   │   ├── CrossService/
│   │   │   └── C2PyContractTests.cs
│   │   └── AspireApp.UnitTests.csproj

src/AspireApp.PythonServices/tests/
├── conftest.py
├── __init__.py
├── unit/
│   ├── test_documents_router.py
│   ├── test_processing_router.py
│   └── test_health_router.py
├── integration/
│   ├── test_database_service.py
│   ├── test_processing_pipeline.py
│   └── test_neo4j_integration.py
└── fixtures/
    └── sample_data.py
```

---

## 📊 Coverage Goals

| Component | Phase 1 | Phase 2 | Phase 3 | Target |
|-----------|---------|---------|---------|--------|
| **C# Controllers** | 0% | 50% | 70% | 80% |
| **C# Services** | 0% | 30% | 60% | 75% |
| **Python Routers** | 0% | 60% | 80% | 85% |
| **Python DatabaseService** | 0% | 40% | 70% | 80% |
| **Overall** | 0% | 40% | 60% | 75% |

---

## 🔗 Related Instructions

- `.github/instructions/testing.instructions.md` — Testing patterns for Python/C#
- `.github/instructions/cross-service-contracts.instructions.md` — Contract sync strategies
- `.github/decisions.md` — Architecture decisions affecting tests (see Keaton, Fenster, McManus reviews)

---

## Next Steps

1. **This Week:** Create test infrastructure (Phase 1) — unblock CI
2. **Week 2:** Write high-risk path tests (Phase 2) — contract + controller + router tests
3. **Week 3:** Integration suite (Phase 3) — end-to-end validation
4. **Week 4+:** Edge cases & stress (Phase 4) — production readiness

**If you can't test it, you can't trust it.**
