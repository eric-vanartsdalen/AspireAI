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
- **Testing strategy:** Phased (Phase 1 foundation → Phase 4 edge cases) coordinated with Hockney
- **Decision record:** Document in `.squad/decisions/inbox/keaton-plan-review.md`
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
- Squad owns code fixes (Sprints 1-1.5): McManus (Python contracts), Fenster (Web/orchestration)
- Hockney owns test infrastructure bootstrap (Sprint 2, coordinated)
- Fenster handles observability cleanup (Sprint 2.5, parallel)
- All tracked in `.squad/decisions/inbox/keaton-plan-review.md`

**Files Modified:**
- `PLAN.md` — comprehensive action plan (14 items, execution roadmap, success criteria)
- `.squad/decisions/inbox/keaton-plan-review.md` — decision record for squad alignment

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

**From Fenster:**
- LightRAG and Ollama have no health checks, causing webfrontend to block on WaitFor indefinitely
- Config key mismatch: AI-Chat-Model (AppHost) vs AI-Model (Web services)
- SemanticKernel version skew (1.71.0 vs 1.68.0-alpha connector) needs alignment

**From McManus:**
- Save_document_page() signature mismatch: router passes DocumentPage object, method expects individual args
- FK column name conflict: Python creates `file_id`, C# maps to `document_id`
- Legacy C# entities reference non-existent tables (documents, processed_documents)
- Requirements.txt has no version pinning

**From Hockney:**
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
5. Testing: xUnit + pytest phased roadmap (Hockney owns)
6. Logging: Full ILogger<T> replacement (high-impact files first)

**Execution Ready:**
- Sprint 1 (Gate B1/B2 unblock): Fenster + McManus in parallel
- Sprint 1.5 (Orchestration stabilize): Fenster leads
- Sprint 2 (Test infrastructure): Hockney leads
- All tracked in `.squad/decisions.md`
