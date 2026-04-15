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
