# AspireAI Plan

Phased roadmap from foundation through advanced features. Each phase is an epic. Active work is on the Stabilization Track (Phase 3 foundation).

**Last Updated:** 2026-02-27
**Active Branch:** `task/documents-cleanup`

---

## Vision

A configurable, modular Blazor-based chat assistant that supports local or hosted LLMs, document ingestion, and retrieval-augmented generation — built on a structured knowledge graph with a flexible plugin architecture.

---

## Phase Summary

| Phase | Focus | Status |
|-------|-------|--------|
| 0 | Repo Cleanup & Setup | ✅ Complete |
| 1 | Blazor Chat UI | ✅ Complete |
| 2 | Speech-to-Text & Text-to-Speech | ✅ Complete |
| 3 | Document Upload & Ingestion | ⏳ In Progress |
| 4 | Flat Vector RAG | 🔜 Next |
| 5 | LightRAG / GraphRAG | 🔜 Planned |
| 6 | Plugin Ecosystem | 🔜 Planned |
| 7 | Testing, UX Polish & Deployment | 🔜 Planned |
| 8 | Advanced Features | 🔮 Future |

---

## ✅ Phase 0: Repo Cleanup & Setup

Solution/project structure, Aspire AppHost orchestration, plugin scaffolding, README, contributor guidelines, branch strategy.

## ✅ Phase 1: Blazor Chat UI

Chat.razor component with message history, user/assistant bubbles, text input, backend LLM integration (Ollama via Semantic Kernel), auto-scroll and focus management.

## ✅ Phase 2: Speech I/O

SpeechService.cs + speech.js interop using Web Speech API. Microphone input, text-to-speech for AI responses, browser compatibility detection, visual feedback with record/playback/stop buttons. See `docs/SPEECH_FEATURES.md`.

---

## ⏳ Phase 3: Document Upload & Ingestion (Active)

**Objective:** Stable end-to-end path from file upload through Docling processing to page-level persistence, ready for RAG retrieval.

### Completed ✅

- Blazor file upload component (drag-and-drop, file picker)
- SQLite metadata persistence (`files` table via EF Core)
- Timestamped file storage with `original_file_name` / `file_name` distinction
- Volume mapping: uploaded files visible inside Python container
- Python router/service contract alignment to canonical `files` + `document_pages` schema
- Status casing normalization (`uploaded` lowercase)
- `save_document_page` signature alignment
- FK column name unified to `file_id`
- Backward-compatibility wrappers in DatabaseService

### Remaining

- **Processing Pipeline** — Process uploaded records through Docling; persist page content in `document_pages`; handle retries on error.
- **Upload Path Normalization** — Resolve full physical file as `file_path` (directory) + `file_name` (stored timestamped file); add Windows-to-container path guardrails.
- **Docling → LightRAG Ingestion** — Export Docling output into LightRAG ingest; confirm LightRAG uses Neo4j backend and records are queryable.
- **Python Footprint Minimization** — Remove non-essential SQLite usage and legacy schema dependencies; document retained API surface.
- **Testing Baseline** — Upload smoke test, Python integration test (uploaded record → pages), RAG integration test, E2E manual smoke.

### Acceptance Gates

| Gate | Criteria | Status |
|------|----------|--------|
| A | Upload record has valid path + status; file exists on disk | ✅ |
| B | Processing writes `document_pages` rows | ⏳ |
| C | RAG query returns source-bearing results | ⏳ |
| D | Chat displays citation references | ⏳ |
| E | Volume-mounted files visible to Python container | ✅ |
| F | Docling output ingested to LightRAG and queryable | ⏳ |
| G | Python SQLite/API footprint reduced and documented | ⏳ |

---

## 🔜 Phase 4: Flat Vector RAG

**Objective:** Simple RAG using flat vector retrieval to give chat responses augmented context from documents.

- `FlatVectorRetriever` plugin implementation
- Top-K chunk retrieval from vector store
- Context-augmented prompt builder
- Citation/footnote display in chat UI (`[1]`, `DocName – p3`)
- "Enable RAG" configuration toggle

---

## 🔜 Phase 5: LightRAG / GraphRAG

**Objective:** Advanced retrieval strategies using the knowledge graph.

- `LightRagRetriever` or `GraphRagRetriever` plugin
- Graph construction from document chunks and metadata
- Multi-hop retrieval and semantic filtering
- Enhanced prompt builder for graph context
- UI for reasoning paths or graph fragments
- Fallback to flat vector retrieval

---

## 🔜 Phase 6: Plugin Ecosystem

**Objective:** Community-extensible platform for custom providers and strategies.

- Documentation for writing and registering plugins
- Example plugins: custom LLM backend (OpenAI/Anthropic), custom chunker/embedder, TTS provider
- Feature-flag based plugin loading via `appsettings.json`
- Plugin registration tests

---

## 🔜 Phase 7: Testing, UX Polish & Deployment

**Objective:** Production readiness — robust testing, containerized deployment, CI/CD.

- Unit and integration tests (xUnit for .NET, pytest for Python)
- End-to-end test plans for upload → process → retrieve → cite
- Dockerfiles for Blazor frontend and Python backend
- `docker-compose.yml` example
- GitHub Actions CI/CD: build + test gating
- UI/UX polish (loading states, error messages, progress indicators)

---

## 🔮 Phase 8: Advanced Features

Stretch goals once core is solid:

- Multi-agent chat flows (Planner → Researcher → Writer)
- Long-term conversation memory
- Web crawler / URL ingestion
- Mobile-friendly PWA version
- Analytics dashboard (usage, query stats, citation tracking)
- Knowledge evolution tracking and semantic diffing between document versions
- Multi-tenant knowledge workspaces with tenant isolation

---

## Infrastructure Priorities (Cross-Phase)

Stabilization work that supports multiple phases:

| Priority | Item | Status |
|----------|------|--------|
| P0 | Pipeline contract + status alignment | ✅ Done |
| P1 | Test infrastructure (xUnit + pytest + CI gates) | ⏳ |
| P2 | Orchestration stability (LightRAG WaitFor, config keys) | ⏳ |
| P3 | Logging (`Console.WriteLine` → `ILogger<T>`) | ⏳ |
| P3 | Pin Python dependency versions | ⏳ |
| P3 | Consolidate duplicate `ServiceDiscoveryUtilities` | ⏳ |
| P3 | Remove legacy EF entity classes | ⏳ |

---

## Done Criteria (Stabilization Track)

- Uploading a file from Web reliably appears in Python unprocessed list.
- Processing completes without contract/signature errors.
- Data writes/readbacks are consistent across C#, Python, and schema.
- CI runs build + tests automatically and fails on regressions.
- Core integration flow is covered by automated tests.
