# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

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
