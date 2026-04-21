# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Core Context

**Initial Assessment (2026-02-21 to 2026-02-22):**
- Codebase has **5 critical blockers** preventing processing pipeline: Python router contract mismatches, status casing bug, FK column mismatch, legacy entities, zero tests
- **AppHost orchestration is clean** (6 services, proper WaitFor, health checks)
- **Canonical schema exists** (`files` + `document_pages` shared via SQLite)
- Cross-agent findings: Jeff (config/health checks), Jarvis (signature mismatches), Buster (test infrastructure)
- Stabilization plan: 4 sprints, ~8 days to full unblock of Gates B1/B2
- Team coordination: Jarvis (Python contracts), Jeff (Web/orchestration), Buster (tests), Bob (architecture decisions)

**Key Architectural Decisions (Feb-Apr 2026):**
- **SQLite → Postgres migration:** Eliminate ~400 lines of bind-mount boilerplate (path resolution, journal-mode hacks, WAL checkpointing). Aspire already manages Postgres. Both services get clean database connections via pooling.
- **Shared schema stability:** Keep `files` + `document_pages` unchanged during provider migration. Column names, types, FKs all match across Web (EF Core) and Python (psycopg2).
- **BRAIN pivot direction:** Postgres is foundational infrastructure. BRAIN requires service decomposition (Ingestion/Knowledge/Validation), new Validation Layer (zero today), explicit reasoning orchestration (Semantic Kernel agents).
- **Contract-driven testing:** Regression tests derive infrastructure names from AppHost (single source of truth). Prevents false failures on intentional naming changes.

**Key File Paths:**
- Orchestration: `src/AspireApp.AppHost/AppHost.cs`
- C# upload store: `src/AspireApp.Web/Program.cs` (now Npgsql), `src/AspireApp.Web/Shared/UploadDbContext.cs`
- Python store: `src/AspireApp.PythonServices/app/services/database_service.py` (now psycopg2)
- Contract audit: `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`

---

## Learnings

### 2026-04-23 — Search Latency Review: Neo4j Indexing Distraction; Parallelization Is the Fix

**Context:** User reported 40-60s retrieval + 40-60s generation in regular mode and frequent 180s timeouts in critique mode. Architecture review requested.

**Synthesis:**
- Three independent investigations (Bob, Jarvis, Jeff) converged on consistent findings
- Jarvis traced retrieval paths: LightRAG 40-60s dominates, Neo4j fallback <500ms (indexing would save <1s)
- Jeff mapped timeout layers: Web HttpClient (120s) fires before Blazor token (180s) — design clarity issue
- Bob synthesized: Neo4j indexing is distraction; retrieval parallelization is highest-impact fix

**Architecture Recommendations (Priority Order):**
1. **Parallelize critique sub-query retrieval** (Jarvis, ~20 lines): Replace serial `for` loop with `asyncio.gather()`. Expected: 3x speedup on retrieval phase, total critique ~90-140s (often fits 180s).
2. **Raise Web HttpClient timeout to 240s** (Jeff, 1-2 lines): Remove hidden 120s boundary before intended 180s.
3. **Convert service instantiation to singletons** (Jarvis, ~15 lines): Eliminate per-request Neo4j/embedding service construction overhead.
4. **Enable SSE streaming** (Jeff + Jarvis, medium effort): User sees "Searching..." → "Reasoning..." → "Done" instead of silent wait.
5. **Post-P2-C: Invert retrieval priority** (Jarvis, future): Neo4j vector first (1-5s), LightRAG supplement (40-60s). Hot path drops from 40-60s to 1-5s for populated embeddings.

### 2026-04-21 — Processing-to-Retrieval Freshness Gap: Two-Phase Status Problem + Polling Solution

**Context:** Parallel audit (Bob, Jarvis, Jeff, Buster) on user scenario: upload document → processed → reload chat → query returns empty. Four-agent investigation converged on root cause and architectural fix.

**Four-Agent Findings:**

1. **Bob (Architecture):** Two-phase status problem. Status marked "processed" immediately after `trigger_scan()` handoff, but LightRAG indexing may take 30–300s. Window where UI shows ✅ but chat returns ❌.

2. **Jarvis (Python):** Fire-and-forget handoff in `_attempt_lightrag_handoff()` (processing.py:398-402) calls `trigger_scan()` (HTTP POST, returns immediately) without polling. No mechanism to verify ingestion before status update.

3. **Jeff (.NET):** Audit of Upload/Chat reload path clean. No caching issues, no stale request construction. Issue localized to Python retrieval/indexing layer.

4. **Buster (QA):** Current test suite would NOT catch regression. No end-to-end cycle test validates upload→process→reload→query freshness. Test gap is systematic.

**Root Cause Summary:**
- LightRAG handoff is fire-and-forget; no polling for actual index completion
- Status boundary mismatch: "processed" means "Neo4j done" but users interpret as "chat-ready" (includes LightRAG indexed)
- Async visibility gap: No polling to confirm LightRAG ingestion before declaring retrieval-ready
- Test coverage gap: No end-to-end test proves freshness

**Architectural Recommendation:**
- Separate "processing complete" (Neo4j populated) from "retrieval-ready" (LightRAG indexed)
- Add `indexing_status` field to `files` table (`pending` → `indexing` → `indexed` or `error`)
- Two-phase status: mark `processed` after Neo4j, poll LightRAG until doc appears, mark `indexed=True`
- Strengthen Neo4j fallback to use `source_confidence` when LightRAG metadata missing
- UI shows "Indexing..." badge until `indexed=True`

**Ownership & Timeline:**
- Jarvis: Polling logic + `indexing_status` column + DB service updates (1–2 days)
- Jeff: UploadData UI indicators for indexing state (1 day)
- Buster: End-to-end cycle tests + freshness validation (2–3 days)
- Bob: Architecture alignment review (on PR)

**Decision Status:** Merged to decisions.md inbox (awaiting Phase 3 roadmap prioritization)

**Key Decision:** Establish as team standard for future data retrieval features: end-to-end cycle test, freshness validation, timing boundary assertion.

---

### 2026-04-16 — Processing-to-Retrieval Gap: LightRAG Async Handoff Creates Stale-State Window

**Context:** User uploaded website, processing completed, reloaded chat, new information not found. Investigate why processed data isn't retrieval-ready immediately.

**Root Cause:**
- **Status marked "processed" before LightRAG ingestion completes:** Line 429 (`db.update_file_status(document_id, "processed")`) executes immediately after `_attempt_lightrag_handoff()` (line 398-402), which only *requests* a scan via `/documents/scan` API but doesn't wait for completion.
- **LightRAG async workflow:** `handoff_document()` → `stage_markdown()` (copies file) → `trigger_scan()` (HTTP POST, returns immediately) → background pipeline processes → eventually writes to `kv_store_doc_status.json`.
- **Timing gap:** Document shows "processed" in UI within ~5-15s, but LightRAG may take 30-300s to index (especially with Ollama entity extraction). User reloads chat during this window.
- **Retrieval failure mode:** `LightRagRetriever.retrieve()` queries LightRAG first; if doc not in index yet, falls back to Neo4j. Neo4j *does* have the data (pages ingested lines 308-336), but confidence enrichment fails (line 52-59 in retrievers.py) because LightRAG didn't provide scores, triggering fallback logic that may return empty results.

**Affected Boundary:**
- Processing router (processing.py:398-429): Fire-and-forget handoff, immediate status update
- LightRAG handoff service (lightrag_handoff_service.py:30-56): Synchronous staging, async scan trigger
- Retrieval layer (retrievers.py:52-99): Confidence enrichment depends on LightRAG or Neo4j provenance; unresolved confidence returns None
- Chat UI: Reloads conversation/tenant context on page load (Chat.razor.cs:103-140), hitting retrieval immediately

**Architectural Implications:**
1. **Contract mismatch:** "processed" status doesn't mean "retrieval-ready." Should be two states: `processed` (Neo4j populated) + `indexed` (LightRAG ready).
2. **Async visibility gap:** No polling mechanism to check LightRAG ingestion completion before marking retrieval-ready.
3. **Fallback brittleness:** Neo4j fallback *should* work but confidence enrichment logic (line 70-99) is fragile when LightRAG metadata missing.
4. **User expectation:** UI shows "processed" → user assumes chat will find it → retrieval fails silently or returns low-confidence results filtered out.

**Recommended Fixes (Priority Order):**
1. **Add `indexed` status field** (database_service.py + files table): Track LightRAG ingestion separately from Neo4j processing.
2. **Polling loop in handoff** (lightrag_handoff_service.py:54-56): After `trigger_scan()`, poll `/documents` until doc appears in `kv_store_doc_status.json` with status "processed" before returning.
3. **Two-phase status update** (processing.py:429): Mark `processed` after Neo4j, mark `indexed` after LightRAG polling completes.
4. **Strengthen Neo4j fallback** (retrievers.py:70-99): When LightRAG missing and Neo4j provenance resolves, use source_confidence from canonical document instead of failing closed with None.
5. **UI feedback** (Chat/UploadData): Show "Indexing in progress..." badge until `indexed=True`, disable chat queries for unindexed docs.

**Key File Paths:**
- Processing workflow: `src/AspireApp.PythonServices/app/routers/processing.py:398-429`
- LightRAG handoff: `src/AspireApp.PythonServices/app/services/lightrag_handoff_service.py:30-56`
- Retrieval fallback: `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py:52-99`
- Status tracking: `data/rag_storage/kv_store_doc_status.json`

**Decision:** Flagged for team discussion. Quick fix: Add polling in handoff before marking processed. Long-term: Separate processing vs indexing states, expose both in UI.

**Key Decision:** Critique Mode is **structurally expensive by design** (4 LLM calls + 3 retrieval calls). No pure performance fix; requires parallelization + streaming for UX. Honest timeline: recommendations 1-3 are quick wins (this sprint); 4-5 are deeper design.

**Decisions Recorded:** All three new decisions now in `.squad/decisions.md` (Neo4j Indexing, Retrieval Parallelization, Timeout Boundary Alignment).

### 2026-04-15 — Planning Docs Need Explicit Roles After a Pivot

**Context:** Roadmap review showed three planning documents drifting in different directions: `roadmap/Roadmap.md` still read like the pre-pivot plan, `roadmap/Plan.md` was closest to the active BRAIN roadmap, and `roadmap/Tasks.md` had the best execution detail but stale Phase 3 status.

**Decision Pattern:** Give each planning document an explicit job:
- `roadmap/Plan.md` = canonical active roadmap / phase status
- `roadmap/Tasks.md` = execution tracker with honest gate status
- `roadmap/Roadmap.md` = historical legacy roadmap, not the current source of truth

**Why This Matters:** During a product pivot, old roadmap tables survive longer than the code they describe. If we do not mark legacy docs as historical, maintainers will read contradictory summaries and prioritize the wrong next step.

**Key File Paths:**
- `roadmap/Plan.md`
- `roadmap/Tasks.md`
- `roadmap/Roadmap.md`
- `CRITIQUE-MODE-UI-GUIDE.md`
- `PHASE1_CONTRACTS_AUDIT.md`

**Operational Rule:** When reconciling planning docs, verify each claim against implementation surfaces (`AppHost.cs`, gateway endpoints, Python reasoning routes, Blazor chat wiring) and validation evidence (`dotnet build`, focused tests). Mark both understatement and overstatement; do not only correct optimistic docs.

### 2026-04-22 — PydanticAI Agent Framework Selection with Swappable Architecture

**Context**: Phase 3b Critique mode required multi-agent orchestration (Planner → Retriever → Synthesizer → Critic). Eric directed to use PydanticAI but design for replaceability.

**Decision**: Adopted PydanticAI abstracted behind `IAgentProvider` interface. Framework selection became an implementation detail, not a contract commitment.

**Key Pattern — The Provider Abstraction**:
```python
# Abstract interface owns the contract
class IAgentProvider(ABC):
    @abstractmethod
    async def reason(
        self, 
        request: BrainChatRequest,
        knowledge_context: List[KnowledgeItem]
    ) -> ReasonResponse:
        pass

# PydanticAI is just one implementation
class PydanticAIProvider(IAgentProvider):
    def __init__(self, ollama_endpoint, model_name):
        self._ollama = OllamaModel(...)
        self._agents = self._create_agents()
    
    async def reason(self, request, knowledge_context):
        # PydanticAI-specific orchestration
        return ReasonResponse(...)

# Factory allows env-var swap
def create_agent_provider() -> IAgentProvider:
    provider = os.getenv("AGENT_PROVIDER", "pydantic-ai")
    if provider == "pydantic-ai":
        return PydanticAIProvider(...)
    elif provider == "langgraph":
        return LangGraphProvider(...)
```

**Swap Procedure**: Change `AGENT_PROVIDER` env var in AppHost, implement alternative provider. Zero changes to routers, orchestrator, or contracts.

**Why This Matters**: 
- Protects against framework abandonment (Python agent ecosystem is volatile)
- Enables side-by-side benchmarking (test PydanticAI vs LangGraph performance)
- Isolates framework updates to single provider class
- BRAIN reasoning logic lives in contracts (`ReasonResponse`, `ReasoningStep`), not framework APIs

**Files Updated**:
- `.squad/decisions/inbox/bob-pydanticai-architecture.md` — Full decision document with interface design
- `roadmap/Tasks.md` — Phase 3b work items now concrete (Jarvis owns 5 Python extension points)
- `roadmap/Plan.md` — Updated Phase 3 framework selection + risk mitigation
- `requirements.txt` — Added `pydantic-ai==0.0.14`
- Session plan updated with framework decision

**Team Coordination**:
- **Jarvis**: Owns Python implementation (5 files: agent_provider.py, orchestrator.py, pydantic_ai_provider.py, agent_factory.py, brain.py updates)
- **Jeff**: No changes required (Gateway already expects `ReasonResponse`)
- **Buster**: Test strategy defined (contract compliance, mock swap, E2E critique mode)

**Learnings**:
1. **Defer framework commitment**: When evaluating volatile dependencies (agent frameworks, UI libs), abstract early. The "right" choice today may not exist tomorrow.
2. **Contracts over implementations**: The `IAgentProvider → ReasonResponse` boundary is our stability. PydanticAI could disappear and we'd survive.
3. **Factory pattern for swaps**: Environment-driven provider selection (`AGENT_PROVIDER=langgraph`) makes A/B testing trivial.
4. **Document the seam**: The decision doc explicitly names Jarvis's extension points so implementation is unambiguous.

**Anti-Pattern Avoided**: If we'd coupled directly to PydanticAI's `Agent` class in `brain.py`, a framework swap would require refactoring every router. Interface abstraction isolated the damage.

**Next Application**: When choosing ANY infrastructure component (vector DB, LLM provider, auth system), ask: "What's the interface that makes this swappable?" Build that first.

---

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-23 — Chat Latency Root Causes: LightRAG Dominates, Critique Mode is Structurally Expensive

**Context:** Eric reported 40-60s retrieval + 40-60s Ollama generation in regular mode, and frequent 180s timeouts in critique mode. Investigation requested with question: "Is Neo4j indexing the answer?"

**Key Findings:**

1. **Neo4j indexing is a distraction, not the fix.** The 40-60s retrieval latency comes from LightRAG's internal processing (HTTP call to a separate container that does its own graph traversal + likely its own LLM reasoning). Neo4j vector indexes already exist in code; even if populated and used, they'd save <1s on the fallback path. The hot path is LightRAG, which Neo4j indexing doesn't touch.

2. **Critique mode is structurally expensive.** It runs 4 serial LLM calls + 3 serial LightRAG calls. Minimum wall time is 150-270s — it *always* exceeds the 180s timeout unless retrievals are parallelized. This is not a bug; it's a design consequence of sequential multi-agent orchestration.

3. **The Web HttpClient timeout (120s) cuts out before the intended Blazor 180s timeout.** Jeff identified this independently: `Program.cs` line 38 sets `client.Timeout = TimeSpan.FromMinutes(2)` (120s), which fires before Blazor's 180s CancellationToken. Users see an abrupt fail before the system would naturally time out.

4. **Ollama generation is non-streaming (`stream=False`).** The entire LLM response is buffered before anything returns to the user. Switching to streaming won't reduce server compute time but dramatically improves perceived latency — the user sees the first token in seconds, not the last token after 60 seconds.

5. **Service instances are created fresh per request.** `get_neo4j_service()` and `get_embedding_service()` are FastAPI dependency factories that instantiate new objects on every request. The Neo4j driver lazy-initializes its connection pool each time. These should be module-level singletons.

**Ordered Recommendations:**

| # | Change | Mode Affected | Impact | Effort |
|---|--------|---------------|--------|--------|
| 1 | Parallelize critique sub-query retrievals (`asyncio.gather`) | Critique | Cuts retrieval 120-180s → 40-60s | Low (20 lines) |
| 2 | Raise Web HttpClient timeout from 120s → 240s | Both | Stops premature abort | Low (1 line) |
| 3 | Enable Ollama streaming + SSE endpoint | Both | Perceived latency: near-instant first token | Medium |
| 4 | Singleton service instances (Neo4j driver, EmbeddingService) | Both | Eliminates per-request connection overhead | Low |
| 5 | Invert retrieval priority: Neo4j vector primary, LightRAG supplement | Both | Cuts 40-60s to ~1-5s on primary path | High |

**Architecture Principle Reinforced:** When multiple I/O operations are independent, always `asyncio.gather`. Serial fan-out stacks linearly; parallel fan-out is bounded by the slowest single call. In critique mode: 3 × 40-60s serial → 1 × 40-60s parallel.

**Coordination Required:**
- **Jarvis:** Owns critique retrieval parallelization (#1) + singleton service instances (#4)
- **Jeff:** Owns Web HttpClient timeout fix (#2); owns SSE endpoint wiring (#3)
- **Buster:** Update critique mode test timeout expectations post-fix; add streaming contract test

**Key Files:**
- `src/AspireApp.PythonServices/app/brain/reasoning/critique_pipeline.py` (lines 103-138: serial loop)
- `src/AspireApp.PythonServices/app/services/lightrag_query_service.py` (timeout, line 14)
- `src/AspireApp.PythonServices/app/services/llm_chat_service.py` (`stream=False`, line 104)
- `src/AspireApp.Web/Program.cs` (HttpClient.Timeout 120s, line 38)
- `src/AspireApp.ApiService/Services/BrainBackendClientServiceCollectionExtensions.cs` (AttemptTimeout 3min)

### 2026-04-18 — Ollama Contention: Serialize Embedding vs LightRAG Workloads

**Pattern:** `process_document_task` in `processing.py` performs both Ollama embedding work (page + claim vectors) and triggers LightRAG ingestion (which also calls Ollama for LLM inference + embedding). Running these concurrently against a single Ollama instance with `MAX_ASYNC=1` causes serial queuing. With 60-second embedding timeouts, total processing time can exceed the 2-minute test polling window.

**Fix:** Reordered the task to complete all Python-side Ollama calls BEFORE triggering LightRAG scan. Pure sequencing change — no logic or interface modifications. Eliminates the contention window.

**Architecture Principle:** When multiple consumers share a single-instance AI model server (Ollama), orchestrate their workloads sequentially, not concurrently. This applies to any future pipeline step that calls Ollama.

**Key Files:**
- `src/AspireApp.PythonServices/app/routers/processing.py` (lines 59-170: processing task ordering)
- `src/AspireApp.AppHost/AppHost.cs` (LightRAG config: `MAX_ASYNC=1`, `EMBEDDING_FUNC_MAX_ASYNC=1`)

### 2026-04-15 (Updated 2025-11-02) — Phase 2 Knowledge Layer: P2-B Confidence Scoring Slice Scoped

**Scope:** Architecture decision on smallest next slice to unblock P2-B gate (confidence scoring).

**Key Finding:** Jarvis is on the right track with retrievers and Validation Layer foundation. However, P2-B gate specifically measures **confidence scoring in semantic retrieval**, not full Validation Layer completeness. These are separate concerns.

**Decision:** P2-B unblocking slice = persist `source_confidence` on Neo4j `Page` nodes during ingestion + surface it in `SemanticKnowledgeRetriever.retrieve()`. This is ~90 min of work, unblocks gate measurement, and lets Phase 3 Validation Layer (claim extraction, evidence chains) proceed in parallel.

**Architecture Boundary:** Page-level confidence (P2-B) vs. Claim-based confidence (Phase 3). Validate this separation before Jarvis starts; it's the seam between retrieval hardening and validation reasoning.

**Key Files:**
- `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py` (confidence extraction logic already present; just needs Neo4j to return it)
- `src/AspireApp.PythonServices/app/services/neo4j_service.py` (schema extension: `source_confidence` on `Page` nodes)
- `src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs` (extend with semantic confidence test)

**Risk Reduction:** Splits P2-B measurement from Validation Layer complexity. Gateway now returns real confidence (not defaults), unblocking P2-C vector index work.

**Decision Recorded:** `.squad/decisions/inbox/bob-phase2-knowledge-layer-next-slice.md`

### 2026-04-15 — Phase 2 Architecture Review: Retriever Interfaces Live, Confidence Scoring Gap Identified

**Scope:** Architectural review of Phase 2 Knowledge Layer implementation against Tasks.md roadmap.

**Status Summary:**
- **Delivered:** `IKnowledgeRetriever` abstraction with three implementations: `LightRagRetriever` (legacy LightRAG path), `SemanticKnowledgeRetriever` (Neo4j semantic), `BrainKnowledgeRetriever` (LightRAG-first fallback)
- **Gateway wiring complete:** `/brain/ingest` and `/brain/query` endpoints tested and working; `BrainBackendClient` successfully maps Python responses to C# contracts
- **Live test proof:** `BrainGatewayPhase2Tests.QueryKnowledgeAsync_MapsContractShapedKnowledgeResult_FromPythonQueryRoute` confirms contract shape + source refs working

**P2-B Blocker Identified:**
The semantic fallback path in `BrainKnowledgeRetriever` currently **hard-codes `DEFAULT_CONFIDENCE=0.5`** when LightRAG fails. P2-B gate requires real confidence scores from Neo4j retrieval. Options:
1. Persist `source_confidence` on `Page` nodes during ingestion, surface in `SemanticKnowledgeRetriever.retrieve()`
2. Compute confidence from Neo4j graph structure (centrality, validation metadata)

This blocks P2 completion and requires Jarvis + Neo4j schema extension.

**Still TODO for Phase 2:**
- Neo4j schema extension: `Claim`, `Evidence`, `Concept`, `Entity` labels + constraints
- Vector index creation on `Page.content` and `Claim.text` (P2-C gate)
- Validation layer: claim extraction + contradiction detection (Jarvis orchestration)
- Semantic confidence scoring (P2-B blocker)

**Key files referenced:**
- Retrievers: `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py`
- Gateway client: `src/AspireApp.ApiService/Services/BrainBackendClient.cs`
- Test harness: `src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs`
- Contracts: `src/AspireApp.ApiService/Contracts/` + `src/AspireApp.PythonServices/app/contracts/`

**Roadmap alignment:** Tasks.md updated with accurate status; P2-B marked as **Blocked** pending confidence scoring implementation. Decision logged for cross-team visibility.



### 2026-04-05 — Mock Auth UX Revision Uses Dedicated Sign-In Route + Framework Redirect

**Scope:** Independent architectural revision of the Blazor mock auth shell after QA rejected the first UX pass.

**Decision:** Keep `/` as the unauthenticated product landing, but redirect unauthorized access to protected routes through Blazor's built-in `AuthorizeRouteView` into a dedicated `/signin?returnUrl=...` page. This preserves the marketing-style landing while making route protection explicit and testable.

**Stable hook contract implemented:**
- Landing CTA: `auth-sign-in-cta`
- Provider list/buttons: `auth-provider-list`, `auth-provider-mock-microsoft`, `auth-provider-mock-google`, `auth-provider-demo`
- Authenticated shell: `auth-user-display`, `auth-sign-out`
- Tenant visibility/binding: `auth-current-tenant`, `data-auth-tenant`, `#tenant-select`

**Key file paths:**
- Router redirect seam: `src/AspireApp.Web/Components/Routes.razor`
- Sign-in page: `src/AspireApp.Web/Components/Pages/SignIn.razor`
- Redirect component: `src/AspireApp.Web/Components/Shared/RedirectToSignIn.razor`
- Shared auth surface: `src/AspireApp.Web/Components/Shared/SignInPanel.razor`
- Shell hooks: `src/AspireApp.Web/Components/Layout/MainLayout.razor`

**Validation note:** `dotnet build src/AspireApp.Web/AspireApp.Web.csproj /p:UseAppHost=false` passes. Focused `AspireApp.WebTest` auth runs still abort in the existing host fixture pipeline before yielding assertions, so infra follow-up remains separate from this UX revision.

### 2026-07-26 — Mock Pluggable Auth Slice Recommended as Next UX Leg

**Scope:** Architecture assessment of next UX stage after tenant-context completion.

**Decision:** Recommended a Mock Pluggable Auth Slice using Blazor's built-in `AuthenticationStateProvider` + `<AuthorizeView>` pattern. This establishes the abstraction that real Microsoft/Google OAuth plugs into at Phase 6.

**Key Architecture:**
- `IAuthStateProvider` → `MockAuthProvider` (dev) → `OAuthAuthProvider` (Phase 6)
- `AuthenticatedUser` model: `UserId`, `DisplayName`, `Email`, `AvatarUrl`, `Provider`, `TenantId`
- Mock login page with provider picker (Microsoft/Google style buttons)
- Unauthenticated landing page at `/` with sign-in CTA
- `TenantContextService` auto-selects tenant from `AuthenticatedUser.TenantId`
- Playwright UI tests for full auth flow

**What this unlocks for Phase 6:** DI swap from MockAuthProvider to real OAuth — zero UI rewrites needed. `AuthenticatedUser.TenantId` becomes the `tenant_id` header on Gateway requests.

**Explicit out-of-scope:** Real OAuth, JWT/tokens, API auth middleware, RBAC, persistent sessions, Python auth.

**Decision recorded:** `.squad/decisions/inbox/bob-mock-auth-slice.md`

**Current state of TenantContextService:** Scoped Blazor service with hardcoded tenant list ("default", "tenant-a", "tenant-b", "demo"). TenantSelector in NavMenu. Chat.razor.cs has TODO comment for Phase 3 injection. FileUploadController reads `X-Tenant-Id` header and propagates to FileStorageService.

**Owner mapping:** Jeff (UI + auth provider), Buster (Playwright + unit tests), Bob (review).

### 2026-07-26 — Postgres Migration Verified & Next UI Objective Scoped

**Scope:** Architecture verification of SQLite → Postgres migration completion; roadmap alignment and next feature scope.

**Outcome:** ✅ Migration is **operationally complete** for the Web ↔ Python contract. Both services connect to the shared Postgres container (`appdb`). Schema is unchanged, tests pass, build clean.

**Evidence:**
- C# Web: Uses `builder.AddNpgsqlDbContext<UploadDbContext>("appdb")`. All SQLite-specific hacks (DeleteJournalModeInterceptor, CheckpointDatabaseAsync, path resolution) are removed.
- Python: Uses `psycopg_pool.ConnectionPool`. Environment variables (POSTGRES_HOST/PORT/DATABASE/USER/PASSWORD) are read; fallbacks exist for backward compat.
- AppHost: Postgres container registered, `uploadStore` database created, services reference it and wait for it.
- Tests: `OperationalUploadStoreTests` uses NpgsqlConnection and verifies Postgres storage.

**Minor Gap:** `docs/CROSS_SERVICE_CONTRACT.md` still says "SQLite" in the preamble (column schema is accurate). One-line doc update needed, not blocking.

**Next Objective Identified:** "Persist Chat Messages & Retrieval on Reload." This is the foundation for BRAIN Phase 1 (Core Contracts). Smallest slice:
- Add `Conversation` and `Message` EF entities + DbSets to UploadDbContext
- Create ConversationService (C#) to manage lifecycle
- Add one Python endpoint: `POST /chat/message`
- Modify chat component to call ConversationService on each exchange
- On page load, hydrate chat history from Postgres

**Recommended owners:** Jeff (C# Blazor) + Jarvis (Python endpoint). Bob to review schema.

**Decision recorded:** `.squad/decisions/inbox/bob-postgres-ui-handoff.md` — approved for immediate implementation.

**Key pattern from this session:** The Postgres migration was clean because (1) we kept the schema unchanged, (2) we used Aspire's `WithReference()` to wire connection strings, (3) both C# and Python respected environment variable naming conventions. Future migrations should follow this pattern: change infrastructure, keep contracts stable, use Aspire to wire parameters.

### 2026-04-14T20:00Z — Phase 2 State Review: Smallest Critical Slice Identified

**Scope:** Architectual assessment of Phase 2 implementation state; roadmap clarification; execution sequencing for P2-A/P2-B closure.

**Current State:**
- ✅ Gateway wiring complete: `/brain/ingest`, `/brain/query` endpoints scaffolded & tested (BrainGatewayPhase2Tests)
- ✅ Python client: `PythonBrainBackendClient` implements resilient fallback (LightRAG → semantic-search)
- ✅ Contract types: C# + Python `BrainIngestRequest`, `BrainQueryRequest`, `KnowledgeResult` defined & tested
- ⚠️ **Ingestion refactor (P2-A):** Canonical shape building blocks exist (`build_canonical_document`, `resolve_source_confidence`) but ingestion router has not been refactored to emit canonical shape in production code
- ⚠️ **Query confidence:** Returns static 0.5 scores; payload score extraction not yet implemented
- ❌ Neo4j Knowledge Layer: Constraints exist; no claim/evidence nodes, no vector indexes, no `IKnowledgeRetriever` implementation
- ❌ Validation Layer: Not started
- ❌ LightRAG round-trip proof: No integration test yet

**Smallest Slice for P2-A→P2-B Closure (Prioritized):**

1. **Option 1 (RECOMMENDED): P2-A Ingestion Refactor** — Jarvis lead
   - Refactor `processing.py` to call `build_canonical_document()` and emit canonical shape
   - Update `database_service.py` to accept `CanonicalDocument` and persist `source_confidence` 
   - Wire `/brain/ingest` end-to-end: upload → canonical shape → database
   - **Done when:** Upload shows `source_confidence` populated, document has correct tenant/correlation IDs
   - **Time:** ~4 hours
   - **Why first:** Unblocks P2-B testing (LightRAG round-trip), validates Phase 1 contracts in production code

2. **Option 2: LightRAG Round-Trip Gate** — Jarvis + Buster
   - Write integration test: upload → ingest → query LightRAG → assert document in results
   - Does not require P2-A; reuses existing paths
   - **Done when:** Test passes cold-start Aspire; proves Neo4j persistence + LightRAG retrieval
   - **Time:** ~6 hours

3. **Option 3: Confidence Score Extraction** — Jarvis
   - Replace static 0.5 with semantic score extraction from response payload
   - Update `KnowledgeItem` confidence tracking
   - **Done when:** Query test asserts actual payload confidence, not hardcoded
   - **Time:** ~2 hours

**Roadmap Corrections (Minimal):**
- Line 159: "...emit `CanonicalDocument`" → "...call `build_canonical_document()` and emit canonical shape; persist `source_confidence` in `files` table"
- Line 168: "...with constraints" → "...with `IS UNIQUE` constraints on `(label).id` properties"
- Line 171–175 (note): "Baseline Note: Gateway query routing is now in place..." → "**P2-B Dependency:** Gateway query routing is scaffolded but returns static 0.5 confidence scores. Phase 2 is not complete until dynamic confidence extraction from payloads is live and the LightRAG round-trip is proven in integration tests."

**Recommendation:**
Execute Option 1 (P2-A) → Option 2 (round-trip) → Option 3 (confidence). This sequence validates contracts, proves data flow persistence, then closes the confidence gate.

**Roadmap Edits Applied:**
✅ Ingestion Refactor checkbox unchecked, wording tightened
✅ Knowledge Layer schema note made specific
✅ P2-B note rewritten to clarify blockers

### 2026-04-05 — Tenant-Context UI Slice - Architectural Revision

**Status:** ✅ COMPLETE (Revision 2)

**Scope:** Jeff's initial tenant-context UI implementation was rejected by Buster for API coherence. Bob took revision ownership to align FileUploadController signature changes with FileStorageService updates.

**Outcome:**
- `FileStorageService.GetAllFilesAsync()` now accepts optional `tenantId` parameter with backward-compatible null filtering
- `FileUploadController.GetUploadedFiles()` calls `GetTenantId()` helper and passes tenant to service layer
- Chat.razor.cs build errors fixed (removed duplicate property declarations)
- Tenant filtering works end-to-end: upload (X-Tenant-Id header) → FileStorageService → schema → retrieval

**Build Status:** ✅ `dotnet build src/AspireApp.Web/AspireApp.Web.csproj` passes

**Architectural Pattern:** Tenant isolation is tenant_id column concern. Query filtering is optional (null = all tenants, for backward compat). API layer reads header once via `GetTenantId()` and propagates consistently.

**Next:** Data layer now ready for Jarvis (Python schema) and Buster (QA validation) to close schema alignment and contract audit gaps.

### 2026-04-16 — MVP Documentation & Post-MVP Prioritization Session

**Scope:** MVP closure documentation and elevation of post-MVP fixes as highest-priority next steps (coordinated with Verbal + Coordinator).

**Session Type:** Cross-agent decision consolidation (Bob lead, Verbal review, Coordinator SQL tracking)

**What I Did:**
1. Updated `README.md` to reflect AspireAI as a **functional MVP**
   - Documented working features: document upload, chat interface, Neo4j knowledge graph integration, local auth
   - Established clarity on product stage for future external communication
   - Marked limitations alongside features for honest stakeholder understanding

2. Updated `roadmap/Tasks.md` with post-MVP priority elevation
   - Marked `mvp-conversation-context-memory` as **P1-immediate** 
   - Marked `mvp-evidence-persistence` as **P1-immediate**
   - Documented rationale: user-facing weaknesses identified post-MVP validation
   - Sequenced: complete P3b critique UI phase → then tackle memory + evidence in Phase 3c

3. Updated `roadmap/Plan.md` session plan
   - Incorporated post-MVP learnings for future reference
   - Documented context gaps and team transition to depth-focused work
   - Noted shift from feature breadth to known-issue resolution

**Coordination Completed:**
- ✅ Verbal reviewed and confirmed prioritization rationale
- ✅ Coordinator SQL-tracked memory + evidence tasks (blocked on P3b completion)
- ✅ No blocking gates for next phase; team can proceed in parallel

**Key Pattern Recognition:**
- MVP is **live and functional**; no architectural gates prevent user engagement
- Post-MVP priorities are **data-driven** (feedback from usage) not speculative
- Conversation context memory and evidence persistence address real UX gaps
- P3b critique UI completion unblocks higher-value engineering in Phase 3c
- Team now shifts toward **depth** (fixing known issues) rather than **breadth** (new features)

**Status:** MVP documentation complete; post-MVP priorities locked; ready for P3b → Phase 3c transition

### 2026-04-21 — MVP Documentation Pattern: Clear State + Ordered Next Steps

**Scope:** Product milestone documentation; roadmap honesty; stakeholder clarity after achieving functional MVP.

**Decision:** When a product reaches MVP milestone, update all planning docs simultaneously to:
1. Mark achievement explicitly with clear success criteria
2. Document working features AND known limitations side-by-side
3. Add ordered "Next Steps" with priority, technical scope, and ownership
4. Update phase tables to reflect honest completion status (not vague "in progress")

**Context:** AspireAI had reached functional MVP (gateway-routed chat with Regular mode works end-to-end: upload → knowledge graph → retrieval-augmented chat with citations), but docs still said "Phase 3 in progress" without clear milestone markers. Two critical product weaknesses identified by Eric needed explicit ordering, not buried in "remaining work."

**Pattern Applied:**

```markdown
## Current State: Functional MVP ✅

**What's Working:**
- Core user flow end-to-end
- Feature A, B, C operational

**Known Limitations (Next Priorities):**
1. Problem with user impact statement
2. Another problem with user impact statement
```

**Files Updated:**
- README.md: Added MVP declaration + working features + known limitations
- roadmap/Tasks.md: "MVP ACHIEVED ✅" banner + ordered post-MVP fixes (high priority)
- roadmap/Plan.md: Phase 3 marked "MVP Achieved"; Phase 0 marked complete
- session plan.md: Current state reflects MVP milestone

**Post-MVP Fixes (Ordered by User Impact):**
1. **Conversation Context Not Passed on Follow-Ups** (HIGH PRIORITY) — Users can't build multi-turn reasoning. Owner: Jeff + Jarvis
2. **Gateway Evidence Not Persisted** (HIGH PRIORITY) — Citations vanish after session ends. Owner: Jeff + Buster

**Why This Pattern Matters:**
- Honest milestone tracking: Phase tables reflect real completion
- Prioritized work: Ordered next steps prevent drift
- Stakeholder clarity: "MVP achieved" signals shippable product state
- Team alignment: Technical scope + ownership eliminate ambiguity
- Documentation hygiene: Regular reconciliation prevents code-doc drift

**Anti-Patterns to Avoid:**
- ❌ "Phase 3 still in progress" without MVP distinction
- ❌ Flat bullet lists of "remaining work" without ordering
- ❌ Vague problem statements ("improve memory")
- ❌ Missing technical scope on priorities
- ❌ MVP claims without documenting limitations side-by-side

**Learnings:**
1. **Milestone declarations need honest limitations:** If you claim MVP, document known gaps explicitly. Builds credibility, not erosion.
2. **Prioritized work unblocks team focus:** Numbered next steps with technical scope (files affected, contracts involved) become actionable execution plans, not aspirational lists.
3. **Documentation reconciliation is architectural work:** Planning docs drift from code during pivots. Regular audits (quarterly?) against implementation surfaces prevent maintainers from prioritizing superseded work.

**Key Files:**
- `README.md`
- `roadmap/Plan.md`
- `roadmap/Tasks.md`

### 2026-04-05 — BRAIN Pivot Architectural Assessment Complete

**Scope:** Bob took revision ownership after Buster rejected Jeff's `AspireDashboardLoads` test.

**Root Cause:** The original test called `WaitForFunctionAsync("() => document.title && ...")` immediately after `GotoAsync` to the login URI. This evaluated the title on the login page (which might briefly have content), then the Blazor-driven auth redirect replaced the page with the dashboard root where the `<PageTitle>` component hadn't hydrated yet. `TitleAsync()` read the empty title of the new page — classic navigate-during-poll race.

**Fix Applied:**
1. Inserted `WaitForURLAsync(url => !url.Contains("/login"))` with 120s timeout after `GotoAsync`. This gates all subsequent assertions on the redirect being complete.
2. Added explicit `PageWaitForFunctionOptions { Timeout = 60_000 }` to the title poll, accommodating cold-start Blazor SignalR circuit initialization.
3. Preserved Jeff's model/fixture infrastructure (`AppHostMappingModel.AspireDashboardBrowserToken`, `TestFixture` resource snapshot reads) — only the test method changed.

**Validation:** `dotnet build AspireApp.sln` — 0 warnings, 0 errors. `dotnet test --filter AspireDashboardLoads` — 1 passed, 0 failed (40s wall clock).

**Pattern:** For Blazor Server UI tests via Playwright, always wait for the URL to settle after auth redirects before polling `document.title`. The `<PageTitle>` component sets title via JS interop *after* the SignalR circuit completes.

**Key Files:**
- Test: `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs`
- Fixture: `src/AspireApp.WebTest/Fixtures/TestFixture.cs`
- Model: `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`

### 2026-02-27 — Jarvis Completes P0.2 (save_document_page Fix)

**Status:** ✅ COMPLETE  
**Commit:** `e9d90ea` on `feature/doc-upload`

**What Was Fixed:**
- `processing.py` lines 67–75: Invocation mismatch on `save_document_page()` corrected
- Router now passes individual keyword args instead of DocumentPage object
- FK value corrected from `processed_doc_id` to `document_id` (correct target is files.id, not processed documents)

**Impact on Bob's Plan:**
- **P0.2 (save_document_page fix):** ✅ Done. Blocks Gate B2 removal.
- **Status:** Now awaiting P0.1 (router contract rewrite) + P0.3 (status casing fix) for full Gate B1/B2 closure.
- **Next coordination:** Bob reviews combined P0 fixes for sprint validation before Phase 1.5 (orchestration cleanup).

### 2026-02-27 — Upload Path Normalization & Python Footprint Review

**Scope:** Architecture review of P0 tasks: Upload Path Normalization + Python Footprint Minimization.

**Blocking Issue Found — DoclingService Path Resolution:**
- `DoclingService.process_document()` line 32 constructs `self.uploads_path / document.file_path`
- `uploads_path` = `/app/data/uploads` (wrong — no uploads subdirectory in contract)
- `document.file_path` = host-side directory (e.g., `C:\Users\...\data`) — meaningless in Linux container
- Correct construction: `Path(os.environ.get("DATA_PATH", "/app/data")) / document.filename`
- This is the reason Gate B1 is still blocked. One-file fix in `docling_service.py`.

**Python Endpoint Audit — 7 endpoints marked for removal:**
- 5 from documents router: health/concurrent-access, health/schema-sync, admin/force-sync, stats/performance, health/database (all dead/redundant)
- 2 from processing router: status/{id} (duplicate), processed-documents (reimplements status filter)
- 16 endpoints retained covering the full upload→process→retrieve lifecycle

**DatabaseService Audit — 5 methods marked for removal:**
- `get_statistics()`, `get_active_services()`, `get_file_document_sync_status()`, `force_sync_files_and_documents()`, `save_document()` — all dead code after endpoint pruning
- Legacy compatibility layer (7 methods) justified and retained — routers depend on Document/ProcessedDocument models
- Core pipeline (8 methods) + infra (3 methods) all needed

**Contract Documentation Created:**
- `docs/CROSS_SERVICE_CONTRACT.md` — canonical reference for C#↔Python shared state
- Documents: shared DB schema, status lifecycle, path resolution rule, volume mounts, retained API surface, Pydantic model shapes

**Decisions Recorded:**
- `.squad/decisions/inbox/bob-python-footprint-p0.md` — full decision record with execution plan

### 2026-02-28 — Upload Path Audit Converted to Passing Regression Gate

**Scope:** Two reviewer issues — expectedFailure tests and stale contract doc.

**What Changed:**
- Removed `@unittest.expectedFailure` from both `UploadPathNormalizationAuditTests`
- Tests now exercise the live two-arg contract: `DatabaseService.resolve_upload_path()` → `DoclingService.process_document(doc, resolved_path)`
- Extracted shared `_run_with_resolved_path` helper to DRY test setup/teardown
- Updated `docs/CROSS_SERVICE_CONTRACT.md`:
  - Path resolution section documents the multi-root `resolve_upload_path()` search algorithm
  - Endpoint table matches live routers (added `/documents/health/database`, `/processing/status/{id}`; removed phantom `/documents/status/{status}`)
  - Section renamed from "Retained" to "Live" to reflect actual state

**Architecture Pattern Confirmed:**
- Path normalization lives in `DatabaseService.resolve_upload_path()`, not in DoclingService
- DoclingService receives a pre-resolved `Path` — clean separation of concerns
- Regression tests validate the integration boundary between the two services

**Validation:** All 4 contract audit tests pass, .NET build clean (0 warnings)

**Key Decisions for Execution:**
1. Python contracts: Rewrite routers to existing DatabaseService methods (Option A)
2. Status casing: Normalize to lowercase `"uploaded"` in Web (30 min)
3. ApiService: Remove entirely (0 business value, 500ms latency)
4. LightRAG: Remove from WaitFor chain until integration ready
5. Testing: xUnit + pytest phased roadmap (Buster owns)
6. Logging: Full ILogger<T> replacement (high-impact files first)

**Execution Ready:**
- Sprint 1 (Gate B1/B2 unblock): Jeff + Jarvis in parallel
- Sprint 1.5 (Orchestration stabilize): Jeff leads
- Sprint 2 (Test infrastructure): Buster leads
- All tracked in `.squad/decisions.md`

### 2026-02-22 — Consolidated copilot-instructions.md

**Scope:** Merged previous rich-context version (replaced at commit e8a32af) with current Squad boilerplate into a single authoritative file.

**What Changed:**
- Opened with team personas (Bob, Jeff, Jarvis, Buster) — evocative 2-line descriptions per member
- Restored all operational context: Quick Overview, Day-One Checklist, Build/Run/Test, Validation Before PR, Troubleshooting Cheatsheet, Repo Map
- Retained Squad conventions: team context, capability self-check, branch naming, PR guidelines, decision inbox
- Updated Instruction Lookup table to reflect all 15 current instruction files with glob patterns
- Updated Prompt Directory to reflect all 12 current prompt files
- Corrected .NET SDK version references from 9 to 10 (matches `global.json`)
- Final size: 167 lines (target was 150-200)

**Design Decisions:**
- Personas go first — they set tone and ownership before any technical content
- Instruction files are referenced, not replicated — keeps the file lean
- Squad conventions are a section, not the whole file — they serve the project, not the other way around
- Removed stale references (Tasks & Memory Notes section, "Plan to add" comments)

### 2026-02-27 — Jarvis Completes P0.2 (save_document_page Fix)

**Status:** ✅ COMPLETE | **Commit:** `e9d90ea`
- `processing.py` invocation mismatch corrected
- FK value corrected: `processed_doc_id` → `document_id`

### 2026-02-27 — Upload Path Normalization & Python Footprint Review

**Blocking Issue Found — DoclingService Path Resolution:**
- Correct construction: `Path(os.environ.get("DATA_PATH", "/app/data")) / document.filename`
- 7 endpoints marked for removal (dead/redundant); 16 retained (upload→process→retrieve lifecycle)
- 5 DatabaseService methods marked for removal; 8 core + 7 legacy retained
- Contract documentation created: `docs/CROSS_SERVICE_CONTRACT.md` 

### 2026-02-28 — Upload Path Audit Converted to Passing Regression Gate

**What Changed:**
- Removed `@unittest.expectedFailure` from both `UploadPathNormalizationAuditTests`
- Tests now exercise the live two-arg contract: `DatabaseService.resolve_upload_path()` → `DoclingService.process_document(doc, resolved_path)`
- Updated `docs/CROSS_SERVICE_CONTRACT.md` with current route signatures
- Regression tests validate the integration boundary between the two services

**Key Files:**
- Test file: `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`
- Contract doc: `docs/CROSS_SERVICE_CONTRACT.md`
- Path resolver: `src/AspireApp.PythonServices/app/services/database_service.py:370`

### 2026-03-20 — P0 Decision Merge: Upload Path Normalization & Footprint Minimization Approved

**Scope:** Final squad coordination session — all P0 work approved and merged into shared decision log.

**Work Summary Across Squad:**
1. **Jarvis:** Implemented upload path fix + endpoint/method pruning. Removed 7 endpoints, 5 dead methods. FastAPI surface trimmed.
2. **Buster:** QA gates (3 phases). Initial rejection for incomplete test gate. Post-Bob revision approval for path normalization. Post-Jeff approval for footprint minimization.
3. **Jeff:** Finished Python footprint cleanup by removing sync shims and updating canonical contract methods.
4. **Bob (revision):** Converted upload-path audit from `expectedFailure` to passing regression gate. Aligned CROSS_SERVICE_CONTRACT.md with live contract.

**Inbox → Decisions.md:** 6 files merged and deduplicated. Decisions form coherent audit trail from initial blocker through implementation to approval.

**Orchestration Logs Created:** One per agent documenting spawn phases, context for successors, related decisions.

**Session Log Created:** Brief summary of P0 completion, blocked issues resolved, decisions merged, next phase assignments.

**Next Phase:** Jeff to maintain canonical Python contract surface methods and docs. Validation gates remain live as regression coverage.

---

## Cross-Agent Coordination — Scribe Merge (2026-03-20)

**Session:** Roadmap Tasks Update (background spawn)

**Work:** Bob completed:
- Moved completed P0 items into Completed Work section in `roadmap/Tasks.md`
- Updated milestone gates table (Gates A, B1, B2, E, G marked ✅ CLEAR)
- Corrected metadata: Last Updated → 2026-03-20, Active Branch → `task/p0-python-tasks`

**Decisions:** P0 completion decision merged into `.squad/decisions.md`. Upload Path Normalization and Python Footprint Minimization officially approved and archived in roadmap.

**Related:** Orchestration log created at `.squad/orchestration-log/20250303T000000Z-bob.md`. Session log at `.squad/log/20250303T000000Z-roadmap-tasks-update.md`.

**Status:** Ready for P1 Phase (Processing Pipeline Stabilization, Test Infrastructure Bootstrap, Docling → LightRAG Ingestion).

### 2026-03-21 — Aspire Dashboard Test Redirect/Title Poll Revision (Bob → Rejection → Fix)

**Status:** Complete (Bob revision after Buster rejection)

**Context:** After Buster rejected Jeff's `AspireDashboardLoads` test (infrastructure sound but title assertion racing), Bob took revision ownership to fix the assertion strategy.

**Root Cause Race Condition:**
1. Jeff's `TestFixture.cs` correctly captures dashboard URL + browser token from Aspire resource snapshot
2. `AppHostMappingModel.AspireDashboardLoginUri` correctly computed with token query param
3. But test called `WaitForFunctionAsync("() => document.title && ...")` immediately after `GotoAsync` to login URI
4. Between redirect start and page fully load: login page → new page (empty title) → Blazor hydration (title set by `<PageTitle>`)
5. Title was read on the new page before `<PageTitle>` component ran

**Fix Applied:**
1. Inserted `WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 120_000 });` immediately after `GotoAsync`
2. Changed title poll to explicit `PageWaitForFunctionOptions { Timeout = 60_000 }` (60s to account for Blazor SignalR cold-start)
3. Changed title assertion from exact match to flexible substring: `Contains("resources", StringComparison.OrdinalIgnoreCase)`

**Validation:**
- `dotnet build AspireApp.sln` ✅ — 0 warnings, 0 errors
- `dotnet test` → `AspireDashboardLoads` ✅ — 1 passed, ~40s wall clock
- Full WebTest suite ✅ — all tests passing

**Pattern Documented in Decisions:**
"Aspire Dashboard Playwright Tests Must Wait for Auth Redirect" — team standard for future dashboard UI tests via Playwright.

**Key Learning:** For Blazor Server UI tests, always gate on URL settling before polling titles. The `<PageTitle>` component sets title asynchronously after the SignalR circuit completes.

### 2026-04-21 — MVP Documentation Pattern & Post-MVP Fix Prioritization

**Context:** AspireAI reached functional MVP state (gateway-routed chat with document upload → knowledge graph → retrieval-augmented response end-to-end). However, documentation still read "Phase 3 in progress" without declaring the milestone. Eric flagged this and identified two user-facing weaknesses that needed explicit prioritization.

**Decision Pattern Established:** When product reaches MVP, simultaneously:
1. Mark achievement clearly with criteria (what works end-to-end, what doesn't)
2. Document known limitations side-by-side with achievements
3. Create ordered "Next Steps" section with priority ranking, technical scope, and ownership
4. Update phase status tables to reflect honest completion state

**Implementation (2026-04-21):**
- Updated `README.md`: Added "Current State: Functional MVP ✅" section with working features (multi-conversation chat, gateway routing, citations, auth) and known limitations
- Updated `roadmap/Tasks.md`: Changed status from "in progress" to "MVP ACHIEVED ✅", added two ordered post-MVP fixes
- Updated `roadmap/Plan.md`: Marked Phase 0 complete, Phase 3 "MVP Achieved" with post-MVP fixes in progress
- Added explicit ordering of post-MVP fixes by user impact:
  - **#1 (HIGH):** Conversation context not passed on follow-ups → prevents multi-turn reasoning
  - **#2 (HIGH):** Gateway evidence (citations/confidence) not persisted → evidence vanishes when users return to conversations
- Documented technical scope and ownership: Jeff + Jarvis (context), Buster + Jeff (evidence)

**Why This Pattern Matters:**
- Vague "still working" status prevents stakeholders from distinguishing beta from shippable product
- Ordered next steps prevent priority drift that happens when "remaining work" is unranked
- Real achievements get undersold; documentation-code drift compounds
- Technical scope + ownership eliminate ambiguity and blocking on unclear prioritization

**Key Files:**
- `README.md` — MVP declaration
- `roadmap/Tasks.md` — Post-MVP fixes with scope
- `roadmap/Plan.md` — Phase status
- `.squad/decisions.md` — Pattern decision entry

**Related Decision:** `.squad/decisions.md` — "MVP Documentation Pattern — Clear State + Ordered Next Steps"

**Key Files:**
- Test: `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs`
- Fixture: `src/AspireApp.WebTest/Fixtures/TestFixture.cs`
- Model: `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`


### 2026-03-25 — Roadmap Maintenance & Challenge Tracking

**Scope:** User directive — keep oadmap/Tasks.md updated as work progresses; track emerging implementation challenges.

**Work Completed:**
- Added maintainer reminder blockquote at top of Tasks.md (enforces active status updates)
- Created "Implementation Challenges & Revisit Items" section:
  - Infrastructure risks: Volume mount validation (Gate B may fail silently)
  - Architectural unknowns: LightRAG integration surface clarity
  - Technical debt signals: Weak env-var testing, sparse test coverage
  - Performance: Neo4j batch write profiling not yet done

**Decision Generated:**
- **Roadmap Status Tracking & Challenge Log** — Process rule: roadmap edits happen during/immediately after task completion, not retroactively

**Impact:** Roadmap is now a living document with embedded discipline. Challenges surface early for cross-team visibility. Foundation for next iteration planning.

**Related:** Decision merged to .squad/decisions.md. Orchestration log at .squad/orchestration-log/2026-03-25T14-09-00Z-bob.md. Session log at .squad/log/2026-03-25T14-09-00Z-roadmap-maintenance.md.

### 2026-07-26 — Deep Ingestion Flow Architecture Review

**Scope:** Eric updated the `FlowEndToEnd` test to upload a document, verify it in the files table, and confirm the file on disk. He asked: what triggers Docling parsing and LightRAG handoff?

**Critical Finding — No Automatic Processing Trigger Exists:**

The entire codebase has a gap between C# upload and Python processing:

1. **Upload (C#):** `FileUploadController.UploadFile()` saves file to `data/`, writes `files` row with `status = "uploaded"`, returns 200. No further action.
2. **The Gap:** Nothing calls Python processing. No file watcher, no background poller, no startup scan, no C#→Python HTTP call.
3. **Processing (Python, manual only):** `POST /processing/process-document/{id}` and `POST /processing/process-all` exist but are never called automatically.
4. **Inside Processing:** `process_document_task()` → resolve path → Docling → LightRAG handoff → Neo4j → `document_pages` → status `processed`.
5. **LightRAG Handoff:** Embedded inside processing — copies markdown to INPUT_DIR, POSTs to `lightrag:9621/documents/scan`.

**Where Code/Docs Agree:** Status lifecycle, path resolution, shared schema, API surface all consistent.
**Where Code/Docs Disagree:** Neither contract doc documents the trigger mechanism. Roadmap marked P1 processing done but trigger was never wired.

**Architecture Recommendation:** Option A — `FileUploadController` calls `POST /processing/process-document/{id}` on Python service after upload. `PYTHON_SERVICE_URL` env var already passed to webfrontend. Simplest path, no new infrastructure.

**Roadmap Updated:** Added "Ingestion Trigger" P1 section with three options. Updated test tasks. Added CRITICAL challenge.

**Key Files:** FileUploadController.cs, processing.py, database_service.py, docling_service.py, lightrag_handoff_service.py, fastapi.py, AppHost.cs, BasicAspireAppHostTests.cs

### 2026-07-26 — SQLite-to-Postgres Migration Architecture Decision

**Scope:** Replace shared SQLite file with Postgres for operational data (`files` + `document_pages`).

**Key Findings:**
- Postgres already provisioned in AppHost (`AddPostgres("postgres")`, `appdb` database, pgWeb, bind mount)
- Both services already `WaitFor(postgres)` and receive `POSTGRES_USER`/`POSTGRES_PASSWORD` env vars
- Neither service actually connects to Postgres yet — all operational data still flows through SQLite bind-mounted file
- ~400+ lines of SQLite workarounds exist across both services (WAL/DELETE journal mode, fresh connection fallbacks, multi-candidate path resolution, checkpoint calls)

**Decision Summary:**
1. Keep same `files` + `document_pages` schema — stable and well-documented
2. Jeff: NuGet swap (`Sqlite` → `Npgsql`), `AddNpgsqlDbContext<UploadDbContext>("appdb")`, remove `DeleteJournalModeInterceptor`, remove `CheckpointDatabaseAsync`
3. Jarvis: `psycopg2-binary`, replace `ConnectionPool` + SQLite pragmas with psycopg2 pool, remove path resolution
4. AppHost: Wire Postgres host/port/db to Python, `.WithReference(postgres)` to Web, remove SQLite file setup block
5. Deferred: Legacy entity removal, EF Migrations, diagnostic scripts, test updates (Buster's scope)

**Decision Written:** `.squad/decisions/inbox/bob-postgres-cutover.md`

**Key Files Affected:**
- `src/AspireApp.AppHost/AppHost.cs` — Remove SQLite plumbing, wire Postgres refs
- `src/AspireApp.Web/Program.cs` — Provider swap, remove SQLite helpers
- `src/AspireApp.Web/AspireApp.Web.csproj` — NuGet swap
- `src/AspireApp.Web/Shared/FileStorageService.cs` — Remove CheckpointDatabaseAsync
- `src/AspireApp.PythonServices/app/services/database_service.py` — Backend swap (~400 lines removed)
- `src/AspireApp.PythonServices/requirements.txt` — Add psycopg2-binary
- `docs/CROSS_SERVICE_CONTRACT.md` — SQLite → PostgreSQL header update
### 2025-02-01 — Tenant Context UI Slice Revision (Buster Rejection Recovery)

**Scope:** Took over tenant-context implementation after Buster rejected Jeff's first pass. FileUploadController was passing tenantId but FileStorageService/schema/UI were incomplete.

**Root Cause:** Merge conflict in Chat.razor.cs created duplicate property declarations, breaking build. Service layer didn't filter by tenant. GetAllFilesAsync() signature mismatch.

**Resolution:**
1. **Fixed Chat.razor.cs build errors** - Removed duplicate AiInfoState, CurrentlySpeakingMessage declarations; added missing ElementReference questionInput
2. **Updated FileStorageService** - Made GetAllFilesAsync(string? tenantId = null) backward-compatible; added .Where(f => f.TenantId == tenantId) filter
3. **Updated FileUploadController** - GetUploadedFiles() now calls GetTenantId() and filters results
4. **Verified UI plumbing** - TenantSelector component + CSS present, NavMenu includes tenant context section

**Build Status:** ✅ Clean build, no new warnings, one pre-existing test failure (unrelated EF Core issue)

**Key Lesson:** When implementing cross-cutting features (like tenant context):
- Start with contracts first (service signatures, DTOs)
- Wire end-to-end before considering "done" (controller → service → query)
- Test build after each layer to catch signature mismatches early
- Duplicate declarations from merge conflicts block compilation - PowerShell file reconstruction can fix when edit tool struggles

**Not Included (Out of Scope):**
- Chat tenant filtering (Phase 3 TODO remains)
- Python service tenant filtering
- Full authentication/authorization
- Migration script for existing tenant-less data

**Files Changed:**
- Chat.razor.cs - Fixed duplicates
- FileStorageService.cs - Added tenant parameter + filtering
- FileUploadController.cs - Tenant-aware GET endpoint

**Follow-Up:**
- Add integration test for tenant-filtered retrieval
- Document tenant flow in architecture guide
- Chat.razor integration in Phase 3



### 2026-04-05 — Mock Pluggable Auth Slice Recommended (Cross-Agent Consensus)

**Agent Assessment:** Bob recommended mock pluggable auth as next UX leg.  
**Cross-Agent Inputs:** Jeff (concrete UX/service design), Buster (acceptance gates).  

**Key Points:**
- **Jeff alignment:** Blazor AuthenticationContext + MockAuthProvider mirrors existing TenantContextService pattern ✅
- **Buster alignment:** 5-layer acceptance gates (UI → Contract) before implementation ✅
- **Outcome:** Three agents converged on same direction; Eric approval pending
- **Next:** Sprint assignment for Landing/SignIn/Dashboard + mock auth implementation

**Decision Merged:** .squad/decisions.md — Mock Pluggable Auth Slice section

### 2026-07-29 — Local Username/Password Auth — First Slice Architecture Decision

**Scope:** Eric requested "classic managed basic username and password login." Bob assessed the smallest viable first slice.

**Key Decisions:**

1. **Provider seam fit: YES.** New `LocalAuthService : IAuthService` with `ServiceKey = "local"`, registered via the existing `AddAuthServiceRegistration<>` pattern. Zero teardown of mock or Microsoft auth.

2. **No ASP.NET Core Identity.** It would fight every existing abstraction (`AuthenticatedUser` vs `IdentityUser`, `AppAuthenticationStateProvider` vs Identity's provider, `AuthenticationContext` scoped state). Use standalone `PasswordHasher<T>` or `BCrypt.Net-Next` for hashing only.

3. **Sign-in only, pre-provisioned users.** Users defined in `appsettings.json` under `Authentication:Local:Users`. No self-service registration (email validation, password strength, duplicate detection = massive scope). Registration is a follow-up slice.

4. **Tenant assignment: same as mock.** Each user gets `DefaultTenantId` in config. `TenantContextService.InitializeForUser()` handles hydration. No new mechanism.

5. **Critical implementation risks flagged:**
   - `CompositeAuthService` must become dynamic (currently hardcodes Mock + Microsoft)
   - `SignInPanel.razor` needs a credentials form branch (new `RequiresCredentials` on `AuthProviderOption`)
   - Password form must POST to server endpoint, NOT submit via Blazor interactive (credentials must not travel over SignalR)
   - No new DbContext for users yet — config-based keeps first slice additive

**Decision recorded:** `.squad/decisions/inbox/bob-local-auth-slice.md`

**Key files for implementation:**
- Provider seam: `Services/IAuthService.cs`, `Services/AuthServiceFactory.cs`, `Services/AuthServiceRegistration.cs`
- Composite (needs refactor): `Services/CompositeAuthService.cs`
- Options pattern: `Services/AuthenticationOptions.cs`
- Registration: `Services/AuthenticationServiceCollectionExtensions.cs`
- UI surface: `Components/Shared/SignInPanel.razor`
- Cookie endpoints: `Program.cs` (lines 144-195 for mock pattern to follow)
- Config: `appsettings.json` → `Authentication:Local` section

### 2026-04-09 — Tenant Slice Session: Architecture Coordination

**Role:** Lead / Architect (Cross-Service Tenant Model)

**Outcome:** Approved tenant slice foundation; recommended local-auth-slice as next layer.

**What Bob Did:**
1. Reviewed tenant architecture: persisted model, default-tenant protection, membership enforcement
2. Approved seam fit: tenant context integrated seamlessly with existing auth provider abstraction
3. Identified local-auth-slice as natural next step: config-provisioned users + extensible provider pattern
4. Recommended starting local-auth work after tenant slice complete

**Key Decisions Contributed:**
- Local Username/Password Auth — First Slice Recommendation — managed credentials, no full Identity import, provider seam extensibility

**Coordination Notes:**
- Tenant slice unblocks multi-user auth stories
- Local auth foundation will support tenant per-user provisioning in later phase
- BRAIN gateway Phase 6 will propagate tenant_id header across services

**Status:** Approved; recommendation ready for implementation

---

## Cross-Agent Coordination — Scribe Merge (2026-04-15T20:25:34Z)

**Session:** Planning Doc Reconcile & Test Failure Triage

**Work:** Bob reconciled branch state against roadmap reality, verified Phase 1/2 gates closed, and locked Phase 3 critical path.

**Coordination Points:**
- Verbal recommended Phase 3 beta reframing (honest milestone instead of vague framework-selection gate)
- Buster identified chat-mode regression coverage gap and mapped test failure root causes (3 distinct issues across team)
- Jeff synced planning docs and uploaded-status race fix; confirmed Phase 3 beta milestone alignment
- Jarvis analyzed Python processing timeout (infrastructure issue, not code bug); confidence enrichment fix in review
- Warden hardened Playwright form selectors; Bob/Buster confirmed split-brain auth pattern remains (endpoint wiring issue)

**Key Outcome:** Phase 3 critical path locked with agent framework selection (PydanticAI) as BLOCKING GATE, decision deadline 2026-04-24. 7 gates cannot start until framework chosen.

**Related:** Orchestration logs created (one per agent). Session log at `.squad/log/2026-04-15T20-25-34Z-planning-doc-reconcile.md`. 17 inbox decisions merged into `.squad/decisions.md`.


### 2026-04-21 — MVP Documentation Update: Clear Current State and Next Priorities

**Context:** Product owner Eric identified that the application had reached a functional MVP state but documentation still reflected beta/in-progress status. Two critical weaknesses were identified that needed to be captured as explicit next steps.

**Action:** Updated core documentation (README, roadmap/Tasks.md, roadmap/Plan.md, session plan.md) to:
1. Mark MVP achievement clearly (gateway-routed Regular mode chat works end-to-end with citations)
2. Document what's working (multi-conversation persistence, authentication, knowledge graph ingestion, citation display)
3. Add "Next Steps: Post-MVP Fixes" section with two high-priority items ordered by user impact
4. Update phase status tables to reflect Phase 0 complete, Phases 1-2 complete, Phase 3 MVP achieved

**Post-MVP Fixes Identified:**
1. **Conversation context not passed on follow-ups** — When users reference prior questions after uploading new documents, the LLM doesn't receive conversation history to re-answer with new data
2. **Gateway evidence not persisted** — Citations/confidence/reasoning_steps from backend brain responses are not saved with conversation messages; reopening a conversation loses the evidence metadata

**Why This Matters:** 
- Documentation-code drift erodes stakeholder confidence and misaligns priorities
- Vague "still working on Phase 3" status masks real achievement and obscures actionable next steps
- Unstructured fix lists become stale; ordered priorities with technical scope keep work focused

**Pattern — MVP Declaration Criteria:**
- Core user flow works end-to-end (document upload → knowledge graph → chat with citations)
- Known limitations are documented with user impact, not hidden
- Next steps are ordered by priority with clear ownership and technical scope
- Phase summary tables reflect honest completion status

**Files Updated:**
- README.md — Added "Current State: Functional MVP" section with working features and known limitations
- oadmap/Tasks.md — Updated status to MVP achieved, added "Next Steps: Post-MVP Fixes" section with ordered priorities
- oadmap/Plan.md — Updated execution snapshot and Phase 3 status to MVP achieved with post-MVP fixes in progress
- Session plan.md — Updated current state assessment to reflect MVP achievement and post-MVP priorities

**Related Decision:** .squad/decisions/inbox/bob-mvp-docs.md — Documents the MVP declaration pattern and post-MVP fix prioritization strategy


### 2026-04-22 — Extensible URL & Document Ingestion Architecture

**Context:** Eric requested support for additional document types (txt, md, docx, json) and URL ingestion including YouTube videos and channels with transcript extraction.

**Analysis of Existing System:**
- Document processing: docling_service_fallback.py already handles PDF, DOCX, TXT, MD with fallback processors
- URL storage: iles table has source_type and source_url columns (existing infrastructure)
- Gap: JSON file processing missing, URL content not fetched, no YouTube handling

**Architecture Decision — Handler Registry Pattern:**
Created pluggable URL handler infrastructure with explicit extensibility seams:
- UrlHandler abstract base class defines can_handle() and etch() interface
- FetchedContent dataclass returns extracted text + metadata + optional child URLs
- Handlers sorted by priority (YouTube > Generic Webpage)
- UrlContentFetcher service orchestrates handler selection

**Key Pattern — Child URL Expansion:**
YouTube channels return child_urls list (video URLs). Processing router creates new iles rows for each child, queuing them for individual transcript extraction. This allows single URL submission to ingest entire channel content.

**Implementation Phases:**
1. JSON support added to docling fallback (immediate value, low risk)
2. URL handler infrastructure created (pp/services/url_handlers/)
3. Processing router updated to detect source_type == "url" and fetch before docling
4. YouTube handlers implemented with youtube-transcript-api dependency

**Files Created/Modified:**
- src/AspireApp.PythonServices/app/services/url_handlers/__init__.py
- src/AspireApp.PythonServices/app/services/url_handlers/base.py
- src/AspireApp.PythonServices/app/services/url_handlers/webpage.py
- src/AspireApp.PythonServices/app/services/url_handlers/youtube.py
- src/AspireApp.PythonServices/app/services/url_content_fetcher.py
- src/AspireApp.PythonServices/app/services/docling_service_fallback.py (JSON handler)
- src/AspireApp.PythonServices/app/routers/processing.py (URL fetch integration)
- src/AspireApp.PythonServices/app/services/database_service.py (URL datasource methods)
- src/AspireApp.Web/Controllers/FileUploadController.cs (.json extension)

**Dependencies Added:**
- httpx (async HTTP client)
- eautifulsoup4 (HTML parsing)
- 	rafilatura (main content extraction)
- youtube-transcript-api (video transcripts)

**Extensibility for Future:**
- RSS feed handler: implement RssFeedHandler, return child URLs for articles
- GitHub repo handler: parse README/docs, queue individual files
- PDF at URL: download to temp file, route to existing PDF processing
- Playlist handler: extract video URLs like channel handler

### 2026-06-30 — YouTube Transcript Rate-Limit Queue Architecture

**Context:** YouTube blocked rapid-fire transcript requests when processing channel child URLs. Current `_process_child_documents()` in `processing.py` (line 111-140) calls `_process_document_task_sync()` for each child video immediately in sequence — up to 50 transcripts back-to-back with zero delay.

**Decision:** New `youtube_transcript_queue` table in Postgres + lightweight `asyncio.create_task()` background poller in FastAPI. One transcript per minute, 50 per UTC day. No external scheduler (Celery/APScheduler rejected as overkill for single-item-per-minute loop).

**Key Architecture Points:**
- Separate table, not columns on `files` — rate-limiting is operational concern, not document lifecycle
- Daily cap tracked via `attempt_date DATE` column (`COUNT WHERE attempt_date = CURRENT_DATE`)
- `FOR UPDATE SKIP LOCKED` for defensive single-processing guarantee
- Schema created in `_ensure_database_schema()` following existing idempotent pattern
- Worker started in `startup_event()`, stopped in `shutdown_event()` (lines 93-143 of `fastapi.py`)
- Only change to existing code: conditional gate in `_process_child_documents()` — if `source_type == 'youtube_video'`, enqueue instead of process inline

**Key Files:**
- Processing pipeline: `src/AspireApp.PythonServices/app/routers/processing.py` (lines 69-140: child URL flow)
- Database schema: `src/AspireApp.PythonServices/app/services/database_service.py` (line 228: `_ensure_database_schema`)
- YouTube handler: `src/AspireApp.PythonServices/app/services/url_handlers/youtube.py`
- FastAPI lifecycle: `src/AspireApp.PythonServices/app/fastapi.py` (lines 93-143)
- Regression tests: `src/AspireApp.PythonServices/tests/test_processing_pipeline_regression.py`

**User Preference:** Eric wants date-based tracking for transcript attempts. UTC date column satisfies this cleanly.

**Test Impact:** `test_process_document_task_processes_child_urls` and `test_process_document_task_reuses_retryable_child_url_records` need updating — they currently expect inline processing of YouTube video children.

**Ownership:** Jarvis (table + worker + gate), Buster (test updates + worker tests), Jeff (optional UI for queued status).

**Decision recorded:** `.squad/decisions/inbox/bob-youtube-transcript-queue.md`

---

## Session: YouTube Transcript Queue Rate-Limiting (2026-04-20T07:07:50Z)

**Participants:** Bob (architecture), Jarvis (implementation), Buster (QA)
**Status:** COMPLETE — 3 decisions merged, 27+ tests passing
**Output:** Persistent PostgreSQL queue + async drainer for YouTube child video throttling (1/min, 50/day cap)

**Architectural Approval:**
- Queue table separation from document lifecycle (clean concern boundary)
- No external scheduler dependency (APScheduler/Celery rejected as over-engineering)
- Restart-safe via persisted attempt history
- Schema + async worker lifecycle validated; low risk (additive change only)

**Key Decisions Recorded:**
1. Bob: YouTube Transcript Rate-Limit Queue (architecture boundary, schema approval)
2. Jarvis: Persistent YouTube Transcript Queue (implementation details, throttle methods)
3. Buster: YouTube Transcript Queue Regression Seams (two-seam coverage strategy)

**Validation:** All 3 decisions merged to decisions.md; orchestration logs created; session log in squad/log/

**Cross-Agent Notes:**
- Jarvis implemented PostgreSQL integration (schema + throttle methods) — no conflicts with existing service boundary
- Buster split regression coverage across router seam (enqueue) and database seam (throttle policy) — good determinism
- Jeff not directly involved (UI polish deferred to Phase 3)

**Next Phase:** Monitor YouTube ingestion flows; Phase 3 UI signal for queued-but-not-yet-processed videos
