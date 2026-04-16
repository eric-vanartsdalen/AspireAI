# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Core Context

**Active Themes (as of 2026-04-15):**
- **P2-C Vector Infrastructure:** Foundation-first scoping validated; embedding population + retriever wiring in progress
- **Test Reliability:** Intermittent failures traced to environmental prerequisites (Playwright Chromium) and timing assumptions (Ollama latency variance). Must document setup; test infrastructure hardening in progress
- **Aspire Orchestration:** Service dependency ordering critical; health checks prevent false-positive startup; environment variable configuration passes across service boundaries cleanly
- **Document Processing Pipeline:** Background task queuing (fire-and-forget) must preserve status polling; async patterns require explicit event-loop management to avoid starving FastAPI

**Key Technical Decisions:**
1. Upload processing deferred to background task after response returns (status reporting via polling)
2. Python background work must not block FastAPI event loop (`asyncio.to_thread()` pattern)
3. Chat focus state managed via explicit render-time flags, not eager autofocus (prevents rename-typing interference)
4. Playwright browser installation must be documented as dev prerequisite (not bundled in repo)
5. Ollama contention serialized (defer LightRAG handoff until Python embedding work complete)

**Working Relationships:**
- Close collaboration with Jeff on timing/architecture issues
- Bob drives Phase roadmap and architecture decisions
- Jarvis handles Python/Neo4j implementation details
- Eric provides user direction and design intent

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-16 — Upload/delete Playwright flake was a test seam, not a product regression

**Task:** Reproduce the reported `BasicAspireAppHostTests.DeleteUploadedTestFile` failure (`Navigation target 'Upload Documents' did not become clickable within 30000ms`).

**Key findings:**
- The failure did **not** reproduce as a stable product bug: `DeleteUploadedTestFile` passed three consecutive reruns before hardening, `FlowEndToEnd` passed, and the protected-route auth slice (`AuthUxFoundationTests.SignedInUserCanReachProtectedAppAreas`) also passed.
- The brittle seam was the Playwright helper path, not the Upload UI. `BasicAspireAppHostTests` reached `/upload` by clicking the off-canvas `"Upload Documents"` sidebar link through `ClickByRole(...)`, while adjacent tests already used more reliable patterns: direct protected-route navigation (`AuthUxFoundationTests`) or direct mock sign-in with `returnUrl=%2Fupload` (`AuthenticatedUploadUxTests`).
- For `BasicAspireAppHostTests`, the durable pattern is to enter the upload surface through the mock auth endpoint with `returnUrl=%2Fupload` and wait for tenant/upload controls, instead of asserting that a sidebar link becomes viewport-clickable.

**Validation:**
- `dotnet test .\src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~AspireApp.WebTest.Tests.BasicAspireAppHostTests.DeleteUploadedTestFile"` ✅
- `dotnet test .\src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~AspireApp.WebTest.Tests.BasicAspireAppHostTests.FlowEndToEnd"` ✅
- `dotnet test .\src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~AspireApp.WebTest.Tests.AuthUxFoundationTests.SignedInUserCanReachProtectedAppAreas"` ✅

**Key file paths:**
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`
- `src\AspireApp.WebTest\Tests\AuthenticatedUploadUxTests.cs`
- `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`

**Pattern:**  
- When Playwright test failures occur, always classify as "test seam" or "product bug" before fixing
- Use adjacent passing tests as control validation (proof that infrastructure works)
- Sidebar nav visibility is environment-dependent (animation, viewport). Use hard redirects for upload/chat/tenant tests
- Direct protected-route entry (`returnUrl=/upload`, `page.GotoAsync("/upload")`) is more stable than sidebar click + link-visibility wait

**Decision merged:** `.squad/decisions.md` — "Direct Protected-Route Sign-In for Upload UI Tests"
- `src\AspireApp.Web\Components\Layout\NavMenu.razor`
- `src\AspireApp.Web\Components\Layout\MainLayout.razor`

### 2026-04-16 — Team Sync: Regression Coverage for Conversation Context + Evidence Persistence

**Context:** Jarvis implemented `conversation_history` support. Jeff wired gateway history + metadata persistence. Cross-service alignment proven.

**What I Did:**
- Added regression coverage proving follow-up requests carry prior turns into backend chat calls
- Validated that evidence/confidence/reasoning metadata survives persistence and re-renders on conversation reopen
- Closed focused test slices: gateway history carriage (9/9), metadata round-trip (44/44 .NET), critique mode state persistence (44/44)
- Verified cross-service contract alignment (Python/C# history shapes match)
- Identified carry-forward QA gap: E2E browser proof (Playwright/Aspire) for save → hard reload → reopen → citations visible

**Coverage Status:**
- ✅ Unit-level follow-up context proven (54 Python tests)
- ✅ Service-level persistence proven (44 .NET tests)
- ✅ Contract round-trip proven (cross-service serialization)
- ⏳ E2E browser proof pending (Playwright/Aspire orchestration test)

**Key Patterns Validated:**
- **Follow-up context:** Prior turns successfully travel through retrieval + generation
- **Metadata survival:** Evidence/confidence/reasoning persisted and rehydrated on reload
- **Backward compatibility:** Null history normalized cleanly; older callers unaffected
- **Chat mode persistence:** Regular ↔ Critique toggle state survives reload

**Carry-Forward Gap:**
- Honest assessment: All plumbing proven in isolation. Missing: real Playwright browser test that uploads doc → asks question → saves → hard reload → reopen → verify citations/confidence visible
- Deferred to Phase 3b polish (Playwright Chromium installation not documented in dev prerequisites)

**Key file paths validated:**
- `src\AspireApp.WebTest\Tests\BrainGatewayPhase2Tests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`
- `src\AspireApp.PythonServices\tests\test_brain_chat.py`
- `src\AspireApp.PythonServices\tests\test_critique_pipeline.py`

### 2026-04-15 — Critique mode failures need provider-level and no-retry regression coverage

**Task:** Reproduce the critique-mode 502/api-key failure, validate Jeff/Jarvis fixes, and close the most obvious regression gaps.

**Key findings:**
- `pydantic-ai==0.0.55` in this repo does **not** expose `OpenAIChatModel` or `pydantic_ai.providers.ollama`; the working Ollama seam uses `OpenAIModel` plus `OpenAIProvider(base_url=f"{OLLAMA_ENDPOINT}/v1", api_key="ollama")` so critique mode does not depend on ambient `OPENAI_API_KEY`.
- BRAIN gateway/chat POST clients now live behind `AddBrainBackendClient()` and `AddBrainGatewayChatClient()` extension methods, and both explicitly disable retries for unsafe HTTP methods so deterministic critique-mode 503s are surfaced once instead of fanning out into Polly retry noise.
- Saved conversation coverage needed one more seam: service tests now assert `chat_mode` persistence/normalization, and UI tests now assert switching between saved critique and regular threads flips the radio state correctly after reload.

**Key file paths:**
- `src\AspireApp.PythonServices\app\brain\reasoning\pydantic_ai_provider.py`
- `src\AspireApp.PythonServices\tests\test_critique_pipeline.py`
- `src\AspireApp.WebTest\Tests\BrainGatewayPhase2Tests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`
- `src\AspireApp.Web\Services\BrainChatClientServiceCollectionExtensions.cs`
- `src\AspireApp.ApiService\Services\BrainBackendClientServiceCollectionExtensions.cs`

### 2026-04-15 — Upload regression fixes were test-scaffolding, not product regressions

**Task:** Reproduce and fix failing `BasicAspireAppHostTests.FlowEndToEnd` and `FileUploadControllerTests`.

**Key findings:**
- `src\AspireApp.Web\Controllers\FileUploadController.cs` now queues automatic processing on a fire-and-forget background task after a short delay so the upload response can return with `status="uploaded"` first.
- `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs` must poll the fake coordinator for queueing instead of asserting synchronously at controller-return time.
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs` polls the Python `processing/status/{id}` endpoint over a long overall timeout; individual `HttpClient.Timeout` cancellations can happen during cold or busy startup and should be treated as transient inside the polling helper, not as immediate test failure.

**User preference:**
- Eric explicitly said this repo does **not** use GitHub Issues for this workflow.

**Key file paths:**
- `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs`
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`
- `src\AspireApp.Web\Controllers\FileUploadController.cs`

### 2026-04-17 — P2-C Vector Infrastructure Review: APPROVED with Honest Foundation-First Status

**Task:** Review P2-C uncommitted working tree changes for correctness and roadmap honesty.

**Verdict:** ✅ **APPROVED** — P2-C foundation work is honestly scoped and correctly claimed.

**What Was Reviewed:**
1. `AppHost.cs` — Embedding config passed to Python services (OLLAMA_ENDPOINT, EMBEDDING_MODEL, EMBEDDING_DIM)
2. `neo4j_service.py` — Vector index creation (`_ensure_vector_indexes()`) + search methods (`search_claims_vector()`, `search_pages_vector()`)
3. `embedding_service.py` — New service with Ollama-first/local-fallback pattern
4. `test_vector_infrastructure.py` — 11 tests validating infrastructure (all passing)
5. `roadmap/Tasks.md` — P2-C status updated to "IN PROGRESS" with clear remaining work

**Why This Is Honest:**
- **Roadmap correctly states "foundation implemented"** — Does NOT claim vector retrieval is live
- **Clear separation:** Infrastructure (indexes, search helpers, embedding service) vs. population pipeline (not started)
- **Tests validate infrastructure contracts:** Index creation is idempotent; search methods use correct Neo4j 5.x vector syntax; embedding service consumes Aspire config
- **Status wording:** "🟡 IN PROGRESS" instead of "✅ COMPLETE" — accurate signal that embedding population + retrieval integration remains
- **No overclaim:** Tasks.md line 173 explicitly states "Remaining P2-C work: Populate embeddings ... wire vector search into retrievers"

**Specific Validation:**
- ✅ Vector indexes created with `IF NOT EXISTS` (idempotent, safe on startup)
- ✅ Search methods use `db.index.vector.queryNodes()` with cosine similarity (Neo4j 5.x syntax)
- ✅ Embedding dimension configurable via `EMBEDDING_DIM` env var (1024 for bge-m3, 384 for MiniLM fallback)
- ✅ AppHost wires `OLLAMA_ENDPOINT`, `EMBEDDING_MODEL`, `EMBEDDING_DIM` to Python services
- ✅ Python service waits for `ollama` and `embeddingmodel` before starting
- ✅ `EmbeddingService` gracefully handles missing dependencies (returns None instead of crashing)
- ✅ 11/11 tests pass (`test_vector_infrastructure.py`)
- ✅ Related tests still pass (28/28 in `test_lightrag_retriever.py`, `test_knowledge_retriever.py`, `test_rag_semantic_search.py`)

**Correctness Checks:**
- **Config wiring:** `AppHost.cs` correctly retrieves `aiEmbeddings` from `AI-Embedding-Model` parameter, passes to Python via `EMBEDDING_MODEL` env var
- **Dependency ordering:** Python service correctly waits for both Ollama container AND embedding model resource before starting
- **Index syntax:** Vector index queries use correct Neo4j 5.x syntax (`CREATE VECTOR INDEX ... IF NOT EXISTS`, `vector.dimensions`, `vector.similarity_function: 'cosine'`)
- **Dimension consistency:** `EMBEDDING_DIM=1024` matches bge-m3 model output; fallback to 384 for sentence-transformers/MiniLM is documented
- **Search contract:** `search_claims_vector()` and `search_pages_vector()` return standard result shape (content, confidence, document_id, page_number, relevance_score) compatible with existing retrievers

**No Bugs Found:**
- No config mismatches
- No missing dependencies in AppHost wiring
- No test gaps for infrastructure contracts
- No overclaims in roadmap wording

**Contrast With Earlier P2-B Review:**
- **P2-B (2026-11-02):** Rejected for overclaiming "done" when blocker existed (confidence scoring gap)
- **P2-C (2026-04-17):** Approved because roadmap honestly says "foundation complete, population pending"
- **Key difference:** P2-C uses "🟡 IN PROGRESS" with explicit remaining work list; P2-B marked items "[x] done" without flagging blockers in the section

**What This Proves:**
- Vector index infrastructure is ready for embedding population
- Search methods are tested and will work when embeddings exist
- AppHost correctly wires embedding config to Python services
- Team can proceed with embedding population pipeline in parallel with Phase 3 agent work

**What This Does NOT Prove:**
- That embeddings are actually populated in Neo4j (explicitly deferred)
- That vector search is integrated into retrievers (explicitly deferred)
- That vector retrieval returns better results than text search (Phase 4 evaluation work)

**Key Learning:**
- **Foundation-first pattern works well:** Implementing infrastructure (indexes, helpers, config) before population pipeline enables parallel work and validates contracts early
- **Honest "IN PROGRESS" status:** Roadmap correctly signals partial completion instead of false "done" — builds trust and prevents confusion
- **Test-driven infrastructure:** All vector index and search methods validated without requiring live embeddings — proves correctness of contracts before integration

**Related Files:**
- `src/AspireApp.AppHost/AppHost.cs` (lines 145-157: embedding config wiring)
- `src/AspireApp.PythonServices/app/services/neo4j_service.py` (lines 51-96: vector indexes; lines 418-505: vector search)
- `src/AspireApp.PythonServices/app/services/embedding_service.py` (new file: Ollama-first/local-fallback embedding service)
- `src/AspireApp.PythonServices/tests/test_vector_infrastructure.py` (new file: 11 tests)
- `roadmap/Tasks.md` (lines 170-178: P2-C status update)

### 2026-04-22 — Ingestion & Upload Regression Test Plan for Multi-Format & URL Ingestion

**Task:** Analyze current test coverage and create a plan for adding regression tests for txt/md/docx/json uploads and URL ingestion (plain web pages, YouTube transcripts, YouTube channel expansion).

**Key findings:**
- **Current coverage is PDF-focused:** `AuthenticatedUploadUxTests.cs` (Playwright E2E), `FileUploadControllerTests.cs` (controller unit tests), `OperationalUploadStoreTests.cs` (Postgres persistence). No tests for plaintext formats or URL ingestion.
- **Fallback processors exist but are untested:** `docling_service_fallback.py` has `_extract_pages_text()`, `_extract_pages_docx()`, `_extract_pages_pdf()` but only PDF processing has regression coverage.
- **URL ingestion has no tests:** `FileUploadController.UploadUrl` exists (line 194) with duplicate detection via hash, but no regression tests prove URL metadata persistence, duplicate rejection, or malformed URL handling.
- **External dependency risk not isolated:** No mocking layer for YouTube API calls (`yt-dlp`) or web scraping (`httpx`). Live integration tests would be flaky.

**Recommended Test Execution Order:**
1. **Phase 1 (Cheapest):** Python unit tests for txt/md/docx/json processing with mocked dependencies (2 days)
2. **Phase 2 (Medium):** C# controller tests for new MIME types and URL validation (3 days)
3. **Phase 3 (Integration):** Contract round-trip tests for txt/md/docx content (C# → Python → Postgres) (4 days)
4. **Phase 4 (External):** URL ingestion integration tests with mocked `httpx`/`yt-dlp` (6 days)
5. **Phase 5 (E2E - Optional):** Playwright upload flow for new file types (8 days, defer unless critical)

**Cheapest Trustworthy Validation Layers:**
- **Plain text/markdown:** Python unit test (`_extract_pages_text()` fallback processor) — deterministic, no Aspire required
- **DOCX:** Python unit test with mocked `python-docx` library — avoids real file parsing edge cases
- **Web pages:** Python unit test with mocked `httpx.AsyncClient.get()` — isolates external HTTP dependency
- **YouTube transcripts:** Python unit test with mocked `yt-dlp.YoutubeDL.extract_info()` — avoids YouTube API quota/rate limits
- **YouTube channel expansion:** Python unit test with mocked `yt-dlp` returning 3-video channel (not 100+) — prevents quota exhaustion

**Flaky/External Dependency Risks:**
- **YouTube API:** Videos deleted, transcripts disabled, API quota exhausted, rate limiting → **Mitigation:** Mock all `yt-dlp` calls in default test suite; add opt-in integration tests (`pytest -m integration`) for real YouTube
- **Web scraping:** Sites change HTML, add rate limiting, block scrapers → **Mitigation:** Mock `httpx` responses; add integration test against one stable URL (e.g., `httpbin.org/html`)
- **DOCX parsing:** `python-docx` fails on corrupted files, complex formatting → **Mitigation:** Mock library in unit tests; add error handling for extraction failures
- **JSON semantic ambiguity:** Unclear if JSON should be plaintext or parsed semantically → **Mitigation:** Block implementation until product owner clarifies requirements

**Key file paths:**
- `src/AspireApp.PythonServices/app/services/docling_service_fallback.py` (lines 337-350: `_extract_pages_text()`, line 305: `_extract_pages_docx()`)
- `src/AspireApp.Web/Controllers/FileUploadController.cs` (line 194: `UploadUrl`, line 230: `CalculateUrlHash`, line 238: duplicate detection)
- `src/AspireApp.WebTest/Tests/FileUploadControllerTests.cs` (line 70: uses `notes.txt` mock but no processing validation)
- `src/AspireApp.WebTest/DataExample/dotnet-readme.md` (unused test fixture, good candidate for markdown ingestion test)

**Decisions to Document:**
- Ingestion test strategy: Phase 1-4 execution order, Python mocking patterns, YouTube/web scraping isolation
- Recommend deferring E2E Playwright tests until product owner identifies critical UX gaps (high maintenance cost vs. contract round-trip tests)

**Deliverable:** Created `.squad/agents/buster/ingestion-test-plan.md` — 26KB detailed plan with test templates, mock patterns, error scenarios, time estimates (15 days without E2E, 23 days with E2E)

### 2026-04-14 — P2-B Live Proof: Confidence Validation via Aspire Integration Test

**Task:** Create live validation proof for the next Knowledge Layer slice (P2-B confidence-enrichment).

**What I Did:**
- Added `BasicAspireAppHostTests.BrainQueryReturnsConfidenceEnrichedResults` — a Priority(2) live proof that uploads a document, waits for processing + LightRAG ingestion, then queries `/brain/query` and asserts results for the uploaded document do NOT carry DEFAULT_CONFIDENCE (0.5).
- Updated `roadmap/Tasks.md` to reflect that live proof now exists but implementation remains incomplete.
- Test currently scaffolds the expected behavior; will FAIL until Jarvis implements confidence-enrichment in `LightRagRetriever`.

**Why This Matters:**
- **Honest proof before implementation:** Test defines what "done" means for P2-B without pretending the feature is already complete.
- **Minimal addition to existing test suite:** Leverages the same upload → process → query workflow as `LiveLightRagNeo4jQueryRoundTrip`, adding only the confidence assertion.
- **Clear blocker signal:** Test will fail until confidence-enrichment logic is added; failure message will pinpoint exactly which results have placeholder scores.

**What This Test Proves:**
- `/brain/query` results for uploaded documents have real confidence values (not 0.5 default).

**What This Test Does NOT Prove:**
- That confidence values are *accurate* or *calibrated* (that's Phase 4 evaluation work).
- That all confidence-scoring paths work (only validates uploaded-document retrieval path).
- That semantic fallback confidence works (already covered by `test_knowledge_retriever.py`).

**Coordination Notes:**
- Test uses existing `WaitForKnowledgeQueryResultAsync` helper, filters results to uploaded document via source_refs, and asserts Confidence != 0.5.
- No new test infrastructure required; integrates cleanly with Priority ordering.
- Marked with `[Trait("Category", "P2-B")]` for easy filtering during focused P2-B work.

### 2026-11-02 — REJECT: Tasks.md Overstates Completion & Masks P2-B Blocker

**Task:** Review Tasks.md against actual code/tests. Verdict: **REJECT**—roadmap marks items done that are incomplete or are misleading about completion state.

**Specific Findings:**

1. **Line 172 is MISLEADING**
   - Claim: "Wire Gateway `POST /brain/query` to the current Python knowledge seam ... confirmed via `BrainGatewayPhase2Tests.QueryKnowledgeAsync_MapsContractShapedKnowledgeResult_FromPythonQueryRoute`"
   - Reality: Test **exists and passes**, BUT endpoint maps to `/rag/query` (Python), NOT a finalized Brain gateway layer. Test verifies HTTP contract only, not production readiness.
   - Impact: Suggests Phase 2 gateway wiring is done; in fact, gateway is only a stub that delegates to Python. No Brain-layer orchestration yet.

2. **Line 176 + 182 are INCOMPLETE**
   - Claims semantic fallback "must supply real confidence values" but code still has `DEFAULT_CONFIDENCE = 0.5` (line 14 of retrievers.py)
   - Test `test_lightrag_retriever_falls_back_to_response_text()` confirms this: expects 0.5 when no score provided
   - Tasks.md correctly marks P2-B as **Blocked** (line 294), but the "Knowledge Layer" section (line 169–174) marks semantic retriever as `[x] done` without flagging the blocker.
   - **This is a documentation mismatch:** Marked done but blocked.

3. **Line 169 is INCOMPLETE**
   - Claim: "Implement `BrainKnowledgeRetriever` ... delivers ... full graph traversal, vector scoring, and confidence extraction"
   - Reality: `BrainKnowledgeRetriever` **only does LightRAG-first + Neo4j fallback**, NOT graph traversal, vector scoring, or confidence extraction. Those are deferred to Validation Layer.
   - Lines 169–170 are confusing: mark as done but then line 169 parenthetically admits core features are deferred.

4. **Line 167–168 (Neo4j Schema) is INCOMPLETE**
   - Tasks.md marks as `[ ]` (not started), correctly. No Claim/Evidence/Concept/Entity nodes in schema.
   - However, this directly blocks P2-B and P2-C (lines 295–296), which are also marked Blocked.
   - **Good:** Schema tasks are correctly marked incomplete. **Bad:** Blocked status for P2-B/P2-C not in milestone table as "why blocked."

5. **Line 242 (P1 Carry-Forward) is OVERSTATED**
   - Claim: "Covered by `BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip`"
   - Reality: No test by that name exists in provided artifacts. Likely renamed or removed. Tasks.md references a non-existent test as proof of completion.
   - **Action needed:** Verify if this test exists; if not, remove claim.

6. **Missing in Phase 2 Section: Validation Layer NOT Started**
   - Lines 178–184 list "Validation Layer (Basic)" as phase 2, but all items are `[ ]` (not started).
   - Yet line 294 (P2-B gate) says "Blocked — Requires Validation Layer." This is correct but **Tasks.md does not clearly state that Validation Layer work is a Phase 2 blocker, not a later phase task.**
   - Reader looking at Phase 2 might assume Validation is Phase 3+; in fact, it's Phase 2 and blocks P2-B.

**Summary of What's Actually Done (Per Code):**
- ✅ Contracts defined (Phase 1)
- ✅ Python retrievers implemented (LightRAG + semantic + Brain orchestration wrapper)
- ✅ Gateway routes mapped (stub endpoints)
- ✅ Integration tests verify HTTP contract (BrainGatewayPhase2Tests)
- ✅ Default confidence fallback coded (but explicitly NOT production-ready)
- ❌ Validation Layer (claim extraction, contradiction detection, confidence scoring) NOT started
- ❌ Semantic fallback does not emit real confidence scores (still 0.5 default)
- ❌ Neo4j schema not extended with Claim/Evidence/Concept/Entity nodes
- ❌ Vector indexes not created
- ❌ No graph traversal or confidence extraction implemented

**The Blocker is Real:**
- P2-B gate correctly identifies that semantic fallback must emit real confidence.
- This REQUIRES Validation Layer (Claim extraction + evidence scoring).
- Validation Layer is listed in Phase 2 but marked as not started.
- **Tasks.md should flag this more explicitly: Phase 2 cannot close until Validation Layer begins and supplies confidence-aware storage.**

**Verdict:**
✅ **REJECT** — Tasks.md contains:
1. Misleading claims about gateway wiring (overstates what tests prove).
2. Confusing mixed signals on semantic retriever (marked done, but blocker not highlighted in section).
3. Non-existent test reference (BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip).
4. Missing clarification that Validation Layer is a Phase 2 blocker, not a later phase task.

**Action:** Correct roadmap before next commit. Clarify:
- Line 172: "HTTP contract verified; production gateway orchestration pending Phase 2–3 checkpoint."
- Line 169–174: Separate "retriever interface done" from "confidence scoring & graph traversal deferred to Validation Layer."
- Line 178–184: Explicitly flag "Validation Layer blocks P2-B gate; must start Phase 2 before P2-B closure."
- Verify/remove non-existent test reference (line 242).

---

### 2026-04-15 — Phase 1 Validation: Contract Parity ≠ Pipeline Completion

**Clarified:**

- Phase 1 gates are **contract readiness** only: definitions and serialization parity across C# ↔ Python.
- Phase 1 is NOT blocked by pipeline incompleteness (ingestion, query, storage, retrieval).
- Tests will fail when exercising Phase 2+ paths; these are expected failures, not Phase 1 gate failures.

**Key distinction:**
- **Phase 1-A gate:** Contract models exist, compile, have all required fields, types match (e.g., `datetime` ↔ `DateTime`)
- **Phase 1-B gate:** Serialization round-trip test passes (Python → JSON → C# → JSON → Python)
- **NOT P1 failures:** Integration test fails because ingestion service incomplete (Phase 2), query returns empty (Phase 2+), Neo4j empty (Phase 2+)

**Implementation for Buster:**
- Write contract definition unit tests (model instantiation, field presence, type correctness)
- Write serialization unit tests (round-trip fidelity, JSON schema validation, DateTime formatting)
- Skip integration tests that depend on Phase 2+ pipeline work
- Use failure triage matrix: contract issues = P1 failure, pipeline issues = expected, skip for now

**Test failure categories for P1:**
| Symptom | P1 Action |
|---------|-----------|
| "Model not found" or "import fails" | FAIL (fix contract definition) |
| "JSON key mismatch" or "type conversion fails" | FAIL (fix serialization) |
| "Document not stored" or "query returns empty" | SKIP (expected until Phase 2+) |

**Related decision:** `.squad/decisions/inbox/buster-phase1-contract-gate-clarification.md` (recorded for Scribe merge)

**Key insight:** Contracts are the proof-of-concept for service integration design. Validate contract parity without waiting for full pipeline. Once contracts are locked, the pipeline work (Phase 2+) can proceed in parallel with confidence that the wire format is stable.

---

### 2026-04-11 — Chat Privacy Browser Tests Must Decouple Persistence From Assistant Latency

**Completed:**
- Reproduced `ChatConversationPersistenceTests.ConversationsRemainPrivateEvenWithinSharedTenantMembership` failing after the owner prompt was already persisted; the UI stayed disabled because `Chat.razor.cs` intentionally keeps send/rename/delete controls locked while `IsAIResponsing` is true.
- Confirmed this was not shared browser or storage state: `TestFixture` still gives each page its own browser context and isolated test-run data/database roots, and the failure happened on the owner page before the second user page was created.
- Updated the privacy Playwright test to stop the in-flight AI response after the owner prompt becomes visible, capture the rendered saved-conversation title, and continue the shared-tenant privacy assertions without waiting on full model completion.
- Re-ran the smallest focused validation Eric asked for: `ChatConversationServiceTests` plus the targeted privacy UI test both pass on the current tree.

**Key pattern:**
- For chat persistence/privacy browser tests, treat the visible owner prompt as the persistence gate and separate that from assistant latency. The owner-only contract is already in play once the conversation is saved and listed.
- When the scenario is about ownership/isolation rather than answer quality, use `data-testid="chat-stop-button"` to cancel the live response once the prompt is visible, then continue assertions after controls re-enable.
- Keep privacy enforcement double-covered: service tests (`ListAndGetConversationAsync_ReturnOnlyOwnerRecords`, `AddMessageAsync_DoesNotAllowAnotherUserToContinueConversation`) provide the cheap authorization contract, while Playwright proves the shared-tenant UI still hides the conversation end to end.
- User preference: for slow browser suites in this repo, run only the smallest targeted validation needed instead of broad reruns.

**Key file paths:**
- `src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.WebTest\Fixtures\TestFixture.cs`

---

### 2026-04-22 — REJECT: Critique-Mode Tests Fail to Compile — RemoveAll Pattern Not Supported by Bunit

**Task:** Validate the final state of Jeff's Critique-mode UI implementation.

**Verdict:** ❌ **REJECT** — Tests do not compile. Product layer implementation appears complete, but test infrastructure has a critical error.

**Issue Found:**
- `ChatCritiqueModeTests.cs` line 377 uses `testContext.Services.RemoveAll(typeof(IChatConversationService))` which does not exist in Bunit's `BunitServiceProvider`.
- Bunit does not support replacing services after test context creation.
- The test `ExistingConversation_LoadsWithStoredChatMode()` attempts to replace the service registered by `CreateTestContext()` but cannot.

**Build Error:**
```
error CS1061: 'BunitServiceProvider' does not contain a definition for 'RemoveAll' and no accessible extension method 'RemoveAll' accepting a first argument of type 'BunitServiceProvider' could be found
```

**What Was Working:**
- ✅ Chat.razor has critique toggle at line 935-943 (enabled, not disabled)
- ✅ Reasoning steps render with proper test IDs (`chat-reasoning-panel`, `chat-reasoning-step`)
- ✅ Mode selection wired to `SelectedChatMode` property
- ✅ Other critique tests structurally valid (except line 377)

**Root Cause:**
- Test pattern mismatch: Other tests register all services **before** rendering via `CreateTestContext()`.
- `ExistingConversation_LoadsWithStoredChatMode()` tries to replace a service **after** `CreateTestContext()` returns, which requires `RemoveAll()` — not available in Bunit.

**Required Fix:**
1. Option A: Refactor `CreateTestContext()` to accept optional service overrides (preferred pattern).
2. Option B: Create separate test context factory for tests needing custom conversation services.
3. Option C: Remove the failing test if not critical path.

**File Requiring Revision:**
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs` line 377

**Reviewer Decision:**
This is a **compilation blocker**. Cannot validate critique-mode UI behavior when test suite won't build. Jeff's product code appears correct from inspection, but tests must compile before validating runtime behavior.

**Next Step:**
Jeff or another agent must fix the test infrastructure pattern before this work can be approved.

---

### 2026-04-11 — Fixture-Backed Web Tests Can Die Before Assertions When Shared Container State Is Dirty

**Completed:**
- Reproduced `AuthUxFoundationTests` and `AuthenticatedUploadUxTests` as class-level hangs/crashes under filtered `dotnet test` runs; neither class reached an individual test result before the WebTest child process was dumped by the hang detector.
- Traced the shared failure out of the test bodies and into the Aspire stack started by `TestFixture`: PostgreSQL exited with `invalid checkpoint record` / `could not locate a valid checkpoint record`, and Neo4j also failed on later reruns with `/data/databases/store_lock` because the repo bind mount was already in use.
- Confirmed the service/component auth seam still gives trustworthy signal once stale runner state is cleared: `CompositeAuthServiceTests`, `SignInPanelTests`, and `MockAuthServiceTests` all pass on the current tree after aligning the MockAuthService test constructor call with the live three-argument surface.

**Key pattern:**
- When a fixture-backed `AspireApp.WebTest` class aborts with `Xunit.Sdk.TestPipelineException` before reporting individual tests, inspect Docker container state and logs before blaming the assertions.
- `TestFixture` isolates shared data and SQLite paths, but PostgreSQL and Neo4j still come from AppHost bind mounts under `database\`, so dirty repo storage can sink every browser/integration class during fixture startup.
- Keep auth triage layered: use service/component tests (`CompositeAuthServiceTests`, `SignInPanelTests`) to judge auth logic while full Aspire fixture classes are blocked on orchestration state.

**Key file paths:**
- `src\AspireApp.WebTest\Fixtures\TestFixture.cs`
- `src\AspireApp.AppHost\AppHost.cs`
- `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`
- `src\AspireApp.WebTest\Tests\AuthenticatedUploadUxTests.cs`
- `src\AspireApp.WebTest\Tests\CompositeAuthServiceTests.cs`
- `src\AspireApp.WebTest\Tests\MockAuthServiceTests.cs`

---

### 2026-04-10 — Chat Rename Focus Regression Locked Down

**Completed:**
- Added `ChatFocusTests.cs` in `src\AspireApp.WebTest\Tests` to cover the rename-title focus regression at the Blazor component seam.
- Validated the current chat rename implementation in `Chat.razor` / `Chat.razor.cs`, where the title input now has its own `ElementReference`, a `ShouldFocusConversationTitleInput` flag, and an `OnAfterRenderAsync` guard that skips refocusing the main chat input while rename mode is active.
- Re-ran the focused chat validation: `ChatFocusTests` plus the existing `ChatConversationServiceTests` both pass locally.

**Key pattern:**
- For Blazor input-focus regressions caused by `OnAfterRenderAsync`, the smallest reliable QA seam is a focused WebTest component test that counts `focusElement` JS invocations rather than a full Aspire/Playwright browser run.
- Seed only the chat dependencies needed for render (`AuthenticationContext`, `TenantContextService`, `IChatConversationService`, `SpeechService`, `AiInfoStateService`) and drive the UX through the actual rendered controls so the regression stays tied to the user-visible seam.
- Keep `ChatConversationServiceTests` as the persistence/ownership contract layer; use the new component test for UI focus behavior that service tests cannot see.

**Key file paths:**
- `src\AspireApp.Web\Components\Pages\Chat.razor`
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.WebTest\Tests\ChatFocusTests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`

---

### 2026-04-06 — Self-Provisioning Test Coverage: Anticipatory QA for Unknown-User Creation

**Completed:**
- Created comprehensive test suite for self-provisioning local auth behavior: unknown credentials automatically create accounts
- 21 tests covering: new user creation, duplicate prevention, existing-user login, error paths, contract stability, end-to-end integration
- Tests document expected implementation requirements and critical edge cases before Warden implements the feature
- All tests compile and build successfully; will PASS once self-provisioning logic is added to `LocalAccountAuthenticator.AuthenticateAsync()`

**Key Pattern:**
- **Anticipatory testing:** Write tests that document expected behavior BEFORE implementation exists. Tests fail initially (or skip), pass when feature lands.
- For self-provisioning auth, the critical test boundary is: "Does unknown identifier + password return null (current) or create user + authenticate (new)?"
- Test categories mirror security concerns: duplicate prevention (race conditions, case insensitivity), error paths (empty/whitespace/disabled), contract stability (normalization, timestamps, tenant assignment)
- Integration tests verify the form → endpoint → service contract survives self-provisioning changes

**Key Edge Cases Covered:**
- **Duplicate race condition:** Two simultaneous requests with same unknown username (database constraints prevent, retry on failure)
- **Email vs username ambiguity:** Identifier parsing (`john.doe` vs `john@example.com`)
- **Normalization bypass attempts:** Case variations must resolve to same user (`JohnDoe` == `johndoe`)
- **Invalid tenant assignment:** Self-provisioned users must get valid default tenant per `TenantContextService`

**Implementation Requirements (for Warden):**
- Check existing user first (by normalized username OR email)
- If exists: validate password, return AuthenticatedUser or null
- If new: validate non-empty, derive email/username from identifier, hash password, assign default tenant, persist, return AuthenticatedUser
- Handle duplicate INSERT race condition: catch constraint violation, retry lookup + password validation

**Adjustments If Behavior Changes:**
- If self-provisioning deferred: mark tests with `[Fact(Skip = "...")]`, add negative tests (unknown creds return null)
- If additional validation required (email verification, password strength): add validation test section, update error path tests
- Form field name must stay `identifier` across SignInPanel → Program.cs → LocalAccountAuthenticator (contract protected by `LocalAuthEndpointContractTests`)

**Key File Paths:**
- `src\AspireApp.WebTest\Tests\LocalAccountSelfProvisioningTests.cs` (new: 21 tests, 6 categories)
- `src\AspireApp.Web\Services\LocalAccountAuthenticator.cs` (implementation target)
- `src\AspireApp.Web\Program.cs` (line 207: `[FromForm] string identifier` endpoint)
- `src\AspireApp.Web\Components\Shared\SignInPanel.razor` (line 128: `name="identifier"` form field)
- `.squad\decisions\inbox\buster-self-provision-test-coverage.md` (decision log entry)

### 2026-04-06 — Form-Endpoint Contract Regression Coverage Added — Session Logged

**Completed:**
- Background session coordinated by Eric: Jeff fixed endpoint contract, Buster added regression test suite
- Created `LocalAuthEndpointContractTests.cs` with regression coverage to prevent future mismatches between form field names and endpoint parameter names
- Added integration tests for the full local auth flow: username/email login, password validation, unknown user handling
- All focused local-auth Web tests passing on current tree

**Key pattern:**
- Form-endpoint contracts are fragile and easily broken during refactoring. Protect them with explicit contract tests that document the three-way agreement:
  1. Form field name (SignInPanel.razor: `name="identifier"`)
  2. Endpoint parameter name (Program.cs: `[FromForm] string identifier`)
  3. Service method parameter (LocalAccountAuthenticator.AuthenticateAsync: `string identifier`)
- When a form posts to a server-side endpoint, add a regression test that explicitly verifies the field name matches the endpoint expectation
- For auth endpoints that accept username OR email, test both paths to ensure normalization works correctly

**Quality gap identified:**
- No integration tests existed that would catch form-endpoint parameter mismatches before user testing
- Component tests (bUnit) verify form markup but don't validate POST contract
- Endpoint tests would need to simulate form submission to catch this

**Session Artifacts:**
- `.squad/orchestration-log/2026-04-06T16-01-59Z-buster.md` — QA regression suite summary
- `.squad/log/2026-04-06T16-01-59Z-local-login-bugfix.md` — session brief

**Key file paths:**
- `src\AspireApp.Web\Components\Shared\SignInPanel.razor` (line 128: name="identifier")
- `src\AspireApp.Web\Program.cs` (line 207: [FromForm] string identifier)
- `src\AspireApp.Web\Services\LocalAccountAuthenticator.cs` (line 21: string identifier parameter)
- `src\AspireApp.WebTest\Tests\LocalAuthEndpointContractTests.cs` (new regression coverage)

### 2026-04-06 — Local Managed Auth QA Coverage Landed

**Completed:**
- Aligned the auth regression suite with the live local-auth seam across `AuthenticationOptions`, `AuthServiceFactory`, `CompositeAuthService`, `LocalAuthService`, `SignInPanel`, `LocalAccountAuthenticator`, and `LocalAuthBootstrapper`.
- Added/updated tests for local auto-resolution, explicit local factory resolution, composite provider exposure/routing, managed-credential form rendering, and the generic invalid-credentials UI path.
- Enabled `Microsoft.EntityFrameworkCore.InMemory` in `AspireApp.WebTest` so the local account/auth bootstrapper tests can exercise `UploadDbContext` behavior without needing a live PostgreSQL container.
- Re-ran the focused auth suite and the full `dotnet test --no-restore` repository suite successfully (57/57 passing).

**Key pattern:**
- For this repo, local auth is testable in layers without a live external identity provider: factory/config resolution, provider metadata (`RequiresCredentials`, `SignInPath`), component rendering, then operational-store unit tests over `UploadDbContext`.
- When auth tests need EF-backed behavior in `AspireApp.WebTest`, the smallest acceptable harness is `Microsoft.EntityFrameworkCore.InMemory`; do not invent a fake `DbContext` surface.
- The generic invalid-credentials UX contract is now explicit in the component layer: `"We couldn't sign you in with those credentials."`

**Key file paths:**
- `src\AspireApp.Web\Services\AuthenticationOptions.cs`
- `src\AspireApp.Web\Services\AuthServiceFactory.cs`
- `src\AspireApp.Web\Services\CompositeAuthService.cs`
- `src\AspireApp.Web\Services\LocalAuthService.cs`
- `src\AspireApp.Web\Components\Shared\SignInPanel.razor`
- `src\AspireApp.WebTest\Tests\LocalAuthServiceTests.cs`
- `src\AspireApp.WebTest\Tests\LocalAccountAuthenticatorTests.cs`
- `src\AspireApp.WebTest\Tests\LocalAuthBootstrapperTests.cs`
- `src\AspireApp.WebTest\Tests\CompositeAuthServiceTests.cs`
- `src\AspireApp.WebTest\Tests\SignInPanelTests.cs`

### 2026-04-05 — Real Microsoft Auth Regression Gate: QA Coverage Expanded

**Completed:**
- Audited the current auth seam across `AuthenticationOptions`, `AuthServiceFactory`, `CompositeAuthService`, `MicrosoftEntraAuthService`, `SignInPanel`, `Program.cs`, and the existing WebTest auth suite.
- Confirmed the implementation is **not** mock-first when Microsoft is actually configured and intended: `AuthenticationOptions.ResolveEffectiveService()` resolves `auto` to `microsoft`, not to mock or combined.
- Added focused regression coverage for the config resolver and the composite auth seam so the UI path can be proven without breaking demo-provider coverage.
- Re-ran focused auth tests plus the full `dotnet test` suite successfully.

**Key pattern:**
- For this repo, proving "real auth is reachable" does **not** require a live tenant in automation. The durable QA gate is service-level: assert effective-mode resolution, assert the live Microsoft provider exposes `RequiresUserSelection = false`, assert `GetUsers("microsoft-entra")` is empty, and assert sign-in routes to `/auth/microsoft/signin` instead of `/auth/mock/signin`.
- Keep demo coverage in the same seam test pass. If a change preserves Microsoft routing but breaks demo sign-in, it is still a regression.

**Key finding:**
- The code currently prefers the live Microsoft provider when configured, but `README.md` still describes `auto` as resolving to `combined` or `mock`. QA should treat the code path as authoritative until docs are aligned.

**Key file paths:**
- `src\AspireApp.Web\Services\AuthenticationOptions.cs`
- `src\AspireApp.Web\Services\AuthServiceFactory.cs`
- `src\AspireApp.Web\Services\CompositeAuthService.cs`
- `src\AspireApp.Web\Components\Shared\SignInPanel.razor`
- `src\AspireApp.WebTest\Tests\AuthenticationOptionsTests.cs`
- `src\AspireApp.WebTest\Tests\CompositeAuthServiceTests.cs`
- `src\AspireApp.WebTest\Tests\AuthServiceFactoryTests.cs`

### 2026-04-06 — Microsoft Entra Integration: Regression Validation Complete — APPROVED

**Completed:**
- Ran full `dotnet test` suite: 23 tests pass, 0 failures, ~25 seconds
- Validated auth seam integrity: `AuthServiceFactory` correctly resolves configured providers
- Validated mock auth demo layer unchanged: all `MockAuthServiceTests` + `AuthUxFoundationTests` Playwright tests pass
- Validated Microsoft provider isolation: unit tests confirm it hides when unconfigured, registers OIDC only when configured
- Validated critical regression: `BasicAspireAppHostTests::FlowEndToEnd` and `DeleteUploadedTestFile` pass (upload pipeline intact)

**Key Pattern:**
- Microsoft Entra integration is **live and pluggable**. Real manual testing can proceed (Eric will test with actual credentials). The automated regression surface is 100% solid.
- Design follows the approved factory pattern: configuration-driven provider swapping without code recompile.
- No breaking changes detected. All existing tests pass as-is, confirming backward compatibility with mock auth demo flow.

**Key Finding:**
- `CompositeAuthService.SignOutAsync()` always delegates to Microsoft provider, but this is safe by design: all sign-out paths converge at `/auth/signout` which handles cleanup. Recommend adding a comment to document this intentional behavior.

**Decision Created:** `.squad/decisions/inbox/buster-microsoft-auth-regression-verdict.md`

**Key File Paths:**
- `src\AspireApp.Web\Services\AuthServiceFactory.cs` — Provider resolution works
- `src\AspireApp.Web\Services\MicrosoftEntraAuthService.cs` — Live OIDC integration
- `src\AspireApp.Web\Services\CompositeAuthService.cs` — Multi-provider routing
- `src\AspireApp.WebTest\Tests\MicrosoftEntraAuthServiceTests.cs` — Microsoft unit tests (4/4 pass)
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs` — Critical regression suite (all pass)

### 2026-04-05 — Warden Auth QA Verdict: Reject on Route Re-Protection + Flow Regression

**Completed:**
- Re-audited the landed auth UX slice across `Program.cs`, `Routes.razor`, `MainLayout.razor`, `NavMenu.razor`, `Home.razor`, `SignIn.razor`, `SignInPanel.razor`, `AuthenticationContext`, and `MockAuthService`.
- Re-ran the focused auth gate with `AuthServiceFactoryTests`, `MockAuthServiceTests`, and `AuthUxFoundationTests`.
- Re-ran the critical browser regression `BasicAspireAppHostTests.FlowEndToEnd`.

**Key pattern:**
- Do **not** approve the auth leg unless sign-out actually re-protects direct navigation to `[Authorize]` routes. A landing page plus working sign-in is not enough if `/chat`, `/upload`, or other protected routes remain reachable after logout.
- In this repo, auth approval also depends on the existing browser smoke staying honest: if `FlowEndToEnd` cannot observe the uploaded fixture in API-backed state, the auth slice is still leaving the app misleadingly green.

**Blockers observed:**
- `AuthUxFoundationTests.SignOutReturnsToLandingAndReprotectsAppAreas` failed because a signed-out browser could still stay on `/chat` instead of being redirected or shown the public landing/sign-in surface.
- `BasicAspireAppHostTests.FlowEndToEnd` failed because `processing-smoke.pdf` never appeared in `GET /api/FileUpload` after the UI upload attempt (`{"success":true,"files":[]}`), so the auth-enabled browser path still regresses the core upload flow.

**Key file paths:**
- `src\AspireApp.Web\Components\Routes.razor`
- `src\AspireApp.Web\Components\Pages\Chat.razor`
- `src\AspireApp.Web\Components\Shared\RedirectToSignIn.razor`
- `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`

### 2026-04-05 — Auth UX Revision Verdict: Shell Exists, Gate Set Still Incomplete

**Completed:**
- Audited Bob's independent auth UX revision across `Home.razor`, `MainLayout.razor`, `Routes.razor`, `ProtectedRoutePrompt.razor`, `SignInPanel.razor`, and the scoped auth services.
- Confirmed the mock auth shell now physically exists: unauthenticated landing, interactive sign-in panel, protected Blazor routes, top-bar identity surface, sign-out action, and tenant seeding from `AuthenticatedUser.DefaultTenantId`.
- Re-ran focused WebTest coverage and found the broader browser suite still regresses in `BasicAspireAppHostTests.FlowEndToEnd`, while the new auth acceptance path did not produce a clean completed run in automation during review.

**Key pattern:**
- Do **not** approve an auth slice just because the shell renders. Approval still requires the agreed gate set to be materially closed: provider pluggability must be configuration-driven, and the multi-layer QA story cannot stop at one Playwright class.
- For this repo, mock auth is coherent only when these files stay aligned: `Program.cs` DI registration, `AuthenticationContext`, `AppAuthenticationStateProvider`, `MockAuthService`, `Routes.razor`, `MainLayout.razor`, and the tenant selector surface.

**Key gaps observed:**
- `Program.cs` resolves only `MockAuthService` and throws for any non-mock mode; that is not the approved config-swappable provider seam.
- No `AuthProviderFactoryTests.cs` or equivalent service/contract tiers exist yet, so AUTH-F and AUTH-G remain open even though the UI shell landed.

**Key file paths:**
- `src\AspireApp.Web\Program.cs`
- `src\AspireApp.Web\Services\MockAuthService.cs`
- `src\AspireApp.Web\Services\AppAuthenticationStateProvider.cs`
- `src\AspireApp.Web\Components\Pages\Home.razor`
- `src\AspireApp.Web\Components\Shared\ProtectedRoutePrompt.razor`
- `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`

### 2026-04-05 — Auth UX QA Gate: Use Existing Aspire WebTest Fixture + Skip Until Shell Exists

**Completed:**
- Added `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs` to stage Playwright acceptance coverage for mock auth UX without inventing a second harness.
- Reused `TestFixture` browser/AppHost lifecycle rather than creating bespoke auth test infrastructure.
- Encoded the minimum UX gates: unauthenticated landing visibility, mock sign-in, protected-route access, sign-out protection, and tenant binding visibility.

**Key pattern:**
- For UI slices that are approved but not yet landed, QA can add real acceptance tests that **reuse the live Aspire fixture** and **dynamically skip** until Jeff's shell exists. That keeps the contract executable without breaking the current suite.
- Require stable auth hooks for resilient Playwright coverage: `data-testid='auth-sign-in-cta'`, provider hooks under `auth-provider-*`, `auth-sign-out`, `auth-user-display`, and either `auth-current-tenant` or `data-auth-tenant` on the identity surface.

**Key file paths:**
- `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`
- `src\AspireApp.WebTest\Fixtures\TestFixture.cs`
- `.squad\decisions\inbox\buster-auth-ui-test-hooks.md`

### 2026-04-05 — Mock Auth UX Test Strategy: UI/Integration/Contract Tiers Before Real Wiring

**Completed:**
- Audited current auth state: no authentication exists; tenant context provides data-layer isolation only

- Designed multi-layer test strategy (UI → Component → Integration → Service → Contract)
- Defined 6 acceptance gates blocking real Google/Microsoft auth wiring
- Identified mock auth contract shape and provider pluggability pattern
- Documented provable behaviors that must be stable before real provider implementation

**Key Pattern:**
- **Don't build real auth yet.** Stage mock auth gates in order: unauthenticated landing → mock login flow → cross-service contract → provider pluggability
- **Layer model:** UI (Playwright E2E) → Component (optional Bunit) → Integration (API contract) → Service (provider factory) → Contract (Python tenant audit)
- **Mock auth contract:** Email-based login, token response includes tenant ID, provider swappable via configuration
- **Tenant isolation proof:** Authenticated requests must preserve tenant ID through API headers into Python database queries

**Key Decisions (In Inbox):**
- `IAuthProvider` interface shape (pluggable backends)
- `AuthContextService` mirrors `TenantContextService` pattern (scoped, event-driven)
- Token stored in-memory (no localStorage); auto-injected via DelegatingHandler
- Provider switching configuration-only (no code recompile)

**Key File Paths:**
- `.squad/decisions/inbox/buster-auth-ux-test-strategy.md` — Full strategy doc (21 KB)
- `src/AspireApp.Web/Services/TenantContextService.cs` — Service pattern to mirror
- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` — Playwright baseline

**Test Artifacts to Create (Priority):**
1. `LandingPageTests.cs` — Unauthenticated access
2. `MockAuthEndpointTests.cs` — Login contract (POST /auth/login)
3. `test_p0_auth_contract_audit.py` — Tenant ID propagation
4. `AuthFlowE2ETests.cs` — Sign-in flow (Playwright)
5. `AuthProviderFactoryTests.cs` — Provider pluggability

**What's Deferred:**
- Real OAuth callback handling (Google/Microsoft)
- Encrypted token storage
- Session timeout / refresh token lifecycle
- MFA flow
- PKCE / SAML details

**Rationale:** Mock auth proves the *shape* and *contract*. Real provider specifics are implementation details; pluggable factory ensures no rearchitecting when wiring Google/Microsoft.

---

### 2026-04-10 — Chat Persistence QA Contract Staged

**Completed:**
- Added `ChatConversationServiceTests.cs` to cover save/load/rename/delete and owner-only access on Jeff's new `ChatConversationService`.
- Added `ChatConversationPersistenceTests.cs` with two focused Playwright/Aspire acceptance tests: one for save → auto-title → rename → resume → delete, and one for user-only isolation even when two users share the same tenant membership.
- Kept the tests on the existing `TestFixture` browser/AppHost harness instead of inventing a separate chat test host.
- Hardened `TestFixture.GetAppHostContentRoot()` so custom `BaseOutputPath` runs can still discover `src\AspireApp.AppHost`.

**Key pattern:**
- For this repo's chat history slice, start with service-layer ownership tests against `ChatConversationService`, then stage Playwright contracts for the saved-conversation UX that Jeff still needs to land.
- For Blazor chat persistence, stage executable UX contracts early and dynamically skip until the saved-conversation shell is actually present.
- Require stable `data-testid` hooks on the conversation-management seams (`chat-conversations-shell`, `chat-conversation-list`, `chat-new-conversation`, `chat-conversation-item`, `chat-current-conversation-title`, `chat-conversation-rename`, `chat-conversation-title-input`, `chat-conversation-delete`) and only use semantic fallbacks for obvious chat input/send controls.
- To prove privacy is user-scoped rather than tenant-scoped, seed a shared tenant membership in Postgres for two users and assert the second user cannot see the first user’s renamed conversation or transcript.

**Environment note:**
- Focused chat service unit tests and non-Docker auth unit tests passed after the changes.
- The Docker/Aspire browser slice is currently blocked in this environment by `Aspire.Hosting.DistributedApplicationException`: Docker is present but unhealthy, so the chat/browser fixture cannot start.

**Key file paths:**
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs`
- `src\AspireApp.WebTest\Fixtures\TestFixture.cs`
- `.squad\decisions\inbox\buster-chat-history-tests.md`
- `.squad\skills\chat-persistence-ux-contracts\SKILL.md`

---

### 2026-04-05 — Python Test Discovery Alignment: pyproj + Dependency Bootstrap

**Completed:**
- Audited Visual Studio Python test discovery behavior and identified that `AspireApp.PythonServices.pyproj` must explicitly list regression test files in `<Compile Include>` for them to appear in Test Explorer.
- Identified that utility scripts (e.g., `test_build_config.py`) expose `test_*` functions that trigger Docker builds during discovery; renamed to remove false automation hooks.
- Validated bootstrap dependency completeness: `.venv` must install psycopg[binary], psycopg-pool, and pytest for smoke gate to run without errors.
- Confirmed 14 regression + contract audit tests collect and pass under VS Python environment after fixes.

**Key pattern:**
- `AspireApp.PythonServices.pyproj` is the test-discovery gate for VS workflow; regression tests must be explicitly included.
- Utility scripts must not expose `test_*` function names unless they're intended to execute during automated discovery.
- Local `.venv` bootstrap must include all dependencies required by the smoke gate, not just the runtime requirements.

**Key file paths:**
- `src/AspireApp.PythonServices/AspireApp.PythonServices.pyproj` (test file includes)
- `test_build_config.py` (utility function renamed)
- Smoke gate dependency list: `psycopg[binary]`, `psycopg-pool`, `pytest`

- **Visual Studio Python discovery contract (2026-04-05):**`src\AspireApp.PythonServices\AspireApp.PythonServices.pyproj` is the test-discovery gate for the VS workflow. Pytest files under `src\AspireApp.PythonServices\tests\` are not reliably visible to Test Explorer unless they are explicitly listed in `<Compile Include=...>`, and utility scripts must not expose `test_*` functions unless they are meant to execute during automated runs.
- **Test scaffolding for unimplemented features (2026-07-26):** When a feature doesn't exist yet (tenant context UI), stage commented test templates showing expected test coverage rather than inventing the contract yourself. The implementation team (Jeff/Bob) owns the contract design; QA owns the test shape once the contract exists. Blocked requests should document why blocking is correct and what unblocks progress.
- **Browser smoke fixture rule (2026-04-05):** `BasicAspireAppHostTests.FlowEndToEnd` needs a *small, processable PDF* fixture, not a plain-text stand-in and not a large real-world document. Swapping to unsupported `.txt` input produced false UI-level confidence while the Python pipeline stayed stuck in `processing`; the stable regression path is a tiny PDF like `src\AspireApp.WebTest\DataExample\processing-smoke.pdf`.
- **Browser regression verdict (2026-04-05):** Jeff's `.txt` smoke-fixture change was not acceptable QA state because it made the browser suite validate an upload type the processing pipeline does not complete. The corrected, verifiable gate is: Python contract audit passes, `OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres` passes, and the full `BasicAspireAppHostTests` suite passes after clearing stale `AspireApp.WebTest.exe` runners.

## Core Context

**Key QA learnings from active development (Feb-Apr 2026):**

- **Contract test pattern:** Don't hardcode infrastructure names (DB names, connection strings). Derive them from AppHost config and assert all three surfaces (AppHost, Web, Python) use the same value. Prevents false test failures on legitimate infrastructure changes.
- **Regression diagnosis methodology:** When tests fail after infrastructure changes, audit the actual runtime (build + run, check config resolution). Distinguish test/harness regressions from product regressions. May require clearing stale process locks (`AspireApp.WebTest.exe`) before re-running.
- **Smoke test contract:** Test against the abstraction, not implementation details. E.g., `service_factory` selected implementation (Docling full vs. fallback) rather than direct package import. Allows multiple supported environments (with/without optional packages).
- **End-to-end test architecture:** Upload flows through UI (Blazor Server), resolved via API state, then python processing triggered directly. Playwright can't intercept browser network, so resolve document ID from Web API instead of waiting on browser response.
- **Quality gate layers:** Unit (logic), Integration (API + DB), E2E (UI workflow). Each layer catches different failure classes. AppHost health checks provide fast orchestration proof.

**Current state (as of 2026-04-05):**
- Contract audit passes: AppHost `appdb` → Python `POSTGRES_DATABASE=appdb` → Web `GetConnectionString("appdb")` all aligned
- Smoke test passes: 30 Python tests (contract audit, startup path, processing, docling factory)
- WebTest regression resolved: Fixture now reads `appdb` from Aspire injection instead of hardcoded `DefaultConnection`
- Regression pattern established: Derive, don't hardcode. Assert alignment, not literal values.

**Next phase (BRAIN pivot):**
- Quality gates for evidence-backed agentic system: evidence attribution, confidence transparency, insufficient-evidence handling
- Evaluation framework: Assess BRAIN quality metrics (not just "works")
- End-to-end proof automation: Full pipeline from ingest → retrieve → reason → response

---

### 2026-04-05 — Postgres Cutover Regression Verdict

- **QA verdict:** the immediate failures were stale test expectations, not a fresh application regression. `src/AspireApp.AppHost/AppHost.cs`, `src/AspireApp.Web/Program.cs`, and Python runtime all align on the live Postgres upload store name `appdb`.
- **Do not hardcode legacy connection names in regression tests.** `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` now derives the database name from `postgres.AddDatabase("...")` and asserts Web/Python consume that same name instead of assuming `DefaultConnection`.
- **WebTest fixture contract:** `src/AspireApp.WebTest/Fixtures/TestFixture.cs` must validate `ConnectionStrings__appdb` and `POSTGRES_DATABASE=appdb`; otherwise the Aspire E2E harness rejects a correct runtime.
- **Operational validation path:** fast QA proof for this area is `python -m pytest tests\test_p0_contract_audit.py -q`; heavier `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --disable-build-servers` also needs stale `AspireApp.WebTest.exe` processes cleared if a prior run left the binary locked.

### 2026-04-05 — Final Tenant Context Verdict — Data Layer APPROVED

**Scope:** Comprehensive validation of tenant-context data layer and API contract after multi-revision cycle.

**QA Path:**
1. Jeff's initial implementation → Buster rejected (API coherence gap)
2. Bob revised (FileStorageService alignment) → Buster rejected (schema not persisted in Python)
3. Jarvis fixed Python schema → Buster rejected (contract audit gap—column existed but not persisted/read)
4. Kujan closed audit gap (explicit round-trip assertion) → Buster approved ✅

**Validation Results:**

| Component | Tests | Result |
|-----------|-------|--------|
| Python contract audit | 8/8 | ✅ PASS |
| C# operational test | 1/1 | ✅ PASS |
| API contract review | — | ✅ Coherent |
| Service layer alignment | — | ✅ Complete |

**Key validations:**
- `test_database_service_initializes_canonical_schema_and_indexes` — tenant_id column and indexes exist
- `test_web_file_metadata_columns_match_python_projection` — tenant_id alignment across boundary
- `create_file_record(tenant_id="test-tenant")` → `get_file_by_id()` → assertion on tenant_id value (explicit round-trip)
- `OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres` — SELECT includes tenant_id, default value persists

**What's Ready:**
- ✅ Schema: tenant_id with NOT NULL + DEFAULT 'default'
- ✅ Indexes: idx_files_tenant, idx_files_tenant_status
- ✅ API: GetTenantId() extraction, X-Tenant-Id header propagation
- ✅ Service: Both C# and Python accept/persist tenant_id
- ✅ Query filtering: GetAllFilesAsync(tenantId) scopes results

**What's Deferred to UI Phase (with test scaffolding provided):**
- Tenant selector UI (NavMenu component)
- Session state management
- Frontend header attachment
- Multi-tenant duplicate detection
- Tenant-aware delete operations

**Pattern:** Tenant_id is a schema concern. Multi-tenancy is implemented via column, indexes, and optional query filtering. Authentication (which tenants you're allowed to see) is Phase 6. This slice closes the infrastructure gap for data isolation.

**Verdict:** ✅ **APPROVED** — Data layer is coherent, protected by validation, ready for UI implementation.

---

## Core Context

> This section summarizes key learnings from Phase 0 & early Phase 1 (before 2026-04-01).
> Full details are below; these notes capture the essential patterns and decisions.

### Phase 0 & Early Phase 1 Summary (Entries before 2026-04-01)

- **Document ingestion pipeline verified:** Docling extraction → Neo4j storage → Chat retrieval works end-to-end
- **Aspire orchestration smoke-tested:** All services start, health-check passing, logs accessible via dashboard
- **Test infrastructure scaffolded:** Upload controller, file storage, coordinator patterns established; async dispatch model in place
- **Multi-tenancy foundation:** Tenant isolation via columns + indexes; auth layer deferred to Phase 6
- **Local auth slice approved:** Mock provider pattern with pluggable factory; tests verify sign-in/sign-out flows
- **Vector infrastructure foundation (P2-C):** Neo4j indexes created, embedding service pattern established; population pipeline deferred
- **Python processing patterns:** Docling, Neo4j, embedding calls; event loop management critical to responsiveness
- **Test QA discipline:** Chat persistence requires owner message + visibility seam; confidence enrichment requires fail-closed behavior; live tests deferred

### Key Decision Patterns

1. **Fire-and-forget async patterns:** Upload queues processing but returns immediately with `status="uploaded"`; tests must poll dispatch instead of asserting sync completion
2. **Test scaffolding alignment:** Test assumptions (esp. timeouts, retries) must match implementation semantics; transient failures during cold startup are retryable
3. **Event loop discipline:** FastAPI background tasks must keep event loop responsive; sync-heavy work moves to thread pool
4. **Honest roadmaps:** Mark items "foundation-only" when infrastructure is ready but population/integration pipelines remain
5. **Fail-closed confidence:** Return None rather than synthetic defaults; downstream consumers handle missing data

### Outstanding Items (Tracked in Phase 1+)

- **BRAIN pivot recording:** Complete; decision-recording gate closed 2026-04-14
- **P2-B confidence gate:** Closed 2026-04-17 (fail-closed behavior verified)
- **P2-C embedding population:** Blocked on time; infrastructure foundation ready for Phase 2+
- **Phase 3 agent selection:** Decision deadline 2026-04-24 (BLOCKING gate for Phase 2→3 transition)
- **Docker validation:** Caveat noted as outstanding quality gate (not blocking Phase 1 parallel work)

---

### 2026-03-28 — Docling Smoke Gate Alignment (QA Audit Complete)

**Audit Focus:** Validate that `app.services.service_factory` is the correct smoke-test contract for document processing initialization.

**Findings:**
- **Default local environment is fallback-first:** `.venv` from `setup_dev_env.py` installs only `requirements.txt`, which includes `docling-core` but not full `docling`, so common dev setup lacks full processor.
- **Supported contract is dual-mode:** Full Docling when installed, fallback processor otherwise. Service factory enforces this selection.
- **Smoke test should mirror this contract:** Validate factory-selected implementation can initialize, not require optional package.

**Verification Results:**
- ✅ `python src\AspireApp.PythonServices\test_services.py -v` passed
- ✅ `python -m unittest discover -s tests -p test_p0_contract_audit.py -v` passed (10 tests)
- ✅ `python -m pytest tests test_services.py -q` passed (32 passed, 1 skipped)
- ✅ Full test suite: 30 passing tests

**Test Contract:** `test_services.py` smoke gate is `app.services.service_factory` initialization. Passes when current environment can initialize either full Docling or fallback. Direct import of `docling_service` is no longer required.

### 2025-02-22 — Initial Quality Audit
- **Zero automated tests exist.** All 6 "test" files are manual diagnostic/benchmark scripts with no assertions and no pytest/xUnit integration.
- **CI is non-functional.** `squad-ci.yml` echoes a placeholder string — no build, no test execution.
- **.NET build succeeds** (when Aspire is not running). SDK is .NET 10.0 Preview. 5 projects: AppHost, ApiService, Web, ServiceDefaults, Neo4JService.
- **Python has no test infrastructure.** No `pytest` in requirements.txt, no conftest.py, no pytest.ini.
- **Python dependencies are unpinned** — no version numbers in requirements.txt.
- **C# logging inconsistency.** Chat.razor.cs uses 35+ `Console.WriteLine` calls instead of `ILogger`. Multiple other files do the same.
- **Broad exception catching** everywhere — 9+ `catch(Exception)` in C#, 18+ `except Exception` in Python. Most return generic error messages.
- **Legacy schema debt.** `DocumentEntities.cs` contains deprecated `Document` and `ProcessedDocument` classes with a TODO to remove.
- **Cross-service contract risk.** C# uses `FileMetadata` with EF Core column mappings; Python uses `Document` Pydantic model with different field names. No contract tests verify alignment.
- **OllamaWarmupService** creates `new HttpClient()` directly instead of using `IHttpClientFactory`.
- **Global FastAPI exception handler** returns raw exception strings — potential information leak.
- **Python DatabaseService** is re-instantiated per request via `get_database_service()` dependency, re-running schema checks each time.
- **Highest-risk untested areas:** FileUploadController (validation, security), FileStorageService (data integrity), Python DatabaseService (all CRUD), FastAPI routes (all 3 routers).

### 2026-02-21 — Cross-Agent Findings

**From Bob:**
- Processing pipeline blocked by ~10 missing DatabaseService methods in Python
- Status casing bug ("Uploaded" vs "uploaded") prevents file discovery
- ApiService vestigial, should be removed

**From Jeff:**
- LightRAG and Ollama have no health checks, causing webfrontend to block indefinitely
- Config key mismatch: AI-Chat-Model (AppHost) vs AI-Model (Web services)
- SemanticKernel version skew (1.71.0 vs 1.68.0-alpha connector)

**From Jarvis:**
- Save_document_page() signature mismatch will crash during processing
- FK column name conflict creates data integrity risk
- Requirements.txt unpinned — reproducibility issue

### 2026-02-22 — Squad Orchestration Complete

**Status:** All four agents completed independent reviews; findings merged into shared decisions.md.

**Buster's Test Roadmap (Phase 1 Gates P0/P1 Fixes):**

**Phase 1 (Week 1): Foundation — **CRITICAL BLOCKER** for merges**
- Create `AspireApp.UnitTests.csproj` (xUnit)
- Add `pytest` + `conftest.py` to Python services
- Update CI (`squad-ci.yml`): run `dotnet build`, `dotnet test`, `pytest`
- **Effort:** 4 hours
- **Blocks:** No PR merges without CI passing

**Phase 2 (Week 2): High-Risk Paths**
- Contract tests: C# ↔ Python JSON serialization validation
- FileUploadController validation tests
- Python router unit tests (mocked DatabaseService)
- Status casing verification ("uploaded" lowercase)
- **Effort:** 22 hours
- **Blocks:** No model refactoring without contract tests

**Phase 3 (Week 3): Integration Suite**
- End-to-end: file upload → processing → retrieval
- Python DatabaseService integration (real SQLite)
- Cross-service E2E (real Neo4j)
- **Effort:** 12 hours

**Phase 4 (Week 4+): Edge Cases & Stress**
- Concurrent uploads, large files, timeouts, cleanup

**Dependency:** P0 code fixes (Jeff + Jarvis) must land and pass manual validation before Phase 2 starts.

### 2026-02-22 — Test Posture Review & Plan

**Key Findings:**
- **Zero automated tests.** No xUnit projects, no pytest integration. 6 files named `test_*.py` are diagnostic scripts with no assertions.
- **CI is broken.** `squad-ci.yml` echoes placeholder; no build verification, no test runs, no PR gating.
- **Contract misalignment risk is CRITICAL.** C# (`FileMetadata`) ↔ Python (`Document`) have no JSON serialization tests. Field renames or type changes will crash Python at runtime silently.
- **Cross-service testing completely absent.** 0 tests verify C# JSON serialization matches Python Pydantic deserialization.
- **High-risk paths untested:** File upload validation, processing pipeline, error handling, concurrent access.
- **Python dependencies unpinned.** Reproducible builds impossible; docling updates could break silently.

**Test Gap Priorities:**
1. Phase 1 (Week 1): Test infrastructure + CI pipeline (xUnit project, pytest, conftest, CI workflow) — **4h effort**
2. Phase 2 (Week 2): Contract tests + controller tests + router unit tests — **22h effort**  
3. Phase 3 (Week 3): Integration suite (end-to-end file upload → processing → retrieval) — **12h effort**
4. Phase 4 (Week 4+): Stress/edge case tests (concurrent, large files, timeouts, cleanup)

**File Paths (Key Components):**
- C# projects: `src/AspireApp.Web`, `src/AspireApp.ApiService`, `src/AspireApp.ServiceDefaults`
- Python services: `src/AspireApp.PythonServices/app/` (routers, services)
- SQLite: `database/data-resources.db` (shared via bind mount)
- CI: `.github/workflows/squad-ci.yml` (currently placeholder)

**Deliverable:** `plan.md` created with phase-based roadmap, quality gap matrix, and test organization.

**Recommendation to Team:** Start Phase 1 immediately. Cannot merge code safely without CI. Contract tests must precede any refactoring of C# models or Python routes.

**Skill Learned: Cross-Service Contract Testing Pattern**
- C# models require `JsonPropertyName` attributes to match Python field names
- Python Pydantic models must have snake_case fields matching JSON
- Contract tests must verify round-trip serialization (C# → JSON → Python deserialize)
- DateTime must use ISO 8601 format on both sides
- Status/enum casing must be tested explicitly (e.g., "uploaded" lowercase vs "Uploaded")
- Missing field names in JSON should fail test (regression detection)
- Documented in `.squad/skills/` for future contract creation in AspireAI

### 2026-03-20 — P0 QA Audit: Upload Paths + Python Footprint
- **QA verdict: reject current P0 state.** Added focused dependency-free unittest coverage in `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`; 2 schema/contract checks pass and 2 expected-failure tests capture the still-broken upload path normalization rules.
- **Cross-service upload contract is explicit now:** C# writes `files.file_path` as the physical directory and `files.file_name` as the timestamped stored filename (`src/AspireApp.Web/Shared/FileStorageService.cs`, `src/AspireApp.Web/Data/DocumentEntities.cs`). Python `DoclingService` still ignores `document.filename` and prepends `self.uploads_path` to `document.file_path`, so it cannot safely resolve either container-relative or Windows host paths.
- **Python footprint is only partially minimized.** The unified SQLite schema is consistent (`files` + `document_pages` only), but the FastAPI surface is still bloated at 21 routes across `documents.py`, `processing.py`, and `rag.py`, including health/admin/stats endpoints and legacy compatibility paths Bob already marked for removal.
- **Key QA paths:** `src/AspireApp.PythonServices/app/services/database_service.py`, `src/AspireApp.PythonServices/app/services/docling_service.py`, `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py`, and `roadmap/Tasks.md`.

### 2026-03-20 — Final P0 QA Re-Review
- **Runtime path normalization now lives in `DatabaseService.resolve_upload_path()`.** `processing.py` resolves the physical file before calling either `docling_service.py` or `docling_service_fallback.py`, and manual smoke validation resolved both container-style directories and Windows-style `...\data\uploads` values to the same mounted file.
- **The Python footprint is materially smaller.** The live schema remains `files` + `document_pages`, and the current FastAPI surface is 17 total endpoints (15 routed lifecycle endpoints plus `/` and `/health`), which matches `docs/DATABASE_MANAGEMENT.md`.
- **The contract documentation is not yet coherent.** `docs/CROSS_SERVICE_CONTRACT.md` still claims Python ignores `file_path` and still documents `/documents/status/{status}`, while the code and `docs/DATABASE_MANAGEMENT.md` use `file_path` + `file_name` resolution and no longer expose that route.
- **QA validation command for this area:** `python -m unittest discover -s src\AspireApp.PythonServices\tests -p test_p0_contract_audit.py -v`. Current result is 2 passing schema checks plus 2 `expectedFailure` upload-path tests, so the fix is present but the regression coverage is not yet upgraded to a passing gate.

### 2026-03-20 — Post-Cleanup P0 QA Gate
- **Upload Path Normalization is now test-green.** `python -m unittest discover -s src\AspireApp.PythonServices\tests -p test_p0_contract_audit.py -v` passed 4/4 tests, including both path-resolution cases that cover container-style directories and Windows host-path remapping.
- **Processing now consumes the resolved physical path.** `processing.py` calls `DatabaseService.resolve_upload_path()` before handing work to both `docling_service.py` and `docling_service_fallback.py`, so the runtime contract is explicit and exercised.
- **Python Footprint Minimization is still not cleanly closed.** The live route surface is down to 17 endpoints and `docs/DATABASE_MANAGEMENT.md` matches that retained subset, but `DatabaseService` still carries multiple `Legacy compatibility` methods plus `get_file_document_sync_status()` / `force_sync_files_and_documents()`, and support artifacts like `README.md`, `fix_database.py`, `diagnose_database.py`, and `scripts/fix_schema.py` still reference `documents` / `processed_documents`.
- **Cross-service docs are closer but not exact.** `docs/CROSS_SERVICE_CONTRACT.md` is functionally aligned, yet it still uses placeholder/query forms (`{id}`, `{page}`, `/rag/search-documents?query=&limit=`) that do not match the literal live route signatures extracted from FastAPI code.

### 2026-03-20 — P0 Python Footprint Approval
- **QA verdict: approve Python Footprint Minimization.** The live runtime surface in `src/AspireApp.PythonServices/app/services/database_service.py`, `src/AspireApp.PythonServices/app/routers/documents.py`, and `src/AspireApp.PythonServices/app/routers/processing.py` now reads and writes the canonical `files` / `document_pages` contract directly.
- **Legacy contract shims are no longer the active surface.** `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` asserts deprecated sync helpers are absent, and the current maintainer references (`src/AspireApp.PythonServices/README.md`, `docs/DATABASE_MANAGEMENT.md`, `docs/CROSS_SERVICE_CONTRACT.md`) describe `documents` / `processed_documents` as retired historical tables only.
- **Current validation gate for this area:** `test_p0_contract_audit.py` and `test_database_schema.py` both pass against a temp canonical database; support helpers (`migrate_database.py`, `diagnose_database.py`, `fix_database.py`, `scripts/fix_schema.py`, `scripts/test_concurrent_access.py`) all target the same footprint.
- **Reusable QA pattern:** when helper scripts only fail on `pydantic.BaseModel` imports, a temporary `PYTHONPATH` stub is enough to validate them without changing the workstation Python install.

### 2026-03-20 — P0 Decision Merge Complete

**Status:** All P0 work merged into shared decisions.md and approved by squad.

**Work Summary Across Squad:**
- **Buster (this agent):** QA gates (3 phases). Initial rejection for incomplete test gate. Post-Bob revision approval for path normalization. Post-Jeff approval for footprint minimization.
- **Jarvis:** Implemented upload path fix + endpoint/method pruning.
- **Bob:** Post-QA revision work. Converted audit tests from `expectedFailure` to live regression. Aligned CROSS_SERVICE_CONTRACT.md.
- **Jeff:** Finished Python footprint cleanup by removing sync shims and updating canonical contract methods.

**Inbox → Decisions.md:** 6 files merged. Buster's three QA gate decisions now part of permanent squad record.

**Orchestration Log Created:** Scribe created one per agent documenting spawn phases and context for successors.

**Session Log Created:** Scribe created brief summary of P0 completion status.

**Next Phase:** P1 items ready. Validation gates remain live as regression coverage. Buster to maintain gate discipline going forward.

### 2025-02-22 — Aspire Dashboard Test Audit (P1 Work)

**Status:** AUDIT COMPLETE — Test cannot pass without production changes.

**Key Findings:**
- **TestFixture never populates `AspireDashboardUri`** — property declared but marked with TODO, test navigates to null/empty string.
- **Dashboard is not a registered Aspire resource** — it's launched as side effect of `DisableDashboard = false`, not accessible via `_app.GetEndpoint()`.
- **No public API to retrieve dashboard URL + token** — Aspire generates secure token at startup and prints to console only. No `DistributedApplication` method exposes it.
- **DashboardEnabledAspireAppHostFactory exists but incomplete** — sets `applicationOptions.DisableDashboard = false` but doesn't capture/store dashboard URL for tests.
- **Test lacks critical assertions** — no `WaitForLoadStateAsync()`, no auth redirect check, no health check before navigation.

**5 Critical Test Gaps Identified:**
1. Dashboard endpoint contract missing from AppHostMappingModel
2. Token retrieval mechanism not implemented
3. No dashboard health check before test
4. No authentication success validation (redirect check)
5. No page load state wait before title assertion

**Guidance Provided for Jeff:**
- Research Aspire.Hosting API for dashboard credential retrieval
- Extend DashboardEnabledAspireAppHostFactory or modify AppHost.cs to surface dashboard URL
- Update TestFixture.InitializeAsync() to populate AspireDashboardUri
- Add health check + WaitForLoadStateAsync() to test

**Decision File:** `.squad/decisions/inbox/buster-aspire-dashboard-test-audit.md` — documents full audit, edge cases, regression tests, and correct implementation flow.

**Recommendation:** Jeff owns implementation research (Aspire API discovery); Buster validates test passes before merge. Do not merge test or fixture changes without dashboard URL extraction working.

### 2026-03-21 — Aspire Dashboard Auth Re-Review

- **Dashboard credentials are now surfaced in test state.** `src/AspireApp.WebTest/Fixtures/TestFixture.cs` waits for the `aspire-dashboard` resource, reads `DASHBOARD__FRONTEND__PUBLICURL` and `DASHBOARD__FRONTEND__BROWSERTOKEN`, and stores them through `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`.
- **The smoke test is still red.** `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj` fails only `BasicAspireAppHostTests.AspireDashboardLoads` because the browser reaches a page with an empty title after `GotoAsync(_data.AspireDashboardLoginUri, ...)`; the other two WebTest smoke checks pass.
- **Reusable QA pattern:** for Aspire dashboard browser auth, capturing the token is not enough. The Playwright test must wait for the page to settle and assert post-login state (final URL, expected heading/content, or redirect away from `/login`) instead of checking the title immediately.
- **Recommended owner when Jeff is conflicted out:** Bob should revise this area next because the remaining gap is in Aspire dashboard orchestration/auth flow, not basic test plumbing.

### 2026-03-21 — Aspire Dashboard Test Redirect Wait Approval

**Status:** Complete (Bob revised after Buster rejection; Buster approved)

**Context:** Jeff's infrastructure was sound (resource snapshot capture, token extraction, login URI computation), but the test assertion strategy was race-prone. After Buster's re-review rejection, Bob revised the test method with proper redirect/settle gates.

**Root Cause:** Title was being polled immediately after `GotoAsync`, catching the moment between login page and Blazor hydration where `document.title` is empty.

**Fix Applied by Bob:**
1. `WaitForURLAsync(url => !url.Contains("/login"), 120s timeout)` — gates all assertions on redirect completion
2. Explicit `PageWaitForFunctionOptions { Timeout = 60_000 }` on title poll — 60s buffer for Blazor cold-start SignalR circuit
3. Flexible title assertion: `Contains("resources")` instead of exact match

**QA Validation:**
- `dotnet build AspireApp.sln` ✅ — 0 warnings, 0 errors
- `dotnet test BasicAspireAppHostTests.AspireDashboardLoads` ✅ — 1 passed (~40s)
- `dotnet test` (full suite) ✅ — all WebTest tests passing

### 2025-11-02 — FastAPI Endpoint Proof Requirements (FlowEndToEnd Expansion)

**Context:** User asks: "Can we add the FastAPI processing calls to FlowEndToEnd to prove they work? I am not convinced the FastAPI endpoints are working as expected." Buster audits proof surface.

**QA Audit — FastAPI Endpoints Present & Contract-Sound:**

**Endpoints Found & Working:**
- `POST /processing/process-document/{document_id}` — Accepts work, triggers background processing (FastAPI `BackgroundTasks`), returns 202-like response `{"message": "Processing started for document {id}"}`
- `GET /processing/status/{document_id}` — Returns `ProcessingStatus` model (document_id, status, total_pages, processed_pages, error_message, started_at, completed_at)
- `GET /documents/{document_id}` — Returns full `Document` (id, filename, original_filename, file_path, file_size, mime_type, upload_date, processed, processing_status)

**Endpoint Quality:**
- ✅ Error handling: HTTPException for 404 (not found), 409 (already processing), 400 (already processed), 500 (service error)
- ✅ Dependency injection: DatabaseService + Neo4jService injected, proper error propagation
- ✅ Logging: All endpoints log entry/exit; background task logs progress
- ✅ Background task isolation: Long-running work (Docling extraction, Neo4j graph build) runs async, doesn't block HTTP response

**Contract Validation:**
- ✅ Pydantic models well-defined (ProcessingStatus, Document, ProcessedDocument)
- ✅ Optional fields properly nullable (error_message, processed_pages, completed_at)
- ✅ No type mismatches between C# and Python contracts

**Proof Gap — Endpoints Not Called in Tests:**
- `BasicAspireAppHostTests.PythonServiceOpenAPILoads` validates Swagger UI exists ✅ but **never invokes actual endpoints**
- `FlowEndToEnd` uploads document then stops — **never calls `POST /processing/process-document` to trigger work**
- No test polls `GET /processing/status/{id}` to verify background task state transition

**Minimum Assertions Required for FlowEndToEnd to Prove Processing Works:**

1. **Endpoint Reachability Beyond Swagger UI:**
   - `Assert.Equal(200, (await pythonClient.GetAsync("/processing/service-info")).StatusCode);` — Health check response
   - **Fails if:** Python service is offline, endpoint not registered, or broken route

2. **POST Trigger Endpoint Accepts Real Work:**
   - After uploading file (current FlowEndToEnd already does this), capture `document_id` from response
   - `var triggerResponse = await pythonClient.PostAsync($"/processing/process-document/{document_id}", null);`
   - `Assert.Equal(200, triggerResponse.StatusCode);` — 200 or 202 (depending on framework choice) signals accepted
   - Parse response JSON: `var responseBody = await triggerResponse.Content.ReadAsStringAsync();`
   - `Assert.Contains("Processing started", responseBody);` — Message indicates queued work
   - **Fails if:** Invalid document_id (404), already processing (409), service error (500)

3. **GET Status Endpoint Reflects Real Processing Progress:**
   - Immediately post-trigger: `var statusResponse1 = await pythonClient.GetAsync($"/processing/status/{document_id}");`
   - `Assert.Equal(200, statusResponse1.StatusCode);`
   - Parse status JSON into `ProcessingStatus` object
   - `Assert.Equal("processing", status.Status, ignoreCase: true);` — Status should shift from "pending"/"uploaded" to "processing"
   - Poll in loop (up to 10 iterations, 1s delay, 10s total timeout):
     ```
     for (int i = 0; i < 10; i++) {
       await Task.Delay(1000);
       var polledStatus = await GetProcessingStatus(document_id);
       if (polledStatus.Status == "processed" || polledStatus.Status == "error") break;
     }
     ```
   - Final assertion: `Assert.True(status.Status == "processed" || status.Status == "error", $"Processing did not complete; final status: {status.Status}, error: {status.ErrorMessage}");`
   - If processed: `Assert.True(status.TotalPages > 0, "Processed document should have pages");`
   - If error: `Assert.NotNull(status.ErrorMessage, "Error status should include error_message");`
   - **Fails if:** Endpoint returns 404, status stuck in "processing", or database query crashes

4. **Test Fails Loudly and Readably When Contracts or Background Work Break:**
   - **Contract break example:** If Python changes `ProcessingStatus.status` field name to `processing_status`, deserialization fails with `JsonSerializationException` — test stops with readable error
   - **Endpoint break example:** If POST route is removed, HTTP 404 response, assertion on status code catches it
   - **Background work break example:** If Docling call inside background task throws, Python catches exception, calls `db.update_file_status(id, "error", str(e))`, next status poll returns `status="error"` with populated `error_message` field
   - **Database query break example:** If `get_processing_status()` crashes, endpoint returns 500, test fails on status code assertion

**Expected Response Shapes (for assertion clarity):**

```
POST /processing/process-document/{id} (200 or 202)
{
  "message": "Processing started for document 1"
}

GET /processing/status/{id} (200)
{
  "document_id": 1,
  "status": "processing",
  "total_pages": null,
  "processed_pages": null,
  "error_message": null,
  "started_at": "2025-11-02T14:30:00Z",
  "completed_at": null
}

GET /processing/status/{id} (200) [after completion]
{
  "document_id": 1,
  "status": "processed",
  "total_pages": 12,
  "processed_pages": 12,
  "error_message": null,
  "started_at": "2025-11-02T14:30:00Z",
  "completed_at": "2025-11-02T14:30:45Z"
}
```

**Test Implementation Strategy (for Jeff or Buster to code):**

1. Reuse `TestFixture.AppHostMapping.PythonServiceUri` (already available)
2. Create `HttpClient` with base address = `PythonServiceUri`
3. After file upload in FlowEndToEnd, extract `document_id` from file table or from upload response
4. Call `POST /processing/process-document/{document_id}`, assert 200
5. Poll `GET /processing/status/{document_id}` 10 times with 1s delay, break on terminal state
6. Assert final status is "processed" with `total_pages > 0`, or handle "error" case explicitly
7. If error, output `error_message` and fail test with context

**QA Verdict:** Endpoints are well-designed and testable. The missing artifact is **test code calling them**, not broken backend. Adding 30–50 lines of test assertions will move proof from "Swagger UI loads" to "Processing runs end-to-end."

**Recommendation:** Integrate this into `FlowEndToEnd` as P1 item. Current test passes but proves nothing about pipeline. With these assertions, test becomes a **regression detector** for the entire document processing flow.

**Approved:** Buster accepts revised artifact. Dashboard test harness now complete.

**Pattern for Future:** When testing Blazor Server UI redirects via Playwright:
1. Use `WaitForURLAsync` to gate on redirect completion (check URL no longer contains `/login`)
2. Use 60s+ timeouts for title polls (cold-start lag)
3. Prefer post-login state assertions (URL/content/title contains key token) over exact title strings

### 2026-03-26 — Windows SQLite Startup Path QA

- **Root cause reproduced:** local `DatabaseService()` startup was selecting `C:\app\database\data-resources.db` when `ASPIRE_DB_PATH` was unset, then failing on legacy schema/index mismatch (`no such column: file_hash`) even though the repo database at `database\data-resources.db` was canonical.
- **Exception posture:** the startup exception was not swallowed, but the old surface was misleading because it pointed at a missing column instead of making the wrong-file / legacy-schema diagnosis explicit. The revised startup path and diagnostic text now surface the failing path, SQLite error type, and schema mismatch context.
- **Reusable QA gate:** for environment-driven fallback bugs, do **not** patch the candidate list you want to test. Patch only the environment detectors (`_get_repository_root`, `_is_running_in_container`, `Path.cwd`) so the real ordering code runs, and add a real startup-failure assertion against a temp legacy SQLite file to verify the emitted diagnostics.
- **Smoke harness status:** `src/AspireApp.PythonServices\test_services.py` is now a real unittest smoke harness that uses the current `DatabaseService.list_documents()` API and skips optional Docling dependencies cleanly instead of dying at import time.
3. Assert on flexible conditions (substring, not exact match)

**Decision Logged:** "Aspire Dashboard Playwright Tests Must Wait for Auth Redirect" in `.squad/decisions.md` for team adoption.

### 2026-03-25 — P1 Processing Pipeline Regression Gate

- **Focused regression coverage exists now.** Added `src/AspireApp.PythonServices/tests/test_processing_pipeline_regression.py` with dependency-free `unittest` coverage for the processing lifecycle plus direct `process_document_task()` orchestration.
- **Retry behavior is part of the QA contract.** Failed `files` rows must remain eligible for `list_unprocessed_documents()`, and a transition back to `processing` must clear stale completion/error fields before a rerun can be treated as clean.
- **Validation gate for this area:** `python -m unittest discover -s src\AspireApp.PythonServices\tests -p "test_*.py" -v` plus `python test_database_schema.py` both pass on the current working tree.
- **Reusable test pattern:** when workstation Python lacks FastAPI/Pydantic, stub those modules via `sys.modules`, purge cached `app.*` modules between imports, and keep SQLite scratch data under a repo-local test folder instead of OS temp paths.


### 2026-03-25 — LightRAG P1 Proof Gate Prep
- **Accepted state remains partial.** Markdown export, staged LightRAG handoff, and explicit AppHost HTTP/Neo4j wiring are present, but no live ingest → query proof artifact exists yet.
- **Validated commands:** `python -m unittest discover -s src\AspireApp.PythonServices\tests -p "test_*.py" -v` ✅, `python test_database_schema.py` ✅, `dotnet build AspireApp.sln` ✅.
- **Current evidence is still non-live.** `test_processing_pipeline_regression.py` proves handoff with a fake collaborator and a local HTTP test server; `LightRagAppHostContractTests` only inspect `AppHost.cs` source text.
- **Remaining open item is precise now:** `src\AspireApp.PythonServices\app\routers\rag.py` still queries `Neo4jService` directly and never calls LightRAG, so “keep orchestration through Python retrieval APIs” cannot be honestly closed until a Python API-backed round-trip is demonstrated.
- **QA closure criteria recorded:** `.squad\decisions\inbox\buster-lightrag-proof-gate.md`.

### 2026-03-25 — BasicAspireAppHostTests.FlowEndToEnd E2E Ingestion Audit (READ-ONLY)

**Context:** User asked Buster to audit the FlowEndToEnd test and explain the ingestion process gap. Test uploads a file and verifies the row appears in the table, but Eric doesn't see what triggers Docling or LightRAG.

**QA Verdict (Read-Only):** Test is a regression risk masquerading as an end-to-end proof.

**What the test proves:**
- ✅ Upload payload accepted, C# FileUploadController.UploadFile() works, database row created, file on disk, row visible in UI table
- ❌ Everything after upload: processing trigger, Docling invocation, page persistence, markdown export, LightRAG handoff, Neo4j ingestion

**The gap:** C# upload controller does NOT call Python processing. Test never calls /process-all or /process-document/{id}. Without explicit trigger, Python processing never starts, and test passes happily with zero ingestion proof.

**Why it's invisible:**
- No test code invokes Python processing
- No test polls processing status or waits for completion
- No test queries document_pages table (would prove Docling extracted pages)
- No test checks filesystem for markdown staging
- No test verifies Neo4j node creation
- No error detection if processing fails silently

**Exact checkpoints needed to prove full pipeline:**
1. Call POST /processing/process-all to trigger Python processing
2. Poll GET /processing/status/{file_id} until status != "processing" (timeout after 30s)
3. Query SQLite: assert document_pages row count > 0 (proves Docling ran)
4. Check filesystem: assert markdown file exists at {data}/inputs/{file_id}*.md
5. Query Neo4j: assert MATCH (d:Document {id: }) RETURN count(d) == 1
6. Assert final status == "processed" or "error", with error message visible if failed

**Observability gaps (blocking test expansion):**
- No async wait mechanism for background processing
- No Python endpoint to query consolidated ingestion status (need GET /processing/status/{id} returning pages_count, neo4j_node, error)
- No filesystem introspection in test (need to read shared mount directly or add endpoint)
- No Neo4j query capability in test (need driver or endpoint)

**Plan implications:**
- Current test must be rewritten (P1): add processing trigger, polling, database queries, status assertions
- Consider adding observability endpoints (P1): GET /processing/status/{id}, GET /files/{id}/pages, GET /health/ingestion
- Add edge-case tests (P2): error path (processing fails), timeout (processing hangs), cleanup (file deleted)
- Add Neo4j verification (P2): query node creation, verify relationships

**Recommendation:** Bring this test to P1 scope alongside processing pipeline stabilization. Current state is passing but untested—a regression vector.

### 2025-11-02 — Legacy Schema Test Exception Chain Update

**Test:** DatabaseStartupPathAuditTests.test_legacy_schema_startup_failure_reports_path_and_cause

**Status:** UPDATED (test still valid, assertion fixed)

**What the test verifies:** 
When DatabaseService encounters a legacy schema (missing required columns like ile_hash), it should:
1. Detect the schema incompatibility when trying to create indexes
2. Provide detailed diagnostics (database path, missing columns, existing schema)
3. Raise RuntimeError with sqlite3.OperationalError in the exception chain

**Issue Found:** 
After the multi-candidate database initialization refactor (introduced in the path resolution updates), the exception chaining changed:
- **Before:** RuntimeError -> sqlite3.OperationalError
- **After:** RuntimeError (_initialize_database) -> RuntimeError (_ensure_database_schema) -> sqlite3.OperationalError

The test was checking context.exception.__cause__ directly for OperationalError, but now needs to walk the chain.

**Fix Applied:**
Updated the test to traverse the exception chain until it finds the root sqlite3.OperationalError:
`python
root_cause = context.exception.__cause__
while root_cause and not isinstance(root_cause, sqlite3.OperationalError):
    root_cause = root_cause.__cause__
self.assertIsInstance(root_cause, sqlite3.OperationalError, ...)
`

**Current Startup Behavior (as of 2025-11-02):**
1. DatabaseService tries candidates in order: explicit path, ASPIRE_DB_PATH env var, then default candidates
2. For each candidate, attempts: ensure directory → create pool → ensure schema → build data roots
3. If schema creation fails (e.g., missing columns prevent index creation), captures error and tries next candidate
4. If all candidates fail, raises RuntimeError with last error message and chained exception
5. Error message includes: database path, SQLite error, existing schema diagnostics (tables, columns, missing columns)

**Why Test Remains Valid:**
The behavior being tested (legacy schema detection with detailed diagnostics) still exists and works correctly. Only the exception chaining depth changed due to the retry-across-candidates pattern. Manual testing confirms the service starts correctly with proper schemas and fails with clear diagnostics on legacy schemas.

**Test Results:** ✅ All 10 tests in test_p0_contract_audit.py pass


---

### 2026-04-05 — Postgres Regression Verdict & BRAIN Pivot Context

**Status:** Regression verdict issued (test/harness, not product). Joined BRAIN pivot decision consolidation session.

**What Happened:**
1. **Regression Diagnosis (Postgres Upload Store Cutover):**
   - AppHost/Web/Python changes caused WebTest harness failures
   - Investigation: AppHost → Web → Python all using canonical ppdb Postgres database correctly
   - Verdict: **Test/harness regression**, not product rollback
   - Root cause: WebTest fixture hardcoded stale database name DefaultConnection instead of deriving it from AppHost
   
2. **Test Infrastructure Pattern Established:**
   - Contract tests must derive shared configuration from AppHost source (single source of truth)
   - Pattern: Don't hardcode database names, connection strings, or other infrastructure literals
   - Update 	est_p0_contract_audit.py to read active database name from AppHost and validate alignment
   - WebTest fixture corrected to read ConnectionStrings__appdb instead of hardcoded DefaultConnection
   - Secondary finding: Stale AspireApp.WebTest.exe processes can block re-runs (environment cleanup needed)

3. **BRAIN Pivot Context:**
   - Kujan review: Current architecture has zero implementation for Validation, Reasoning, Application layers
   - QA implications: Three new services needed before BRAIN MVP is credible
   - Evaluation challenge: No framework exists to assess BRAIN quality (confidence scoring, evidence attribution, insufficient-evidence handling)
   - Test strategy shifts: From UI-only proof to full pipeline proof (Buster established pattern with FlowEndToEnd)
   - Verbal strategy: MVP should prove one evidence-backed agentic loop; defer multi-tenancy until product thesis validated

**Key Decisions for Test Work Going Forward:**
- Contract tests are now regression gates (e.g., AppHost Postgres name change must fail tests immediately)
- Test fixture pattern: Derive infrastructure config from AppHost, not hardcoded literals
- Test harness must be resilient to intentional infrastructure changes (e.g., renaming stores) as long as all three surfaces stay aligned
- BRAIN MVP requires quality gates for evidence attribution, confidence transparency, and insufficient-evidence handling

**Regression Coverage Pattern:**
- Upload API test → verify iles row in Postgres ✅
- Python contract audit → verify AppHost DB name matches Python env var ✅
- Web contract audit → verify AppHost DB name matches Web connection string ✅
- Pattern prevents: Silent contract drift, hardcoded assumptions about infrastructure naming

**Related Agent Work:**
- **Jeff:** Web Postgres cutover; AppHost wiring is contract source of truth
- **Jarvis:** Python Postgres cutover; updated contract audit to use AppHost-derived names
- **Kujan:** Architecture review identifies need for Quality gates and evaluation framework for BRAIN
- **Verbal:** Strategy review emphasizes honest insufficient-evidence behavior as MVP requirement

**Orchestration Log:** Created for session context at 20260405T143735Z-buster.md

---


### 2026-04-05 — Auth UX Test Gates Defined (Cross-Agent Consensus)

**Agent Assessment:** Buster defined multi-layer acceptance gates for mock auth phase.  
**Cross-Agent Inputs:** Bob (provider abstraction seams), Jeff (Blazor component/service contracts).  

**Test Strategy: 5-Layer Model**
1. **UI (Playwright E2E)** — Mock login flow visual/navigation
2. **Component (Bunit, lightweight)** — Auth state component rendering  
3. **Integration (API contract)** — POST /auth/login shape (email/provider → accessToken/user)
4. **Service (xUnit)** — Provider factory isolation, state transitions
5. **Contract (Audit)** — Tenant ID flows through API headers into Python DB queries

**5 Acceptance Gates (Must All Pass):**
1. **AUTH-A:** Unauthenticated landing page (HTTP 200, sign-in buttons visible)
2. **AUTH-B:** Provider sign-in flow (click → sign in → redirect to dashboard)
3. **AUTH-C:** User identity displayed (name/avatar in top bar)
4. **AUTH-D:** Tenant auto-selection on login
5. **AUTH-E:** Sign-out flow (returns to landing, nav inaccessible)
6. **AUTH-F:** Provider pluggability (config-only swap, no code recompile)
7. **AUTH-G:** All 7-layer tests pass + cross-service tenant propagation verified

**Test Artifacts:**
- Tier 1: LandingPageTests.cs, MockAuthEndpointTests.cs, 	est_p0_auth_contract_audit.py
- Tier 2: AuthFlowE2ETests.cs (Playwright), AuthProviderFactoryTests.cs

**Pass/Fail Checklist:** All 5 gates must pass + no console errors + Playwright stable + code review approved

**Cross-Service Impact:** Tenant selector regression test required (Buster + Jeff coordination)

**Next:** Implementation by Jeff; validation by Buster per gate pass/fail criteria

### 2026-04-09 — Tenant Slice Session: Test Audit & Multi-Gate Review

**Role:** QA / Tester (Tenant Coverage & Authorization)

**Outcome:** Produced comprehensive test strategy; 3 review cycles; final approval after edge-case fixes. 28/28 tests passing.

**What Buster Did:**
1. Conducted audit of tenant provisioning, protection, and isolation requirements
2. Produced 42-50 test case strategy across auth, security, UI, and regression categories
3. First review: Rejected — missing direct recovery tests and add-member edge cases
4. Second review: Rejected — missing UploadUrl tenant-isolation regression test
5. Escalated UploadUrl test to specialist agent (focused fix)
6. Final review: Approved — all gates met; 28 targeted tests passing

**Key Learnings:**
- Test-first validation catches implementation gaps early
- Multi-gate QA (security + coverage) ensures distinct risk classes don't slip
- Specialist escalation works well for targeted edge-case tests

**Key Decisions Contributed:**
- Tenant Management Test Coverage Audit — 42-50 test cases + prerequisites + risk summary

**Status:** Final approval; tenant slice ready for merge; 28/28 tests green

### 2026-04-09 — Upload Authentication Regression: Test Gap Fixed

**Context:**
- After tenant hardening landed, manual testing found signed-in users hit authentication errors when uploading documents
- `AuthenticatedUploadUxTests` existed but didn't catch the regression because it only verified UI state (row appeared in table)
- The test didn't verify backend persistence via authenticated API call, so it couldn't detect auth failures in the controller path

**Root Cause Analysis:**
- Blazor Server upload flow has TWO distinct paths:
  1. **UI path (current):** UploadData.razor.cs → FileStorageService directly (bypasses controller, uses scoped auth from circuit)
  2. **API path (legacy/external):** JavaScript/HTTP → FileUploadController → FileStorageService (requires explicit auth headers/cookies)
- Post tenant-hardening, `FileUploadController.ResolveTenantContextAsync()` enforces auth (line 345-355: returns 401 if user null)
- `AuthenticatedUploadUxTests` simulated browser interaction which triggers the Blazor Server path (doesn't hit controller), so it never saw auth failures
- `BasicAspireAppHostTests.FlowEndToEnd` DOES verify via authenticated HttpClient after UI upload, following the smoke-test pattern

**The Fix:**
- Updated `AuthenticatedUploadUxTests.SignedInTenantScopedUserCanUploadDocumentWithoutAuthenticationError` to:
  - Create authenticated HttpClient (same pattern as `BasicAspireAppHostTests` and `OperationalUploadStoreTests`)
  - Call mock sign-in endpoint to establish session cookies
  - Query Postgres for user's default tenant ID
  - Add `X-Tenant-Id` header to all API requests
  - **CRITICAL:** After UI upload completes, query `GET /api/FileUpload` with authenticated client to verify backend persistence
  - Assert uploaded file has valid ID, filename, status, and **tenant_id matches signed-in user's default tenant**
  - Clean up via authenticated DELETE before and after test

**Key Pattern — API-Backed Verification:**
- Browser tests for authenticated upload flows MUST verify via the API after UI interaction completes
- Relying on UI state alone (table row, alert message) is insufficient—need backend proof the file persisted with correct tenant scope
- Pattern: `WithPageAsync` (UI interaction) + `WaitForUploadedFileByPrefixAsync` (authenticated API poll) ensures full contract validation
- Aligns with .squad/skills/web-upload-smoke-tests/SKILL.md guidance: "Treat an API-backed empty upload list after the UI click as a hard regression"

**Key File Paths:**
- `src\AspireApp.WebTest\Tests\AuthenticatedUploadUxTests.cs` (updated: added API verification, auth client setup, tenant validation)
- `src\AspireApp.Web\Controllers\FileUploadController.cs` (line 345-390: tenant resolution with auth enforcement)
- `src\AspireApp.Web\Components\Pages\UploadData.razor.cs` (line 556-654: Blazor Server upload bypasses controller)
- `.squad\skills\web-upload-smoke-tests\SKILL.md` (documents smoke test pattern)
- `.squad\skills\playwright-auth-ux-contracts\SKILL.md` (documents auth test hooks)

**Verification Strategy Going Forward:**
- Run `AuthenticatedUploadUxTests` specifically to catch tenant-scoped upload regressions
- Test now validates: UI flow works + backend persistence succeeds + tenant context propagates correctly + signed-in user can query their upload via API
- If controller auth changes or tenant scoping evolves, this test will surface the regression immediately


**Cross-Agent Coordination:**
- Warden approved fix direction (security gates intact)
- Jeff implemented 3 app-level fixes (hydration, tenant fallback, upload readiness)
- All 13 tests passing; security audit complete
- Session logged: .squad/log/2026-04-11T17-53-25-auth-test-fixes.md

---

### 2025-11-02: Phase 2 Knowledge Layer Review

**Reviewed:** Jarvis's Phase 2-B Knowledge Layer implementation slice

**Key Files:**
- `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py` - Retrieval orchestration
- `src/AspireApp.PythonServices/app/services/neo4j_service.py` - Neo4j query layer with claim support
- `src/AspireApp.PythonServices/app/services/claim_extraction_service.py` - Claim extraction logic
- `src/AspireApp.PythonServices/app/routers/processing.py` - Processing pipeline
- `src/AspireApp.PythonServices/tests/test_claim_extraction.py` - Unit tests (5/5 passing)
- `src/AspireApp.PythonServices/tests/test_knowledge_retriever.py` - Integration tests (10/10 passing)

**Testing Verdict:**
1. ✅ **Semantic fallback confidence fixed** - `SemanticKnowledgeRetriever.retrieve()` now queries Neo4j `search_claims()` first (returns `cl.confidence`), then falls back to `search_similar_content()` which returns `d.source_confidence`. No more hardcoded `DEFAULT_CONFIDENCE=0.5` when Neo4j data exists.
   - Validated by `test_semantic_retriever_uses_real_source_confidence_from_neo4j()` - passes with 0.9 confidence from Neo4j
   - Validated by `test_semantic_retriever_queries_claims_before_pages()` - confirms claim-first query order

2. ❌ **Claims NOT persisted end-to-end** - `ClaimExtractionService` exists and is unit-tested (5/5 passing), BUT:
   - `processing.py:process_document_task()` does NOT call `ClaimExtractionService.extract_claims()`
   - `processing.py` does NOT call `neo4j_service.create_claim_nodes()` after page creation
   - Claims are **scaffolded and tested in isolation only** - no live pipeline integration
   - Real queries against Neo4j will hit empty `Claim` nodes and fall back to `Page` nodes every time

3. ⚠️ **Roadmap status overstates P2-B progress** - Tasks.md line 184 says "P2-B can advance once basic claim extraction is wired into the ingestion pipeline" but also marks semantic fallback as `[x]` complete. The actual blocker (wiring claim extraction into `processing.py`) is still open at line 193.

**Architecture Notes:**
- `SemanticKnowledgeRetriever` correctly implements claim-first strategy - code is production-ready
- `ClaimExtractionService` uses simple sentence-splitting heuristics (good for Phase 2 baseline)
- Neo4j schema constraints for `Claim` nodes exist and are enforced at startup
- Test coverage on retrieval layer is excellent (claim fallback, empty result handling, confidence propagation)

**Blockers for P2-B Gate:**
- **CRITICAL:** Wire claim extraction into `processing.py:process_document_task()` after line 76 (page node creation)
- Add call: `claim_extraction_service.extract_claims(page.content, source_confidence=canonical_document.source_confidence)`
- Add call: `neo4j_service.create_claim_nodes(claims, page_node_id, document_id, page_number)` for each page
- Add integration test validating live claim persistence through full pipeline

**Quality Standard:** If it's not tested end-to-end with real data flow, it doesn't work. The retrieval layer is solid, but claims are phantom infrastructure until the pipeline populates them.

**Decision Document:** `.squad/decisions/inbox/buster-p2b-knowledge-layer-verdict.md`


### 2026-04-14 — Test Failure Triage: Upload Status Race Condition + Python Processing Timeouts

**Task:** Investigate 6 failing tests reported by user. Group by root cause and identify .NET vs Python issues.

**Test Failures Reproduced:**

1. ✅ **AuthenticatedUploadUxTests.SignedInTenantScopedUserCanUploadDocumentWithoutAuthenticationError** 
   - **Error:** Expected status "uploaded", actual "processing"
   - **Line:** AuthenticatedUploadUxTests.cs:82
   
2. ✅ **OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres**
   - **Error:** Expected status "uploaded", actual "processing"
   - **Line:** OperationalUploadStoreTests.cs:75

3. ✅ **BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip**
   - **Error:** TaskCanceledException - HttpClient.Timeout of 30 seconds in PollForProcessingCompletionAsync
   - **Line:** BasicAspireAppHostTests.cs:1418 (during polling), called from line 319

4. ❌ **BasicAspireAppHostTests.BrainQueryReturnsConfidenceEnrichedResults**
   - **Error:** Test host process crashed (likely timeout during Python processing poll)
   - **Line:** Test doesn't reach assertion; crashes during setup/processing at line 391

5. ❌ **BasicAspireAppHostTests.FlowEndToEnd**
   - **Error:** Test host process crashed (likely timeout during Python processing poll)
   - **Line:** Test doesn't reach assertion; crashes during setup/processing at line 273

6. ✅ **ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation**
   - **Status:** PASSED (not a failure; user misreported or stale run)

**Root Cause Analysis:**

**GROUP 1: Upload Status Race Condition (.NET-side)**
- Tests: AuthenticatedUploadUxTests, OperationalUploadStoreTests
- **Defect:** Tests expect immediate "uploaded" status after file upload completes, but system now transitions to "processing" automatically
- **Location:** .NET FileUploadController or FileStorageService changes upload workflow to trigger background processing immediately
- **Impact:** Test assumptions are stale; tests written when uploads stayed in "uploaded" state until manual trigger
- **Not a bug:** System behavior likely changed intentionally to auto-start processing; tests need updating to accept "processing" as valid post-upload state

**GROUP 2: Python Processing Timeout (Python-side or Infrastructure)**
- Tests: LiveLightRagNeo4jQueryRoundTrip, BrainQueryReturnsConfidenceEnrichedResults, FlowEndToEnd
- **Defect:** PollForProcessingCompletionAsync times out waiting for Python service to mark document as "processed"
- **Location:** Python processing pipeline (document ingestion, Neo4j graph creation, artifact generation)
- **Evidence:**
  - HttpClient.Timeout of 30s exceeded during status polling
  - ProcessingPollTimeout = 2 minutes should be adequate, but timeouts occur at HTTP client level (30s default)
  - Test host crashes suggest hung Python service or database lock preventing processing completion
- **Possible Causes:**
  1. Python service stuck waiting on Neo4j (lock, connection timeout)
  2. Document processing logic enters infinite loop or resource starvation
  3. Processing status endpoint not updating status field in database
  4. Shared database file (SQLite) locked by concurrent writes from .NET upload + Python processing

**TRIAGE SUMMARY:**

| Group | Root Cause | Affected Component | Test Count | Severity |
|-------|-----------|-------------------|------------|----------|
| 1 | Upload status race | .NET upload flow | 2 | Low (test fix) |
| 2 | Python processing timeout | Python service or DB | 3 | High (system hang) |

**Recommendations:**

1. **GROUP 1 (Test Fix):** Update test assertions to accept "processing" as valid post-upload status
   - Change: Assert.Equal("uploaded", ...) → Assert.Contains(..., new[] {"uploaded", "processing"})
   - Owner: Buster (test-only fix)

2. **GROUP 2 (System Investigation):** Requires Jeff or Jarvis to diagnose Python processing hang
   - Check Python service logs for stuck processing jobs
   - Verify Neo4j connection pool not exhausted
   - Review SQLite concurrent write handling (WAL mode enabled?)
   - Confirm Python background worker is running and picking up jobs
   - Owner: Jarvis (Python) or Jeff (.NET orchestration)

**Files Reviewed:**
- src/AspireApp.WebTest/Tests/AuthenticatedUploadUxTests.cs
- src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs
- src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs (lines 231-290, 384-425, 1409-1458)
- src/AspireApp.WebTest/Tests/ChatConversationPersistenceTests.cs

**Next Steps:**
- Do NOT implement feature fixes for GROUP 2 (requires Jarvis/Jeff)
- Safe to fix GROUP 1 tests if user authorizes test-only changes
- Monitor for shared defect: If Python processing never completes, GROUP 1 tests will also eventually fail on waiting for processed status


### 2026-04-15 — Post-Fix Test Audit: P2-B Confidence & Upload Fire-and-Forget Changes

**Task:** Re-run historical failing test slice after Jarvis (Neo4j confidence enrichment), Bob (Ollama contention fix), and Jeff (upload fire-and-forget) changes.

**Test Results (6 targeted tests):**

✅ **PASSING (3/6):**
- ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation
- OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres  
- AuthenticatedUploadUxTests.SignedInTenantScopedUserCanUploadDocumentWithoutAuthenticationError (after expectation update)

❌ **FAILING (3/6):**
- BasicAspireAppHostTests.FlowEndToEnd — HttpClient timeout in PollForProcessingCompletionAsync (30s limit)
- BasicAspireAppHostTests.BrainQueryReturnsConfidenceEnrichedResults — LightRAG pipeline stuck in busy state (timeout after 120s)
- BasicAspireAppHostTests.LiveLightRagNeo4jQueryRoundTrip — Query returns empty results

**AuthenticatedUploadUx Fix:**
Jeff's fire-and-forget upload change caused immediate status transition from "uploaded" → "processing". Updated test expectation from strict Assert.Equal("uploaded") to Assert.True(status == "uploaded" || status == "processing") to accommodate asynchronous processing start.

**Root Cause — LightRAG Processing Deadlock:**
All three BasicAspireAppHost failures share a common symptom: **LightRAG pipeline gets stuck in busy state and never completes processing**. Test evidence:
- Pipeline status remains {busy: true} for 120+ seconds
- Processing never transitions to "processed" status
- Knowledge queries return empty results (documents never make it into the graph)

**Smoking Gun:**
- BrainQueryReturnsConfidenceEnrichedResults timeout message shows LightRAG stuck with: "busy":true,"job_name":"000001-processing-sm...[1 files]","latest_message":"Processing d-id: doc-c02e43dc0ea33f0638d99df2dfa48834"
- The document ID is captured, processing started, but never finishes

**Likely Culprit:**
Bob's Ollama contention fix moved LightRAG handoff after page/claim embedding work in processing.py (lines 165-169). While this prevents concurrent Ollama saturation, it may have introduced a **handoff timing or dependency issue** that blocks the LightRAG pipeline from completing.

**Test File Paths:**
- src\AspireApp.WebTest\Tests\AuthenticatedUploadUxTests.cs (line 82 — expectation updated)
- src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs (FlowEndToEnd, BrainQuery, LiveLightRag tests)
- src\AspireApp.WebTest\Tests\OperationalUploadStoreTests.cs (passing)
- src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs (passing)

**Product Code Suspect:**
- src\AspireApp.PythonServices\app\routers\processing.py (lines 165-169 — LightRAG handoff moved post-embedding)
- src\AspireApp.PythonServices\app\services\lightrag_handoff_service.py (handoff implementation)
- src\AspireApp.PythonServices\app\brain\knowledge\retrievers.py (LightRAG retriever initialization)

**Verdict:** 
✅ Jeff's upload changes validated (2/2 upload-related tests passing after expectation fix)
❌ **REJECT Bob's Ollama contention fix** — LightRAG handoff deadlock blocks all document processing end-to-end flows
❌ **REJECT Jarvis's confidence enrichment integration** — Cannot verify confidence enrichment because documents never complete processing due to LightRAG deadlock

**Next Revision Owner:**
**Bob** must revise the Ollama contention mitigation strategy. The current approach (defer LightRAG until after embedding) prevents saturation but creates a **processing completion blocker**. Alternative approaches:
1. Restore original handoff timing + add Ollama request throttling/queuing
2. Investigate why deferred LightRAG handoff doesn't complete (timeout? deadlock? missing status update?)
3. Make LightRAG handoff truly fire-and-forget with separate completion tracking

**Confidence:**
High — Three independent test failures all exhibit the same LightRAG stuck-in-busy symptom. This is a systematic processing pipeline failure, not flaky test behavior.


---

### 2026-04-15 — Fixed Upload/E2E Test Failures: Async Dispatch and Event Loop Starvation

**Task:** Reproduce and fix failing BasicAspireAppHostTests.FlowEndToEnd and FileUploadControllerTests.

**Status:** ✅ COMPLETE — 8/8 tests passing

**Root Causes Identified:**
1. **Test Assumption Mismatch:**
   - Tests expected synchronous completion; app uses fire-and-forget background processing
   - FileUploadController.UploadFile() now queues automatic processing on a delayed task after the upload response returns with status="uploaded"
   - FileUploadControllerTests must poll background queue dispatch instead of asserting sync completion

2. **Python Event Loop Starvation (Jeff's fix):**
   - BasicAspireAppHostTests.FlowEndToEnd was timing out while polling GET /processing/status/{id}
   - Python process_document_task was sync-heavy (Docling extraction, Neo4j writes, embedding) on the FastAPI event loop
   - Status polling requests hit client timeouts during busy processing
   - Jeff moved heavy work to thread-pool via syncio.to_thread(...), restored event loop responsiveness

**Test Updates:**
- FileUploadControllerTests: Changed to poll coordinator for background queue dispatch with reasonable waits
- BasicAspireAppHostTests: Treat transient HttpClient.Timeout during status polling as retryable within overall processing window (don't fail immediately)

**Decisions Captured:**
1. "Upload test scaffolding must respect async dispatch" — Buster
2. "Keep Python processing off the FastAPI event loop" — Jeff
3. "Do not use GitHub Issues for this repo" — Eric (directive)

**Key Files:**
- src\AspireApp.Web\Controllers\FileUploadController.cs
- src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs
- src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs
- src\AspireApp.PythonServices\app\routers\processing.py (Jeff's fix)

**Validation:** dotnet test ... --filter "FullyQualifiedName~BasicAspireAppHostTests.FlowEndToEnd|FullyQualifiedName~FileUploadControllerTests" — **8/8 passed**

**Lessons:**
- Async dispatch patterns require test-time understanding of fire-and-forget semantics, not just implementation details
- FastAPI event loop can be easily starved by sync-heavy background tasks; always move heavy work to thread pool
- Transient timeouts during service startup should be retryable, not fatal

### 2026-04-15 — ChatConversationPersistenceTests Pass After Playwright Installation

**Task:** Diagnose reported failure in `ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation`.

**Root Cause:** Missing Playwright browser binaries — not a test logic or UI regression.

**What Happened:**
- Test crashed with `Microsoft.Playwright.PlaywrightException : Driver not found`
- Missing file: `C:\Users\ericv\source\repos\eric-vanartsdalen\AspireAI\src\AspireApp.WebTest\bin\.playwright\node\win32_x64\node.exe`
- This is a **local environment issue**, not a code defect

**Resolution:**
1. Installed Playwright CLI: `dotnet tool update --global Microsoft.Playwright.CLI`
2. Installed Chromium browser: `playwright install chromium`
3. Test now passes in 107 seconds (chat conversation save, rename, resume, delete workflow validated)

**Key Learning:**
- Playwright browser binaries are **not** committed to the repository
- After a fresh clone or environment change, must run `playwright install chromium` before running Playwright tests
- This is standard Playwright behavior — not an AspireAI-specific issue

**Test Validation:**
- ✅ User signs in via mock authentication
- ✅ Chat prompt sent and conversation auto-titled
- ✅ Conversation renamed successfully
- ✅ New conversation started (isolation verified)
- ✅ Original conversation resumed with correct history
- ✅ Follow-up message appended to correct conversation
- ✅ Conversation deleted from sidebar

**No Code Changes Required:**
- Test logic is correct
- Recent chat UI changes (mode selector, evidence display) did NOT break this test
- Auth session establishment works correctly
- Conversation persistence behavior is correct

**Related Context:**
- User context mentioned recent chat UI changes and prior auth UX regressions
- This failure was purely environmental (missing browser binaries), not related to recent code changes
- Prior learnings about hard navigation after sign-in and mock sign-in helpers remain valid but were not the issue here

**Key File Paths:**
- `src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs`
- `src\AspireApp.WebTest\Fixtures\TestFixture.cs`



## 2026-04-15T17-41-59 — Chat Persistence Test Investigation & Orchestration Merge (Scribe collaboration)

**Role:** QA investigator (ChatConversationPersistenceTests diagnosis)
**Outcome:** No product defect; environmental + timing issues identified
**Output:** 
- Diagnosed missing Playwright Chromium as test blocker
- Test scenario passed after local browser install  
- Collaborated with Jeff on timing race analysis (90s vs 180s AI timeout)
- Determined issue is test infrastructure, not product code
- Contributed to merged decision log

**Learning:** Intermittent test failures often have environmental prerequisites; Playwright browser setup must be documented in dev prerequisites.

**Files:** 
- .squad/orchestration-log/2026-04-15T17-41-59-buster.md (session log)
- .squad/decisions/inbox/buster-*.md → merged to decisions.md


### 2026-04-22 — Critique-Mode UI/Product Test Coverage Created (Phase 3b Product Layer)

**Task:** Add test coverage for Critique-mode UI/product behavior: toggle enablement, mode selection wiring, reasoning/progress rendering, and Regular mode regression.

**What Was Delivered:**
- Created `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs` with 8 comprehensive tests covering:
  1. Critique toggle enabled after product implementation
  2. Mode selection updates component state
  3. Mode propagates to `BrainChatClient.ChatAsync` (both critique and regular)
  4. Reasoning steps render with tool/result details
  5. Regular mode doesn't render reasoning panel
  6. Progress details visible in reasoning steps
  7. Mode hint text changes based on selection
  8. Existing conversations load with stored chat mode

**Coverage Strategy:**
- **Toggle Enablement:** `CritiqueToggle_IsEnabled_AfterProductLayerImplementation` validates the `disabled` attribute is removed from the critique radio button
- **Mode Wiring:** `SendingMessage_InCritiqueMode_PassesCritiqueModeToClient` and `SendingMessage_InRegularMode_PassesRegularModeToClient` prove selected mode reaches the gateway
- **Reasoning Rendering:** `CritiqueResponse_WithReasoningSteps_RendersReasoningPanel` validates reasoning step display with step/reasoning/tool/result details
- **Regular Mode Regression:** `RegularResponse_WithoutReasoningSteps_DoesNotRenderReasoningPanel` ensures Regular mode unchanged
- **Progress Details:** `CritiqueMode_RendersProgressDetails_WhenReasoningStepsIncludeToolResults` validates tool execution visibility
- **UI Feedback:** `ModeHintText_ChangesBasedOnSelectedMode` ensures user sees mode-appropriate guidance
- **Conversation Persistence:** `ExistingConversation_LoadsWithStoredChatMode` validates mode survives conversation reload

**Test Scaffolding Pattern:**
- `RecordingBrainChatClient` test double captures `(query, mode, tenantId, conversationId, topK)` for verification
- `ResponseToReturn` property allows stubbing response with reasoning steps, evidence, confidence
- Reuses existing test fixtures (`CreateTestContext`, `StubChatConversationService`, `StubAuthenticationStateProvider`)
- Added `StubChatConversationServiceWithCritiqueConversation` for conversation-mode-persistence test

**Test Execution Notes:**
- Tests are syntactically valid but cannot run until Jeff implements the product changes (remove `disabled` attribute, add reasoning-panel rendering)
- Build blocked by locked DLL from running Aspire services - this is environmental, not a test defect
- Tests are scaffolded to FAIL until implementation completes, then PASS once:
  - Critique radio `disabled` attribute removed
  - Reasoning steps rendered with `data-testid="chat-reasoning-panel"` and `data-testid="chat-reasoning-step"`
  - Mode hint text conditional logic works

**Why This Is Good Coverage:**
- **Proves mode selection wiring:** Tests validate `SelectedChatMode` reaches `BrainChatClient.ChatAsync` with correct value
- **Proves rendering logic:** Reasoning panel renders when `ReasoningSteps.Count > 0`, not rendered when empty
- **Proves Regular mode regression safety:** Regular mode behavior unchanged (no reasoning panel, only evidence)
- **Proves UI feedback works:** Mode hint text changes correctly
- **Proves persistence:** Conversations load with stored mode

**Coordination Notes:**
- Tests created while Jeff implements product layer in parallel
- Tests will fail initially (expected) until Jeff's implementation completes
- Once Critique toggle enabled + reasoning rendering wired, these 8 tests should pass
- No integration test suite run yet (Aspire services running blocked build)

**Key File Paths:**
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs` (new, 690 lines, 8 tests)

**Next Steps for Product Implementation (Jeff):**
1. Remove `disabled` attribute from Critique radio in `Chat.razor` (line 855)
2. Add reasoning panel rendering logic after evidence panel
3. Ensure reasoning steps display with `data-testid` attributes for test validation
4. Verify mode hint text conditional is correct

### 2026-04-23 — Critique-Mode Test Harness Revision Verified

**Task:** Re-review critique-mode UI batch after harness revision (compile fix + chat mode persistence).
**Outcome:** ✅ Approved; `ChatCritiqueModeTests` now pass cleanly (9/9) with the revised stubs and selected-mode persistence path.
**Evidence:** `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~ChatCritiqueModeTests"` succeeded.

### 2026-04-15T19:37:41Z — Critique Mode UI Test Coverage: Complete and Approved

**Status:** All 9 tests passing; residual risk (mode-switch persistence test) identified but non-blocking.

**Work Completed:**
- Designed 8-test Critique-mode UI test suite covering:
  - Toggle enablement, mode selection, mode wiring (critique + regular), reasoning rendering, progress details, mode hint text, conversation persistence
  - One additional test for persistence added (9 total)
- Identified test blocker: Bunit doesn't support RemoveAll() for service replacement after context creation
- Documented three fix options with rationale and trade-offs
- Reviewed Jeff's parameterized factory fix and approved it
- Validated all 9 tests passing (9/9)

**Test Coverage Delivered (9 Tests):**
1. ✅ Toggle Enablement — disabled attribute removed
2. ✅ Mode Selection — Two-way binding works
3. ✅ Mode Wiring (Critique) — Reaches BrainChatClient.ChatAsync
4. ✅ Mode Wiring (Regular) — Regression safety verified
5. ✅ Reasoning Rendering — Panel shows with steps when present
6. ✅ Regular Mode Rendering — No reasoning panel (only evidence)
7. ✅ Progress Details — Tool results visible in reasoning steps
8. ✅ Mode Hint Text — Updates based on selection
9. ✅ Conversation Persistence — Mode survives conversation reload

**Test Double Pattern:**
- RecordingBrainChatClient captures (query, mode, tenantId, conversationId, topK)
- ResponseToReturn property stubs responses with reasoning steps
- No HTTP mocking needed — pure in-memory test double

**Validation Results:**
- dotnet test: 9/9 passing
- No flakiness detected
- Test assertions unchanged (data-testid attributes properly scoped)

**Cross-Team Updates:**
- **Jeff (.NET):** Resolved test blocker via parameterized factory approach; product layer complete
- **Jarvis (Python):** Critique reasoning pipeline ready for integration
- **Bob (Architecture):** QA validates swappable agent framework wiring

**Residual Risk (Non-Blocking):**
- No dedicated test exercises mode switching after loading an existing conversation
- **Recommendation:** Manual spot-check or follow-up test for Phase 3b polish
- **Status:** Doesn't block Phase 3b gate closure

**Key Files Created/Modified:**
- src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs (new, 690 lines, 9 tests)
- .squad/decisions/inbox/buster-critique-ui-*.md (diagnostic, strategy, approval) → merged to decisions.md

**Related Decisions Merged:**
- Critique-Mode UI Test Coverage Strategy (Buster, 2026-04-22)
- Critique-Mode UI Test Blocker (Buster, 2026-04-22, resolved by Jeff)
- Critique-Mode Harness Revision Approved (Buster, 2026-04-23)

**Orchestration Log:** .squad/orchestration-log/2026-04-15T19-37-41Z-buster.md

**Session:** Critique Mode UI Batch (2026-04-15T19:37:41Z, log: .squad/log/2026-04-15T19-37-41Z-critique-ui-batch.md)

**Key Insight for QA:**
- Test infrastructure patterns (parameterized factories, test doubles, data-testid scoping) proved flexible enough to handle framework constraints
- Initial blocker had three valid fix options; choosing the one most consistent with existing patterns was key
- All 9 tests passing without assertion weakening validates that UI/product layer behaves as designed

### 2026-04-15 — Chat mode switch-back coverage gap is real and should be planned honestly

**Task:** Audit whether the chat tests already cover a saved conversation that moves Regular → Critique → Regular across multiple turns.

**What I Found:**
- `ChatCritiqueModeTests` covers single-send mode routing (`SendingMessage_InCritiqueMode_PassesCritiqueModeToClient`, `SendingMessage_InRegularMode_PassesRegularModeToClient`) plus loading a conversation whose stored mode is already Critique (`ExistingConversation_LoadsWithStoredChatMode`).
- No current test exercises the full transition sequence: start in Regular, switch to Critique mid-thread, continue, then switch back to Regular and verify the later send is truly Regular again.
- `ChatConversation` persists `chat_mode` on the conversation row only; `ChatConversationMessage` has no per-message mode field, so historical turn mode is not persisted today.
- `Chat.razor.cs` sends `SelectedChatMode` to `BrainChatClient.ChatAsync`, but loading a conversation clears `_messageEvidence`, which means critique reasoning/evidence is an in-memory seam rather than a persisted transcript contract.

**Why This Matters:**
- The missing regression test is exactly where mode leakage would hide: a critique turn could accidentally poison a later regular turn, or reload behavior could imply false historical fidelity.
- Planning language must stay honest: test per-message request routing and conversation-level persistence boundaries, but do not promise persisted per-turn mode history unless Jeff/Bob fund a schema change.

**Key file paths:**
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.Web\Data\ChatConversationEntities.cs`
- `src\AspireApp.Web\Services\ChatConversationService.cs`
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`

---

## Cross-Agent Coordination — Scribe Merge (2026-04-15T20:25:34Z)

**Session:** Planning Doc Reconcile & Test Failure Triage

**Work:** Buster audited chat-mode test coverage, triaged 6 reported test failures, and mapped root causes to owners.

**Coordination Points:**
- Verbal recommended Phase 3 beta reframing (honest milestone reflects foundation reality)
- Bob reconciled branch state against roadmap; verified Phase 1/2 gates closed; locked Phase 3 critical path
- Jeff synced planning docs; incorporated regression testing task into Phase 3b roadmap
- Jarvis analyzed Python processing timeout (infrastructure issue); confidence enrichment fix applied
- Warden hardened auth test selectors; Buster confirmed split-brain session-establishment pattern remains (endpoint fix needed)

**Key Outcomes:**
- Chat-mode transition regression coverage gap identified and added to Phase 3b roadmap with honest persistence-boundary wording
- 6 test failures triaged: 2 upload status race (test assumption), 3 Python processing timeout (infrastructure), 1 auth split-brain (endpoint)
- WebTest fixture guard decision: skip gracefully when Aspire health checks fail (preserves signal in lighter tests)
- Auth split-brain pattern diagnosed: hard-navigation proof recommended over passive UI observation

**Related:** Orchestration logs created. Session log at .squad/log/2026-04-15T20-25-34Z-planning-doc-reconcile.md. 17 inbox decisions merged into .squad/decisions.md.

### 2026-04-15 — Critique Mode Regression Coverage — Three-Seam Validation

**Problem:**
- Critique mode failing deterministically with 502/503 errors. Root causes spread across three independent boundaries:
  1. Python provider wiring (PydanticAI initialization order)
  2. .NET gateway/Web HTTP clients (error preservation + retry behavior)
  3. Saved conversation persistence (chat_mode reload + UI mode selector consistency)

**Fix & Validation:**
- **Python provider seam:** Verified Jarvis's explicit OllamaProvider config eliminates reliance on late env mutation
- **HTTP client seam:** Confirmed Jeff's error preservation prevents 503→502 collapse; no-retry policy stops resilience amplification
- **Saved conversation seam:** Added tests proving persisted chat_mode survives reload; UI mode selector updates correctly when switching between critique and regular threads

**Result:**
- Focused .NET tests: 30/30 passed (ChatCritiqueModeTests + BrainGatewayPhase2Tests + ChatConversationServiceTests)
- Focused Python tests: 36/36 passed (test_critique_pipeline.py + test_brain_chat.py)
- Full suite regressions: 0 failures
- Three-seam regression coverage now guards against cross-boundary initialization, HTTP, and persistence failures

**Key Validation Paths:**
- **Provider wiring:** src/AspireApp.PythonServices/tests/test_critique_pipeline.py (proves Ollama config without OPENAI_API_KEY)
- **HTTP clients:** src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs + src/AspireApp.Web/Services/BrainChatClient.cs (error preservation)
- **Saved conversation:** src/AspireApp.WebTest/Tests/ChatCritiqueModeTests.cs + src/AspireApp.WebTest/Tests/ChatConversationServiceTests.cs (mode reload consistency)

**Cross-Agent Impact:**
- **Jarvis (Python):** Provider fix provides foundation; Python tests validate provider wiring seam.
- **Jeff (.NET):** Error preservation + no-retry policy enable accurate Blazor UI feedback and prevent retry amplification.
- **Overall:** Three-seam strategy prevents future cross-boundary failures; regression suite validates all initialization, HTTP, and persistence paths.

**Key Pattern:**
- **Multi-seam regression strategy:** Deterministic failures often span multiple boundaries (init → HTTP → persistence). Regression suite must validate all seams independently and together to prevent breaks at any interface.
- **Saved conversation mode switching:** Regression tests should cover both initial load and mode-switch reload scenarios; UI state must stay consistent with persisted state.

**Key file paths:**
- src/AspireApp.PythonServices/tests/test_critique_pipeline.py (provider validation)
- src/AspireApp.WebTest/Tests/ChatCritiqueModeTests.cs (mode persistence)
- src/AspireApp.WebTest/Tests/ChatConversationServiceTests.cs (conversation reload)
- src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs (gateway error handling)
- .squad/decisions.md (full decision details + evidence paths)
- .squad/orchestration-log/2026-04-15T21-17-30Z-buster.md (session details)

### 2026-04-16 — MVP Achieved: P3b Ready for Final QA Push, Post-MVP Validation Queued

**Scope:** Cross-agent session confirming MVP milestone and queuing post-MVP validation work for Phase 3c.

**What Happened (Summary for Buster):**
- MVP is **officially declared functional** (gateway-routed Regular mode chat works end-to-end)
- Two post-MVP fixes elevated to **P1-immediate** status with validation requirements:
  1. **Conversation context not passed on follow-ups** (test: multi-turn session persistence)
  2. **Gateway evidence not persisted** (you lead: Evidence storage + UI display regression suite)
- P3b critique UI remains on track; ready for final QA pass
- Both post-MVP tasks blocked on P3b completion (2026-04-30 target)

**What This Means for Buster:**
- **Immediate (P3b phase):** Final QA pass on critique UI (Jeff's implementation complete; focus on cross-seam regression)
- **Post-MVP (Phase 3c, lead role):** Evidence persistence validation
  - Scope: Backend persistence tests + UI display regression suite
  - Goal: Evidence survives session reload; citations display correctly in Chat.razor
  - Cross-context: Jarvis Neo4j schema, Jeff UI wiring
- Context memory task (secondary): Validate multi-turn session state in Chat + BrainChatClient

**Coordinator SQL-Tracked Tasks (Post-P3b):**
- `mvp-evidence-persistence` — Evidence storage validation (owner: you)
- `mvp-conversation-context-memory` — Session context propagation tests (co-lead with Jarvis)

**P3b Final QA Checklist (Before 2026-04-30):**
- ✅ Critique toggle works (Jeff implemented)
- ✅ Reasoning steps render correctly (Jeff implemented)
- ✅ Gateway error preservation flows through clients (Jeff + Jarvis fixed)
- ⏳ Regression suite: provider wiring + HTTP error + mode reload scenarios
- ⏳ Blazor UI state consistency (mode toggle + navigation)

**Post-MVP Phase 3c Test Plan (Evidence Persistence):**
1. Backend persistence: Evidence objects created, persisted to Neo4j, retrieved on follow-up
2. UI display: Evidence renders with citations + confidence scores
3. Regression: Mode switching, conversation reload, evidence display integrity
4. Cross-seam: Python Neo4j storage + C# retrieval + Blazor UI rendering

**Key Files for Post-MVP Validation:**
- Evidence models: `src/AspireApp.PythonServices/app/contracts/models.py` (Evidence shape)
- Storage service: `src/AspireApp.PythonServices/app/services/neo4j_service.py` (persistence layer)
- Gateway response: `src/AspireApp.ApiService/Contracts/BrainContractModels.cs` (Evidence contract)
- UI display: `src/AspireApp.Web/Components/Pages/Chat.razor.cs` (evidence rendering)
- Regression tests: `src/AspireApp.WebTest/Tests/ChatCritiqueModeTests.cs` (new post-MVP suite)

**Status:** MVP locked; P3b ready for final QA; post-MVP evidence validation queued 2026-04-30

---

### 2026-04-22 — Conversation Memory + Assistant Metadata Regression Coverage Landed

**Task:** Lock down the two post-MVP conversation fixes: follow-up context propagation and persisted evidence/confidence reload.

**Key findings:**
- The cross-service request seam now treats prior turns as `conversation_history` (`{ role, content }`) and the Blazor chat page intentionally strips the just-entered prompt before calling the gateway, so retrieval/generation get prior context without duplicating the current question.
- Saved assistant metadata belongs on the chat message row itself (`assistant_response_json` on `chat_messages`), then the UI must rehydrate that JSON back into `BrainChatResponse` when a conversation is reopened; an in-memory `_messageEvidence` cache alone is not enough.
- The cheapest regression stack for these fixes is: Python contract/router tests, .NET contract/gateway tests, `ChatConversationServiceTests` for persistence, and bUnit `ChatCritiqueModeTests` for reload rendering. Full browser proof is still a follow-up, not the first gate.

**User preference / validation posture:**
- Keep validation focused. For this slice, targeted WebTest + Python regression suites are the right gate instead of broad repo reruns.

**Key file paths:**
- `src\AspireApp.Web\Services\BrainChatClient.cs`
- `src\AspireApp.Web\Services\ChatConversationService.cs`
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.Web\Services\ChatConversationStoreBootstrapper.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.WebTest\Tests\BrainGatewayPhase2Tests.cs`
- `src\AspireApp.PythonServices\app\routers\brain.py`
- `src\AspireApp.PythonServices\app\services\llm_chat_service.py`
- `src\AspireApp.PythonServices\tests\test_brain_chat.py`
