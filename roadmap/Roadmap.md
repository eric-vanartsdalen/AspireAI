# AspireAI Roadmap

## Vision
AspireAI is a configurable, modular Blazor-based chat assistant platform that supports conversation with local or hosted LLMs, document ingestion and RAG. Ideally, this will use flexible plug-in architecture — all designed for reuse, extension, and future upgrades (e.g. GraphRAG, multi-agent workflows).

Note: Updates to Aspire and .Net framework SDK will be likely and affected as separate branch updates. Expect these changes to occur outside of this roadmap.

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

**Documentation**: See [Speech Features Documentation](docs/SPEECH_FEATURES.md) for detailed implementation details.

---

## Phase 3: Document Upload & Ingestion Pipeline

**Status**: ⏳ TO-DO

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
|------|-----------|--------|--------|
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

If you want, I can also generate a **starter Blazor Chat component + JS interop module** for mic and speech synthesis as a base-coded file to drop into your repo.
::contentReference[oaicite:3]{index=3}
