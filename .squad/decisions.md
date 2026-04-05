# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-04-05):** Merged 8 inbox decisions from Postgres cutover (Jeff, Jarvis, Buster) and BRAIN pivot (Kujan, Verbal, Eric). Archived 9 decisions from 2025-11-02 and 2026-03-27/28 (~7 KB) to `decisions-archive.md` to maintain ~20 KB target. Inbox cleared.

<!-- Decisions are appended below. Each entry starts with ### -->
## Postgres Cutover — Operational Data Migration — Bob — 2026-07-26

**Author:** Bob (Lead / Architect)  
**Status:** APPROVED — Ready for execution  
**Scope:** Replace SQLite shared-file pattern with Postgres for `files` and `document_pages` tables

### Context

The Web UI (C#/Blazor) and Python processing service currently share a single SQLite file (`data-resources.db`) via Docker bind mounts. This works but has caused recurring operational pain:

- WAL vs DELETE journal-mode conflicts across the Windows host / Linux container boundary
- Stale-read workarounds in Python (`_should_prefer_fresh_reads`, fresh connection fallbacks)
- Multi-candidate path resolution logic (8+ code paths to find the right `.db` file)
- `DeleteJournalModeInterceptor` hack in C# to force journal mode on every connection
- SQLite `CheckpointDatabaseAsync` calls after every write in FileStorageService
- Bind-mount file visibility issues between services

**Postgres is already provisioned in AppHost** (`builder.AddPostgres("postgres")` with `appdb` database, pgWeb, bind mount, user/pass parameters). Both services already `WaitFor(postgres)` and receive `POSTGRES_USER`/`POSTGRES_PASSWORD` environment variables. Neither service actually connects to Postgres yet.

### Decision

#### 1. Keep the same `files` + `document_pages` schema in Postgres

The schema is stable and well-documented in `docs/CROSS_SERVICE_CONTRACT.md`. Both sides agree on column names, types, and writer/reader ownership. No structural redesign needed.

**DDL changes (SQLite → Postgres):**

| SQLite | Postgres |
|--------|----------|
| `INTEGER PRIMARY KEY AUTOINCREMENT` | `SERIAL PRIMARY KEY` (or `GENERATED ALWAYS AS IDENTITY`) |
| `DATETIME` | `TIMESTAMPTZ` |
| `DEFAULT CURRENT_TIMESTAMP` | `DEFAULT NOW()` |
| `TEXT` (for JSON columns) | `JSONB` for `page_metadata`; `TEXT` for everything else |
| Placeholder `?` | Placeholder `%s` (psycopg2) |

**Indexes and constraints transfer directly.** The `UNIQUE(file_id, page_number)` and FK cascade behavior are standard SQL.

#### 2. C# Web Changes (Jeff owns)

| What | Action |
|------|--------|
| **NuGet packages** | Remove `Microsoft.EntityFrameworkCore.Sqlite`. Add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` |
| **Program.cs** | Replace `AddDbContext<UploadDbContext>(options.UseSqlite(...))` with `builder.AddNpgsqlDbContext<UploadDbContext>("appdb")`. Remove `ResolveSqliteConnectionString`, `GetSqliteDataSource`, `ShouldResolveAgainstContentRoot` helpers. Remove `DeleteJournalModeInterceptor` class entirely |
| **FileStorageService** | Delete `CheckpointDatabaseAsync()` and all calls to it. Remove `Microsoft.Data.Sqlite` import |
| **UploadDbContext** | Replace `HasDefaultValueSql("CURRENT_TIMESTAMP")` with `HasDefaultValueSql("NOW()")` in legacy entity config. Primary table config is attribute-driven and works cross-provider |
| **DocumentEntities.cs** | No changes needed — `[Column]` attributes are provider-agnostic |
| **AppHost.cs (webfrontend)** | Add `.WithReference(postgres)` to webfrontend. Remove `ConnectionStrings__DefaultConnection` env var (Aspire injects it via `WithReference`) |

#### 3. Python Changes (Jarvis owns)

| What | Action |
|------|--------|
| **requirements.txt** | Add `psycopg2-binary` (sync) or `psycopg[binary]` (async-capable). Remove: nothing (sqlite3 is stdlib) |
| **Dockerfile** | No change needed — `psycopg2-binary` has no native build deps |
| **DatabaseService class** | Replace `sqlite3` connection pool with `psycopg2.pool.ThreadedConnectionPool`. Remove `ConnectionPool` class. Remove all SQLite pragma logic. Remove multi-candidate path resolution. Remove fresh-connection workaround methods. SQL: `?` → `%s`, `AUTOINCREMENT` → `SERIAL`, add `RETURNING id` to inserts |
| **Connection config** | Read `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` from env. AppHost must pass these (see below) |
| **Schema init** | `_ensure_database_schema()` keeps `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS` — standard Postgres DDL. Remove `_ensure_required_columns` ALTER TABLE migration logic (fresh Postgres, no legacy schemas to heal) |

#### 4. AppHost.cs Changes (Jeff owns, but affects both)

**Add to Python service env vars:**
```
.WithEnvironment("POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
.WithEnvironment("POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
.WithEnvironment("POSTGRES_DB", "appdb")
```

Or, simpler: `.WithReference(postgres)` and read the Aspire-injected connection string. For a Dockerfile-based service the explicit env vars are cleaner since Python won't use Aspire service discovery natively.

**Remove from AppHost:**
- SQLite file setup block (lines 17-31): `sharedDatabaseFileName`, `sharedDatabaseFile`, `sharedDatabaseConnectionString`, `Directory.CreateDirectory`, `File.Create`
- `ASPIRE_DB_PATH` env var from Python service
- `/app/docs-database` bind mount from Python service (keep `/app/data` mount for file storage)
- `ConnectionStrings__DefaultConnection` env var from webfrontend

**Keep:** `sharedDatabasePath` directory creation and bind mount for the postgres data directory (already wired).

#### 5. Cross-Service Contract Update

`docs/CROSS_SERVICE_CONTRACT.md` section "Shared Database (SQLite)" becomes "Shared Database (PostgreSQL)". The table schema, status lifecycle, writer/reader ownership, and processing trigger contract all remain unchanged. Remove the journal-mode paragraph and path-resolution section.

### What This Eliminates

- `ConnectionPool` class (150+ lines of SQLite workarounds)
- `DeleteJournalModeInterceptor` class
- `CheckpointDatabaseAsync` method
- `_should_prefer_delete_journal` / `_should_prefer_fresh_reads` / `_fetch_*_from_fresh_connection` methods
- Multi-candidate database path resolution (~100 lines)
- SQLite pragma tuning (WAL, synchronous, mmap, cache_size, busy_timeout)
- All stale-read workarounds
- Journal-mode conflicts between host and container
- SQLite file creation at AppHost startup

**Net reduction:** ~400+ lines of SQLite-specific complexity across both services.

### Rationale

Postgres eliminates reliability issues inherent to bind-mounted SQLite files. Modern relational database handles concurrency, journal modes, and persistence correctly. Existing Aspire infrastructure already supports it.

### Impact

- Operational reliability: No more journal-mode conflicts or stale-read workarounds ✅
- Architectural clarity: Web and Python have separate, proper database connections via connection pooling ✅
- Code reduction: ~400+ lines eliminated across both services ✅
- Test infrastructure can now use proper test databases instead of in-memory SQLite ✅

---

## Web upload store Postgres cutover — Jeff — 2026-04-05

**Owner:** Jeff  
**Scope:** AspireApp.AppHost, AspireApp.Web, AspireApp.WebTest

### Decision

The Web operational upload store now uses the Aspire-managed PostgreSQL database resource exposed as `DefaultConnection`. AppHost injects that connection via `.WithReference(postgres)`, and the Web app resolves it through the existing `GetConnectionString("appdb")` path.

### Rationale

This keeps the Web-side cutover surgical: the upload API, EF context, and file-storage service keep their existing shape while the backing store switches from SQLite to Postgres. For this phase we intentionally kept Python's legacy configuration in AppHost so the current Python service startup path is not broken while Jarvis finishes the Python-side Postgres adoption.

### Impact

- Upload metadata is now written to Postgres instead of the shared SQLite file ✅
- SQLite-only Web concerns (path resolution, journal-mode interceptor, WAL checkpointing) are removed ✅
- WebTest now has a focused regression that uploads through the Web API and verifies the `files` row lands in Postgres ✅

---

## Python Postgres Upload Store Cutover — Jarvis — 2026-04-05

**Scope:** Python FastAPI operational document store (`files` + `document_pages`) shared with the Web upload flow

### Decision

The Python service should treat PostgreSQL as the source of truth for upload lifecycle state and extracted page rows. It should preserve the existing table and column contract used by the Web project instead of introducing Python-specific schema variants.

### Implementation notes

1. Resolve the operational store from connection-string-first configuration (`ASPIRE_DB_CONNECTION_STRING`, `POSTGRES_CONNECTION_STRING`, `DATABASE_URL`) with `POSTGRES_*` environment variables as the fallback contract.
2. Keep Python writes on the same canonical tables and columns:
   - `files` for lifecycle + processing metadata
   - `document_pages` for extracted page content
3. Keep `document_pages` uniqueness on `(file_id, page_number)` so retries and upserts stay deterministic.
4. Validate locally with fake pooled Postgres connections in Python tests rather than relying on a live database for every regression run.

### Rationale

The Web UI uploads documents that Python later processes, so both services need one shared operational schema. Holding the line on the existing `files` / `document_pages` contract reduces cross-service churn while allowing the runtime storage engine to move from SQLite to PostgreSQL.

### Impact

- Python no longer depends on SQLite journaling, file paths, or PRAGMA-based startup repair ✅
- Aspire now passes explicit Postgres connection settings to the Python service ✅
- Follow-up work on the Web side can switch providers without changing the Python-side table contract again ✅

---

## Shared Postgres Contract Audit Uses AppHost-Derived Store Name — Jarvis — 2026-04-05

**Scope:** Python regression coverage for the shared Postgres upload store

### Context

Eric updated `src/AspireApp.AppHost/AppHost.cs` so Aspire now loads with the correct Postgres connection-string wiring. Python manual verification still worked, but `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` was failing because it hardcoded an older database name literal (`DefaultConnection`) instead of validating the active shared-store contract.

### Decision

Cross-service Postgres contract audits should derive the upload-store database name from AppHost source and then verify that:

1. AppHost references that store from dependent services,
2. Python receives the same name through `POSTGRES_DATABASE`, and
3. Web resolves the same name through `GetConnectionString(...)`.

### Rationale

The durable contract is "all three surfaces point at the same named Postgres upload store," not a specific historical connection-string name. Deriving the name from AppHost keeps the test sensitive to real drift while avoiding false failures when the store is legitimately renamed during infrastructure fixes.

### Impact

- Python contract tests now flag real contract mismatches instead of stale literals ✅
- AppHost naming fixes remain verifiable without touching Python runtime code ✅
- The shared `files` / `document_pages` schema audit remains the primary Python-side proof surface ✅

---

## Buster Regression Verdict — 2026-04-05

**Scope:** Postgres upload-store regression checks spanning AppHost, Web, WebTest, and Python contract audit

### Context

Eric updated the AppHost/Web connection behavior so the app would start and manual upload + Python document API checks worked again. The automated gate then failed, so QA needed to determine whether the break was in product code, the harness, or stale assumptions left behind by the Postgres cutover.

### Decision

Treat this as a **test/harness regression**, not a product rollback:

1. The live runtime contract uses the Aspire-managed Postgres database name `appdb`.
2. Regression tests must assert **alignment** across AppHost, Web, and Python, not a hardcoded legacy name like `DefaultConnection`.
3. WebTest fixture validation must read `ConnectionStrings__appdb` and require `POSTGRES_DATABASE=appdb`.
4. A locked `AspireApp.WebTest.exe` process is an execution-environment failure mode, not application evidence; clear the stale process before rerunning WebTest.

### Rationale

Manual behavior and source inspection both pointed to a consistent runtime: AppHost registers `appdb`, Web reads `GetConnectionString("appdb")`, and Python receives `POSTGRES_DATABASE=appdb`. The red tests were rejecting that valid state because they encoded the old database name directly.

### Impact

- Python contract audit reflects the current Postgres contract again ✅
- WebTest fixture no longer blocks a correct AppHost/Web/Python startup because of a stale connection-string key ✅
- Future renames stay testable if the contract test continues deriving the DB name from AppHost rather than hardcoding it ✅

---

## BRAIN Pivot — Kujan Architecture Review — 2026-07-15

**Agent:** Kujan (Adversarial Architect Reviewer)  
**Scope:** BRAIN pivot viability — all six roadmap/planning documents vs. actual implementation  
**Date:** 2026-07-15

### Executive Summary

The AspireAI codebase is a well-orchestrated document processing pipeline masquerading as an agentic AI platform in its planning documents. The gap between the BRAIN specification (6 independent service layers with structured contracts, multi-agent reasoning, confidence scoring, and domain extensibility) and the actual implementation (a Blazor chat UI + a Python monolith that writes document pages to Neo4j) is **structural, not incremental**. The current architecture can serve as infrastructure scaffolding (Aspire orchestration, Dockerized services, Neo4j/Ollama containers), but the service boundaries, data model, and inter-service communication patterns all need to be redesigned. The most critical finding: three of the six BRAIN layers (Validation, Reasoning, Application) have zero implementation and zero infrastructure to support them. The LightRAG integration, which consumed significant effort, is architecturally opposed to BRAIN's requirement for transparent, controllable knowledge construction.

### Key Findings

**BRAIN layers with zero implementation:**
1. **Validation Layer (Truth Engine)** — No claim extraction, confidence scoring, evidence references, or contradiction detection
2. **Reasoning Layer (Agent System)** — No agent infrastructure; Semantic Kernel only used for basic chat
3. **Application Layer** — No domain abstraction; hardcoded to one workflow (upload → parse → store)

**What can be reused:**
- Aspire AppHost orchestration (solid, extensible)
- Dockerized Neo4j + Ollama container infrastructure
- Docling parsing (core for Ingestion Layer)
- SQLite operational schema (for pipeline state, not knowledge store)
- Health check patterns

**What is wasted or misaligned:**
- LightRAG integration is architecturally opposed to BRAIN (opaque, uncontrollable knowledge construction)
- Neo4j Document→Page graph doesn't serve BRAIN's entity/claim/concept model
- Phase 4-5 (Flat RAG, LightRAG/GraphRAG) are superseded by BRAIN layers

### Critical Gaps

| BRAIN Layer | Current Implementation | Coverage |
|-------------|----------------------|----------|
| **Ingestion** | Docling parsing + markdown export | ~40% — file-based ingestion works, but no connector architecture |
| **Knowledge** | Neo4j Document→Page graph + LightRAG | ~25% — graph store exists but schema is wrong for BRAIN |
| **Validation** | None | **0%** |
| **Reasoning** | None | **0%** |
| **Application** | None | **0%** |
| **Interface** | Blazor chat + FastAPI endpoints | ~30% — chat UI exists but no API gateway, no response contracts |

### Recommendations

**Do Now (Before Writing New Code):**
1. Define BRAIN core contracts — Create `CanonicalDocument`, `Claim`, `ValidatedDocument`, `KnowledgeResult`, `ReasoningStep`
2. Repurpose ApiService as Interface Service — Delete weather stub, wire as BRAIN API gateway
3. Choose and add a vector store — Add Qdrant or similar to AppHost

**Do Next (First Vertical Slice):**
4. Extract Knowledge Service — Move Neo4j/RAG logic into dedicated service
5. Build minimal Validation Service — LLM-based claim extraction + confidence scoring
6. Wire Semantic Kernel for agent orchestration

**Stop:**
7. LightRAG integration work — Not aligned with BRAIN's need for transparent knowledge construction
8. Phase 4/5/6 as currently scoped — These are superseded by BRAIN layer architecture

### Critical Questions for Eric

1. Agent framework choice (Semantic Kernel vs. LangGraph)?
2. LightRAG disposition (keeper, fallback, or deprecated)?
3. Vector store selection (Qdrant, Chroma, Neo4j indexes, pgvector)?
4. Multi-tenant timeline (Phase 1 or later)?
5. Python service decomposition (split now or keep monolith)?
6. Confidence scoring strategy (LLM-based, heuristic, or cross-reference)?
7. Which domain for first vertical slice (QA intelligence recommended)?

### Decision

**BRAIN pivot is viable but requires architectural redesign, not incremental feature addition.** The Aspire orchestration layer, container infrastructure, and Docling parsing are reusable foundations. The Python monolith needs decomposition, the Neo4j schema needs extension (not replacement), a vector store must be added, and three entirely new service layers (Validation, Reasoning, Application) must be built from scratch. LightRAG should be deprecated as a primary integration path because it's architecturally opposed to BRAIN's requirement for transparent knowledge construction with validation interception.

### Impact

- Roadmap (`Plan.md`) needs rewrite — current Phases 4-8 are superseded ✅
- LightRAG integration effort is sunk cost — keep for reference, don't extend ✅
- ApiService gets repurposed — no longer vestigial ✅
- New contracts directory needed before implementation begins ✅
- Three new services needed — Validation, Reasoning, Application ✅

---

## Tenant Context UI Slice — Data Layer and API Contract — Bob, Jarvis, Kujan, Buster — 2026-04-05

**Authors:** Bob (Lead/Architect), Jarvis (Python/Data Dev), Kujan (Adversarial Architect), Buster (QA/Tester)  
**Status:** APPROVED — Data layer complete, ready for UI phase  
**Scope:** Multi-tenant foundation for BRAIN Phase 1; establish tenant_id in schema, API, and service layer

### Context

The BRAIN roadmap (Plan.md line 97) requires all contracts to include `tenant_id` for multi-tenant isolation. Jeff's initial tenant-context implementation was rejected because FileUploadController signatures changed without matching FileStorageService updates, creating a coherence gap that broke the build. Subsequent revisions addressed schema synchronization, contract validation, and operational testing. Final approval by Buster closes the data layer and API contract for the next UI phase.

### Decision

#### 1. Tenant Schema Pattern (Bob, Jarvis)

**Column definition (both C# and Python):**
```sql
tenant_id TEXT NOT NULL DEFAULT 'default'
```

**Indexes:**
- `idx_files_tenant` (single column) for tenant-scoped full queries
- `idx_files_tenant_status` (composite on tenant_id, status) for filtered queries

**Rationale:** Allows multi-tenant file isolation without schema redesign. DEFAULT 'default' provides backward compatibility for existing clients. Composite index optimizes common query pattern (files by tenant and processing status).

#### 2. API Contract (Bob)

**Header-based tenant selection:**
```
X-Tenant-Id: <tenant_id>
```

**Extraction logic (FileUploadController.GetTenantId()):**
- Read `X-Tenant-Id` header from request
- Default to `"default"` if not provided
- Pass to FileStorageService methods

**Service signatures:**
- `AddFileAsync(string filename, Stream content, string tenantId)`
- `AddUrlAsync(string url, string tenantId)`
- `GetAllFilesAsync(string? tenantId)` — returns all if tenant is null (backward compatible)

**Rationale:** Header-based selection keeps Web/Python separation clean. Both services read the same header and persist/query by tenant_id. Optional parameter allows backward-compatible null filtering.

#### 3. Python Schema Alignment (Jarvis, Kujan)

**Changes to DatabaseService:**
- Added `tenant_id` to `_files_column_definitions` (line 88)
- Added `tenant_id` to CREATE TABLE statement (line 235)
- Added `tenant_id` to INSERT placeholders in `create_file_record()`
- Added `tenant_id` to all SELECT projections (`_fetch_file_row`, `_fetch_all_file_rows`, `_fetch_unprocessed_file_rows`)
- Added two indexes in `_ensure_database_schema()` (lines 263-264)
- Updated `_row_to_file_dict()` tuple mapping to include tenant_id at correct ordinal

**Rationale:** Explicit round-trip testing (write tenant_id, read it back) closes the contract audit gap where the column existed but was not actually persisted/retrieved.

#### 4. Contract Audit Validation (Kujan, Buster)

**Python test coverage:**
- `test_database_service_initializes_canonical_schema_and_indexes` — verifies tenant_id column and indexes exist
- `test_web_file_metadata_columns_match_python_projection` — asserts tenant_id alignment across Web/Python boundary
- Explicit round-trip: `create_file_record(tenant_id="test-tenant")` → `get_file_by_id()` → assertion on tenant_id value

**C# test coverage:**
- `OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres` — SELECT includes tenant_id column, assertion validates default tenant_id value

**Verification:** 8/8 Python contract tests pass. 1/1 C# operational test passes.

### Intentional Deferrals (Next UI Phase)

The following are explicitly **not** implemented in this slice (test scaffolding provided):

1. **Tenant selector UI** — NavMenu component for user tenant selection
2. **Session state** — Store selected tenant_id in browser session/cookie
3. **Frontend header propagation** — UploadData and Chat components must attach X-Tenant-Id header to API calls
4. **Multi-tenant duplicate detection** — Current file hash uniqueness is global; should scope to (tenant_id, file_hash)
5. **Tenant-aware delete** — Verify delete operations respect tenant boundary
6. **Python query filtering** — `get_unprocessed_files()` reads all files; should accept optional tenant_id parameter

**Scaffolding location:** `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs` lines 157-258 contain commented test templates showing expected coverage when UI components are implemented.

### Verdict

✅ **APPROVED** — Data layer and API contract are coherent and protected by validation. All tests pass. The missing UI selector is the expected next phase, not a blocking defect in this slice.

**What this slice accomplishes:**
- Schema stability: tenant_id column with NOT NULL + default constraint
- Index support: Query optimization for tenant-scoped and composite queries
- API contract: Header extraction with "default" fallback
- Service layer: Both C# and Python accept and persist tenant_id
- Query filtering: GetAllFilesAsync(tenantId) scopes results
- Cross-service validation: Contract audit confirms C#/Python alignment

**What remains for UI phase:**
- Tenant selection UI component
- Session state management
- Frontend header attachment
- Multi-tenant duplicate detection
- Tenant-aware delete operations
- Python processing pipeline tenant awareness

### Trade-Offs

**Chosen:** Minimal scope — only add tenant_id to schema and service layer without enforcing global filtering or tenant isolation in processing pipelines.

**Alternative (rejected):** Full tenant filtering across all queries. Rejected because broader scope (requires deciding on filtering strategy, row-level security policies) and can be layered incrementally.

---

## BRAIN Pivot — Key Decisions — Eric — 2026-07-15

**Status:** APPROVED

### Decisions

1. **Product Direction:** Pivot from chat-oriented RAG application to BRAIN — a domain-agnostic agentic knowledge assistant with proactive Jarvis-like behavior
2. **First Domain Slice:** Domain-agnostic knowledge coalescing engine with source-aware confidence scoring and proactive assistant personality (not a specific domain module)
3. **LightRAG Disposition:** Keep behind `IKnowledgeRetriever` abstraction. BRAIN contracts are primary. LightRAG is a pluggable retrieval backend, not the system of record
4. **Agent Framework Location:** Python for Reasoning/Validation/Knowledge layers. C# with Microsoft.Extensions.AI for Interface/Gateway/Aspire
5. **Multi-Tenancy Timing:** Design for tenancy from day 1 (tenant_id in all contracts). Implement isolation enforcement in Phase 6
6. **Vector Store:** Neo4j vector indexes (Neo4j 5.x). Swap to Qdrant later if needed, behind `IKnowledgeRetriever` abstraction
7. **Python Decomposition:** Internal packages first (`app/brain/ingestion/`, `app/brain/knowledge/`, etc.). Extract to separate Aspire services when contracts stabilize
8. **Breaking Changes Strategy:** Feature branch (`brain-pivot`). Merge to main when first agentic slice works end-to-end
9. **Proactive Behavior:** Core to MVP. Jarvis-like personality (suggesting, inferring, offering context) ships in Phase 3, not deferred

### Superseded Plans

- Plan.md Phases 4-8 (Flat Vector RAG, LightRAG/GraphRAG, Plugin Ecosystem, Testing/Deployment, Advanced Features)
- ApiService as vestigial weather stub (now: BRAIN API Gateway)
- Direct Blazor → Ollama chat path (now: Blazor → Gateway → Reasoning → Knowledge)

---

## BRAIN Pivot — Strategic Product Review — Verbal — 2026-07-15

**Scope:** Vision-roadmap alignment, scope risk assessment, MVP definition, prioritization

### Executive Summary

The current roadmap is still optimizing a document-chat product, not building BRAIN. `Plan.md` defines the product as a "configurable, modular Blazor-based chat assistant," while the BRAIN specs define a domain-agnostic cognition layer with explicit ingestion, knowledge, validation, reasoning, application, and interface layers. Those are not the same product, and the roadmap never resolves that conflict.

### Vision-Roadmap Misalignment

1. **Product definition inconsistency:** Plan.md frames as Blazor chat assistant; Architecture.md frames as retrieval-augmented chat; BRAIN specs frame as reusable cognition layer. These are different products.
2. **Phases don't lead to BRAIN:** Current sequence (Phase 3: ingestion → Phase 4: flat RAG → Phase 5: LightRAG → Phase 6: plugins) optimizes RAG application, not BRAIN layers. No dedicated phase for Validation, Reasoning, Application, or unified interface/gateway.
3. **Task breakdown confirms pre-BRAIN state:** No automatic upload → processing trigger, no chat retrieval + citations wired, LightRAG stability remains shaky.
4. **Architecture.md mixes incompatible ambition:** Tries to hold both pragmatic current system (upload → Docling → Neo4j → chat) and ambitious target system (tenants, claims, evidence, concepts, contradiction detection). No bridge between schemas.

### Scope Risk Assessment

| Risk | Severity | Mitigation |
|---|---|---|
| Product identity drift (chat app vs cognition layer) | Critical | Rewrite roadmap around BRAIN contracts and one domain slice |
| No first domain selected | Critical | Pick one slice now; QA intelligence is strongest |
| Platform-first overbuild | Critical | Build one orchestrated flow before additional layers |
| Multi-tenancy too early | High | Defer until after MVP proof |
| Direct UI → Ollama architecture persists | High | Insert BRAIN gateway/reason endpoint early |

### Recommended MVP: "Minimum Viable Agentic"

**Definition:** BRAIN is minimally viable when it can take a user goal, retrieve relevant evidence from ingested sources, produce a structured recommendation or plan, and explain both confidence and evidence.

**Concrete interaction flow:**
1. User uploads document or adds website URL
2. BRAIN normalizes both into canonical ingestion contract
3. User asks goal-oriented question
4. BRAIN runs single orchestrated flow: retrieve evidence → synthesize answer → run critic/self-check → return structured output
5. UI shows recommendation, evidence, confidence, unresolved questions

**Acceptance criteria:**
1. Two source types, one contract (file upload + URL ingestion)
2. One reasoning endpoint (`/reason`)
3. Evidence is mandatory on all responses
4. Confidence is explicit using transparent heuristic
5. One domain workflow works end to end
6. System honestly says "insufficient evidence" instead of improvising
7. One automated proof path exists end-to-end

### Recommended New Phase Sequence

**Phase 0 — Reframe the Product**
- Declare BRAIN as product core
- Declare chat as one interface, not the architecture
- Choose first domain slice
- Define non-goals for MVP

**Phase 1 — Define Core Contracts**
- CanonicalDocument, KnowledgeResult, ReasonResponse
- Evidence and confidence schema
- Correlation/trace contract

**Phase 2 — Unify Ingestion + Knowledge Baseline**
- File + URL through one ingestion contract
- Explicit processing trigger
- Stable retrieval backend behind one interface
- Status visibility and failure visibility

**Phase 3 — Ship Minimum Viable Agentic Slice**
- Implement `/reason`
- Support `answer` + `plan` modes
- Retrieve → synthesize → critic/self-check
- Return evidence, confidence, unresolved questions
- Single-tenant only

**Phase 4 — Evaluate and Harden**
- Automated end-to-end proof
- Latency and quality baselines
- Honest insufficient-evidence behavior
- Operational observability

**Phase 5 — Prove Reusability**
- Add second connector OR second domain module
- Keep same contracts
- Decide if graph-specific enrichment materially improves outcomes

**Phase 6 — Scale Deliberately**
- Multi-tenancy, auth/access control
- Deployment hardening, plugin ecosystem
- Advanced graph reasoning, long-term memory

### What Should Be Cut From Near-Term Plan

- **Cut** the old product framing: "Blazor-based chat assistant"
- **Cut** separate product phases for Flat RAG then GraphRAG
- **Cut** plugin ecosystem as a near-term phase
- **Cut** multi-tenant RAG from the pivot-critical path
- **Cut** any assumption that chat polish proves BRAIN

### Impact

- Roadmap clarity: BRAIN is the product, not RAG optimization ✅
- Scope discipline: MVP focuses on one evidence-backed agentic loop ✅
- First domain selection unlocks concrete success criteria ✅
- Multi-tenancy deferred until product thesis is proven ✅
- Vertical slice approach prevents platform-first overbuild ✅

---
