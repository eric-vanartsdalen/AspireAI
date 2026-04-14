# BRAIN Tasks - AspireAI

Working task breakdown for the [BRAIN Plan](Plan.md). Tracks what's been accomplished and what remains.

> **Warning: Maintainer Reminder:** This roadmap should be updated as work progresses. Check this file during implementation and mark items complete/blocked as they change.

Note: This will be a living document.

**Last Updated:** 2026-07-15

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
- [x] File upload UI component with SQLite metadata persistence
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

### Processing Pipeline Stabilization (P1)

- [x] Process uploaded records through Docling and persist page content in `document_pages`
- [x] Persist processing timestamps and error details consistently
- [x] Add retry behavior for failed processing records
- [x] Ensure processing status transitions use canonical values (`uploaded` to `processing` to `processed` / `error`)

### Docling to LightRAG Ingestion (P1)

- [x] Export Docling free-text output to markdown and stage it for LightRAG document scanning
- [x] **Prove a live LightRAG ingest to query round-trip** _(foundation: ingestion & idle pipeline proven; **query round-trip assertion pending P2**)_
- [x] **Confirm AppHost LightRAG graph storage stays on explicit Neo4j contract** _(foundation: config explicit; **live Neo4j state assertion pending P4**)_
- [x] Keep orchestration through Python retrieval APIs (no parallel retrieval path)

**Partial Coverage Note:** Items 2–3 are foundation-only in P1. Live query round-trip (item 2) requires full Knowledge Layer integration in Phase 2 before round-trip can be tested end-to-end. Live Neo4j state assertion (item 3) requires integration test harness added in Phase 4 (observability & cross-service testing).

---

## Phase 0: Reframe Product

### Project Structure and Branch Setup

- [ ] Create `brain-pivot` feature branch from current main
- [ ] Create `contracts/` directory at repo root for shared BRAIN data contracts
- [ ] Create `app/brain/` Python package structure:
  - `app/brain/__init__.py`
  - `app/brain/ingestion/` - connector and normalization modules
  - `app/brain/validation/` - claim extraction, confidence scoring
  - `app/brain/knowledge/` - Neo4j graph + vector, retrieval
  - `app/brain/reasoning/` - agent orchestration, proactive monitoring
- [ ] Create `app/contracts/` Python package for shared Pydantic models

**Files:** New directories + `__init__.py` files

### ApiService Repurpose to Gateway

- [ ] Delete weather forecast stub from `AspireApp.ApiService/Program.cs`
- [ ] Scaffold BRAIN API Gateway endpoints:
  - `POST /brain/chat` (stub - returns 501 until Phase 3)
  - `POST /brain/ingest` (stub - returns 501 until Phase 2)
  - `POST /brain/query` (stub - returns 501 until Phase 2)
  - `GET /brain/health`
- [ ] Add `Microsoft.Extensions.AI` package reference (replace Semantic Kernel dependency in gateway)
- [ ] Update AppHost to wire Gateway as entry point for Web frontend

**Files:**
- `src/AspireApp.ApiService/Program.cs`
- `src/AspireApp.ApiService/AspireApp.ApiService.csproj`
- `src/AspireApp.AppHost/AppHost.cs`

### Documentation and Config

- [ ] Update `README.md` to reflect BRAIN vision
- [ ] Resolve AI model config key mismatch - standardize `AI-Model` across AppHost and Web services
- [ ] Pin Python dependency versions in `requirements.txt`
- [ ] Consolidate duplicate `ServiceDiscoveryUtilities` classes into single shared class
- [ ] Remove legacy EF entity classes (`Document`, `ProcessedDocument`) that reference non-existent tables
- [ ] Update `.squad/decisions.md` with BRAIN pivot decision

---

## Phase 1: Core Contracts

### Python Contracts

- [ ] Define `CanonicalDocument` Pydantic model in `app/contracts/`
  - Fields: `tenant_id`, `document_id`, `source_type`, `source_confidence`, `pages: List[PageContent]`, `metadata: dict`
  - `PageContent`: `page_number`, `content`, `section`, `metadata`
- [ ] Define `ValidatedDocument` Pydantic model
  - Extends CanonicalDocument with `claims: List[Claim]`, `contradictions: List[Contradiction]`, `overall_confidence: float`
  - `Claim`: `claim_id`, `text`, `confidence`, `evidence: List[Evidence]`, `source_ref`
- [ ] Define `KnowledgeResult` Pydantic model
  - `results: List[KnowledgeItem]` with `content`, `confidence`, `source_refs`, `relevance_score`
- [ ] Define `ReasonResponse` Pydantic model
  - `answer`, `confidence`, `evidence: List[Evidence]`, `reasoning_steps: List[ReasoningStep]`, `proactive_suggestions: List[str]`
- [ ] Define common envelope mixin: `tenant_id`, `correlation_id`
- [ ] Define `IKnowledgeRetriever` ABC (Python)

### C# Contracts (Mirror)

- [ ] Define C# record types mirroring Python contracts
- [ ] Place in `AspireApp.ApiService/Contracts/` or new shared project
- [ ] Add `System.Text.Json` serialization attributes for JSON parity

### Cross-Language Validation

- [ ] Write serialization round-trip test: Python Pydantic to JSON to C# deserialization to JSON to Python deserialization
- [ ] Verify `tenant_id` and `correlation_id` present in all contract JSON

---

## Phase 2: Ingestion + Knowledge Baseline

### Ingestion Refactor

- [ ] Refactor `processing.py` to emit `CanonicalDocument` instead of raw page writes
- [ ] Add `source_confidence` tagging (textbook PDF: 0.9, general file: 0.7, URL: 0.5, user note: 0.3)
- [ ] Update SQLite `files` table: add `tenant_id` (TEXT, default 'default'), `source_confidence` (REAL)
- [ ] Implement ingestion trigger contract - Gateway `POST /brain/ingest` calls Python processing

### Knowledge Layer

- [ ] Extend Neo4j schema - add `Claim`, `Evidence`, `Concept`, `Entity` node labels with constraints
- [ ] Create Neo4j vector indexes on `Page.content` and `Claim.text` properties
- [ ] Implement `BrainKnowledgeRetriever` (graph traversal + vector similarity + confidence scoring)
- [ ] Implement `LightRAGRetriever` (wraps existing LightRAG query path behind `IKnowledgeRetriever`)
- [ ] **[P1 Carry-Forward] Prove live LightRAG ingest-to-query round-trip** — ingest document, validate it appears in Neo4j graph, query it back successfully
- [ ] Wire Gateway `POST /brain/query` to Knowledge retrieval
- [ ] Add Ollama embedding model usage for vector index population

### Validation Layer (Basic)

- [ ] Implement basic claim extraction using Ollama LLM
- [ ] Implement basic contradiction detection against existing Neo4j claims
- [ ] Wire: Ingestion to Validation to Knowledge storage path

---

## Phase 3: Ship MVP Agentic Slice

### Agent Framework Setup

- [ ] Select and integrate agent framework (evaluate LangGraph, CrewAI, Autogen)
- [ ] Define agent base contract: input, output, tools, memory access
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
- [ ] **[P1 Carry-Forward] Prove LightRAG-backed retrieval reads persisted Neo4j state** — integration test verifying that Knowledge Layer queries return documents ingested via LightRAG and confirmed in Neo4j
- [ ] Latency baselines - document acceptable response times

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
| P0-A | Feature branch exists with BRAIN directory structure | Not started | 0 |
| P0-B | ApiService weather stub deleted; Gateway scaffolded | Not started | 0 |
| P1-A | All BRAIN contracts defined (Python + C#) | Not started | 1 |
| P1-B | Serialization round-trip test passes | Not started | 1 |
| P2-A | Upload to CanonicalDocument to Neo4j storage end-to-end | Not started | 2 |
| P2-B | `/brain/query` returns confidence-scored results | Not started | 2 |
| P2-C | Neo4j vector indexes queryable | Not started | 2 |
| P3-A | `/brain/chat` returns evidence-backed response | Not started | 3 |
| P3-B | Multi-step reasoning visible | Not started | 3 |
| P3-C | Proactive Monitor flags contradiction | Not started | 3 |
| P3-D | Blazor chat routes through Gateway (no direct Ollama) | Not started | 3 |
| P3-G | Proactive suggestion appears without prompting | Not started | 3 |
| P4-A | Automated evaluation suite runs | Not started | 4 |
| P4-C | BRAIN says "insufficient evidence" for unknown topics | Not started | 4 |

---

## Implementation Challenges and Revisit Items

- **[Agent Framework Selection]** LangGraph, CrewAI, and Autogen are all viable. Selection should happen early in Phase 3 based on: ease of tool integration, multi-agent conversation support, and Python ecosystem maturity. Prototype with 2 candidates before committing.

- **[Confidence Calibration]** Source-type heuristics (textbook=0.9, web=0.5) are starting points. Real calibration requires evaluation data and feedback loops (Phase 4). Don't over-invest in scoring precision before Phase 4.

- **[Neo4j Vector Index Limitations]** Neo4j 5.x vector indexes work for MVP but may have performance/feature limitations compared to dedicated vector DBs. Monitor retrieval latency and recall quality. `IKnowledgeRetriever` abstraction allows swap to Qdrant if needed.

- **[LightRAG Schema Conflict]** LightRAG writes its own nodes to Neo4j separately from BRAIN schema. These coexist but are not integrated. If schema conflicts emerge, LightRAG may need its own Neo4j database or deprecation.

- **[Proactive Monitor Complexity]** Running a background agent that monitors knowledge state for contradictions and generates suggestions is architecturally different from request/response agents. May need an event-driven or polling approach. Design this carefully in Phase 3.

- **[MEai Migration]** Moving from Semantic Kernel to Microsoft.Extensions.AI in the C# layer is straightforward for chat completion. For embeddings, verify MEai `IEmbeddingGenerator` supports Ollama before removing SK dependency.
