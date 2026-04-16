# BRAIN Plan — AspireAI

Phased roadmap for the BRAIN pivot. Each phase is an epic with clear acceptance gates. This document tracks the active BRAIN roadmap against the current branch state.

**Last Updated:** 2026-04-21
**Active Branch:** `task/P0-brain-pivot`
**Decision Authority:** Eric Van Artsdalen + adversarial review by Kujan and Verbal

---

## Vision

BRAIN is a domain-agnostic agentic knowledge assistant. It ingests diverse sources, scores their trustworthiness, coalesces knowledge into an expanding graph, and acts as a proactive advisor — suggesting insights, flagging contradictions, and offering context unprompted. Chat is one interface over a reasoning engine that knows what it knows and how well it knows it.

---

## Current Execution Snapshot

- **MVP ACHIEVED ✅** (2026-04-21): Gateway-routed chat with Regular mode works end-to-end (document upload → knowledge graph → retrieval-augmented chat with citations). Multi-conversation persistence and authentication are operational. Critique mode is implemented but experimental.
- **Done on this branch:** Legacy phases 0–3, BRAIN Phase 1 (shared contracts), BRAIN Phase 2 (gateway ingest/query, retrieval foundations, vector population/search), and a Phase 3a/3b chat slice (regular + critique routes, gateway-routed chat, Web UI confidence/citations, and critique reasoning display).
- **Post-MVP Fixes (High Priority):** (1) Conversation context not passed to backend on follow-up questions — LLM doesn't receive prior conversation history for multi-turn reasoning. (2) Gateway evidence not persisted with messages — citations/confidence lost when reopening saved conversations.
- **Still active (Phase 3 gaps):** Session memory integration, contradiction/proactive monitoring beyond critique pipeline, proactive suggestions UI, MEai cleanup, chat-mode transition regression coverage.
- **Administrative drift:** Phase 0 setup is complete; README now reflects BRAIN MVP status and gateway role.
- **Next:** Close the two post-MVP fixes (conversation context + evidence persistence), then Phase 4 evaluation/hardening.

---

## Phase Summary

| Phase | Focus | Status |
|-------|-------|--------|
| Legacy 0–2 | Repo Setup, Chat UI, Speech I/O | ✅ Complete (pre-pivot) |
| Legacy 3 | Document Upload & Ingestion (stabilization) | ✅ Complete (pre-pivot) |
| 0 | Reframe Product | ✅ Complete |
| 1 | Core Contracts | ✅ Complete |
| 2 | Ingestion + Knowledge Baseline | ✅ Complete |
| 3 | Ship MVP Agentic Slice | ✅ **MVP Achieved** (post-MVP fixes in progress) |
| 4 | Evaluate + Harden | 🔜 Planned |
| 5 | Prove Reusability | 🔮 Future |
| 6 | Scale Deliberately | 🔮 Future |

---

## ✅ Pre-Pivot Work (Legacy Phases 0–3)

Work completed before the BRAIN pivot. These are foundations that survive:

- Solution/project structure and Aspire AppHost orchestration
- Blazor chat UI with message history, speech I/O
- Ollama integration via Semantic Kernel (to be migrated to MEai)
- File upload with operational metadata persistence (now Postgres-backed on this branch)
- Docling document parsing (PDF/DOCX)
- LightRAG entity extraction proven (handoff + query round-trip)
- Pipeline contract alignment (P0 stabilization complete)
- Processing pipeline stabilization (P1 complete)
- Python footprint minimization and schema repair

**What carries forward:** Aspire orchestration, Docling parsing, Neo4j/Ollama containers, the operational `files`/`document_pages` schema, health patterns, and volume strategy.

**What is superseded:** Phases 4-8 from the original plan (Flat Vector RAG, LightRAG/GraphRAG, Plugin Ecosystem, Testing/Deployment, Advanced Features). These are replaced by the BRAIN phase sequence below.

---

## 🚧 Phase 0: Reframe Product

**Objective:** Declare BRAIN as the core product and keep the pivot structure, wiring, and documentation aligned with the branch reality.

### Deliverables

- [x] Pivot work is already on `task/P0-brain-pivot`; no additional branch-setup work is required for this phase
- [ ] Update README.md to reflect the BRAIN vision (the current README still uses pre-pivot AspireAI framing)
- [x] Create `contracts/` directory structure for shared BRAIN data contracts
- [x] Create `app/brain/` Python package structure (ingestion, validation, knowledge, reasoning)
- [x] Repurpose `AspireApp.ApiService` — weather stub removed, BRAIN API Gateway endpoints scaffolded and wired
- [x] Update `.squad/decisions.md` with pivot decision and rationale
- [x] Update Aspire AppHost wiring to reflect new service roles (gateway, Python service, graph store, Ollama)

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P0-A | Feature branch exists with BRAIN directory structure |
| P0-B | ApiService weather stub is deleted; Gateway shell is scaffolded |
| P0-C | README reflects BRAIN vision |
| P0-D | `dotnet build` succeeds; Aspire dashboard shows renamed services |

**Review result:** P0-A and P0-B are satisfied on this branch. P0-C remains open. P0-D is partially evidenced here (`dotnet build` succeeds); dashboard naming was not revalidated in this review.

---

## ✅ Phase 1: Core Contracts

**Objective:** Define the shared data contracts that every BRAIN layer depends on. These are the foundation — nothing else can be built correctly without them.

### Deliverables

- [x] Define `CanonicalDocument` contract (Python Pydantic + C# record)
  - Required: `tenant_id`, `document_id`, `source_type`, `source_confidence`, `pages[]`, `metadata`
  - Binary payloads handled via file references, not embedded
  - Multi-page represented as ordered page array with content + page number
- [x] Define `ValidatedDocument` contract
  - Extends CanonicalDocument with `claims[]`, `contradictions[]`, `overall_confidence`
- [x] Define `KnowledgeResult` contract
  - For retrieval responses: `results[]` with `content`, `confidence`, `source_refs[]`, `relevance_score`
- [x] Define `ReasonResponse` contract
  - Agent output: `answer`, `confidence`, `evidence[]`, `reasoning_steps[]`, `proactive_suggestions[]`
- [x] Define common envelope: `tenant_id`, `correlation_id`
- [x] Define `IKnowledgeRetriever` interface contract (Python ABC)
- [x] Cross-language validation — ensure Python Pydantic and C# record serialize to identical JSON
- [x] Tenant ID included in all contracts with default value for single-tenant operation

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P1-A | All contracts defined in `contracts/` with both Python and C# representations |
| P1-B | Round-trip serialization test passes (Python → JSON → C# → JSON → Python) |
| P1-C | Every contract includes `tenant_id` and `correlation_id` |
| P1-D | `IKnowledgeRetriever` interface defined with at least two planned implementations |

**Review result:** All Phase 1 gates are satisfied via `src/AspireApp.ApiService/Contracts/`, `src/AspireApp.PythonServices/app/contracts/`, and `src/AspireApp.WebTest/Tests/BrainContractRoundTripTests.cs`.

---

## ✅ Phase 2: Ingestion + Knowledge Baseline

**Objective:** File upload flows through BRAIN contracts. Documents are normalized to `CanonicalDocument`, stored in the extended Neo4j knowledge graph, and retrievable via the Knowledge Layer with source attribution.

### Deliverables

- [x] Refactor Docling processing to emit `CanonicalDocument` (not raw page writes)
- [x] Add `source_confidence` tagging based on source type (upload/URL/API)
- [x] Extend Neo4j schema — add `Claim`, `Evidence`, `Concept`, `Entity` node labels alongside existing `Document`/`Page`
- [x] Create Neo4j vector indexes on key properties (claim text, page content)
- [x] Implement `BrainKnowledgeRetriever` — confidence-aware retrieval combining graph traversal + vector similarity
- [x] Implement `LightRAGRetriever` — wraps existing LightRAG query path behind `IKnowledgeRetriever`
- [x] Wire Gateway (`POST /brain/ingest`) → Ingestion → Knowledge storage path
- [x] Wire Gateway (`POST /brain/query`) → Knowledge retrieval path
- [x] Update the operational `files` store to carry `tenant_id` and `source_confidence` without breaking existing rows
- [x] Add embedding generation using Ollama embedding model for vector index population

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P2-A | Upload → Docling → `CanonicalDocument` → Neo4j storage works end-to-end |
| P2-B | `POST /brain/query` returns results with confidence scores and source references |
| P2-C | Neo4j vector indexes created and queryable |
| P2-D | Both `BrainKnowledgeRetriever` and `LightRAGRetriever` pass the same interface tests |
| P2-E | Operational `files` rows include `tenant_id` (default value) |

**Review result:** Phase 2 is implemented on this branch: ingest/query gateway paths are live, retrievers exist, vector search is wired, and the operational store carries tenant/confidence metadata.

---

## 🚧 Phase 3: Ship MVP Agentic Slice

**Objective:** BRAIN becomes agentic. The Reasoning Layer orchestrates agents that retrieve knowledge, validate claims, synthesize answers, and proactively suggest related context. This is the slice that proves BRAIN is more than RAG.

**Status:** MVP achieved ✅ — gateway-routed Regular mode chat is functional with citations/confidence. Critique mode implemented but experimental. Post-MVP fixes needed for conversation context memory and evidence persistence.

### Deliverables

- [x] Choose and integrate agent framework → **PydanticAI** (swappable via the agent-provider seam)
- [x] Implement the first critique pipeline slice (planning → retrieval → synthesis → critique) with user-visible reasoning steps
- [x] Implement `POST /brain/chat` — conversational interface through Gateway → Python reasoning/knowledge paths
- [ ] **[POST-MVP FIX 1]** Session memory — conversation context persists and is passed to backend reasoning on follow-up questions
- [ ] Chat mode transition regression coverage - prove Regular -> Critique -> Regular mode changes do not leak critique behavior into later Regular turns, and document the persistence boundary between conversation-level mode state and non-persisted per-message critique metadata.
- [x] Confidence indicators in chat UI — responses render confidence badges
- [x] Source citations in chat UI — responses render evidence/source snippets
- [ ] **[POST-MVP FIX 2]** Persist gateway evidence with conversation messages — citations/confidence survive conversation reload
- [ ] Proactive suggestion panel in Blazor UI — display unsolicited insights from Proactive Monitor
- [x] Migrate Blazor from direct Ollama/SK chat to Gateway-routed BRAIN chat
- [ ] Replace remaining Semantic Kernel usage with Microsoft.Extensions.AI in the C# chat path
- [ ] Add contradiction/proactive monitoring beyond the current critique pipeline

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P3-A | `POST /brain/chat` returns evidence-backed response with confidence score | ✅ Complete |
| P3-B | Multi-step reasoning visible (Retriever → Synthesizer → Critic chain) | ✅ Complete |
| P3-C | Proactive Monitor flags at least one contradiction when conflicting documents are ingested | ⏳ Remaining |
| P3-D | Blazor chat no longer talks directly to Ollama — all through Gateway → BRAIN | ✅ Complete |
| P3-E | Session memory works — follow-up questions reference prior context | ⚠️ **POST-MVP FIX 1** |
| P3-F | UI shows confidence indicators and source citations | ✅ Complete |
| P3-G | Proactive suggestion appears without user prompting | ⏳ Remaining |
| **P3-MVP** | **Gateway-routed chat with Regular mode works end-to-end with citations** | ✅ **ACHIEVED** |
| **P3-H** | **Evidence metadata persists with conversation messages** | ⚠️ **POST-MVP FIX 2** |

**Review result:** MVP gates (P3-A, P3-B, P3-D, P3-F, P3-MVP) satisfied. Post-MVP fixes (P3-E conversation context, P3-H evidence persistence) are highest priority. P3-C and P3-G remain future work.

---

## 🔜 Phase 4: Evaluate + Harden

**Objective:** Prove BRAIN works correctly and reliably. Automated evaluation, latency baselines, honest failure modes, and QA infrastructure.

### Deliverables

- [ ] Agent evaluation framework — automated scoring of response quality, evidence coverage, confidence calibration
- [ ] Retrieval quality metrics — precision/recall of knowledge retrieval against known test sets
- [ ] Latency baselines — measure and document acceptable response times for ingest, query, chat
- [ ] Honest failure modes — BRAIN says "I don't know" or "low confidence" rather than hallucinating
- [ ] Observability — structured logging with correlation IDs across all service calls
- [ ] Agent decision logging — trace every agent's reasoning steps and tool calls
- [ ] Replace `Console.WriteLine` with `ILogger<T>` across all C# code
- [ ] Cross-service integration test suite (upload → ingest → validate → store → query → chat → cite)
- [ ] Performance profiling — Neo4j query optimization, embedding generation latency

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P4-A | Automated evaluation suite runs and produces quality scores |
| P4-B | Latency baselines documented; critical paths under threshold |
| P4-C | BRAIN correctly responds "insufficient evidence" for questions outside its knowledge |
| P4-D | Correlation IDs trace through all service boundaries |
| P4-E | CI pipeline runs build + test + evaluation gates |

---

## 🔮 Phase 5: Prove Reusability

**Objective:** Demonstrate that BRAIN's core is domain-agnostic by adding a second connector type or a thin domain specialization module.

### Deliverables

- [ ] Add URL ingestion connector — fetch, parse, normalize web content to `CanonicalDocument`
- [ ] Verify URL sources receive lower initial confidence than uploaded documents
- [ ] OR: Add a domain specialization module (e.g., QA intelligence, tutoring) that uses existing BRAIN contracts
- [ ] Verify no BRAIN core code changes are required for the new connector/module
- [ ] Document the extension pattern — how to add new connectors, validators, or domain modules

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P5-A | Second connector/module works without modifying BRAIN core contracts |
| P5-B | Extension pattern documented and reproducible |

---

## 🔮 Phase 6: Scale Deliberately

**Objective:** Multi-tenancy enforcement, authentication, plugin ecosystem, advanced reasoning features.

### Deliverables

- [ ] Multi-tenant isolation enforcement — tenant filtering on all queries, data segregation in Neo4j
- [ ] Authentication and authorization (API keys, OAuth, or similar)
- [ ] Plugin ecosystem — documentation and registry for custom connectors, validators, agents
- [ ] Advanced graph reasoning — multi-hop traversal, temporal queries, change detection
- [ ] Knowledge evolution tracking — semantic diffing between document versions
- [ ] External LLM provider support (OpenAI, Anthropic) via MEai abstraction
- [ ] Production deployment artifacts (Docker Compose, Helm, or similar)

### Acceptance Gates

| Gate | Criteria |
|------|----------|
| P6-A | Two tenants operate on same instance with isolated knowledge |
| P6-B | Unauthorized queries return 401/403 |
| P6-C | At least one third-party plugin registered and working |
| P6-D | Document version change detected and claims updated |

---

## Infrastructure Priorities (Cross-Phase)

Stabilization work that supports multiple phases:

| Priority | Item | Status | Phase |
|----------|------|--------|-------|
| P0 | Pipeline contract + status alignment | ✅ Done | Legacy |
| P0 | BRAIN core contracts | ✅ Done | Phase 1 |
| P1 | Test infrastructure (pytest + xUnit + CI gates) | ⏳ | Phase 0–1 |
| P1 | Logging (`Console.WriteLine` → `ILogger<T>`) | ⏳ | Phase 4 |
| P2 | Config alignment (AI model keys) | ✅ Done | Phase 0 |
| P2 | Pin Python dependency versions | ✅ Done | Phase 0 |
| P3 | Consolidate duplicate `ServiceDiscoveryUtilities` | ✅ Done | Phase 0 |
| P3 | Remove legacy EF entity classes | ✅ Done | Phase 0 |

---

## Superseded Plans

The following items from the original roadmap are superseded by the BRAIN phase sequence:

| Original | Superseded By |
|----------|--------------|
| Phase 4: Flat Vector RAG | Phase 2 (Knowledge Baseline with Neo4j vector indexes) |
| Phase 5: LightRAG/GraphRAG | Phase 2 (pluggable retrieval) + Phase 3 (agent reasoning) |
| Phase 6: Plugin Ecosystem | Phase 6 (Scale Deliberately — redesigned for BRAIN plugin types) |
| Phase 7: Testing/Deployment | Phase 4 (Evaluate + Harden) |
| Phase 8: Advanced Features | Phases 3–6 (distributed across BRAIN phases) |

---

## Risk Register

From adversarial review — risks that could derail the pivot:

| # | Risk | Severity | Mitigation |
|---|------|----------|------------|
| 1 | Scope creep — BRAIN vision is larger than available effort | High | MVP acceptance gates are hard constraints; each phase stands alone |
| 2 | Agent framework immaturity — Python agent tools evolve rapidly | **Mitigated** | PydanticAI is abstracted behind the agent-provider seam; swap frameworks without refactoring the router contracts |
| 3 | Confidence scoring calibration — garbage in, garbage out | Medium | Start with source-type heuristics; add LLM-based scoring incrementally |
| 4 | Neo4j vector index limitations vs. dedicated vector DB | Low | Abstracted behind `IKnowledgeRetriever`; swap to Qdrant if needed |
| 5 | LightRAG divergence — maintaining two retrieval paths adds cost | Medium | LightRAG investment capped; BRAIN path is primary from Phase 2 |

---

**References:**
- `roadmap/Architecture.md` — BRAIN architecture specification
- `.squad/decisions/inbox/kujan-arch-review.md` — Adversarial architecture review
- `.squad/decisions/inbox/verbal-strategy-review.md` — Strategic product review
- `roadmap/Atlas-for-Data-Smart-Brain.md` — Original BRAIN vision specification
