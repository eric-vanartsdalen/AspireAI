# AspireAI Roadmap

## Vision

AspireAI is a configurable, modular Blazor-based chat assistant platform that supports conversation with local or hosted LLMs, document ingestion and RAG. Ideally, this will use flexible plug-in architecture — all designed for reuse, extension, and future upgrades (e.g. GraphRAG, multi-agent workflows).

Note: Updates to Aspire and .Net framework SDK will be likely and affected as separate branch updates. Expect these changes to occur outside of this roadmap.

---

## 2026-02-13 Stabilization Plan (Active)

This section captures the current execution plan so near-term priorities are not lost.

### 2026-02-14 Status Update

- Blazor upload currently saves file metadata into SQLite and writes the physical file with a timestamped filename.
- Uploaded files are visible in the Python container through the mapped `/app/data` volume.
- SQLite is visible in the Python container at `/app/database/data-resources.db`.
- Remaining blockers are downstream: processing persistence, LightRAG ingestion/query verification, and Python footprint minimization.

#### Observed Upload Row Example (from Web UI)

- `file_name`: timestamped stored filename (example: `Example_Emergency_Survival_Kit_20260111_010426_a0068769.pdf`)
- `original_file_name`: original user filename
- `file_path`: directory path currently persisted (not a full file path)
- `status`: currently written as `Uploaded` (capitalized)
- `source_type`: `upload`

#### Contract Implications from Observed Row

- Python processing must resolve full input path as `file_path + file_name` when `file_path` is stored as a directory.
- Status lifecycle handling must normalize/accept `Uploaded` and canonicalize to the processing workflow values.
- Contract alignment work should preserve both display name (`original_file_name`) and stored file identity (`file_name`).

### Current Reality Snapshot

- Upload flow is implemented in Blazor/Web and persists metadata in SQLite (`files`, `document_pages`).
- Chat currently streams from Ollama via Semantic Kernel without retrieval augmentation.
- Python service has processing/RAG endpoints, but several router/service contracts still reflect legacy document schemas and method signatures.
- LightRAG and Neo4j are orchestrated in AppHost, but runtime integration into chat retrieval is not complete.

### Canonical Decisions

- Canonical SQLite schema remains `files` + `document_pages`.
- Retrieval path for chat is **Web Chat -> Python `/rag` API** (primary orchestration point).
- Delivery priority is stabilization first, then feature expansion.
- Phase is not complete until data-path blockers are resolved end-to-end on this branch.
- Python service scope is intentionally minimal: keep only required SQLite fields and only required API endpoints for upload->process->retrieve.

### Non-Negotiable Blockers (Must Close Before Phase Exit)

- **Shared File Visibility**: Files uploaded by Aspire Web must be visible to Python in Docker via the mapped volume path used by Docling.
- **Docling -> LightRAG Chain**: Parsed free text from Docling must flow into LightRAG ingestion, with LightRAG operating against Neo4j backend.
- **Minimal Python Footprint**: Remove/avoid non-essential SQLite structures and non-essential API surface beyond required processing and retrieval flows.

### Incremental Execution Order

- **Contract Alignment (Blocker Removal)**: Align Python models/routers/services with canonical `files`/`document_pages`, normalize router-to-`database_service` method signatures, and unify lifecycle values (`uploaded`, `processing`, `processed`, `error`).

- **Upload Path Normalization**: Ensure Web file-path persistence and Python Docling file resolution use the same convention so uploaded files are always discoverable.

- **Processing Pipeline Stabilization**: Trigger processing from uploaded records, persist extracted pages to `document_pages`, and store processing timestamps/errors consistently.

- **RAG Ingestion Stabilization**: Keep a single Python-orchestrated ingestion/query flow (`Docling -> pages -> Neo4j/LightRAG`) until contracts are stable.

- **Chat Retrieval + Citations**: Query Python `/rag` before generation and render citation metadata (file, page, snippet) in chat responses.

- **Regression-Safe Testing Baseline**: Add smoke/integration checks for upload, processing, retrieval, and citation rendering, and gate progression on cross-service verification.

### Acceptance Gates (Per Milestone)

- **Gate A**: ✅ Upload record created with valid path + status and file exists on disk.
- **Gate B**: Python processing can load file and write `document_pages` rows.
- **Gate C**: RAG query returns source-bearing results from processed content.
- **Gate D**: Chat response displays citation references from retrieval results.
- **Gate E**: ✅ Volume-mounted file visibility is proven between Web upload location and Python container runtime.
- **Gate F**: Docling text output is successfully ingested to LightRAG and queryable through the Python retrieval path.
- **Gate G**: Python SQLite/API footprint is reduced to required minimum and documented.

### Test Bootstrap (First Iteration)

- **Smoke**: upload via controller/UI, list file, delete file.
- **Integration (Python)**: process uploaded record end-to-end and verify page persistence.
- **Integration (RAG)**: search processed content and validate source metadata fields.
- **E2E manual check**: upload -> process -> ask chat question -> verify citation appears.

---

## Phase 0: Repo Cleanup & Setup *(foundation)*

**Status**: ✅ Complete

**Objective**: Clean up existing repository, add scaffolding and core structure.  
**Outcomes**:

- Define solution/project layout
- Add and plugin scaffolds  
- Set up configuration and feature flags  
- Add basic README and contributor guidelines  
- Establish branch structure for feature development

**Branch suggestion**: `feature/setup`

---

## Phase 1: Basic Blazor Chat UI *(MVP)*

**Status**: ✅ Complete

**Objective**: Create a simple chat interface that can send and receive text-based messages from a backend LLM.  
**Outcomes**:

- Blazor chat page (conversation view, input box, "Send" button)  
- Backend stub endpoint that echoes or forwards messages  
- Wiring of chat frontend → backend → response display  
- Clean UI with user and assistant bubbles, timestamping, scroll behavior  

**Branch suggestion**: `feature/blazor-chat-ui`

---

## Phase 2: Speech-to-Text (Mic) & Text-to-Speech (TTS) Integration

**Status**: ✅ Complete

**Objective**: Enable voice input and voice output in the chat UI using browser-based APIs.  
**Outcomes**:

- ✅ JS interop for speech recognition (mic → text) using Web Speech API
- ✅ JS/Blazor interop for text-to-speech output using SpeechSynthesis API
- ✅ UI controls ("Start/Stop Mic", "Read Aloud") with status indicators  
- ✅ Clean fallback / error handling for browsers without speech support
- ✅ Real-time speech transcription with interim results
- ✅ Markdown-to-speech conversion for AI responses
- ✅ Individual message playback buttons
- ✅ Visual feedback with animated buttons during speech operations

**Branch suggestion**: `feature/speech-io`

**Documentation**: See [Speech Features Documentation](../docs/SPEECH_FEATURES.md) for detailed implementation details.

---

## Phase 3: Document Upload & Ingestion Pipeline

**Status**: ⏳ IN-PROGRESS

**Objective**: Allow users to upload documents (PDF, DOCX, etc.), which are chunked, embedded, and stored for later retrieval.  
**Outcomes**:

- Blazor file upload component (drag-and-drop or file picker)  
- Backend ingestion service: This might be best accomplished via a Python microservice using Docling, or a .NET wrapper around Docling.
  1. Convert uploaded files to DoclingDocument via Python or .NET bridge  
  2. Chunk documents using Docling (or alternative chunker) into **DocumentChunk** objects
  3. Optionally embed or serialize chunk metadata  
  4. Store chunk metadata and embeddings in a vector store or similar index  
- Metadata retention for chunk location, page, source, etc. to support footnotes and references  

**Branch suggestion**: `feature/doc-upload`

---

## Phase 4: Retrieval-Augmented Generation (RAG) — Flat Vector RAG

**Status**: ⏳ TO-DO

**Objective**: Implement simple RAG using flat vector retrieval to give chat responses augmented context from documents.  
**Outcomes**:

- `FlatVectorRetriever` implementation (see plugin scaffold)  
- Frontend config toggle for "Enable RAG" / "No RAG"  
- Backend retrieval logic:  
  1. Receive user query  
  2. Retrieve top-K chunks from vector store  
  3. Build prompt with context + query + optionally history  
  4. Send prompt to LLM via `IChatProvider`  
  5. Return response, including chunk citations/footnotes  
- UI display of footnote-style citations for sources (e.g. `[1]`, `(DocName – p3)`)  
- Optional UI to expand or view referenced chunk content  

**Branch suggestion**: `feature/rag-flat`

---

## Phase 5: RAG Enhancement — LightRAG or GraphRAG

**Status**: ⏳ TO-DO

**Objective**: Implement more advanced retrieval strategies (LightRAG or GraphRAG) and improve citation/graphical retrieval logic.  
**Outcomes**:

- `LightRagRetriever` or `GraphRagRetriever` plugin implementation  
- Graph construction from document chunks and metadata  
- Multi-hop retrieval or semantic filtering logic  
- Enhanced prompt builder logic for graph-based context selection  
- UI adjustments (e.g. showing reasoning paths or graph fragments)  
- Fallback strategy: if graph retrieval fails or is disabled, revert to flat vector retrieval  

**Branch suggestion**: `feature/rag-graph`

---

## Phase 6: Plugin Ecosystem & Community Extensibility

**Status**: ⏳ TO-DO

**Objective**: Harden AspireAI as a plugin-friendly, community extensible platform where others can add new LLM providers, retrieval strategies, or UI components.  
**Outcomes**:

- Documentation showing how to write and register a plugin (e.g. custom `IChatProvider` or `IRagRetriever`)  
- Example plugins:  
  - A custom LLM backend (e.g. OpenAI / Anthropic)  
  - A custom chunker or embedder (e.g. LangChain/LlamaIndex-based)  
  - A TTS provider using Azure Speech or other external API  
- Feature-flag based loading or discovery of plugins (configurable via `appsettings.json`)  
- Tests or validation for plugin registration and runtime swapping  
- README updates describing how to contribute new plugins  

**Branch suggestion**: `feature/plugin-ecosystem`

---

## Phase 7: UX Polish, Testing, Deployment, and Dockerization

**Status**: ⏳ TO-DO

**Objective**: Improve user experience, add robust testing, and prepare AspireAI for containerized deployment using Docker.  
**Outcomes**:

- UI/UX polish (loading states, error messages, conversation controls, upload progress)  
- Unit and integration tests for backend services, retrieval, ingestion, and prompt-building  
- End-to-end tests or manual test plans  
- Dockerfile(s) to containerize:
  - Blazor UI frontend  
  - Python / FastAPI backend (if used)  
  - Optional Ollama container or connection to a local Ollama instance  
- Example `docker-compose.yml` to spin up AspireAI + Ollama + retrieval store  
- CI/CD workflows (GitHub Actions) to build, test, and optionally deploy

**Branch suggestion**: `feature/deployment-ci`

---

## Phase 8: Advanced Features (Optional / Future)

**Status**: ⏳ TO-DO

These are stretch or future goals once the core is solid:

- Multi-agent chat flows (e.g. "Planner → Researcher → Writer")  
- Memory or long-term conversation recall beyond the current session  
- Web crawler or website ingestion to allow querying of live web pages  
- Real-time collaborative chat or document editing  
- Fine-tuning or instruct-based LLM customization  
- Mobile-friendly Blazor UI or PWA version  
- Analytics dashboard (chat usage, document query stats, RAG citations)

---

## Summary Table

| Phase | Key Focus | Branch | Status |
| ----- | --------- | ------ | ------ |
| Phase 0 | Repo setup, extension interface scaffolding | `feature/setup` | ✅ Complete |
| Phase 1 | Basic Blazor chat UI (text only) | `feature/blazor-chat-ui` | ✅ Complete |
| Phase 2 | Mic input (speech-to-text) and TTS (text-to-speech) | `feature/speech-io` | ✅ Complete |
| Phase 3 | Document upload and ingestion (Docling pipeline) | `feature/doc-upload` | ⏳ TO-DO |
| Phase 4 | Flat-vector RAG and citation footnotes | `feature/rag-flat` | ⏳ TO-DO |
| Phase 5 | LightRAG / GraphRAG retrieval strategies | `feature/rag-graph` | ⏳ TO-DO |
| Phase 6 | Plugin ecosystem and extensibility | `feature/plugin-ecosystem` | ⏳ TO-DO |
| Phase 7 | Testing, UX polish, Dockerization, CI/CD | `feature/deployment-ci` | ⏳ TO-DO |
| Phase 8 | Advanced / stretch features | `feature/advanced` | ⏳ TO-DO |

---

### Final Thoughts

- As you work through each branch and use Copilot, we can iterate on each feature one at a time.  
- The extension interfaces and plugin scaffolding help ensure that future changes — like swapping out Ollama for another LLM, or switching to GraphRAG — don't require heavy refactorings.  
- We'll revisit the `Roadmap.md` as things evolve — it's meant to be a living document.
