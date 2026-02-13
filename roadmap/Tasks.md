# AspireAI Tasks

A task-oriented view of the AspireAI project aligned with the [Roadmap](Roadmap.md). This document provides actionable, phase-specific tasks to guide implementation.

---

## Current Focus: 2026-02-13 Stabilization Track

**Branch**: `feature/doc-upload` (active)

### Active Tasks

- [ ] **Contract Alignment (P0 blocker)**
  - Align Python router/service/database contracts to canonical SQLite schema: `files` + `document_pages`
  - Remove/replace legacy `documents`/`processed_documents` assumptions in Python models and router flows
  - Normalize status lifecycle values and casing (`uploaded`, `processing`, `processed`, `error`)

- [ ] **Upload Path Normalization (P0 blocker)**
  - Ensure Web persistence and Python Docling resolution use a shared file-path convention
  - Validate every uploaded row can be located by Python prior to processing
  - Verify Docker volume mapping exposes uploaded files to Python container at runtime

- [ ] **Processing Pipeline Stabilization (P1)**
  - Process uploaded records through Docling and persist page content in `document_pages`
  - Persist processing timestamps/error details consistently
  - Add clear retry behavior for failed processing records

- [ ] **Docling -> LightRAG Ingestion (P1 blocker)**
  - Export Docling free-text output in a form accepted by LightRAG ingest path
  - Confirm LightRAG uses Neo4j backend and ingested records are queryable
  - Keep orchestration through Python retrieval APIs (no parallel retrieval path)

- [ ] **RAG Ingestion via Python (P1)**
  - Keep ingestion/query orchestration behind Python endpoints
  - Ensure Neo4j/LightRAG updates are linked to canonical SQLite records

- [ ] **Python Footprint Minimization (P0 blocker)**
  - Remove non-essential SQLite usage patterns and legacy schema dependencies
  - Minimize API endpoints to required upload->process->retrieve lifecycle
  - Document the retained endpoint/database contract surface

- [ ] **Chat Retrieval + Citations (P2)**
  - Update chat flow to call Python `/rag` retrieval path
  - Render source references in chat UI (file + page + snippet)

- [ ] **Testing Baseline (P0-P2 gates)**
  - Upload smoke test: create/list/delete file and validate filesystem + DB state
  - Python integration test: uploaded record -> process -> `document_pages` rows
  - RAG integration test: retrieval returns source-bearing results
  - E2E manual smoke: upload -> process -> chat response includes citation

### Milestone Gates

- [ ] **Gate A**: Uploaded file exists on disk and row status/path are valid
- [ ] **Gate B**: Processing writes page rows successfully
- [ ] **Gate C**: RAG search returns references from processed content
- [ ] **Gate D**: Chat displays citation references from retrieval
- [ ] **Gate E**: Uploaded files are visible inside Python container through mapped volume path
- [ ] **Gate F**: Docling output is ingested into LightRAG and queryable via Python retrieval route
- [ ] **Gate G**: Python SQLite/API footprint reduced to minimum required and documented

---

## Phase Completion Checklist

### ? Phase 0: Repo Cleanup & Setup

- [x] Solution/project structure defined
- [x] Aspire AppHost orchestration configured
- [x] Plugin/extension scaffolding added
- [x] README and contributor guidelines created
- [x] Branch strategy established

### ? Phase 1: Basic Blazor Chat UI

- [x] Chat.razor component with message history
- [x] User/Assistant message bubbles with styling
- [x] Text input with send button
- [x] Backend LLM integration (Ollama via Semantic Kernel)
- [x] Auto-scroll and focus management

### ? Phase 2: Speech-to-Text & Text-to-Speech

- [x] SpeechService.cs and speech.js implementation
- [x] Microphone input (Web Speech API)
- [x] Text-to-speech for AI responses
- [x] Browser compatibility detection
- [x] Visual feedback with record/playback/stop buttons
- [x] Documentation in `docs/SPEECH_FEATURES.md`

### ? Phase 3: Document Upload & Ingestion

- [x] File upload UI/component baseline
- [ ] Python contract alignment with canonical schema
- [ ] Stable Docling processing path from uploaded file records
- [ ] Page-level persistence and retrieval-ready metadata
- [ ] Processing + retrieval test coverage

### ? Phase 4: Flat Vector RAG

- [ ] Vector store implementation
- [ ] FlatVectorRetriever plugin
- [ ] Top-K chunk retrieval logic
- [ ] Context-augmented prompt builder
- [ ] Citation/footnote display in UI
- [ ] "Enable RAG" configuration toggle

### ? Phase 5: LightRAG / GraphRAG

- [ ] Graph construction from document chunks
- [ ] GraphRagRetriever or LightRagRetriever plugin
- [ ] Multi-hop retrieval logic
- [ ] Enhanced prompt builder for graph context
- [ ] UI for reasoning paths/graph visualization
- [ ] Fallback to flat vector retrieval

### ? Phase 6: Plugin Ecosystem

- [ ] Plugin documentation (how to create custom plugins)
- [ ] Example custom IChatProvider (OpenAI/Anthropic)
- [ ] Example custom chunker/embedder
- [ ] Feature flag-based plugin loading
- [ ] Plugin registration tests
- [ ] Community contribution guidelines

### ? Phase 7: Testing & Deployment

- [ ] Unit tests for backend services
- [ ] Integration tests for upload -> processing -> retrieval path
- [ ] End-to-end chat citation validation
- [ ] Dockerfile for Blazor frontend
- [ ] Dockerfile for Python backend
- [ ] docker-compose.yml example
- [ ] GitHub Actions CI/CD workflow

### ? Phase 8: Advanced Features

- [ ] Multi-agent workflows
- [ ] Long-term conversation memory
- [ ] Web crawler/scraper integration
- [ ] Mobile-friendly PWA version
- [ ] Analytics dashboard

---

## Quick Reference Commands

### Development

```bash
# Build solution
dotnet build

# Run Aspire orchestration (always use AppHost as startup)
dotnet run --project src/AspireApp.AppHost

# Run individual service (debugging only)
dotnet run --project src/AspireApp.Web
```

### Git Workflow

```bash
# Create feature branch
git checkout -b feature/<phase-name>

# Commit changes
git add .
git commit -m "Phase X: Brief description"

# Push and create PR
git push origin feature/<phase-name>
```

### Testing (when implemented)

```bash
# Run .NET tests
dotnet test

# Run Python tests
cd src/AspireApp.PythonServices
pytest
```

---

## Task Tracking Tips

- Keep tasks aligned with Roadmap phases
- Update checkboxes as work progresses
- Reference specific files/components in task descriptions
- Move completed phases to "Phase Completion Checklist"
- Add new tasks as requirements emerge

---

**Last Updated**: 2026-02-13  
**Current Phase**: Stabilization Track (Phase 3 foundation -> Phase 5 enablement)  
**Active Branch**: `feature/doc-upload`
