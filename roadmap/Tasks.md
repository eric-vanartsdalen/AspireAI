# AspireAI Tasks

A task-oriented view of the AspireAI project aligned with the [Roadmap](Roadmap.md). This document provides actionable, phase-specific tasks to guide implementation.

---

## Current Focus: Phase 3 - Document Upload & Ingestion Pipeline

**Branch**: `feature/doc-upload` (active)

### Active Tasks

- [ ] **UI: File Upload Component**
  - Create Blazor drag-and-drop file upload component in `src/AspireApp.Web/Components/Pages/`
  - Support PDF, DOCX, TXT file types
  - Add file validation (size limits, type checking)
  - Display upload progress indicator

- [ ] **Backend: Python FastAPI Ingestion Service**
  - Configure Docling integration in `src/AspireApp.PythonServices/`
  - Create `/api/ingest` endpoint for file processing
  - Implement document-to-DoclingDocument conversion
  - Add chunking logic (configurable chunk size/overlap)

- [ ] **Storage: Metadata & Embeddings**
  - Design database schema for document chunks (SQLite or Neo4j)
  - Store chunk metadata (source file, page number, position)
  - Add optional embedding generation pipeline
  - Implement chunk retrieval API

- [ ] **Integration: Connect Upload UI to Python Service**
  - Wire Blazor upload component to Python ingestion endpoint
  - Handle async upload with status feedback
  - Display ingestion results and errors in UI

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
- [ ] File upload UI component
- [ ] Python Docling integration
- [ ] Document chunking pipeline
- [ ] Metadata storage (database)
- [ ] Embedding generation (optional)

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
- [ ] Integration tests for RAG pipeline
- [ ] End-to-end UI tests
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

**Last Updated**: 2025-01-28  
**Current Phase**: Phase 3 (Document Upload & Ingestion)  
**Active Branch**: `feature/doc-upload`
