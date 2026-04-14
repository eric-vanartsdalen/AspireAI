# BRAIN Tasks - AspireAI

Working task breakdown for the [BRAIN Plan](Plan.md). Tracks what's been accomplished and what remains.

> **Warning: Maintainer Reminder:** This roadmap should be updated as work progresses. Check this file during implementation and mark items complete/blocked as they change.

Note: This will be a living document.

**Last Updated:** 2026-04-17 — **P2-B COMPLETE:** `LightRagRetriever` enriches from Neo4j when provenance exists; unresolved confidence fails closed (no DEFAULT_CONFIDENCE fallback). **P2-C IN PROGRESS:** Vector index foundation implemented (indexes created, search methods ready, embedding config wired). **Next:** Populate embeddings for stored Pages/Claims; integrate vector search into retrievers; (P2) finish integration docs + contract round-trip coverage; (P3) select the agent framework and move contradiction detection into the Critic Agent slice.

---

## Completed Work (Pre-Pivot)

### P0 - Pipeline Contract Alignment (Done)

- [x] Fix `save_document_page` invocation/signature mismatch - aligned caller kwargs with service signature (`e9d90ea`)
- [x] Align `document_pages` FK column - C# `[Column("file_id")]`, Python `file_id`, DB `file_id INTEGER NOT NULL` (`6e5b34b`, `77db074`)
- [x] Fix Python router/service contract mismatches - added 9 backward-compatibility wrappers in DatabaseService (`a8e1b71`)
- [x] Normalize upload status casing - FileUploadController writes `"uploaded"` (lowercase) consistently (`62ee545`)
- [x] Validate uploaded rows are locatable by Python prior to processing
- [x] Verify Docker volume mapping exposes uploaded files to Python container at runtime

### Phase 0-2 (Done)

- [x] Solution/project structure and Aspire AppHost orchestration
- [x] Blazor chat UI with message history, user/assistant bubbles, auto-scroll
- [x] Backend LLM integration (Ollama via Semantic Kernel)
- [x] Speech-to-text and text-to-speech (Web Speech API interop)
- [x] File upload UI component with persisted metadata storage (legacy SQLite foundation, now Postgres-backed operationally)
- [x] Timestamped file storage with `original_file_name` / `file_name` distinction

### Upload Path Normalization (P0)

- [x] Resolve full physical file path as `file_path` (directory) + `file_name` (stored timestamped filename) in Python
- [x] Add guardrails for Windows-style DB paths to container runtime paths
- [x] Python resolves physical upload paths correctly; Docling receives resolved full path
- [x] Validation cleared with Python contract audit (4/4 green)

### Python Footprint Minimization (P0)

- [x] Remove non-essential SQLite usage patterns and legacy schema dependencies
- [x] Minimize API endpoints to required upload to process to retrieve lifecycle
- [x] Document the retained endpoint/database contract surface
- [x] Live Python runtime now uses canonical `files` / `document_pages` contract
- [x] Non-essential Python admin/perf/schema-sync surface trimmed
- [x] Maintainer-facing docs/scripts updated; retired `documents` / `processed_documents` semantics removed
- [x] Schema verification against canonical temp DB and compile checks passed

### SQLite Startup Schema Repair (P0)

- [x] `DatabaseService` now repairs stale canonical `files` columns before creating dependent indexes at startup
- [x] `test_services.py` now runs as a real pytest smoke suite and no longer masks database startup failures
- [x] Regression coverage now exercises the stale `file_hash` upgrade path

### Phase 0 Closeout Note

> Phase 0 is complete. Outstanding Docker-backed integration validation (cold-start orchestration, service health checks, cross-container volume access) has been carried forward to Phase 4 (Evaluate + Harden) as part of the cross-service integration test suite — see Phase 4 quality gate P4-B.

### Processing Pipeline Stabilization (P1)

- [x] Process uploaded records through Docling and persist page content in `document_pages`
- [x] Persist processing timestamps and error details consistently
- [x] Add retry behavior for failed processing records
- [x] Ensure processing status transitions use canonical values (`uploaded` to `processing` to `processed` / `error`)

### Docling to LightRAG Ingestion (P1)

- [x] Export Docling free-text output to markdown and stage it for LightRAG document scanning
- [x] **Prove a live LightRAG ingest to query round-trip** _(closed in Phase 2 via `BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip`)_
- [x] **Confirm AppHost LightRAG graph storage stays on explicit Neo4j contract** _(runtime verified in Phase 2 via live Neo4j graph growth assertions against the shared database)_
- [x] Keep orchestration through Python retrieval APIs (no parallel retrieval path)

**Coverage Note:** The original P1 carry-forward on live LightRAG query round-trip and explicit Neo4j runtime verification is now closed in Phase 2. Remaining later-phase work is hardening and broader cross-service quality coverage, not first-proof validation.

---

## Phase 0: Reframe Product

### Project Structure and Branch Setup

- [x] Create `brain-pivot` feature branch from current main — *Created by Eric (2026-07-15)*
- [x] Create `contracts/` directory at repo root for shared BRAIN data contracts
- [x] Create `app/brain/` Python package structure:
  - `app/brain/__init__.py`
  - `app/brain/ingestion/` - connector and normalization modules
  - `app/brain/validation/` - claim extraction, confidence scoring
  - `app/brain/knowledge/` - Neo4j graph + vector, retrieval
  - `app/brain/reasoning/` - agent orchestration, proactive monitoring
- [x] Create `app/contracts/` Python package for shared Pydantic models

**Files:** New directories + `__init__.py` files

### ApiService Repurpose to Gateway

- [x] Delete weather forecast stub from `AspireApp.ApiService/Program.cs`
- [x] Scaffold BRAIN API Gateway endpoints:
  - `POST /brain/chat` (stub - returns 501 until Phase 3)
  - `POST /brain/ingest` (stub - returns 501 until Phase 2)
  - `POST /brain/query` (stub - returns 501 until Phase 2)
  - `GET /brain/health`
- [x] Add `Microsoft.Extensions.AI` package reference (replace Semantic Kernel dependency in gateway)
- [x] Update AppHost to wire Gateway as entry point for Web frontend

**Files:**
- `src/AspireApp.ApiService/Program.cs`
- `src/AspireApp.ApiService/AspireApp.ApiService.csproj`
- `src/AspireApp.AppHost/AppHost.cs`

### Documentation and Config

- [x] Update `README.md` to reflect BRAIN vision
- [x] Resolve AI model config key mismatch - standardize `AI-Model` across AppHost and Web services
- [x] Pin Python dependency versions in `requirements.txt`
- [x] Consolidate duplicate `ServiceDiscoveryUtilities` classes into single shared class
- [x] Remove legacy EF entity classes (`Document`, `ProcessedDocument`) that reference non-existent tables
- [x] Update `.squad/decisions.md` with BRAIN pivot decision

---

## Phase 1: Core Contracts

### Python Contracts

- [x] Define `CanonicalDocument` Pydantic model in `app/contracts/`
  - Fields: `tenant_id`, `document_id`, `source_type`, `source_confidence`, `pages: List[PageContent]`, `metadata: dict`
  - `PageContent`: `page_number`, `content`, `section`, `metadata`
- [x] Define `ValidatedDocument` Pydantic model
  - Extends CanonicalDocument with `claims: List[Claim]`, `contradictions: List[Contradiction]`, `overall_confidence: float`
  - `Claim`: `claim_id`, `text`, `confidence`, `evidence: List[Evidence]`, `source_ref`
- [x] Define `KnowledgeResult` Pydantic model
  - `results: List[KnowledgeItem]` with `content`, `confidence`, `source_refs`, `relevance_score`
- [x] Define `ReasonResponse` Pydantic model
  - `answer`, `confidence`, `evidence: List[Evidence]`, `reasoning_steps: List[ReasoningStep]`, `proactive_suggestions: List[str]`
- [x] Define common envelope mixin: `tenant_id`, `correlation_id`
- [x] Define `IKnowledgeRetriever` ABC (Python)

### C# Contracts (Mirror)

- [x] Define C# record types mirroring Python contracts
- [x] Place in `AspireApp.ApiService/Contracts/` or new shared project
- [x] Add `System.Text.Json` serialization attributes for JSON parity

### Cross-Language Validation

- [x] Define C# contract types: `BrainContractEnvelope`, `CanonicalDocument`, `ValidatedDocument`, `KnowledgeResult`, `ReasonResponse` with JSON property mapping
- [x] Define Python contract types mirroring C#: Pydantic models with `BaseModel` inheritance and field validation
- [x] Verify `tenant_id` and `correlation_id` present in all contract definitions (C# and Python)
- [x] **[Phase 2 P2-A Dependency]** Write serialization round-trip test: Python Pydantic to JSON to C# deserialization to JSON to Python deserialization

---

## Phase 2: Ingestion + Knowledge Baseline

> **Phase 2 Implementation Directive:** Complete the three-layer pipeline (Ingestion → Validation → Knowledge Storage) end-to-end. All items are gated on Phase 1 cross-language round-trip test (P2-A dependency). Prioritize: (1) Ingestion contract + refactor; (2) Neo4j schema + retriever interface; (3) Validation + wiring.

### Ingestion Refactor (Jarvis lead, Jeff review)

- [x] Refactor `processing.py` to call `build_canonical_document()` and emit the canonical shape; persist `source_confidence` in `files` table (update `processing.py` and `database_service.py` to respect Phase 1 contract shape)
- [x] Add `source_confidence` tagging (textbook PDF: 0.9, general file: 0.7, URL: 0.5, user note: 0.3)
- [x] Update operational `files` table schema/handling: keep `tenant_id` support and add `source_confidence` (REAL) migration/defaults
- [x] **[P2-A Gate Dependency]** Write serialization round-trip test (Buster): Python `CanonicalDocument` → JSON → C# deserialization → JSON → Python
- [x] Implement ingestion trigger contract - Gateway `POST /brain/ingest` calls Python processing (Jeff: wire Gateway endpoint; Jarvis: implement Python endpoint)

### Knowledge Layer (Jarvis lead)

- [x] Extend Neo4j schema - add `Claim`, `Evidence`, `Concept`, `Entity` node labels with `IS UNIQUE` constraints on `(label).id` properties
  - **Unblocks P2-B:** Schema constraints implemented in `neo4j_service.py`
  - Claims can now be stored with their own confidence scores
- [x] **[P2-C FOUNDATION]** Create Neo4j vector indexes on `Page.content_embedding` and `Claim.text_embedding` properties
  - ✅ **Vector indexes implemented** (Jarvis 2026-04-17): `page_content_vector` and `claim_text_vector` are created in `_ensure_vector_indexes()` (idempotent on Neo4jService startup)
  - ✅ **Vector search methods ready** (Jarvis 2026-04-17): `search_claims_vector()` and `search_pages_vector()` use Neo4j 5.x vector similarity syntax
  - ✅ **EmbeddingService foundation** (Jarvis 2026-04-17): consumes Aspire `OLLAMA_ENDPOINT` / `EMBEDDING_MODEL` config when present, with local fallback for direct Python runs
  - ✅ **AppHost embedding config complete** (Jeff 2026-04-17): Python services receive `OLLAMA_ENDPOINT`, `EMBEDDING_MODEL`, `EMBEDDING_DIM` via Aspire environment variables
  - ⏳ **Remaining P2-C work:** Populate `content_embedding` and `text_embedding` properties during document ingestion; wire vector search into `SemanticKnowledgeRetriever`
  - **Next Owner:** Jarvis (embedding population pipeline)
- [x] Implement `BrainKnowledgeRetriever` orchestration seam
  - [x] Interface implemented, LightRAG-first + fallback pattern tested (proves contract and routing)
  - [x] Confidence scoring from stored claims — `SemanticKnowledgeRetriever` queries Claims first, falls back to Pages
  - [ ] Graph traversal and vector similarity ranking (P2-C gate: requires vector indexes)
- [x] Implement `LightRAGRetriever` (wraps existing LightRAG query path behind `IKnowledgeRetriever`)
- [x] **[P1 Carry-Forward] Prove live LightRAG ingest-to-query round-trip** — covered by `BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip` (upload/process → LightRAG scan → Neo4j graph checks → live `/brain/query`)
- [x] Wire Gateway `POST /brain/query` to contract-shaped Python retrieval seam (LightRAG-first + Neo4j fallback)
  - [x] HTTP contract verified via `BrainGatewayPhase2Tests.QueryKnowledgeAsync_MapsContractShapedKnowledgeResult_FromPythonQueryRoute`
  - [ ] Full gateway orchestration (Reasoning Layer, Evidence synthesis) — deferred to Phase 3
- [ ] Add Ollama embedding model usage for vector index population (Jarvis + Ollama config coordination)
- [x] Implement semantic fallback confidence scoring — `SemanticKnowledgeRetriever` now retrieves real confidence from Neo4j Claim/Page nodes
- [x] **[Complete]** Close the LightRAG-first confidence gap — `LightRagRetriever` enriches unscored results from Neo4j when provenance is resolvable; **unresolved confidence now fails closed** (returns empty, forcing semantic fallback) instead of defaulting to 0.5

**P2-B STATUS: ✅ COMPLETE.** Claim-based confidence scoring implemented. `SemanticKnowledgeRetriever` queries Claim nodes first, falls back to Page nodes. Claim extraction wired into ingestion pipeline. LightRAG-first path enriches unscored results via `Neo4jService.get_confidence_by_provenance()` when provenance exists. When enrichment fails, results filtered out (fail-closed) forcing semantic fallback. Tests verify enrichment, fail-closed filtering, and explicit score preservation.

**P2-C STATUS: 🟡 IN PROGRESS.** Vector indexes (`page_content_vector`, `claim_text_vector`) are created and query helpers exist. Python services now receive embedding config from AppHost, and `EmbeddingService` consumes that Ollama path with a local fallback for direct Python runs. **Remaining work:** Populate embeddings during ingestion and wire vector search into retrievers before claiming live vector-backed retrieval.

### Validation Layer (Basic) (Jarvis lead)

**STATUS:** P2-B complete (confidence scoring). Basic claim extraction wired into ingestion. Contradiction detection remains outstanding (non-blocking; Phase 3 leverage for agents).

- [x] Implement basic claim extraction service — `ClaimExtractionService` extracts sentence-based claims with confidence heuristics (Phase 2 baseline; LLM extraction deferred to Phase 3)
- [x] **[P2-B Gate Foundation]** Confidence scoring strategy for semantic retrieval — `SemanticKnowledgeRetriever` now surfaces real confidence from stored Claim/Page nodes (tested in `test_knowledge_retriever.py`)
- [x] Wire: Ingestion → Validation → Knowledge storage path — integrate `ClaimExtractionService` into processing pipeline, call `neo4j_service.create_claim_nodes()` after page creation (**COMPLETED 2026-04-15** — `processing.py` now extracts claims after page nodes; regression coverage added)
- [x] Extend Neo4j schema to support Claim/Evidence/Concept/Entity nodes with confidence properties — schema constraints implemented
- [ ] **[P3 Outstanding → Phase 3 Critic Agent]** Implement contradiction detection against Neo4j claims (graph query pattern) — query `Claim` nodes for semantic conflicts. Foundation layer complete (P2-B); integrate as Critic Agent tool in Phase 3 reasoning layer.

### Cross-Layer Integration (Jeff + Jarvis) — PHASE 2 OUTSTANDING

- [ ] **[P2 Documentation]** Document the Ingest → Validate → Store → Retrieve contract surface for Phase 3 agents (focus: confidence scoring, evidence linkage, provenance resolution)
- [ ] Ensure all contract round-trips serialize consistently (use Phase 1 round-trip test as regression) — coordinate with Eric if regressions surface

---

## Phase 3: Ship MVP Agentic Slice

> **PHASE 3 UNBLOCK SEQUENCE:**
> 1. **[IMMEDIATE]** Select agent framework (LangGraph vs CrewAI vs Autogen). 2-day evaluation prototype; decision by end of sprint. *Owner: Bob + Jarvis (research) + Jeff (C# integration assessment)*
> 2. **[P3-A Gate Prerequisite]** Define agent base contract (input, output, tools) — must finalize before reasoning agents start writing code
> 3. **[P3-A]** Implement Retriever + Synthesizer agents (routes `/brain/query` + confidence → coherent response)
> 4. **[P3-B]** Multi-step reasoning: Planner agent decomposes query → Retriever executes → Critic evaluates → responds
> 5. **[P3-D]** Blazor chat integration: route through Gateway `/brain/chat` (no direct Ollama)
> 6. **[P3-C + P3-G]** Proactive Monitor (background loop, contradiction detection, unsolicited insights)

### Agent Framework Setup — BLOCKING GATE

- [ ] **[URGENT]** Select and integrate agent framework (evaluate LangGraph, CrewAI, Autogen)
   - Evaluation criteria: Tool integration ease, multi-agent conversation support, Python ecosystem maturity, documentation quality
   - Decision: End of sprint (2026-04-24)
   - Owner: Bob (architecture decision), Jarvis (Python prototyping), Jeff (C# backend compatibility check)
- [ ] Define agent base contract: input, output, tools, memory access (align with BrainQueryRequest/ReasonResponse contracts)
- [ ] Set up agent orchestration pipeline in `app/brain/reasoning/`

### Core Agents

- [ ] Retriever Agent - queries Knowledge Layer with confidence-aware ranking
- [ ] Synthesizer Agent - combines multiple knowledge results into coherent responses
- [ ] Critic Agent - evaluates response quality, identifies gaps, scores confidence
- [ ] Planner Agent - decomposes complex questions into reasoning steps
- [ ] Proactive Monitor - detects contradictions, generates unsolicited insights

### BRAIN Chat Endpoint

- [ ] Implement `POST /brain/chat` full pipeline: Gateway to Reasoning to Knowledge to Response
- [ ] Add session memory - conversation context persists across turns
- [ ] Return `ReasonResponse` with answer, confidence, evidence, reasoning steps, proactive suggestions

### UI Integration

- [ ] Migrate Blazor chat from direct Ollama/SK to Gateway `/brain/chat`
- [ ] Replace Semantic Kernel with `Microsoft.Extensions.AI` in C# layer
- [ ] Add confidence score display on chat responses
- [ ] Add source citation links (document, page, claim)
- [ ] Add proactive suggestion panel - unsolicited insights from Proactive Monitor
- [ ] Add "BRAIN is thinking" indicator showing multi-step reasoning progress

---

## Phase 4: Evaluate + Harden

### Observability

- [ ] Replace `Console.WriteLine` with `ILogger<T>` in high-impact C# files
- [ ] Add correlation ID propagation across all service calls
- [ ] Agent decision logging - trace every agent reasoning step and tool call
- [ ] Structured logging in Python (JSON format with context)

### Quality and Testing

- [ ] Agent evaluation framework - automated scoring of responses
- [ ] Retrieval quality metrics - precision/recall against known test sets
- [ ] Honest failure mode tests - verify "I don't know" for out-of-knowledge questions
- [ ] Cross-service integration test suite (upload to ingest to validate to store to query to chat to cite)
- [ ] **[P0 Carry-Forward] Docker-backed integration validation** — cold-start Aspire orchestration, verify all service health checks pass, validate cross-container volume access (uploads mounted from host to Python container)
- [x] **[P1 Carry-Forward] Prove LightRAG-backed retrieval reads persisted Neo4j state** — covered by `BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip` (upload/process → LightRAG scan → Neo4j graph checks → live `/brain/query`)
- [ ] Latency baselines - document acceptable response times

### Knowledge Quality Optimization

- [ ] **Chunking Strategy Review** — Assess document chunking granularity (page-level vs. sub-page chunks) and overlap strategy
  - Measure retrieval quality impact: precision/recall with current page-level chunking vs. proposed multi-level chunking with overlap
  - Define optimal chunk size and stride (overlap percentage) for domain documents
  - Implement cross-page context preservation mechanism — preserve section/chapter boundaries and preceding/following content reference for improved reasoning context
  - Document chunking strategy choice and rationale in Knowledge Layer design doc
  - *Owner: Jarvis (Ingestion/Knowledge Lead)*

### Code Quality (Carried Forward)

- [ ] Fix `OllamaWarmupService` - inject `IHttpClientFactory` instead of raw `new HttpClient()`
- [ ] Remove redundant `IConfiguration` registration in `Web/Program.cs`
- [ ] Update `ServiceDefaults/Extensions.cs` - map health checks in all environments
- [ ] Optimize Neo4j batch writes - move to `UNWIND` patterns
- [ ] Align SemanticKernel package versions (or remove SK entirely if MEai migration complete)

---

## Phase 5: Prove Reusability

- [ ] Add URL ingestion connector - fetch, parse, normalize web content
- [ ] Verify URL sources receive lower initial confidence than uploaded documents
- [ ] Document the extension pattern - how to add new connectors, validators, agents
- [ ] Verify no BRAIN core contract changes required for new connector

---

## Phase 6: Scale Deliberately

- [ ] Multi-tenant isolation enforcement - tenant filtering on all Neo4j queries
- [ ] Authentication and authorization for API Gateway
- [ ] Plugin ecosystem - documentation and registry
- [ ] Advanced graph reasoning - multi-hop traversal, temporal queries
- [ ] Knowledge evolution tracking - semantic diffing between document versions
- [ ] External LLM provider support (OpenAI, Anthropic) via MEai
- [ ] Production deployment artifacts

---

## Milestone Gates

| Gate | Criteria | Status | Phase |
|------|----------|--------|-------|
| P0-A | Feature branch exists with BRAIN directory structure | Complete | 0 |
| P0-B | ApiService weather stub deleted; Gateway scaffolded | Complete | 0 |
| P1-A | All BRAIN contracts defined (Python + C#) | Complete | 1 |
| P1-B | Serialization round-trip test passes | Complete | 1 |
| P2-A | Upload to CanonicalDocument to Neo4j storage end-to-end | Complete | 2 |
| P2-B | `/brain/query` returns confidence-scored results (no default fallback) | ✅ **COMPLETE** — LightRAG enriches from Neo4j when provenance exists; unresolved confidence fails closed. Live proof: `BasicAspireAppHostTests.BrainQueryReturnsConfidenceEnrichedResults`. | 2 |
| P2-C | Neo4j vector indexes queryable | 🟡 **IN PROGRESS** — Vector indexes are created (`page_content_vector`, `claim_text_vector`) and search helpers exist. Embedding config is wired, but live vector retrieval still depends on populating embedding properties during ingestion and routing retrievers through the new vector path. | 2 |
| P3-A | `/brain/chat` returns evidence-backed response | **Blocked on agent framework selection** (due end of sprint 2026-04-24). Once framework chosen, implement Retriever + Synthesizer agents. | 3 |
| P3-B | Multi-step reasoning visible | Blocked on P3-A (agent framework + base agents). Requires Planner + Critic agent implementations. | 3 |
| P3-C | Proactive Monitor flags contradiction | Blocked on P3-A. Requires background agent monitoring Claim nodes for conflicts. | 3 |
| P3-D | Blazor chat routes through Gateway (no direct Ollama) | Blocked on P3-A `/brain/chat` implementation. UI integration via C# `BrainBackendClient` to Gateway. | 3 |
| P3-G | Proactive suggestion appears without prompting | Blocked on P3-C (Proactive Monitor completion). Requires UI panel for unsolicited insights. | 3 |
| P4-A | Automated evaluation suite runs | Not started | 4 |
| P4-B | Docker-backed integration validation passes (cold-start, health checks, volume access) | Not started | 4 |
| P4-C | BRAIN says "insufficient evidence" for unknown topics | Not started | 4 |

---

## Implementation Challenges and Revisit Items

- **[P2-B Complete]** Confidence fail-closed behavior implemented. `LightRagRetriever._build_item()` returns None when confidence cannot be resolved (provenance missing or Neo4j enrichment returns None), filtering results out and forcing semantic fallback. Validated by `test_lightrag_retriever_fails_closed_when_neo4j_returns_none` and `test_lightrag_retriever_without_neo4j_service_fails_closed`.

- **[Agent Framework Selection]** LangGraph, CrewAI, and Autogen are all viable. Selection should happen early in Phase 3 based on: ease of tool integration, multi-agent conversation support, and Python ecosystem maturity. Prototype with 2 candidates before committing.

- **[Confidence Calibration]** Source-type heuristics (textbook=0.9, web=0.5) are starting points. Real calibration requires evaluation data and feedback loops (Phase 4). Don't over-invest in scoring precision before Phase 4.

- **[Neo4j Vector Index Limitations]** Neo4j 5.x vector indexes work for MVP but may have performance/feature limitations compared to dedicated vector DBs. Monitor retrieval latency and recall quality. `IKnowledgeRetriever` abstraction allows swap to Qdrant if needed.

- **[LightRAG Schema Conflict]** LightRAG writes its own nodes to Neo4j separately from BRAIN schema. These coexist but are not integrated. If schema conflicts emerge, LightRAG may need its own Neo4j database or deprecation.

- **[Proactive Monitor Complexity]** Running a background agent that monitors knowledge state for contradictions and generates suggestions is architecturally different from request/response agents. May need an event-driven or polling approach. Design this carefully in Phase 3.

- **[MEai Migration]** Moving from Semantic Kernel to Microsoft.Extensions.AI in the C# layer is straightforward for chat completion. For embeddings, verify MEai `IEmbeddingGenerator` supports Ollama before removing SK dependency.
