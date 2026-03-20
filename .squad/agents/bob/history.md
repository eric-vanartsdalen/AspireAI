# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-02-21 — Comprehensive Review & Stabilization Plan

**Scope:** Full codebase assessment + squad coordination plan for Phase 3 completion

**Key Findings:**
- **5 critical blockers** prevent processing pipeline from working (all 1-line to 1-day fixes)
  - Python routers call ~10 methods that don't exist on DatabaseService
  - Status casing mismatch prevents file discovery (`"Uploaded"` vs `"uploaded"`)
  - Method signature misalignment on `save_document_page()`
  - FK column name conflict on `document_pages` table
  - Zero automated tests block safe refactoring
- **9 supporting fixes** for orchestration cleanup and observability
- **Execution plan:** 4 sprints spanning ~8 days to full stabilization
- **Testing strategy:** Phased (Phase 1 foundation → Phase 4 edge cases) coordinated with Buster
- **Decision record:** Document in `.squad/decisions/inbox/bob-plan-review.md`
- **Plan:** Complete PLAN.md updated with architecture fixes + testing plan sections

**Architecture Assessment:**
- Strengths: Clean AppHost orchestration, canonical schema, proper DI, production-ready Blazor UI
- Gaps: Entirely data contract alignment issues in Python; zero test infrastructure
- No architectural redesign needed; unblock gates B1/B2, stabilize orchestration, bootstrap tests
- Phase 3 can complete on schedule with focused 2-day sprint on contract fixes

**Key Decisions Made:**
1. Python contracts: Rewrite routers to existing methods (Option A, cleaner)
2. Status casing: Normalize to lowercase in Web; Python queries defensively
3. ApiService: Remove entirely (zero business value, 500ms latency)
4. LightRAG: Remove from startup WaitFor chain until integration ready
5. Testing: xUnit + pytest phased from smoke → integration → edge cases
6. Logging: Full ILogger<T> replacement (impact files first)

**Coordination:**
- Squad owns code fixes (Sprints 1-1.5): Jarvis (Python contracts), Jeff (Web/orchestration)
- Buster owns test infrastructure bootstrap (Sprint 2, coordinated)
- Jeff handles observability cleanup (Sprint 2.5, parallel)
- All tracked in `.squad/decisions/inbox/bob-plan-review.md`

**Files Modified:**
- `PLAN.md` — comprehensive action plan (14 items, execution roadmap, success criteria)
- `.squad/decisions/inbox/bob-plan-review.md` — decision record for squad alignment

**User Preference Confirmed:**
- Eric values stabilization over new features ✅
- Maintenance-first approach ✅
- Decisions documented and reasoned ✅

### 2026-02-21 — Architecture Review

- **Solution builds clean** on .NET 10 / Aspire SDK 13.1.0 with zero warnings.
- **AppHost.cs** is the orchestration hub: 6 services (Web, ApiService, Python, Neo4j, Ollama, LightRAG) with proper WaitFor ordering and health checks.
- **Canonical schema:** `files` + `document_pages` tables in SQLite, shared via bind mount between Web (EF Core) and Python (raw SQL).
- **Critical gap:** Python routers call DatabaseService methods that don't exist (legacy method names). This breaks the processing pipeline at runtime.
- **Status casing bug:** FileUploadController writes `"Uploaded"` but Python queries `WHERE status = 'uploaded'` — one-line fix needed.
- **ApiService is vestigial:** Only contains weather forecast stub. Recommend removing or repurposing.
- **LightRAG is wired but unconsumed:** Container runs, no code calls its APIs, web blocks on WaitFor(lightrag).
- **Key file paths:**
  - Orchestration: `src/AspireApp.AppHost/AppHost.cs`
  - C# entities: `src/AspireApp.Web/Data/DocumentEntities.cs`
  - C# storage: `src/AspireApp.Web/Shared/FileStorageService.cs`
  - C# upload: `src/AspireApp.Web/Controllers/FileUploadController.cs`
  - Python models: `src/AspireApp.PythonServices/app/models/models.py`
  - Python DB service: `src/AspireApp.PythonServices/app/services/database_service.py`
  - Python routers: `src/AspireApp.PythonServices/app/routers/` (documents, processing, rag)
  - Neo4j Dockerfile: `src/AspireApp.Neo4JService/Dockerfile`
- **User preferences:** Eric values stabilization over new features. Maintenance-first approach. Canonical decisions documented in roadmap.
- **Top 5 priorities:** (1) Fix Python router contracts, (2) Fix status casing, (3) Decide ApiService fate, (4) Add LightRAG health check or remove from startup chain, (5) Remove legacy EF entities.

### 2026-02-21 — Cross-Agent Findings

**From Jeff:**
- LightRAG and Ollama have no health checks, causing webfrontend to block on WaitFor indefinitely
- Config key mismatch: AI-Chat-Model (AppHost) vs AI-Model (Web services)
- SemanticKernel version skew (1.71.0 vs 1.68.0-alpha connector) needs alignment

**From Jarvis:**
- Save_document_page() signature mismatch: router passes DocumentPage object, method expects individual args
- FK column name conflict: Python creates `file_id`, C# maps to `document_id`
- Legacy C# entities reference non-existent tables (documents, processed_documents)
- Requirements.txt has no version pinning

**From Buster:**
- Zero automated tests; CI is non-functional
- Console.WriteLine used extensively instead of ILogger
- Broad catch(Exception) blocks everywhere
- Cross-service contract tests are highest priority to prevent drift

### 2026-02-22 — Squad Orchestration Complete

**Status:** All four agents completed independent reviews; findings merged into shared decisions.md.

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

**Key File Paths:**
- DoclingService (path bug): `src/AspireApp.PythonServices/app/services/docling_service.py:32`
- Documents router (endpoint pruning): `src/AspireApp.PythonServices/app/routers/documents.py`
- Processing router (endpoint pruning): `src/AspireApp.PythonServices/app/routers/processing.py`
- Contract doc: `docs/CROSS_SERVICE_CONTRACT.md`
- C# upload controller: `src/AspireApp.Web/Controllers/FileUploadController.cs`
- C# entities: `src/AspireApp.Web/Data/DocumentEntities.cs`

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

**Validation:** All 4 contract audit tests pass, .NET build clean (0 warnings).

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
