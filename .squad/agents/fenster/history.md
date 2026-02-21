# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

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

**From Keaton:**
- Processing pipeline blocked by ~10 missing DatabaseService methods in Python
- Status casing bug ("Uploaded" vs "uploaded") prevents file discovery
- ApiService vestigial, should be removed or given real purpose

**From McManus:**
- Save_document_page() signature mismatch will crash during processing
- FK column name conflict (file_id vs document_id) creates data integrity risk
- Requirements.txt unpinned — non-reproducible builds

**From Hockney:**
- Zero automated tests — high regression risk on schema changes
- Console.WriteLine used 35+ times in Chat.razor.cs alone
- Cross-service contract tests critical to prevent JSON field name drift
