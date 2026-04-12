# Kujan — Project History

## Project Context

**Project:** AspireAI — AI-powered document processing and RAG platform pivoting to domain-agnostic agentic assistant (BRAIN)
**Owner:** Eric Van Artsdalen
**Stack:** C# (.NET 10), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
**Current State:** Phase 3 (Document Upload & Ingestion) partially complete; pivoting from chat-oriented RAG to modular agentic architecture

## Learnings

- Joined the team to provide adversarial architectural review for the BRAIN pivot
- Completed full six-lens review of all roadmap documents vs. actual implementation (2026-07-15)
- Key finding: 3 of 6 BRAIN layers (Validation, Reasoning, Application) have zero implementation and zero infrastructure
- LightRAG is architecturally opposed to BRAIN's transparent knowledge construction requirement — recommended deprecation
- Neo4j has Document→Page graph; BRAIN needs Claim→Evidence→Concept→Entity — complete schema mismatch
- Semantic Kernel is only used for basic chat completion; could be repurposed for agent orchestration in Reasoning Service
- ApiService is a weather forecast stub — recommended repurposing as Interface Service / API gateway
- Python monolith carries 3-4 BRAIN layers in a single process — needs decomposition
- No vector store exists under application control (LightRAG's NanoVectorDB is opaque)
- Decision written to `.squad/decisions/inbox/kujan-arch-review.md`

### 2026-04-05 — Tenant ID Contract Audit Gap Closed

**Status:** ✅ COMPLETE — Adversarial review caught silent regression risk, now closed

**What Happened:**
1. Jeff implemented tenant-context UI → Buster rejected (API coherence gap)
2. Bob revised → Buster rejected (schema not persisted in Python)
3. Jarvis added Python schema → Buster rejected (contract audit gap)
4. **Kujan's task:** Verify tenant_id actually persists and round-trips, not just exists as column

**Root Cause of Gap:**
- Contract audit checked that the string `"tenant_id"` appeared in source files
- No explicit test wrote a non-default value and read it back
- Silent regression risk: developer could drop tenant_id from INSERT or SELECT, tests would still pass

**Kujan's Fix:**
1. **Python DatabaseService:**
   - Added `tenant_id` parameter to `create_file_record()` with default `"default"`
   - Added `tenant_id` to INSERT column list and VALUES placeholders
   - Added `tenant_id` to all SELECT projections (`_fetch_file_row`, `_fetch_all_file_rows`, `_fetch_unprocessed_file_rows`)
   - Updated `_row_to_file_dict()` tuple mapping to include tenant_id at index 15

2. **Python Contract Audit Test:**
   - Added explicit `tenant_id="test-tenant"` to `create_file_record()` call
   - Added `get_file_by_id()` fetch and assertion: `self.assertEqual("test-tenant", file_record["tenant_id"])`

3. **Fake Postgres Infrastructure:**
   - Added `"tenant_id"` to `FILE_COLUMNS` list at correct ordinal position
   - Added `"tenant_id": "default"` to `COLUMN_DEFAULTS`
   - Updated INSERT parameter mapping to match new column order

4. **C# Operational Test:**
   - Added `tenant_id` to SELECT column list in `UploadApiPersistsMetadataToPostgres`
   - Added assertion: `Assert.Equal("default", reader.GetString(5));`

**Why This Closes the Gap:**
- **Before:** Column name check only (structural validation)
- **After:** Explicit write/read/assert cycle (behavioral validation)
- Tests now fail if tenant_id is dropped from INSERT, SELECT, or tuple mapping

**Validation:**
- ✅ 8/8 Python contract tests pass (all surfaces aligned, round-trip verified)
- ✅ 1/1 C# operational test passes (default tenant_id persists end-to-end)

**Pattern:** Never rely on string presence in code. Always test the actual operation (write → read → assert value).

**Files Modified:**
- `src/AspireApp.PythonServices/app/services/database_service.py` — tenant_id in INSERT/SELECT
- `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` — explicit round-trip assertion
- `src/AspireApp.PythonServices/tests/fake_postgres.py` — infrastructure tuple order fix
- `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs` — C# operational test

**Result:** Buster issued APPROVED verdict. Data layer ready for UI phase.

### 2026-04-05 — Auth Rescue Investigation

- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` seeds and verifies the critical upload flow by signing in with the demo provider, so tenant-aware API calls must stay aligned with the demo tenant or the API-backed upload assertions go dark.
- `src/AspireApp.Web/Components/Pages/UploadData.razor.cs` was loading datasource rows without tenant scoping; the UI now needs tenant-filtered reads to stay coherent with the controller contract.
- `src/AspireApp.Web/Program.cs` now contains `/auth/mock/signin` and `/auth/mock/signout` endpoints plus `MockAuthSessionCookie` middleware gating for `/chat`, `/upload`, and `/weather`; protected-route honesty on hard navigations depends on that browser cookie path working correctly.
- `src/AspireApp.Web/Services/AppAuthenticationStateProvider.cs` can silently retain stale in-memory auth unless cookie hydration also clears missing-cookie cases; direct browser navigations are the weak seam to pressure-test.
