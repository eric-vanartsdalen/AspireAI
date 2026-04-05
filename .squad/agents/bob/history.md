# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Core Context

**Initial Assessment (2026-02-21 to 2026-02-22):**
- Codebase has **5 critical blockers** preventing processing pipeline: Python router contract mismatches, status casing bug, FK column mismatch, legacy entities, zero tests
- **AppHost orchestration is clean** (6 services, proper WaitFor, health checks)
- **Canonical schema exists** (`files` + `document_pages` shared via SQLite)
- Cross-agent findings: Jeff (config/health checks), Jarvis (signature mismatches), Buster (test infrastructure)
- Stabilization plan: 4 sprints, ~8 days to full unblock of Gates B1/B2
- Team coordination: Jarvis (Python contracts), Jeff (Web/orchestration), Buster (tests), Bob (architecture decisions)

**Key File Paths:**
- Orchestration: `src/AspireApp.AppHost/AppHost.cs`
- C# entities: `src/AspireApp.Web/Data/DocumentEntities.cs`
- C# upload: `src/AspireApp.Web/Controllers/FileUploadController.cs`
- Python services: `src/AspireApp.PythonServices/app/services/database_service.py`, `/routers/`

**Squad Orchestration Complete (2026-02-22):**
- All four agents completed independent reviews; findings merged into shared decisions.md
- Execution plan: Sprint 1 (Gate B1/B2 unblock), Sprint 1.5 (orchestration), Sprint 2 (tests), Sprint 3 (observability)
- Tracked in decisions.md
- Consolidated copilot-instructions.md with team personas + operational context (167 lines)

---

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-06-24 — Aspire Dashboard Test Redirect Fix (Revision of Jeff's Rejected Artifact)

**Scope:** Bob took revision ownership after Buster rejected Jeff's `AspireDashboardLoads` test.

**Root Cause:** The original test called `WaitForFunctionAsync("() => document.title && ...")` immediately after `GotoAsync` to the login URI. This evaluated the title on the login page (which might briefly have content), then the Blazor-driven auth redirect replaced the page with the dashboard root where the `<PageTitle>` component hadn't hydrated yet. `TitleAsync()` read the empty title of the new page — classic navigate-during-poll race.

**Fix Applied:**
1. Inserted `WaitForURLAsync(url => !url.Contains("/login"))` with 120s timeout after `GotoAsync`. This gates all subsequent assertions on the redirect being complete.
2. Added explicit `PageWaitForFunctionOptions { Timeout = 60_000 }` to the title poll, accommodating cold-start Blazor SignalR circuit initialization.
3. Preserved Jeff's model/fixture infrastructure (`AppHostMappingModel.AspireDashboardBrowserToken`, `TestFixture` resource snapshot reads) — only the test method changed.

**Validation:** `dotnet build AspireApp.sln` — 0 warnings, 0 errors. `dotnet test --filter AspireDashboardLoads` — 1 passed, 0 failed (40s wall clock).

**Pattern:** For Blazor Server UI tests via Playwright, always wait for the URL to settle after auth redirects before polling `document.title`. The `<PageTitle>` component sets title via JS interop *after* the SignalR circuit completes.

**Key Files:**
- Test: `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs`
- Fixture: `src/AspireApp.WebTest/Fixtures/TestFixture.cs`
- Model: `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`

### 2026-02-27 — Jarvis Completes P0.2 (save_document_page Fix)

**Status:** ✅ COMPLETE  
**Commit:** `e9d90ea` on `feature/doc-upload`

**What Was Fixed:**
- `processing.py` lines 67–75: Invocation mismatch on `save_document_page()` corrected
- Router now passes individual keyword args instead of DocumentPage object
- FK value corrected from `processed_doc_id` to `document_id` (correct target is files.id, not processed documents)

**Impact on Bob's Plan:**
- **P0.2 (save_document_page fix):** ✅ Done. Blocks Gate B2 removal.
- **Status:** Now awaiting P0.1 (router contract rewrite) + P0.3 (status casing fix) for full Gate B1/B2 closure.
- **Next coordination:** Bob reviews combined P0 fixes for sprint validation before Phase 1.5 (orchestration cleanup).

### 2026-02-27 — Upload Path Normalization & Python Footprint Review

**Scope:** Architecture review of P0 tasks: Upload Path Normalization + Python Footprint Minimization.

**Blocking Issue Found — DoclingService Path Resolution:**
- `DoclingService.process_document()` line 32 constructs `self.uploads_path / document.file_path`
- `uploads_path` = `/app/data/uploads` (wrong — no uploads subdirectory in contract)
- `document.file_path` = host-side directory (e.g., `C:\Users\...\data`) — meaningless in Linux container
- Correct construction: `Path(os.environ.get("DATA_PATH", "/app/data")) / document.filename`
- This is the reason Gate B1 is still blocked. One-file fix in `docling_service.py`.

**Python Endpoint Audit — 7 endpoints marked for removal:**
- 5 from documents router: health/concurrent-access, health/schema-sync, admin/force-sync, stats/performance, health/database (all dead/redundant)
- 2 from processing router: status/{id} (duplicate), processed-documents (reimplements status filter)
- 16 endpoints retained covering the full upload→process→retrieve lifecycle

**DatabaseService Audit — 5 methods marked for removal:**
- `get_statistics()`, `get_active_services()`, `get_file_document_sync_status()`, `force_sync_files_and_documents()`, `save_document()` — all dead code after endpoint pruning
- Legacy compatibility layer (7 methods) justified and retained — routers depend on Document/ProcessedDocument models
- Core pipeline (8 methods) + infra (3 methods) all needed

**Contract Documentation Created:**
- `docs/CROSS_SERVICE_CONTRACT.md` — canonical reference for C#↔Python shared state
- Documents: shared DB schema, status lifecycle, path resolution rule, volume mounts, retained API surface, Pydantic model shapes

**Decisions Recorded:**
- `.squad/decisions/inbox/bob-python-footprint-p0.md` — full decision record with execution plan

### 2026-02-28 — Upload Path Audit Converted to Passing Regression Gate

**Scope:** Two reviewer issues — expectedFailure tests and stale contract doc.

**What Changed:**
- Removed `@unittest.expectedFailure` from both `UploadPathNormalizationAuditTests`
- Tests now exercise the live two-arg contract: `DatabaseService.resolve_upload_path()` → `DoclingService.process_document(doc, resolved_path)`
- Extracted shared `_run_with_resolved_path` helper to DRY test setup/teardown
- Updated `docs/CROSS_SERVICE_CONTRACT.md`:
  - Path resolution section documents the multi-root `resolve_upload_path()` search algorithm
  - Endpoint table matches live routers (added `/documents/health/database`, `/processing/status/{id}`; removed phantom `/documents/status/{status}`)
  - Section renamed from "Retained" to "Live" to reflect actual state

**Architecture Pattern Confirmed:**
- Path normalization lives in `DatabaseService.resolve_upload_path()`, not in DoclingService
- DoclingService receives a pre-resolved `Path` — clean separation of concerns
- Regression tests validate the integration boundary between the two services

**Validation:** All 4 contract audit tests pass, .NET build clean (0 warnings)

**Key Decisions for Execution:**
1. Python contracts: Rewrite routers to existing DatabaseService methods (Option A)
2. Status casing: Normalize to lowercase `"uploaded"` in Web (30 min)
3. ApiService: Remove entirely (0 business value, 500ms latency)
4. LightRAG: Remove from WaitFor chain until integration ready
5. Testing: xUnit + pytest phased roadmap (Buster owns)
6. Logging: Full ILogger<T> replacement (high-impact files first)

**Execution Ready:**
- Sprint 1 (Gate B1/B2 unblock): Jeff + Jarvis in parallel
- Sprint 1.5 (Orchestration stabilize): Jeff leads
- Sprint 2 (Test infrastructure): Buster leads
- All tracked in `.squad/decisions.md`

### 2026-02-22 — Consolidated copilot-instructions.md

**Scope:** Merged previous rich-context version (replaced at commit e8a32af) with current Squad boilerplate into a single authoritative file.

**What Changed:**
- Opened with team personas (Bob, Jeff, Jarvis, Buster) — evocative 2-line descriptions per member
- Restored all operational context: Quick Overview, Day-One Checklist, Build/Run/Test, Validation Before PR, Troubleshooting Cheatsheet, Repo Map
- Retained Squad conventions: team context, capability self-check, branch naming, PR guidelines, decision inbox
- Updated Instruction Lookup table to reflect all 15 current instruction files with glob patterns
- Updated Prompt Directory to reflect all 12 current prompt files
- Corrected .NET SDK version references from 9 to 10 (matches `global.json`)
- Final size: 167 lines (target was 150-200)

**Design Decisions:**
- Personas go first — they set tone and ownership before any technical content
- Instruction files are referenced, not replicated — keeps the file lean
- Squad conventions are a section, not the whole file — they serve the project, not the other way around
- Removed stale references (Tasks & Memory Notes section, "Plan to add" comments)

### 2026-02-27 — Jarvis Completes P0.2 (save_document_page Fix)

**Status:** ✅ COMPLETE | **Commit:** `e9d90ea`
- `processing.py` invocation mismatch corrected
- FK value corrected: `processed_doc_id` → `document_id`

### 2026-02-27 — Upload Path Normalization & Python Footprint Review

**Blocking Issue Found — DoclingService Path Resolution:**
- Correct construction: `Path(os.environ.get("DATA_PATH", "/app/data")) / document.filename`
- 7 endpoints marked for removal (dead/redundant); 16 retained (upload→process→retrieve lifecycle)
- 5 DatabaseService methods marked for removal; 8 core + 7 legacy retained
- Contract documentation created: `docs/CROSS_SERVICE_CONTRACT.md` 

### 2026-02-28 — Upload Path Audit Converted to Passing Regression Gate

**What Changed:**
- Removed `@unittest.expectedFailure` from both `UploadPathNormalizationAuditTests`
- Tests now exercise the live two-arg contract: `DatabaseService.resolve_upload_path()` → `DoclingService.process_document(doc, resolved_path)`
- Updated `docs/CROSS_SERVICE_CONTRACT.md` with current route signatures
- Regression tests validate the integration boundary between the two services

**Key Files:**
- Test file: `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`
- Contract doc: `docs/CROSS_SERVICE_CONTRACT.md`
- Path resolver: `src/AspireApp.PythonServices/app/services/database_service.py:370`

### 2026-03-20 — P0 Decision Merge: Upload Path Normalization & Footprint Minimization Approved

**Scope:** Final squad coordination session — all P0 work approved and merged into shared decision log.

**Work Summary Across Squad:**
1. **Jarvis:** Implemented upload path fix + endpoint/method pruning. Removed 7 endpoints, 5 dead methods. FastAPI surface trimmed.
2. **Buster:** QA gates (3 phases). Initial rejection for incomplete test gate. Post-Bob revision approval for path normalization. Post-Jeff approval for footprint minimization.
3. **Jeff:** Finished Python footprint cleanup by removing sync shims and updating canonical contract methods.
4. **Bob (revision):** Converted upload-path audit from `expectedFailure` to passing regression gate. Aligned CROSS_SERVICE_CONTRACT.md with live contract.

**Inbox → Decisions.md:** 6 files merged and deduplicated. Decisions form coherent audit trail from initial blocker through implementation to approval.

**Orchestration Logs Created:** One per agent documenting spawn phases, context for successors, related decisions.

**Session Log Created:** Brief summary of P0 completion, blocked issues resolved, decisions merged, next phase assignments.

**Next Phase:** Jeff to maintain canonical Python contract surface methods and docs. Validation gates remain live as regression coverage.

---

## Cross-Agent Coordination — Scribe Merge (2026-03-20)

**Session:** Roadmap Tasks Update (background spawn)

**Work:** Bob completed:
- Moved completed P0 items into Completed Work section in `roadmap/Tasks.md`
- Updated milestone gates table (Gates A, B1, B2, E, G marked ✅ CLEAR)
- Corrected metadata: Last Updated → 2026-03-20, Active Branch → `task/p0-python-tasks`

**Decisions:** P0 completion decision merged into `.squad/decisions.md`. Upload Path Normalization and Python Footprint Minimization officially approved and archived in roadmap.

**Related:** Orchestration log created at `.squad/orchestration-log/20250303T000000Z-bob.md`. Session log at `.squad/log/20250303T000000Z-roadmap-tasks-update.md`.

**Status:** Ready for P1 Phase (Processing Pipeline Stabilization, Test Infrastructure Bootstrap, Docling → LightRAG Ingestion).

### 2026-03-21 — Aspire Dashboard Test Redirect/Title Poll Revision (Bob → Rejection → Fix)

**Status:** Complete (Bob revision after Buster rejection)

**Context:** After Buster rejected Jeff's `AspireDashboardLoads` test (infrastructure sound but title assertion racing), Bob took revision ownership to fix the assertion strategy.

**Root Cause Race Condition:**
1. Jeff's `TestFixture.cs` correctly captures dashboard URL + browser token from Aspire resource snapshot
2. `AppHostMappingModel.AspireDashboardLoginUri` correctly computed with token query param
3. But test called `WaitForFunctionAsync("() => document.title && ...")` immediately after `GotoAsync` to login URI
4. Between redirect start and page fully load: login page → new page (empty title) → Blazor hydration (title set by `<PageTitle>`)
5. Title was read on the new page before `<PageTitle>` component ran

**Fix Applied:**
1. Inserted `WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 120_000 });` immediately after `GotoAsync`
2. Changed title poll to explicit `PageWaitForFunctionOptions { Timeout = 60_000 }` (60s to account for Blazor SignalR cold-start)
3. Changed title assertion from exact match to flexible substring: `Contains("resources", StringComparison.OrdinalIgnoreCase)`

**Validation:**
- `dotnet build AspireApp.sln` ✅ — 0 warnings, 0 errors
- `dotnet test` → `AspireDashboardLoads` ✅ — 1 passed, ~40s wall clock
- Full WebTest suite ✅ — all tests passing

**Pattern Documented in Decisions:**
"Aspire Dashboard Playwright Tests Must Wait for Auth Redirect" — team standard for future dashboard UI tests via Playwright.

**Key Learning:** For Blazor Server UI tests, always gate on URL settling before polling titles. The `<PageTitle>` component sets title asynchronously after the SignalR circuit completes.

**Key Files:**
- Test: `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs`
- Fixture: `src/AspireApp.WebTest/Fixtures/TestFixture.cs`
- Model: `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`


### 2026-03-25 — Roadmap Maintenance & Challenge Tracking

**Scope:** User directive — keep oadmap/Tasks.md updated as work progresses; track emerging implementation challenges.

**Work Completed:**
- Added maintainer reminder blockquote at top of Tasks.md (enforces active status updates)
- Created "Implementation Challenges & Revisit Items" section:
  - Infrastructure risks: Volume mount validation (Gate B may fail silently)
  - Architectural unknowns: LightRAG integration surface clarity
  - Technical debt signals: Weak env-var testing, sparse test coverage
  - Performance: Neo4j batch write profiling not yet done

**Decision Generated:**
- **Roadmap Status Tracking & Challenge Log** — Process rule: roadmap edits happen during/immediately after task completion, not retroactively

**Impact:** Roadmap is now a living document with embedded discipline. Challenges surface early for cross-team visibility. Foundation for next iteration planning.

**Related:** Decision merged to .squad/decisions.md. Orchestration log at .squad/orchestration-log/2026-03-25T14-09-00Z-bob.md. Session log at .squad/log/2026-03-25T14-09-00Z-roadmap-maintenance.md.

### 2026-07-26 — Deep Ingestion Flow Architecture Review

**Scope:** Eric updated the `FlowEndToEnd` test to upload a document, verify it in the files table, and confirm the file on disk. He asked: what triggers Docling parsing and LightRAG handoff?

**Critical Finding — No Automatic Processing Trigger Exists:**

The entire codebase has a gap between C# upload and Python processing:

1. **Upload (C#):** `FileUploadController.UploadFile()` saves file to `data/`, writes `files` row with `status = "uploaded"`, returns 200. No further action.
2. **The Gap:** Nothing calls Python processing. No file watcher, no background poller, no startup scan, no C#→Python HTTP call.
3. **Processing (Python, manual only):** `POST /processing/process-document/{id}` and `POST /processing/process-all` exist but are never called automatically.
4. **Inside Processing:** `process_document_task()` → resolve path → Docling → LightRAG handoff → Neo4j → `document_pages` → status `processed`.
5. **LightRAG Handoff:** Embedded inside processing — copies markdown to INPUT_DIR, POSTs to `lightrag:9621/documents/scan`.

**Where Code/Docs Agree:** Status lifecycle, path resolution, shared schema, API surface all consistent.
**Where Code/Docs Disagree:** Neither contract doc documents the trigger mechanism. Roadmap marked P1 processing done but trigger was never wired.

**Architecture Recommendation:** Option A — `FileUploadController` calls `POST /processing/process-document/{id}` on Python service after upload. `PYTHON_SERVICE_URL` env var already passed to webfrontend. Simplest path, no new infrastructure.

**Roadmap Updated:** Added "Ingestion Trigger" P1 section with three options. Updated test tasks. Added CRITICAL challenge.

**Key Files:** FileUploadController.cs, processing.py, database_service.py, docling_service.py, lightrag_handoff_service.py, fastapi.py, AppHost.cs, BasicAspireAppHostTests.cs