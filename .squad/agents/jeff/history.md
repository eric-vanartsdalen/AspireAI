# Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI — AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Created:** 2026-02-21T23:32:00Z

## Core Context

**Active Themes (as of 2026-04-15):**
- **P2-C Embedding Orchestration:** AppHost now passes embedding config (OLLAMA_ENDPOINT, EMBEDDING_MODEL, EMBEDDING_DIM) to Python services; startup dependencies ensure model readiness before ingestion
- **AI Latency Variance:** Product code handling AI response timing is correct; test infrastructure must account for legitimate 180s+ Ollama response windows (test timeouts at 90s race this)
- **Chat Persistence:** Rename-typing focus interference solved via explicit render-time flags instead of eager autofocus; owner-message privacy tests require only persistence + visibility validation, not full AI response
- **Upload Test Architecture:** Async background processing means test must poll for eventual completion, not assert synchronously at controller return

**Key Technical Decisions:**
1. P2-C embedding config wired via Aspire environment variables (consistent with LightRAG pattern)
2. Python services wait for Ollama + embedding model before startup (prevents cold-start failures)
3. Chat focus managed by explicit `ShouldFocusQuestionInput`/`ShouldFocusConversationTitleInput` flags (no eager autofocus)
4. Vector infrastructure foundation complete; embedding population + retriever wiring deferred (honest roadmap)
5. Playwright browser installation not bundled (must be documented in dev prerequisites)

**Cross-Service Patterns:**
- AppHost orchestration via typed endpoints and service discovery
- Environment variable configuration surface minimal (only what services need)
- Dependency ordering critical (wait for AI models, DBs before service startup)
- Health check endpoints monitored by Aspire dashboard

**Working Relationships:**
- Close collaboration with Bob on architecture and orchestration wiring
- Buster drives QA validation and test infrastructure decisions
- Jarvis implements Python/Neo4j details
- Eric provides user direction and design feedback

## Learnings

### 2026-04-21 — AppHost Must Serialize LightRAG File Indexing via `MAX_PARALLEL_INSERT` to Match Ollama Constraint

**Context:**
- Eric asked whether LightRAG exposes a setting that makes documents index automatically "on upload" to address timeout issues.
- AspireAI wires a LightRAG container in AppHost, but the product does not call LightRAG's `/documents/upload` API.
- The Python pipeline stages markdown into the shared `INPUT_DIR` and explicitly calls `POST /documents/scan`.

**What I Confirmed:**
- LightRAG's `/documents/upload` endpoint already indexes in background by design.
- There is no separate boolean "index on upload" flag for the `INPUT_DIR` + `/documents/scan` flow.
- The documented AppHost-relevant concurrency knob is `MAX_PARALLEL_INSERT`, which controls how many files LightRAG indexes in parallel during scan operations.

**Practical Decision:**
- Do **not** add a fictional "index on upload" AppHost flag.
- Treat upload-time indexing as LightRAG endpoint behavior, not as a tunable setting.
- For our scan-based handoff, use the documented `MAX_PARALLEL_INSERT=1` setting.
- This matches the existing `MAX_ASYNC=1` constraint against Ollama and reduces avoidable contention.

**Result:**
- AppHost configuration now sets `.WithEnvironment("MAX_PARALLEL_INSERT", "1")` on the LightRAG container service.
- File-level indexing runs one document at a time, reducing timeout pressure from unnecessary concurrent LightRAG embedding calls against a constrained Ollama backend.
- Scan-based handoff semantics are unchanged.

**Key Pattern:**
- **AppHost Configuration Honesty:** Don't invent fictional environment variables. Understand the actual documented tuning knobs and apply them consistently when multiple services compete for the same backend resource.
- **Cross-Service Constraint Matching:** When Ollama is constrained to `MAX_ASYNC=1`, mirror that in dependent stages (LightRAG `MAX_PARALLEL_INSERT=1`) to prevent unnecessary contention.

**Key File Paths:**
- `src\AspireApp.AppHost\AppHost.cs` — LightRAG service registration with `MAX_PARALLEL_INSERT=1`
- `.squad/decisions/inbox/jeff-lightrag-setting.md` — Decision artifact (merged to decisions.md)
- `.squad/orchestration-log/20260421T171255Z-jeff.md` — Orchestration log
- `.squad/log/20260421T171255Z-lightrag-setting.md` — Session log (shared with Jarvis)

**Validation:**
- `dotnet restore` ✅
- `dotnet build .\AspireApp.sln --no-restore` ✅

### 2026-04-24 — Desktop Menu-Close Regression Was Fixed at the Right Ownership Boundary; Playwright Viewport Timing Is a Test Responsibility

**Context:**
- Desktop Web UI kept the slide-out menu open after clicking a nav item and navigating to a new route.
- Jeff fixed by subscribing `MainLayout` to `NavigationManager.LocationChanged` and closing sidebar on route change.
- Buster added live browser regression test to validate fix.

**The Fix — Why This Ownership Works:**
- The layout already owns `_sidebarOpen`, the backdrop element, and the hamburger toggle button.
- Route-change-driven dismissal covers all internal navigation uniformly without scattering per-link handlers through `NavMenu.razor`.
- Implementation is simple: subscribe in `OnInitializedAsync()`, set `_sidebarOpen = false` on every location change, unsubscribe via `IDisposable`.
- BUnit test `MainLayoutTests.ClosesSidebar_WhenLocationChanges` validates the logic quickly.

**Playwright Timing Discovery:**
- First browser test run (`BasicAspireAppHostTests.DesktopSidebarClosesAfterNavigationSelection`) failed with "Element is outside viewport" when trying to click the `Chat` link.
- Root cause: Playwright clicked before the drawer animation completed settling inside viewport.
- **This was NOT a product code bug.** Browser animations are real timing.
- Buster's fix: Add explicit `WaitForNavigationTargetWithinViewportAsync()` before clicking nav targets in drawer-dependent tests.

**Key Learning:**
- Animation timing failures are a **test responsibility**, not a product issue.
- Explicit waits preserve real-user interaction semantics without brittle workarounds.
- This becomes a reusable pattern for all future drawer/slide-out navigation tests.

**Result:**
- Product code clean ✅
- Unit test passing ✅
- Live browser regression stable and meaningful ✅
- Future drawer tests have a clear pattern to follow ✅
- `BasicAspireAppHostTests.AspireDashboardLoads` ✅

### 2026-04-21 — LightRAG Has No Auto-Index Toggle for Our Scan-Based Handoff

**Context:**
- Eric asked whether LightRAG exposes a setting that makes documents index automatically "on upload" so AppHost could flip it on for the timeout issue.

**What I Confirmed:**
- LightRAG's documented `POST /documents/upload` endpoint already indexes asynchronously by design; there is no separate boolean environment flag that turns upload indexing on.
- AspireAI does **not** use that endpoint for ingestion. Our Python pipeline stages markdown into LightRAG's shared `INPUT_DIR` and explicitly triggers `POST /documents/scan`.
- For that scan-based flow, the documented AppHost-relevant tuning knob is `MAX_PARALLEL_INSERT`, which controls how many files LightRAG indexes in parallel.

**Practical Decision:**
- Keep the existing scan-based handoff.
- When AspireAI also constrains `MAX_ASYNC=1` against the same Ollama backend, set `MAX_PARALLEL_INSERT=1` so LightRAG serializes file ingestion and avoids extra contention.

**Key file paths:**
- `src\AspireApp.AppHost\AppHost.cs`
- `src\AspireApp.PythonServices\app\services\lightrag_handoff_service.py`
- `.squad\skills\lightrag-handoff\SKILL.md`

### 2026-04-21 — Upload Dispatch Must Not Block the Blazor Circuit

**Context:** Eric reported upload-page timeouts after the row flipped to "Processing", plus chat calls timing out while LightRAG-backed responses were still in flight.

**What I Confirmed:**
- Python `POST /processing/process-document/{id}` already queues background work and returns quickly; the long-running work is not supposed to be awaited by the upload surface.
- The Web **interactive** upload path in `UploadData.razor.cs` still awaited `TryStartAutomaticProcessingAsync()` directly, and the URL controller path did the same.
- Chat timeouts were still brittle because transport timeouts could race or outlive the user-facing timeout without being translated into a clear gateway problem.

**What I Changed:**
- Upload/file + URL flows now persist first, return immediately with a queued-processing message, and dispatch automatic processing in background.
- Background dispatch failures now surface as warnings instead of stalling the upload interaction.
- Chat/gateway clients now translate downstream timeouts into explicit 504-style problems, and timeout layers were widened so the UI token is the primary boundary.

**Validation:**
- Focused .NET tests passed: `BrainGatewayPhase2Tests`, `UploadDataTests`, `ChatCritiqueModeTests`, `FileUploadControllerTests` (63/63).
- `dotnet build .\\AspireApp.sln --no-restore` succeeded.

### 2026-12-20 — Chat Reload Diagnosis: Fresh Knowledge Not Appearing

**Context:** User uploaded website, it processed successfully, reloaded chat page, but new knowledge didn't appear in chat responses. Requested audit of .NET side for stale request state or reload behavior issues.

**Investigation Scope:**
- Upload completion path (FileUploadController)
- Chat page reload behavior (OnInitializedAsync, LoadConversationSummariesAsync)
- Conversation persistence (SelectConversationAsync, ApplyConversationDetail)
- Request shaping to BrainChatClient (CallBackgroundAI parameters)
- Conversation history building (BuildConversationHistoryForGateway)

**Key Findings — .NET Side EXONERATED:**
1. **Upload Processing:** URL uploads trigger `TryStartAutomaticProcessingAsync()` synchronously (line 278-279 FileUploadController.cs) — no background delays that would create race conditions
2. **Page Reload:** `OnInitializedAsync()` always calls fresh `LoadConversationSummariesAsync()` → DB query with no client-side caching
3. **Conversation Loading:** `SelectConversationAsync()` rebuilds `_chatHistory` from DB each time (lines 218-230) — no stale in-memory state
4. **Request Parameters:** `CallBackgroundAI()` passes fresh context to BRAIN gateway (lines 990-996):
   - `tenantId`: Current tenant from TenantContext (no caching detected)
   - `conversationId`: Current active conversation ID
   - `conversationHistory`: Last 12 messages from fresh `_chatHistory`
   - No filters or exclusions that would prevent fresh knowledge retrieval
5. **History Building:** `BuildConversationHistoryForGateway()` is simple last-N logic (max 12 messages) with no date/content filtering

**Root Cause Determination:**
- .NET correctly passes tenant ID and all context to BRAIN gateway
- No stale state, no caching, no reload issues in Blazor chat component
- **Issue MUST be in BRAIN gateway or Python processing pipeline:**
  - Knowledge graph may not be committed/visible despite "processed" status
  - BRAIN `/brain/chat` endpoint may query stale state or use cached results
  - Processing completion race: status updated before Neo4j ingestion finishes

**Artifact Created:**
- `chat-reload-diagnosis.md` in repo root with full evidence trail and failure point analysis
- Recommended next steps for Python/BRAIN team (Jarvis) to verify Neo4j knowledge graph state and gateway retrieval logic

**Key File References:**
- `FileUploadController.cs` (line 278-279): Upload → processing trigger
- `Chat.razor.cs` (lines 103-140, 164-189, 204-230, 337-349, 990-996, 1082-1103): Reload + request shaping
- `BrainChatClient.cs` (lines 66-74): Request envelope construction

### 2026-12-19 — Performance Bottleneck Analysis: Chat Timeouts

**Context:** Eric reported chat response latency (80–120s for regular search, 180s for Critique Mode with timeouts). Requested investigation of .NET timeout/resilience limits and whether architecture adds avoidable latency.

**Trace Path:**
- Blazor Chat component → BrainChatClient (Web) → API Service → PythonBrainBackendClient → Python FastAPI backend
- 3-minute (180s) cancellation token set in Chat.razor.cs CallBackgroundAI() (line 980)
- Timeout stacking across three layers:
  - Web HttpClient.Timeout: 120s
  - Web Polly TotalRequestTimeout: 180s
  - API HttpClient.Timeout: 180s
  - API Polly TotalRequestTimeout: 240s

**Critical Finding:**
- Backend latency is the bottleneck: Neo4j search 40–60s + Ollama LLM 40–60s = 80–120s
- .NET layers have **no defects** (no duplicate calls, serial waits are correct, payload size is lean)
- BUT: Web HttpClient.Timeout (120s) fires **before** Blazor's 180s intent because it's too tight
- Critique Mode timeout (reported 180s) is correct behavior: if multiple passes + reasoning occur, hitting the Blazor token boundary is expected

**Key Issues Identified:**
1. **Timeout collision:** Web layer's 120s HttpClient.Timeout is hidden boundary; users see failure before Blazor's 180s token expires
2. **No progress feedback:** User stares at "Waiting..." for 2 minutes; no "Searching graph" or "Generating response" indicator
3. **Timeout alignment not documented:** Future maintainers won't understand why timeouts are stacked the way they are

**Recommendations (High Impact):**
1. Increase Web HttpClient.Timeout to 240s (was 120s) — removes hidden boundary
2. Increase Web Polly TotalRequestTimeout to 240s (was 180s) — aligns with updated client timeout
3. Add documentation explaining timeout stacking rationale
4. Implement Server-Sent Events (SSE) for real-time progress feedback — reduces perceived wait time
5. Add diagnostic logging for timeout patterns (helps identify whether issues are Neo4j, Ollama, or .NET)

**Decision:** These are maintainability + UX improvements, not performance defects. Recommend implementing #1–3 as low-effort high-value fixes, #4–5 as medium-term UX enhancements.

**Artifact:** Full analysis in `/PERFORMANCE_ANALYSIS.md` with 10 prioritized recommendations and code locations.

### 2026-04-23 — Chat Timeout Boundaries Misaligned; Immediate Fixes Approved

**Context:** Search latency review surfaced hidden timeout collision: Web HttpClient.Timeout (120s) fires before Blazor's 180s cancellation token. .NET layers are correct; issue is design clarity + UX.

**Investigation Result:**
- Backend latency is expected (40-60s retrieval + 40-60s LLM = 80-120s regular, longer for critique)
- Critique Mode timeout is not a bug; multi-pass design is structurally expensive (150-270s without parallelization)
- .NET is not adding latency; architecture is sound

**Immediate Fixes (Approved for Next Sprint):**
1. Raise Web HttpClient.Timeout from 120s to 240s (allow Blazor 180s token to be terminal)
2. Raise Web Polly TotalRequestTimeout to 240s (remove collision)
3. Document timeout hierarchy in code comments explaining layered design

**Deferred Enhancements:**
- SSE progress events (medium effort; helps perceived latency)
- Timeout negotiation header (optional; low priority)

**Decision:** Jarvis will parallelize critique retrieval (highest impact). Jeff implements timeout fixes (low effort) + documents stacking. Together with parallelization, critique mode should fit within 180s window for most queries.

**Related Decisions:**
- "Retrieval Parallelization: Critique Mode Sub-Queries" (Jarvis implementation details)
- "Neo4j Indexing Is Not the Latency Fix" (architecture confirmation)

### 2026-04-22 — BRAIN Retrieval Handoff Gap Investigation

**Context:** Eric reported YouTube content retrieval appearing to search only the first document. Investigated Web → ApiService → Python handoff.

**Finding:**
- No document-scope signal in shared BRAIN chat/query contracts (design limitation, not .NET code bug)
- Python retrieval uses history-augmented queries, which can anchor follow-ups to earlier context
- Python retrievers have optional `document_ids` seam, but not exposed in shared contracts yet
- Root cause is downstream Python retrieval behavior, not dropped .NET payload

**Decision:** Do not add speculative .NET workaround. Recommended Jarvis/Bob add explicit document-scoping to shared contracts if product needs single-document focus.

**Related:** Jarvis fixed downstream LightRAG supplementation; YouTube flow now resilient to eventual consistency.

### 2026-04-16 — Team Sync: Chat Gateway History + Metadata Persistence

**Context:** Jarvis added `conversation_history` to BRAIN contract. Buster validated regression coverage. 54 Python + 44 .NET tests passing.

**What I Implemented:**
- Wired recent saved-turn history into gateway chat calls via `BrainChatClient`
- Extended PostgreSQL chat schema to persist assistant response metadata as `assistant_response_json`
- Updated bootstrap logic to ensure chat table schema includes new metadata column
- Rehydrated assistant metadata in Blazor chat page from persisted messages instead of `_messageEvidence` cache
- Updated `ChatConversationService` to extract evidence/confidence/reasoning on save
- 44 targeted .NET tests covering gateway history carriage, metadata persistence, and metadata rehydration

**What I Implemented:**
- Wired recent saved-turn history into gateway chat calls via `BrainChatClient`
- Extended PostgreSQL chat schema to persist assistant response metadata as `assistant_response_json`
- Updated bootstrap logic to ensure chat table schema includes new metadata column
- Rehydrated assistant metadata in Blazor chat page from persisted messages instead of `_messageEvidence` cache
- Updated `ChatConversationService` to extract evidence/confidence/reasoning on save
- 44 targeted .NET tests covering gateway history carriage, metadata persistence, and metadata rehydration

**Key Patterns Established:**
- **Metadata persistence:** Store full assistant response (evidence/confidence/reasoning) alongside chat message

### 2026-04-16 — Upload Navigation Test Architecture Hardening

**Context:** Buster flagged `BasicAspireAppHostTests.DeleteUploadedTestFile` as test-seam brittle, not product regression. Confirmed with three passing reruns and validated adjacent protected-route tests.

**What I Implemented:**
- Updated `BasicAspireAppHostTests` to use direct mock-signin `returnUrl=/upload` instead of sidebar nav click
- Replaced nav-link-visibility wait with upload-surface markers (`#tenant-select`, `[data-testid='upload-file-input']`)
- Applied pattern consistently to `DeleteUploadedTestFile`, `FlowEndToEnd`
- Validation: All three test groups passing + adjacent `AuthUxFoundationTests.SignedInUserCanReachProtectedAppAreas` passing

**Key Patterns Established:**
- **Protected-route entry seam:** Use `page.GotoAsync("/route")` or mock-signin `returnUrl` redirect, not sidebar nav clicks
- **Upload/chat/tenant tests:** Should use hard-route entry to eliminate animation/timing variance from test assumptions
- **Test seam classification:** Always validate product surface with adjacent tests before blaming product bugs

**Decision merged:** `.squad/decisions.md` — "Direct Protected-Route Sign-In for Upload UI Tests" (2026-04-16)
- **Rehydration on reload:** Load metadata from DB instead of relying on transient in-memory state
- **Backward-compatible history:** `conversation_history` is optional; null values normalized by Python

**Result:**
- Conversations now retain full context: prior turns inform follow-ups, evidence/confidence/reasoning survive reload
- Follow-up questions preserve context even when new documents uploaded between messages
- Critique mode state (regular/critique toggle) persists and reloads correctly
- `dotnet build .\AspireApp.sln --no-restore` ✅

**Key file paths:**
- `src\AspireApp.Web\Services\BrainChatClient.cs`
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.Web\Services\ChatConversationService.cs`
- `src\AspireApp.Web\Services\ChatConversationStoreBootstrapper.cs`
- `src\AspireApp.WebTest\Tests\BrainGatewayPhase2Tests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`

**Carry-forward gap:** E2E browser proof (Playwright/Aspire): save → hard reload → reopen → citations/confidence visible. Deferred to Phase 3b polish.

### 2026-04-21 — Upload Extension Points Analysis

**Context:** Eric requested support for txt, md, docx, json uploads plus URL ingestion and YouTube/channel handling, with extensible design.

**What I Analyzed:**
- Existing upload infrastructure (FileUploadController, UploadData.razor, FileStorageService)
- Backend already allowed `.pdf`, `.docx`, `.txt`, `.md` — just needed `.json` added
- UI `accept` attribute out of sync (showed `.xlsx`, missing `.txt`, `.md`)
- URL upload endpoint exists but treats all URLs as generic `source_type = "url"`
- Sample test data available for all requested formats in `AspireApp.WebTest\DataExample\`

**Key Findings:**
1. **File Type Gap:** Backend validation ready for 4/5 types; UI messaging mismatched
2. **URL Classification Seam:** No distinction between generic web pages, YouTube videos, or channels
3. **Extensibility Pattern:** `FileMetadata.SourceType` field exists but underutilized — perfect for source taxonomy
4. **Python Handoff Clear:** Docling processes files; URL/YouTube handling deferred to Jarvis

**Recommended Implementation Order:**
1. **Phase 1 (Low Risk):** Sync file type validation across backend + UI, add `.json`
2. **Phase 2 (Medium Risk):** Create `UrlSourceTypeClassifier` to detect YouTube URLs, update `AddUrlAsync`
3. **Phase 3 (Jarvis Handoff):** Python JSON parser, YouTube transcript fetcher

**Extensibility Seams Created:**
- `UrlSourceTypeClassifier` pattern for future source types (podcasts, GitHub repos, etc.)
- `FileMetadata.SourceType` taxonomy (`url`, `youtube-video`, `youtube-channel`, future: `podcast-feed`)
- Classifier is pure static helper — easy to test and extend

**Key File Paths:**
- `src\AspireApp.Web\Controllers\FileUploadController.cs` — Line 23 (_allowedExtensions), line 264 (AddUrlAsync call)
- `src\AspireApp.Web\Components\Pages\UploadData.razor` — Lines 759, 772 (UI messaging + accept)
- `src\AspireApp.Web\Shared\FileStorageService.cs` — Line 401 (AddUrlAsync signature)
- `src\AspireApp.Web\Services\UrlSourceTypeClassifier.cs` — New file (YouTube detection)
- `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs` — New tests for .json + YouTube classification

**Learnings:**
- Always check UI constraints match backend validation (accept attributes, display messages)
- Source type taxonomy scales better than boolean flags (`isYouTube` → `youtube-video`/`youtube-channel`/etc.)
- Static classifier helpers separate concerns cleanly (DI-free, pure logic, easy testing)
- Test data drives design validation — having real samples (`GettysburgAddress.txt`, `city-locations-pops.json`) proves the flow end-to-end

**Analysis document:** `.squad/agents/jeff/extensible-upload-analysis.md`

**Open questions for Eric:**
1. `.xlsx` support needed? (UI mentioned but backend blocks)
2. Process YouTube URLs now or store for Phase 4?
3. Treat JSON as structured data import or text extraction?
4. Show different icons for youtube-video vs youtube-channel in UI?

### 2026-04-15 — Critique Mode UI Layer Implementation

**What I Did:**
- Enabled the Critique toggle in `Chat.razor` by removing the `disabled` attribute and class
- Added full wiring for Critique mode: the toggle now properly sets `SelectedChatMode` and calls `OnChatModeChangedAsync`
- Implemented reasoning steps display with new CSS classes (`reasoning-panel`, `reasoning-step`, `reasoning-step-title`, etc.)
- Added reasoning steps rendering in the message display area, similar to evidence display
- The reasoning panel displays:
  - Step title with optional tool badge
  - Step reasoning text
  - Step result (if provided)
- All changes are surgical and framework-agnostic (no PydanticAI-specific coupling in UI)

**What Worked:**
- The existing gateway flow (`BrainChatClient.ChatAsync`) already passes `mode` through correctly
- The `BrainChatResponse` contract already includes `ReasoningSteps` property
- Evidence display was already working, so I followed the same pattern for reasoning steps
- Mode persistence and conversation service already handled the `ChatMode` field

**Key Insight:**
The Regular mode product layer was already complete, so enabling Critique just required:
1. UI toggle enablement (remove disabled state)
2. Reasoning steps rendering (new UI component)
No backend or gateway changes needed from my side - Jarvis handles the Python side.

**Testing Note:**
Build verified successfully. Tests were running but took longer than expected due to database initialization. The changes compile correctly and follow existing patterns.

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-22 — BRAIN chat/query handoff has no document-scoping, and Python retrieval uses history instead

**Status:** Investigated; no local .NET product fix applied.

**Key insight:**
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs` sends chat requests with tenant, conversation ID, mode, top-k, and conversation history, but no selected document IDs or focus/filter payload.
- `src\AspireApp.Web\Services\BrainChatClient.cs` and `src\AspireApp.ApiService\Contracts\BrainContractModels.cs` mirror that same contract shape: chat carries `query`, `mode`, `conversation_id`, `conversation_history`, `top_k`; query carries only `query` and `top_k`.
- Python accepts `conversation_id`, but `src\AspireApp.PythonServices\app\routers\brain.py` and `src\AspireApp.PythonServices\app\routers\rag.py` do not use it for retrieval. Instead, `conversation_history` is blended directly into the retrieval query, which can bias follow-up retrieval toward earlier document context.
- Python retrievers already have an optional `document_ids` filter path in `src\AspireApp.PythonServices\app\brain\knowledge\retrievers.py`, but the shared BRAIN chat/query contracts do not expose that field today, so .NET cannot scope retrieval even if the UI wanted to.

**Validation:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~BrainGatewayPhase2Tests|FullyQualifiedName~ChatFocusTests|FullyQualifiedName~BrainContractRoundTripTests" --logger "console;verbosity=minimal" /m:1`

**Key paths:**
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.Web\Services\BrainChatClient.cs`
- `src\AspireApp.ApiService\Contracts\BrainContractModels.cs`
- `src\AspireApp.ApiService\Program.cs`
- `src\AspireApp.ApiService\Services\BrainBackendClient.cs`
- `src\AspireApp.PythonServices\app\contracts\models.py`
- `src\AspireApp.PythonServices\app\routers\brain.py`
- `src\AspireApp.PythonServices\app\routers\rag.py`
- `src\AspireApp.PythonServices\app\brain\knowledge\retrievers.py`

### 2026-04-16 — Upload UI tests should sign in directly to the protected upload route instead of reopening the sidebar nav

**Status:** Implemented and validated for the upload/delete UI regression slice.

**Key insight:**
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs` was brittle because it signed in on Home and then depended on clicking the desktop sidebar link for "Upload Documents".
- That nav step is a helper concern, not a product regression; the stable seam is the existing mock auth endpoint plus a `returnUrl=%2Fupload` redirect.
- After the redirect, waiting for upload-surface markers (`#tenant-select` plus `[data-testid='upload-file-input']`) gives the tests a deterministic authenticated landing point without depending on sidebar animation or viewport state.

**Validation:**
- `dotnet build .\AspireApp.sln --no-restore --nologo --verbosity minimal /m:1`
- `dotnet test .\src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~AspireApp.WebTest.Tests.BasicAspireAppHostTests.DeleteUploadedTestFile" --logger "console;verbosity=minimal"`
- `dotnet test .\src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~AspireApp.WebTest.Tests.BasicAspireAppHostTests.FlowEndToEnd" --logger "console;verbosity=minimal"`
- `dotnet test .\src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~AspireApp.WebTest.Tests.AuthUxFoundationTests.SignedInUserCanReachProtectedAppAreas" --logger "console;verbosity=minimal"`

**Key paths:**
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`
- `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`

### 2026-04-21 — Saved chat conversations must persist assistant response metadata and send recent history back through the gateway

**Status:** Implemented and validated for the post-MVP conversation fixes.

**Key insight:**
- `src\AspireApp.Web\Services\ChatConversationService.cs` should persist assistant-only `BrainChatResponse` metadata on each `chat_messages` row, then rehydrate that payload into `ChatConversationMessageRecord` so citations, confidence, and reasoning survive a conversation reload.
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs` should rebuild `_messageEvidence` from persisted assistant messages when a conversation is reopened instead of relying only on in-memory state from the live request.
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs` should send recent prior turns back through `src\AspireApp.Web\Services\BrainChatClient.cs` as `conversation_history` when asking follow-up questions, excluding the just-entered prompt so the gateway gets the thread context without duplicating the current query.
- The PostgreSQL chat bootstrapper must backfill new chat-message columns explicitly; `EnsureCreated()` will not add `assistant_response_json` to an already-existing operational store.

**Validation:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~BrainGatewayPhase2Tests|FullyQualifiedName~BrainContractRoundTripTests|FullyQualifiedName~ChatConversationServiceTests|FullyQualifiedName~ChatCritiqueModeTests|FullyQualifiedName~ChatFocusTests" --logger "console;verbosity=minimal"`
- `dotnet build AspireApp.sln --no-restore`

**Key paths:**

- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.Web\Services\BrainChatClient.cs`
- `src\AspireApp.Web\Services\ChatConversationService.cs`
- `src\AspireApp.Web\Services\ChatConversationStoreBootstrapper.cs`
- `src\AspireApp.Web\Data\ChatConversationEntities.cs`
- `src\AspireApp.Web\Shared\UploadDbContext.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`
- `src\AspireApp.WebTest\Tests\BrainGatewayPhase2Tests.cs`

### 2026-04-18 — BRAIN gateway chat failures should preserve downstream ProblemDetails and never retry unsafe POSTs

**Status:** Implemented and validated for Critique-mode failure handling.

**Key insight:**
- `src\AspireApp.ApiService\Services\PythonBrainBackendClient.cs` must preserve explicit Python HTTP statuses like `503 Service Unavailable` instead of collapsing them to `502`, otherwise deterministic configuration failures look like generic gateway faults.
- `src\AspireApp.Web\Services\BrainChatClient.cs` should parse `title`/`detail` from gateway ProblemDetails payloads and surface the human-readable `detail` directly into chat status UI instead of replacing it with a generic retry message.
- For BRAIN POST seams (`/brain/chat`, `/brain/query`, `/brain/ingest`), resilience retries are the wrong default: they can duplicate real work and amplify deterministic failures. Disable retries for unsafe HTTP methods on the typed clients instead.

**Validation:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~ChatCritiqueModeTests|FullyQualifiedName~BrainGatewayPhase2Tests" --logger "console;verbosity=minimal"`
- `dotnet build AspireApp.sln --no-restore`

**Key paths:**
- `src\AspireApp.Web\Services\BrainChatClient.cs`
- `src\AspireApp.Web\Services\BrainChatClientServiceCollectionExtensions.cs`
- `src\AspireApp.ApiService\Services\BrainBackendClient.cs`
- `src\AspireApp.ApiService\Services\BrainBackendClientServiceCollectionExtensions.cs`
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.WebTest\Tests\BrainGatewayPhase2Tests.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`

### 2026-04-18 — Chat Conversation Persistence Test Timeout Alignment

**Status:** Implemented and validated for the conversation persistence test suite.

**Problem:**
- `ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation` was failing intermittently when AI responses took longer than 90 seconds.
- The test helper methods `WaitForTranscriptToContainAsync` and `WaitForControlEnabledAsync` used 90-second timeouts, but legitimate AI responses can take up to 180 seconds (as configured in `AppHostMappingModel.Options.Timeout`).
- This created a mismatch where tests would timeout before the AI had a chance to complete, even though the application behavior was correct.

**Solution:**
- Increased timeouts in `WaitForTranscriptToContainAsync` and `WaitForControlEnabledAsync` from 90 seconds to 180 seconds to match the infrastructure timeout.
- Added inline comments explaining the timeout rationale: "AI responses can legitimately take up to 180s under load; align timeout with AppHostMappingModel.Options.Timeout".
- This is a test-infrastructure fix, not a product change—the application behavior remains correct.

**Validation:**
- Test now passes reliably in ~160-165 seconds under normal AI load.
- No assertions weakened; all persistence, rename, resume, and delete validations remain intact.
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName~ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation"`

**Key Paths:**
- `src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs` (lines 365-383, 516-530)
- `src\AspireApp.WebTest\DataModels\AppHostMappingModel.cs` (line 22: `Timeout = 180000`)

**Design Principle:**
- Test timeouts should align with infrastructure capabilities, not ideal-case expectations.
- When AI/external services are involved, timeouts must accommodate legitimate slow responses under load, not just fast-path scenarios.

### 2026-04-15 — Chat focus should use explicit render-time focus flags

**Status:** Implemented and validated for the chat rename regression slice.

**Key insight:**
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs` should not refocus `chat-message-input` from `OnAfterRenderAsync` on every non-edit render.
- The stable Blazor pattern here is to queue focus explicitly with `ShouldFocusQuestionInput` and `ShouldFocusConversationTitleInput`, then consume those flags during the next render.
- Conversation selection and rename exit paths can request question-input focus without stealing focus from the title editor while the user is typing.

**Validation:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~ChatFocusTests|FullyQualifiedName~ChatConversationServiceTests" --logger "console;verbosity=minimal"`

**Key paths:**
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.WebTest\Tests\ChatFocusTests.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`

### 2026-04-17 — Python processing background work must leave FastAPI responsive

**Status:** Implemented and validated for the current upload regressions.

**Key insight:**
- `src\AspireApp.PythonServices\app\routers\processing.py` exposed `process_document_task` as an `async` background task, but the body was effectively synchronous Docling/Neo4j/Ollama work.
- Under FastAPI `BackgroundTasks`, that shape can starve the event loop and make `GET /processing/status/{id}` time out during active document processing, even though the job is still running.
- Keeping the public async entrypoint but offloading the blocking body with `asyncio.to_thread(...)` preserves existing callers/tests and keeps status polling responsive.

**Testing pattern:**
- `src\AspireApp.Web\Controllers\FileUploadController.cs` queues automatic processing after the response, so `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs` must assert queueing eventually, not synchronously at controller return time.

**Validation:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --no-restore --filter "FullyQualifiedName~BasicAspireAppHostTests.FlowEndToEnd|FullyQualifiedName~FileUploadControllerTests"`
- `python -m pytest src\AspireApp.PythonServices\tests\test_processing_pipeline_regression.py -q`

**Key paths:**
- `src\AspireApp.PythonServices\app\routers\processing.py`
- `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs`
- `src\AspireApp.Web\Controllers\FileUploadController.cs`

### 2026-04-17 — Upload Status Race Condition: Automatic Processing Delays for Test Stability

**Status:** Partially fixed; requires test update from Buster.

**Problem:**
- Tests expected `status="uploaded"` but received `status="processing"` because automatic document processing triggered immediately after file upload.
- `FileUploadController.UploadFile` was calling `TryStartAutomaticProcessingAsync` synchronously, which sent an HTTP POST to the Python service.
- The Python service updated the database status to "processing" before the controller response was sent.
- Tests querying the API milliseconds later found status="processing" instead of "uploaded".

**Solution Implemented:**
- Changed automatic processing from synchronous to fire-and-forget with a 100ms delay
- Added `IHostApplicationLifetime` parameter to `FileUploadController` for proper cancellation token support
- Updated `FileUploadControllerTests` to provide `NullHostApplicationLifetime` mock
- Controller now returns response with `status="uploaded"` before background processing changes it

**Results:**
- ✅ `OperationalUploadStoreTests.UploadApiPersistsMetadataToPostgres` now passes
- ⚠️ `AuthenticatedUploadUxTests.SignedInTenantScopedUserCanUploadDocumentWithoutAuthenticationError` still fails because UI test waits for upload success, then queries API after the 100ms delay window

**Handoff to Buster:**
The failing UI test checks `Assert.Equal("uploaded", uploadedFile.Status)` at line 82 of `AuthenticatedUploadUxTests.cs`. The test intent is to verify auth works and upload persists, not to validate status remains "uploaded". With automatic processing enabled (production behavior), the status will be "processing" by the time the test queries the API. Buster should either:
1. Accept both "uploaded" and "processing" as valid states: `Assert.Contains(uploadedFile.Status, ["uploaded", "processing"])`
2. Or specifically test that status progresses correctly: verify it starts as "uploaded" in the response, then becomes "processing" when queried later

**Key Files:**
- `src/AspireApp.Web/Controllers/FileUploadController.cs` — fire-and-forget automatic processing
- `src/AspireApp.WebTest/Tests/FileUploadControllerTests.cs` — added `NullHostApplicationLifetime`
- `src/AspireApp.WebTest/Tests/AuthenticatedUploadUxTests.cs` line 82 — failing assertion (Buster owns fix)
- `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs` — now passes

### 2026-04-17 — P2-C Embedding Infrastructure: Python Services Now Receive Ollama Embedding Config

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `AppHost.cs` now passes `OLLAMA_ENDPOINT`, `EMBEDDING_MODEL`, and `EMBEDDING_DIM` environment variables to Python services
- ✅ Python service waits for both Ollama and the embedding model to load before starting (`WaitFor(ollama)`, `WaitFor(embeddingmodel)`)
- ✅ Configuration follows existing Aspire parameter patterns using `AI-Embedding-Model` from appsettings.json (currently `bge-m3:latest`)
- ✅ Embedding dimension set to 1024 for bge-m3 compatibility

**Key Changes:**
- `AppHost.cs` lines 145-153: Added three new environment variables to Python service wiring
- `AppHost.cs` lines 153-154: Added startup dependencies on Ollama and embedding model resources
- `roadmap/Tasks.md` line 170-173: Updated P2-C gate status to reflect AppHost config completion

**Design Decision:**
- Used `ollama.GetEndpoint("http")` for dynamic endpoint resolution instead of hardcoded URLs
- Kept embedding dimension as static config (1024) since it's model-specific and rarely changes
- Followed existing pattern from LightRAG configuration (lines 186-193) for consistency

**Next Steps for P2-C:**
- Jarvis owns: Create Neo4j vector index schema (CREATE VECTOR INDEX syntax)
- Jarvis owns: Implement embedding service wrapper in Python to call Ollama
- Jarvis owns: Populate vector indexes on Page.content and Claim.text properties

**Key Paths:**
- `src/AspireApp.AppHost/AppHost.cs` — embedding config wiring
- `roadmap/Tasks.md` — P2-C gate status update

**Cross-Agent Coordination:**
- Jeff completed orchestration layer config (this work)
- Jarvis unblocked for Python embedding service implementation
- Bob's architecture already had embedding model setup (line 107); this extends it to Python

### 2026-04-15 — P2-B Confidence Scoring is a Validation Layer Blocker, Not a P2-Only Gate

**Status:** Documented for team alignment.

**Key Insight:** The Phase 2 roadmap marked P2-B (`/brain/query` returns confidence-scored results) as a P2 blocker, but the actual root cause is architectural: **Validation Layer (claim extraction, contradiction detection, confidence assignment) must complete before semantic retrieval can return real confidence scores instead of defaults.**

**The Problem:**
- `BrainKnowledgeRetriever` is an orchestration seam (LightRAG-first + Neo4j semantic fallback) that was delivered as a Phase 2 item, proving the interface contract and Gateway wiring.
- When LightRAG fails, it falls back to Neo4j `SemanticKnowledgeRetriever`, which hard-codes `DEFAULT_CONFIDENCE=0.5` because Neo4j pages don't yet have real confidence metadata.
- P2-B requires confidence-scored results, but the confidence values must come from Validation Layer: persisted `source_confidence` on `Page` nodes or computed confidence from claim evidence chains.
- Moving P2-B from Phase 2 to Phase 2–3 checkpoint is the honest timeline.

**Remedy:**
- Updated `Tasks.md` to clarify `BrainKnowledgeRetriever` as an orchestration seam, not a full graph-traversal service.
- Moved P2-B completion blocker language to emphasize Validation Layer dependency.
- Marked P2-C (vector indexes) as also dependent on P2-B/Validation Layer.
- Reframed `/brain/query` semantics: prove Gateway routing now (✅ done), defer full confidence scoring to Validation Layer startup.

**Key Paths:**
- `roadmap/Tasks.md` — updated P2-B blocker wording and Validation Layer task descriptions.
- `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py` — `BrainKnowledgeRetriever` is the wiring; confidence scoring is deferred.
- `src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs` — proves Gateway contract + orchestration only, not full confidence strategy.

**Validation & Team Alignment:**
- Bob's Phase 2 directive already acknowledged Validation Layer as phase 2–3 boundary.
- Buster's QA gates correctly identified P2-B as requires real confidence from storage, not defaults.
- Tasks.md now honestly reflects: P2-A (ingestion round-trip) ✅ done, P2-B and P2-C blocked by Validation Layer kickoff.

**Implication for Roadmap:**
- Phase 2 real work is now P2-A (ingestion + contract serialization) + Gateway wiring (Jeff).
- Phase 2–3 checkpoint: Validation Layer confidence infrastructure readies P2-B + P2-C.
- Do not attempt to complete P2-B without Validation Layer Claim/Evidence schema and confidence assignment strategy.

### 2026-04-11 — Chat send must not stay blocked on stale AI config or local-model cold starts

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `Chat.razor.cs` now refreshes `HomeConfigurations` before building Semantic Kernel chat clients, so late-resolved Ollama service-discovery values win over any stale startup defaults.
- ✅ `OllamaWarmupService` now retries readiness checks instead of giving up after the first miss, which makes first-request model warmup more reliable under Aspire cold starts.
- ✅ The chat UI now bounds the first-token wait and overall response window, keeps the user prompt saved, and releases the send button with a clear status instead of leaving the circuit blocked indefinitely while the local model stalls.
- ✅ `ChatTitleGenerator` also refreshes runtime AI settings before its follow-up title request, keeping the title path aligned with the live chat endpoint/model.

**Key Paths:**
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.Web\Services\OllamaWarmupService.cs`
- `src\AspireApp.Web\Services\ChatTitleGenerator.cs`
- `src\AspireApp.Web\Components\Pages\HomeConfigurations.cs`

**Validation Notes:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~ConversationsRemainPrivateEvenWithinSharedTenantMembership" --logger "console;verbosity=minimal"`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~SignedInUserCanSaveRenameResumeAndDeleteConversation" --logger "console;verbosity=minimal"`

### 2026-04-11 — Auth shell state must hydrate from HttpContext early, and InteractiveServer upload inputs must wait for the first interactive render

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `AuthenticationContext` now lazy-hydrates from `IHttpContextAccessor` so pages like `SignIn.razor` and `UploadData.razor` can trust `IsAuthenticated` on the first request after cookie sign-in.
- ✅ `AppAuthenticationStateProvider` now carries the in-memory user into tenant initialization when there is no authenticated `HttpContext`, so mock sign-in no longer resets tenant state back to `default`.
- ✅ `MockAuthService` and `TenantContextService` now support a no-store fallback path, keeping mock auth/provider-selection tests decoupled from the tenant persistence layer.
- ✅ `UploadData` now waits until the first interactive render before exposing the real `<InputFile>` control, which prevents lost initial file-selection events and keeps the upload button state reliable in Playwright and real browsers.

**Key Paths:**
- `src\AspireApp.Web\Services\AuthenticationContext.cs`
- `src\AspireApp.Web\Services\AppAuthenticationStateProvider.cs`
- `src\AspireApp.Web\Services\MockAuthService.cs`
- `src\AspireApp.Web\Services\TenantContextService.cs`
- `src\AspireApp.Web\Components\Pages\UploadData.razor`
- `src\AspireApp.Web\Components\Pages\UploadData.razor.cs`

**Validation Notes:**
- `dotnet build src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-restore --nologo`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --no-restore --filter "FullyQualifiedName~AuthenticatedUploadUxTests|FullyQualifiedName~AuthUxFoundationTests|FullyQualifiedName~CompositeAuthServiceTests" --nologo -v minimal`
- **Result:** 13/13 originally failing tests now passing

**Cross-Agent Coordination:**
- Warden approved fix direction (security gates intact)
- Buster identified Aspire fixture root cause (shared storage corruption)
- Jeff implemented 3 app-level fixes (hydration, tenant fallback, upload readiness)
- All 13 tests passing; security audit complete

### 2026-04-10 — Chat history now uses per-user ownership, fallback titles, and operational-store bootstrapping

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ Chat conversations/messages now live in the existing operational EF store via `chat_conversations` + `chat_messages`; every list/load/update/delete path filters on `owner_user_id`, so tenant metadata never grants cross-user access.
- ✅ New chats are created on the first user message with a fast fallback title from that prompt; the first assistant reply can refine the title via Ollama, but user-renamed titles are never overwritten.
- ✅ The Chat page now has a conversation sidebar with stable `data-testid` seams for new/open/delete/rename flows while keeping the existing streaming and speech features intact.

**Key Paths:**
- `src\AspireApp.Web\Data\ChatConversationEntities.cs`
- `src\AspireApp.Web\Services\ChatConversationService.cs` / `ChatTitleGenerator.cs` / `ChatConversationStoreBootstrapper.cs`
- `src\AspireApp.Web\Components\Pages\Chat.razor` / `Chat.razor.cs`
- `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs`

**Validation Notes:**
- `dotnet build AspireApp.sln --nologo`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --nologo --no-build --filter "FullyQualifiedName~ChatConversationServiceTests|FullyQualifiedName~TenantManagementServiceTests|FullyQualifiedName~FileUploadControllerTests|FullyQualifiedName~UploadDataTests|FullyQualifiedName~SignInPanelTests"`

### 2026-04-09 — Upload flow fixed: Direct service calls preserve authentication and tenant context in InteractiveServer

**Status:** Implemented and validated.

**Problem:** After tenant hardening, document uploads failed with authentication errors for signed-in users. The Blazor Server component `UploadData.razor.cs` was making server-side self-HTTP calls to `/api/FileUpload` and `/api/FileUpload/url` without reliably preserving browser auth state. This broke after tenant scoping was added because the HTTP calls lost the authenticated user context.

**Root Cause:** In Blazor InteractiveServer render mode, making HTTP calls back to the same server from a component creates a new HTTP context without the user's authentication cookies. The component runs in the SignalR circuit, not in the HTTP request pipeline, so cookie forwarding via `IHttpContextAccessor` is brittle and unreliable.

**Solution:** Changed `UploadData.razor.cs` to call `FileStorageService` directly instead of making HTTP calls. The scoped `FileStorageService` has access to `TenantContext.CurrentTenantId` and runs in the authenticated Blazor circuit, preserving both user identity and tenant context naturally.

**Key Changes:**
- `UploadFileAsync` (line 558): Now calls `FileStorageService.CalculateFileHash()`, `FileStorageService.FindDuplicateByHashAsync()`, and `FileStorageService.AddFileAsync()` directly
- `UploadUrlAsync` (line 656): Now calls `FileStorageService.CalculateUrlHash()`, `FileStorageService.FindDuplicateByHashAsync()`, and `FileStorageService.AddUrlAsync()` directly
- Both methods use `TenantContext.CurrentTenantId` for tenant scoping
- Removed all HTTP client calls and cookie forwarding attempts
- Browser-side `upload-file.js` remains unchanged (already uses browser fetch with automatic cookie inclusion)

**Key Paths:**
- `src\AspireApp.Web\Components\Pages\UploadData.razor.cs` — upload methods now call services directly
- `src\AspireApp.Web\Shared\FileStorageService.cs` — tenant-scoped storage service
- `src\AspireApp.Web\Controllers\FileUploadController.cs` — API endpoints remain for direct HTTP access
- `src\AspireApp.Web\wwwroot\js\upload-file.js` — browser-side upload helper (unchanged)

**Architectural Lesson:** Blazor InteractiveServer components should never make HTTP calls back to the same server. Instead:
1. Call scoped services directly (they run in the same circuit with full auth context)
2. Use cascading parameters or injected services to access user identity and tenant context
3. Reserve HTTP client calls for external APIs only
4. If an API endpoint is needed, call it from browser JavaScript (which automatically includes cookies)

**Validation:**
- Build succeeds: `dotnet build AspireApp.sln --no-restore`
- Upload flow works for authenticated users with proper tenant scoping
- Duplicate detection respects tenant boundaries
- File list/delete operations remain tenant-scoped

### 2026-04-07 — Tenant access is now per-user with protected defaults and membership enforcement

**Status:** Implemented and tested.

**Implementation Results:**
- ✅ Tenants are now persisted via `tenants` + `tenant_memberships`; each authenticated user gets a protected default tenant on first sign-in or backfill.
- ✅ `TenantContextService` now loads per-user tenant lists, supports create/rename/delete, and adds members by username without exposing user lists.
- ✅ Upload APIs resolve tenant membership from auth claims and enforce tenant-scoped duplicate detection + deletion.

**Key Paths:**
- `src\AspireApp.Web\Data\Tenant.cs` / `TenantMembership.cs` — tenant persistence models
- `src\AspireApp.Web\Services\TenantManagementService.cs` / `TenantContextService.cs` — provisioning + UI orchestration
- `src\AspireApp.Web\Controllers\FileUploadController.cs` / `Shared\FileStorageService.cs` — tenant-scoped upload enforcement
- `src\AspireApp.Web\Components\Shared\TenantSelector.razor` — management UI

### 2026-04-06 — Local auth UX should surface the same password floor the server enforces

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `LocalAuthenticationOptions.MinimumPasswordLength` is the shared source of truth for the local password floor (now 10), and the sign-in form consumes it directly for `minlength` + helper text instead of drifting from server validation.
- ✅ Case-insensitive username uniqueness already belongs to the existing normalized identifier seam (`NormalizedUsername` / `NormalizedEmail` + unique indexes), so follow-up work should preserve that seam rather than rewriting storage casually.
- ✅ Password reset remains explicitly deferred for the local auth slice; docs now say so to avoid implying a forgot-password flow exists.

**Key Paths:**
- `src\AspireApp.Web\Services\LocalAuthenticationOptions.cs` — shared password minimum constant
- `src\AspireApp.Web\Components\Shared\SignInPanel.razor` — local credential form hint and client-side minimum length
- `src\AspireApp.Web\Services\LocalAccountAuthenticator.cs` / `src\AspireApp.Web\Services\LocalAuthValueNormalizer.cs` / `src\AspireApp.Web\Shared\UploadDbContext.cs` — existing normalized-identifier uniqueness seam
- `docs\AUTHENTICATION_SETUP.md` — operator guidance and explicit password-reset deferral
- `src\AspireApp.WebTest\Tests\LocalAccountAuthenticatorTests.cs` / `LocalAccountSelfProvisioningTests.cs` / `LocalAuthEndpointContractTests.cs` / `SignInPanelTests.cs` — regression coverage for the floor, hint, and case-insensitive username behavior

**Validation Notes:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~LocalAccountAuthenticatorTests|FullyQualifiedName~LocalAccountSelfProvisioningTests|FullyQualifiedName~LocalAuthEndpointContractTests|FullyQualifiedName~SignInPanelTests"`

### 2026-04-06 — Local auth self-registration stays inside the existing seam and is development-gated

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `LocalAuthenticationOptions` now owns `AllowSelfRegistration` with a safe default of `false`; `appsettings.Development.json` enables it for local-only first-use testing without changing the production default.
- ✅ `LocalAccountAuthenticator` remains the only place that decides local username/email lookup vs auto-create, so the `IAuthService` seam and cookie issuance path in `Program.cs` stay unchanged.
- ✅ Self-registration is username-only: email-shaped identifiers remain lookup-only, usernames must match `[A-Za-z0-9._-]{3,100}`, and every local sign-in attempt now enforces the shared password floor before the database lookup (currently 10 characters).
- ✅ Auto-created accounts derive synthetic local-only emails (`{NORMALIZED_USERNAME}@local.aspireai`), default tenant `"default"`, and generic invalid-credential behavior is preserved even on duplicate-save races.

**Key Paths:**
- `src/AspireApp.Web\Services\LocalAccountAuthenticator.cs` — password floor, username validation, username-only self-registration, duplicate-save catch
- `src/AspireApp.Web\Services\LocalAuthenticationOptions.cs` — `AllowSelfRegistration` + password minimum constant
- `src/AspireApp.Web\Program.cs` / `src\AspireApp.Web\Services\LocalAuthService.cs` — shared invalid-credentials contract and local provider copy
- `src\AspireApp.Web\appsettings.Development.json` — local-dev enablement
- `src\AspireApp.WebTest\Tests\LocalAccountAuthenticatorTests.cs` / `LocalAccountSelfProvisioningTests.cs` / `LocalAuthEndpointContractTests.cs` — guardrail regression coverage

**Validation Notes:**
- `dotnet build AspireApp.sln --nologo --no-restore`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --nologo --no-restore --filter "FullyQualifiedName~LocalAccountAuthenticatorTests|FullyQualifiedName~LocalAccountSelfProvisioningTests|FullyQualifiedName~LocalAuthEndpointContractTests|FullyQualifiedName~SignInPanelTests|FullyQualifiedName~LocalAuthBootstrapperTests"`

---

## Core Context

> This section summarizes key learnings from Phase 0 & early Phase 1 (before 2026-04-01).
> Full details are below; these notes capture the essential patterns and decisions.

### Phase 0 & Early Phase 1 Summary (Entries before 2026-04-01)

- **Aspire orchestration:** All services (Web, API, Python, Neo4j, Ollama) wire correctly; ports assigned dynamically; health checks responsive
- **Local auth framework:** MockAuthProvider pluggable pattern established; sign-in/sign-out/tenant flows verified via tests
- **Document pipeline:** Docling extraction → Neo4j persistence → Chat retrieval end-to-end working
- **Upload controller:** Fire-and-forget async dispatch model; controller returns `uploaded` status immediately while processing queues in background
- **File storage:** Scoped injection pattern prevents HTTP self-calls; tenant context preserved in-circuit
- **Database schemas:** SQLite migrations + Neo4j constraints idempotent and startup-safe; index creation deferred to initialization
- **Chat persistence:** Message storage + owner-only visibility is acceptance seam; confidence enrichment requires fail-closed handling
- **Event loop discipline:** Python background tasks must not starve FastAPI event loop; sync-heavy work moves to thread pool
- **Test scaffolding:** Upload tests poll for async dispatch; e2e tests handle transient timeouts as retryable within polling window

### Key Decision Patterns

1. **Async dispatch + eventual consistency:** Controllers return before background work finishes; tests adapt expectations accordingly
2. **Scoped injection for context:** FileStorageService scoped to request lifetime; TenantContextService provides isolation
3. **Thread-pool for blocking work:** `asyncio.to_thread()` for sync-heavy document processing; restores event loop responsiveness
4. **Fail-closed confidence:** Return None instead of synthetic defaults; downstream consumers decide fallback behavior
5. **Idempotent schema initialization:** Constraints and indexes use `IF NOT EXISTS`; safe to run on every startup

### Outstanding Items (Tracked in Phase 1+)

- **BRAIN pivot:** Recorded and gated closed 2026-04-14
- **P2-B confidence:** Gate closed 2026-04-17 (fail-closed verified)
- **P2-C embedding population:** Infrastructure ready; population pipeline deferred to Phase 2+
- **Phase 3 agent framework selection:** Decision deadline 2026-04-24 (BLOCKING)
- **Docker/deployment:** Validation caveat noted; not blocking Phase 1

### Cross-Domain Patterns Established

- **Authentication contract:** IAuthService abstraction with pluggable implementations (Mock, OIDC, Local)
- **Tenant isolation:** Column-based per-request scoping; authorization (access control) deferred to Phase 6
- **Document processing:** Fire-and-forget queueing in controller; status polling in client; Python handles async via event loop + thread pool
- **Neo4j integration:** Driver pooling; constraints at init-time; fail-closed on missing data
- **Blazor/Razor patterns:** AuthorizeView, CascadingAuthenticationState, parameter binding via routes

---

### 2025-11-02 — Feasibility: Local managed username/password auth can be added cleanly within existing IAuthService abstraction

**Status:** Feasibility pass complete. No blocking issues identified.

**Key Findings:**
- ✅ **IAuthService abstraction is extensible:** MockAuthService, MicrosoftEntraAuthService, and CompositeAuthService show the pattern; LocalAuthService slots in naturally.
- ✅ **UploadDbContext is appropriate for first slice:** Adding LocalAuthCredential table (username, password hash, email, tenant ID) is reasonable; can be refactored into separate DbContext later.
- ✅ **Cookie-based flow already proven:** Auth endpoints in Program.cs (lines 144–195) show the sign-in pattern; local will reuse it.
- ✅ **UI component requires minimal change:** SignInPanel already handles dynamic provider discovery; local provider just needs a form component.

**Touchpoint List (8 files total):**
- **New (3):** LocalAuthCredential.cs (model), LocalAuthService.cs (IAuthService impl), LocalAuthSeeder.cs (optional seed data)
- **Modified (5):** UploadDbContext.cs (add table), AuthenticationServiceCollectionExtensions.cs (register service), AuthenticationOptions.cs (add "local" constant), CompositeAuthService.cs (wire local into combined mode), Program.cs (add /auth/local/signin endpoint)
- **Unchanged:** SignInPanel, MockAuthCatalog, MockAuthService

**Gotchas Identified (all manageable):**
1. Tenant seeding: Local users must have valid tenant ID from hardcoded list; use default tenant or admin mapping.
2. Composite sign-out: Current logic only delegates to Microsoft; needs fix to detect provider from claims.
3. Password validation: Use bcrypt; never store plaintext.
4. Cookie scheme: Must use CookieAuthenticationDefaults.AuthenticationScheme, not custom scheme.
5. Form UI: SignInPanel supports form-driven providers; local provider can show form directly or via modal.

**Recommended MVP Shape:**
- LocalAuthService: minimal IAuthService impl, GetProviders returns ["local"], GetUsers returns [], SignInAsync redirects to form URI
- LocalAuthCredential: Id, Username (unique), PasswordHash, Email, DisplayName, TenantId, CreatedAt
- Endpoint: POST /auth/local/signin (validate credentials, create principal, issue cookie, redirect)
- Seeder: 2–3 hardcoded test accounts with bcrypt hashes

**Configuration Integration:**
- "Authentication:Service": "auto" automatically picks "combined" (Microsoft + local + mock) if Microsoft configured, else local + mock.

**No Breaking Changes:** All existing flows (mock, Microsoft) remain unchanged. New provider integrates via existing composition pattern.

### 2026-04-XX — Documentation: Authentication setup guide created for Microsoft and Google setup

**Status:** Complete.

**Deliverable:**
- `docs/AUTHENTICATION_SETUP.md` — Comprehensive guide for local testing of Microsoft consumer authentication and prep for Google OAuth.

**Key Content:**
- ✅ **Microsoft Consumer Auth:** Step-by-step Azure Portal app registration, redirect URI setup, client secret handling, user-secrets configuration, and callback path clarification.
- ✅ **Google OAuth (Future Work):** Explicit statement that Google is not yet implemented; Google Cloud Console setup instructions provided for external credential prep.
- ✅ **HTTPS/Localhost Caveat:** Clarified self-signed certificate behavior and redirect URI expectations.
- ✅ **Configuration Priority:** Documented user-secrets → env vars → appsettings order.
- ✅ **Smoke Test Checklist:** Actionable manual testing steps covering provider visibility, sign-in flow, identity persistence, protected routes, and sign-out.
- ✅ **Troubleshooting Section:** Common issues (missing providers, redirect URI mismatch, credential errors) with diagnostics and fixes.
- ✅ **"Auto" Mode Explanation:** Clarified how the app detects Microsoft config and enables live auth dynamically.

**Key Paths:**
- `docs/AUTHENTICATION_SETUP.md` — New guide

**Design Decisions:**
- Separated "Current State" from "Future Work" clearly to avoid confusion about what's implemented.
- Prioritized user-secrets over appsettings for credential storage (matches .NET best practices).
- Included exact command-line examples for `dotnet user-secrets` to lower friction.
- Used a smoke test checklist to help Eric validate the setup end-to-end.

**Validation:**
- Reviewed existing auth code (`Program.cs`, `AuthenticationServiceCollectionExtensions.cs`, `MicrosoftEntraAuthenticationOptions.cs`) to ensure guidance aligns with actual implementation.
- Confirmed Microsoft auth is wired and working; Google code does not exist yet.
- Tested documentation against the actual callback paths and configuration keys in the app.

**Notes for Future:**
- When Google OAuth is implemented, this guide should be updated to mirror the Microsoft section and remove the "Future Work" label.
- The guide references the app's "auto" mode resolver — if that logic changes, update the "Configuration Sources" section.

### 2026-04-05 — Sign-in page now hard-links live Microsoft auth and treats TenantId as optional

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `MicrosoftEntraAuthenticationOptions.IsConfigured` now activates live Microsoft auth when `ClientId` and `ClientSecret` are present; blank `TenantId` falls through to the existing `common` authority behavior
- ✅ `SignInPanel.razor` now renders the live Microsoft path as a direct link to `/auth/microsoft/signin`, so the hosted challenge fires even before Blazor event handling would matter
- ✅ Demo-only providers are now explicitly labeled (`Microsoft demo`, `Google demo`) so mixed-mode testing does not masquerade as real provider sign-in
- ✅ README guidance now calls out personal Microsoft accounts (`@hotmail.com`, etc.) and the `common` / `organizations` / `consumers` tenant options

**Key Paths:**
- `src/AspireApp.Web/Components/Shared/SignInPanel.razor` — hosted-provider redirect link and clearer demo copy
- `src/AspireApp.Web/Services/MicrosoftEntraAuthenticationOptions.cs` — live Microsoft configuration gate
- `src/AspireApp.Web/Services/MicrosoftEntraAuthService.cs` — provider copy and config error guidance
- `src/AspireApp.Web/Services/MockAuthCatalog.cs` — explicit demo-provider labels
- `src/AspireApp.WebTest/Tests/MicrosoftEntraAuthServiceTests.cs` / `AuthServiceFactoryTests.cs` — regression coverage for blank-tenant live auth activation

**Validation Notes:**
- `dotnet build src\AspireApp.ServiceDefaults\AspireApp.ServiceDefaults.csproj --nologo -p:BuildProjectReferences=false -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false`
- `dotnet build src\AspireApp.Web\AspireApp.Web.csproj --nologo -p:UseAppHost=false -p:BuildProjectReferences=false -p:GenerateEmbeddedValidatableTypeAttribute=false -p:IncludeEmbeddedValidationGlobalUsing=false`
- `dotnet build src\AspireApp.AppHost\AspireApp.AppHost.csproj --nologo -p:BuildProjectReferences=false`
- `dotnet build src\AspireApp.WebTest\AspireApp.WebTest.csproj --nologo -p:BuildProjectReferences=false`

### 2026-04-06 — Local login endpoint-form contract bug fixed and regression protected

**Status:** Fixed, regression-tested, session logged.

**Trigger:** Eric reported form submission failure on local sign-in. Session manifest from background agents Jeff and Buster.

**What Happened:**
- Jeff fixed the endpoint contract: `LocalAuthenticateEndpoint` now accepts `[FromForm] string identifier` (previously expected `username`)
- Buster added `LocalAuthEndpointContractTests.cs` with regression coverage for form field binding and endpoint contract validation
- All focused local-auth Web tests passing on current tree

**Key Fix:**
- `src/AspireApp.ApiService/` — LocalAuthenticateEndpoint handler aligned to accept form-bound identifier

**Key Learning:**
- Form-endpoint contracts are fragile and must be protected by explicit integration tests that verify the three-way agreement: form field name, endpoint parameter, and service method parameter

**Session Artifacts:**
- `.squad/orchestration-log/2026-04-06T16-01-59Z-jeff.md` — endpoint fix summary
- `.squad/log/2026-04-06T16-01-59Z-local-login-bugfix.md` — session brief

### 2025-11-02 — Local login form field name mismatch fixed (identifier vs username)

**Status:** Fixed and tested.

**Problem:** The local sign-in form in `SignInPanel.razor` posted a field named `identifier`, but the `/auth/local/signin` endpoint in `Program.cs` expected `[FromForm] string username`. This caused a `BadHttpRequestException` when users attempted local login.

**Root Cause:** Parameter name mismatch between the form field (line 128 in SignInPanel.razor) and the endpoint parameter (line 207 in Program.cs).

**Solution:** Changed the endpoint parameter from `username` to `identifier` to match the form field name. The `LocalAccountAuthenticator.AuthenticateAsync` method already accepts `identifier` and supports both username OR email lookup (lines 21-36 in LocalAccountAuthenticator.cs).

**Key Changes:**
- `src/AspireApp.Web/Program.cs` (line 207): Changed `[FromForm] string username` to `[FromForm] string identifier`
- No other changes required; the authenticator already supported this parameter name

**Validation:**
- Ran `dotnet test src/AspireApp.WebTest/AspireApp.WebTest.csproj --filter "FullyQualifiedName~LocalAuth"` — all 5 tests passed
- The fix preserves the existing behavior: local auth accepts username OR email as the identifier

**Key Insight:** Always verify form field names match endpoint parameter names when using `[FromForm]` binding. The form uses `name="identifier"` which must match the parameter name exactly.
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --nologo --no-build --filter "MicrosoftEntraAuthServiceTests|AuthServiceFactoryTests"`

### 2026-04-05 — `Authentication:Service=auto` now behaves like the live app path

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `AuthenticationOptions.ResolveEffectiveService(...)` is now the shared resolver for DI selection and HTTP endpoint exposure
- ✅ When Microsoft client secrets are present, `auto` resolves to `microsoft` instead of `combined`
- ✅ Mock auth endpoints are disabled whenever the effective mode is live Microsoft, removing the demo-user picker from the default live sign-in path
- ✅ Explicit mixed-mode testing still exists via `Authentication:Service=combined`

**Key Paths:**
- `src/AspireApp.Web/Services/AuthenticationOptions.cs` — effective auth-mode resolver
- `src/AspireApp.Web/Services/AuthServiceFactory.cs` — UI auth implementation selection
- `src/AspireApp.Web/Program.cs` — effective-mode gating for mock vs Microsoft endpoints
- `src/AspireApp.WebTest/Tests/AuthServiceFactoryTests.cs` — regression coverage for `auto` mode resolution

**Validation Notes:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-restore --filter "AuthServiceFactoryTests|MicrosoftEntraAuthServiceTests|MockAuthServiceTests"` passes
- `dotnet build AspireApp.sln --no-restore` passes

### 2026-04-05 — Live Microsoft auth should be auto-discovered, not hidden behind a separate auth-mode toggle

**Status:** Implemented.

**Implementation Results:**
- ✅ `Authentication:Service` now defaults to `auto`, which resolves to `combined` when Microsoft client settings are present and falls back to `mock` otherwise
- ✅ The landing/sign-in UX now treats Microsoft as a direct hosted-login action instead of a two-step picker flow
- ✅ Demo providers remain available for local shell checks without blocking the real Microsoft path

**Key Paths:**
- `src/AspireApp.Web/Services/AuthServiceFactory.cs` — auto-selects combined vs mock auth service
- `src/AspireApp.Web/Components/Shared/SignInPanel.razor` — direct Microsoft button action; demo users stay selectable
- `src/AspireApp.Web/appsettings*.json` — default auth mode switched to `auto`
- `README.md` — local Microsoft setup no longer requires a separate service toggle

**Validation Notes:**
- `dotnet build AspireApp.sln --nologo` succeeds
- Focused auth safety tests pass: `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --nologo --filter "MicrosoftEntraAuthServiceTests|MockAuthServiceTests|AuthServiceFactoryTests"`

### 2026-04-06 — Managed local auth now rides the same Blazor auth seam and operational Postgres store

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `AuthProviderOption` now carries `RequiresCredentials` + `SignInPath`, so `SignInPanel.razor` can render a server-posted username/email + password form without special-casing a separate page
- ✅ `local_auth_users` now lives in `UploadDbContext`; `LocalAuthBootstrapper` repairs the table for persisted Postgres databases and inserts only missing `Authentication:Local:SeedUsers` rows so the database stays the source of truth
- ✅ `LocalAccountAuthenticator` verifies username-or-email credentials with ASP.NET Core `PasswordHasher<LocalAuthUser>` hashes and keeps failures generic
- ✅ `CompositeAuthService` now discovers providers from registered auth services instead of hardcoding mock + Microsoft pairs, so local/Microsoft/demo providers can coexist cleanly
- ✅ `Program.cs` maps `POST /auth/local/signin`, issues the existing auth cookie claims, and tenant initialization still flows through `AppAuthenticationStateProvider` + `TenantContextService`
- ✅ Focused auth tests and the full solution suite now pass after adding local provider, bootstrapper, and credential-verifier coverage

**Key Paths:**
- `src/AspireApp.Web\Data\LocalAuthUser.cs` — managed local account entity stored in Postgres
- `src/AspireApp.Web\Services\LocalAuthBootstrapper.cs` — startup schema repair + create-missing seed path
- `src/AspireApp.Web\Services\LocalAccountAuthenticator.cs` — password hash verification by username or email
- `src/AspireApp.Web\Services\LocalAuthService.cs` — provider metadata and local sign-in surface integration
- `src/AspireApp.Web\Components\Shared\SignInPanel.razor` — shared provider UI with managed credential form
- `src/AspireApp.Web\Program.cs` — local credential POST endpoint and startup bootstrapper invocation
- `src/AspireApp.WebTest\Tests\LocalAuthServiceTests.cs`, `LocalAccountAuthenticatorTests.cs`, `LocalAuthBootstrapperTests.cs` — regression coverage for the new slice

**Validation Notes:**
- `dotnet build AspireApp.sln -nologo`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj -nologo --no-build --filter "FullyQualifiedName~AuthenticationOptionsTests|FullyQualifiedName~AuthServiceFactoryTests|FullyQualifiedName~MockAuthServiceTests|FullyQualifiedName~MicrosoftEntraAuthServiceTests|FullyQualifiedName~CompositeAuthServiceTests|FullyQualifiedName~SignInPanelTests|FullyQualifiedName~LocalAccountAuthenticatorTests|FullyQualifiedName~LocalAuthBootstrapperTests"`
- `dotnet test AspireApp.sln -nologo --no-build`

### 2026-04-05 — Microsoft Entra ID plugged into the existing Blazor auth seam

**Status:** Implemented for manual local testing while keeping mock/demo regression paths intact.

**Implementation Results:**
- ✅ `IAuthService` stays the UI seam; `MicrosoftEntraAuthService` adds a real OIDC provider and `CompositeAuthService` can expose live + mock providers together
- ✅ ASP.NET Core cookie + OpenID Connect middleware now own the Microsoft challenge/callback/sign-out flow instead of custom token handling
- ✅ Tenant seeding for real Microsoft users stays aligned with the current shell via `Authentication:Microsoft:UserTenantSeeds`, `DomainTenantSeeds`, and `DefaultAppTenantId`
- ✅ Mock endpoints and mock/demo provider flow remain unchanged so the current regression tests still target the demo path

**Key Paths:**
- `src/AspireApp.Web/Services/AuthenticationServiceCollectionExtensions.cs` — cookie/OIDC wiring and Microsoft claim-to-app-user mapping
- `src/AspireApp.Web/Services/MicrosoftEntraAuthService.cs` — live provider implementation behind `IAuthService`
- `src/AspireApp.Web/Services/CompositeAuthService.cs` — combined provider catalog for manual live testing without dropping demo auth
- `src/AspireApp.Web/Program.cs` — provider-specific challenge endpoint and provider-aware sign-out endpoint
- `src/AspireApp.Web/appsettings*.json` / `README.md` — local configuration contract and manual setup guidance

**Testing Notes:**
- `dotnet build` succeeds after adding the OpenID Connect package and provider-aware auth wiring
- Auth seam regressions are covered by `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "MicrosoftEntraAuthServiceTests|MockAuthServiceTests|AuthServiceFactoryTests"`

### 2026-04-05 — Mock auth UX foundation landed in Blazor shell

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `AppAuthenticationStateProvider` now bridges a scoped `AuthenticationContext` into Blazor auth primitives
- ✅ `IAuthService` is the UI seam; `MockAuthService` owns hardcoded Microsoft, Google, and demo users
- ✅ `/` is now the unauthenticated landing experience with provider selection and mock account sign-in
- ✅ `Chat`, `UploadData`, and `Weather` use `[Authorize]` and route through `AuthorizeRouteView`
- ✅ `TenantContextService` stays separate from identity and initializes from the signed-in user's default tenant
- ✅ Shell affordances now expose signed-in user, sign-out, and current tenant meaningfully

**Key Paths:**
- `src/AspireApp.Web/Services/AppAuthenticationStateProvider.cs` — Blazor auth bridge
- `src/AspireApp.Web/Services/IAuthService.cs` — pluggable auth service seam
- `src/AspireApp.Web/Services/MockAuthService.cs` — hardcoded provider/user implementation
- `src/AspireApp.Web/Components/Shared/SignInPanel.razor` — reusable provider + mock user sign-in surface
- `src/AspireApp.Web/Components/Routes.razor` — `AuthorizeRouteView` + protected-route prompt
- `src/AspireApp.Web/Components/Layout/MainLayout.razor` / `NavMenu.razor` — signed-in shell affordances

**Testing Notes:**
- `BasicAspireAppHostTests` now signs in through the mock auth surface before protected upload flows
- Existing hidden auth UX acceptance tests look for stable `data-testid` hooks, so auth surfaces should keep explicit selectors when refactored

### 2025-11-02 — Auth & Unauthenticated UX Assessment & Blueprint

**Status:** Assessment complete; blueprint documented.

**Current State Audit:**
- ✅ TenantContextService works well; tenant selection is wired end-to-end (files tagged, UI responsive)
- ✅ File upload & chat flow is functional without auth walls
- ❌ No identity layer: can't distinguish users; all users on same tenant are anonymous
- ❌ No unauthenticated landing page: app boots straight to chat
- ❌ No mock auth providers: can't test "Sign in with Microsoft" or "Sign in with Google" flows
- ❌ Tenant selector is UI-only; no user-tenant membership concept

**Recommended Next Slice: Unauthenticated Landing + Mock Auth Providers**
- User lands on public Landing page (`/`)
- Sign-In page (`/signin`) offers three options: Mock demo sign-in, Microsoft stub, Google stub
- After sign-in, user sees Dashboard (`/dashboard`) with Welcome message + tenant context
- Logout clears auth state and returns to landing
- Internal state: `AuthenticationContext` (scoped) holds CurrentUser + CurrentTenant

**Pluggable Seams for Real Auth Later:**
1. `IAuthProvider` interface: Mock provider today; future: MicrosoftAuthProvider, GoogleAuthProvider (registered in DI, swappable)
2. `AuthenticationContext` scoped service: Separate from TenantContextService; future: compatible with ASP.NET Core Identity
3. `<AuthorizeView>` on protected routes: Built-in Blazor; policy-based auth ready in Phase 6
4. Session-scoped auth: No JWT persistence in Phase 1-5; sufficient for dev + testing

**Services & Components to Add:**
- AuthenticationContext.cs + MockAuthProvider.cs (new services)
- Landing.razor, SignIn.razor, Dashboard.razor, UserMenu.razor (new components)
- Program.cs: Register AuthenticationContext, IAuthProvider

**Key File Paths:**
- Decision documented: `.squad/decisions/inbox/jeff-auth-ux-blueprint.md`
- Future services location: `src/AspireApp.Web/Services/`
- Future components location: `src/AspireApp.Web/Components/Pages/` and `src/AspireApp.Web/Components/Shared/`

**Strategic Insights:**
- Separate identity (user) from multi-tenancy (tenant); they're orthogonal concerns
- Unauthenticated landing is table-stakes UX; users shouldn't land in chat before choosing identity
- DI-based provider swapping eliminates refactoring cost when moving from mock to real auth
- Session is fine for Phase 1-5; persistence layer (JWT, cookies) is Phase 6 concern
- Mock providers now reduce scope; real auth (Microsoft/Google) integrates without component rewrites

**Pattern Learned:**
- When designing pluggable subsystems (e.g., auth), separate interface from implementation (IAuthProvider) and inject via DI. Use scoped services for session state. Guard protected routes with built-in middleware/components. This keeps options open and reduces rework when swapping implementations.

### 2026-07-26 — Tenant Context UI Slice for BRAIN Multi-Tenancy

**Status:** Complete and verified.

**Implementation Results:**
- ✅ TenantContextService registered as scoped DI service for Blazor session isolation
- ✅ TenantSelector component added to NavMenu with dropdown and building icon
- ✅ FileUploadController reads X-Tenant-Id header from upload requests
- ✅ FileStorageService includes tenant_id in file metadata writes
- ✅ FileMetadata schema updated with TenantId property and indexes
- ✅ UploadData component injects TenantContext and sends header
- ✅ Chat component prepared with TODO for Phase 3 Gateway integration
- ✅ Build succeeded (AspireApp.sln) with no errors

**Key Paths for Tenant Context:**
- `src/AspireApp.Web/Services/TenantContextService.cs` — scoped service managing tenant state
- `src/AspireApp.Web/Components/Shared/TenantSelector.razor` — UI dropdown component
- `src/AspireApp.Web/Components/Layout/NavMenu.razor` — integrated selector in navigation
- `src/AspireApp.Web/Controllers/FileUploadController.cs` — GetTenantId() reads X-Tenant-Id header
- `src/AspireApp.Web/Shared/FileStorageService.cs` — AddFileAsync/AddUrlAsync accept tenantId param
- `src/AspireApp.Web/Data/DocumentEntities.cs` — TenantId column (default "default")
- `src/AspireApp.Web/Shared/UploadDbContext.cs` — tenant indexes (idx_files_tenant, idx_files_tenant_status)

**BRAIN Roadmap Context:**
- Aligns with Phase 1 requirement: all contracts include tenant_id (Plan.md line 97)
- Prepares for Phase 2 ingestion: Python will read tenant_id from files table
- Prepares for Phase 3 Gateway: Chat will inject TenantContext and pass tenant_id to POST /brain/chat
- Defers authentication to Phase 6: hardcoded tenant list acceptable for dev (default, tenant-a, tenant-b, demo)

### 2026-04-24 — Blazor layout-owned sidebar state should close on route changes

**Status:** Implemented and validated for the Web shell navigation regression.

**Key insight:**
- `src\AspireApp.Web\Components\Layout\MainLayout.razor` owns the desktop slide-out sidebar state, so the layout should also listen to `NavigationManager.LocationChanged` and clear `_sidebarOpen` when the route changes.
- Keeping the close behavior in the layout avoids per-link JavaScript or duplicated nav-item callbacks in `NavMenu.razor`.
- Fast regression coverage belongs in `src\AspireApp.WebTest\Tests\MainLayoutTests.cs`, where a bUnit layout render can prove the sidebar closes on `NavigationManager.NavigateTo`.
- Browser coverage also belongs in `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`, because the regression depends on real navigation plus the rendered desktop shell.

**Validation:**
- `dotnet build --nologo`
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --no-build --filter "FullyQualifiedName=AspireApp.WebTest.Tests.MainLayoutTests.ClosesSidebar_WhenLocationChanges" --logger "console;verbosity=minimal"`

**Key paths:**
- `src\AspireApp.Web\Components\Layout\MainLayout.razor`
- `src\AspireApp.WebTest\Tests\MainLayoutTests.cs`
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`

**First Tenant Context Slice Decision:**
- Scoped service pattern isolates context per Blazor SignalR circuit (session-based, no auth yet)
- X-Tenant-Id header pattern chosen for API calls (backward compatible: defaults to "default" if absent)
- Schema change: FileMetadata.TenantId column with composite index for tenant+status queries
- No tenant-scoped queries yet: Python/Gateway will enforce isolation in Phase 2-3
- Zero Python changes needed yet: tenant_id column exists in schema, Python reads it when ready
- Decision documented in `.squad/decisions/inbox/jeff-tenant-context-impl.md`

**Pattern Learned:**
- When adding multi-tenant scaffolding before auth, use scoped DI services for session isolation, default values for backward compat, and prepare components with TODO comments for future integration points. This keeps the slice low-risk while establishing the tenant propagation pattern early.

### 2026-04-05 — Tenant-Context UI Slice - Implementation Rejected & Revised by Bob

**Status:** 🔴 REJECTED by Buster (coherence gap), then ✅ Fixed by Bob

**What Happened:**
1. Jeff's initial implementation added TenantContextService, TenantSelector component, and X-Tenant-Id header propagation
2. FileUploadController signatures were updated to extract and pass tenant_id
3. **Build failed:** FileStorageService.GetAllFilesAsync() signature was not updated to accept tenantId parameter
4. Buster rejected: "API layer changed but service layer incomplete"

**Bob's Revision (2026-04-05):**
- `FileStorageService.GetAllFilesAsync(string? tenantId)` now accepts optional tenant parameter
- `FileUploadController.GetUploadedFiles()` calls `GetTenantId()` helper and passes to service
- Tenant filtering: `.Where(f => f.TenantId == tenantId)` when tenant provided; null returns all (backward compatible)
- Chat.razor.cs build errors fixed (duplicate property declarations removed)
- Build: ✅ passes

**Key Learning:** API surface changes must be coordinated with service layer signatures before build. Always verify end-to-end flow (controller → service → query → persistence) before declaring implementation complete.

**Next Steps:** Jarvis to align Python schema, Kujan to validate contract audit, Buster to issue final verdict on data layer readiness for UI phase.

**First Tenant Context Slice Decision:**

### 2026-07-26 — Postgres Migration Verified, BRAIN Auth/Tenant UI Planning

**Status:** Postgres migration complete and verified. Next UI objective identified.

**Verification Results:**
- ✅ AppHost.cs wires Postgres container with bind mount (`../../database/postgres/`), pgWeb admin UI, and user/pass parameters
- ✅ Program.cs uses `builder.AddNpgsqlDbContext<UploadDbContext>("appdb")` with connection string injected via Aspire
- ✅ UploadDbContext properly configured with `files` (FileMetadata) and `document_pages` (DocumentPage) tables
- ✅ Build succeeds for all core projects (Web, ApiService, AppHost) — test project locked by another process but irrelevant
- ✅ Database initialization runs at startup: "Database connection test successful" logged
- ✅ FileMetadata already includes `tenant_id` column (empty string default) ready for BRAIN Phase 1-2

**Key Paths for Postgres:**
- `src/AspireApp.AppHost/AppHost.cs` lines 60-64 — Postgres resource with bind mount and pgWeb
- `src/AspireApp.Web/Program.cs` line 36 — `AddNpgsqlDbContext<UploadDbContext>("appdb")`
- `src/AspireApp.Web/Shared/UploadDbContext.cs` — EF Core configuration with Postgres-compatible schema
- `src/AspireApp.Web/Data/DocumentEntities.cs` — FileMetadata with snake_case column attributes (`[Column("file_name")]`)

**BRAIN Roadmap Context:**
- Tasks.md shows Phase 0 (Reframe Product) and Phase 1 (Core Contracts) as next objectives
- Plan.md confirms tenant_id must be in all BRAIN contracts from Phase 1 onward
- Current UI is chat + upload with no auth/tenant isolation

**First Auth/Tenant UI Slice Decision:**
- Identified minimal viable tenant context UI: TenantSelector component + TenantContextService (scoped DI)
- No full authentication infrastructure yet (deferred to Phase 6: Scale Deliberately per Plan.md)
- Files to touch: TenantSelector.razor (new), TenantContextService.cs (new), NavMenu.razor (add selector), FileUploadController.cs (read X-Tenant-Id header), FileStorageService.cs (populate tenant_id)
- Zero blockers: Postgres schema already supports tenant_id, no Python changes needed yet
- Decision documented in `.squad/decisions/inbox/jeff-auth-ui-slice.md`

**Pattern Learned:**
- When planning UI for a pivot, verify database migration first, then map existing UI structure (nav, pages, components) before proposing concrete file changes. The tenant_id column already existing in FileMetadata (from earlier BRAIN prep) saved significant rework.

### 2026-04-05 — Browser smoke tests must close Playwright pages explicitly

- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs` should close every `IPage` it opens before fixture teardown; leaving pages alive can trigger an xUnit v3 `TestPipelineException` during shutdown even after assertions pass.
- For this suite, the failure mode looked like a browser-host crash at ~60-90 seconds, but the underlying issue was teardown hanging after successful tests rather than the app failing during navigation.

### 2026-04-05 — WebTest smoke fixtures should stay lightweight and match UI affordances

- `src/AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs` is more reliable when `FlowEndToEnd` uses a tiny text fixture (`src/AspireApp.WebTest\DataExample\processing-smoke.txt`) instead of the large rooftop PDF.
- `src/AspireApp.Web\Components\Pages\UploadData.razor` must keep its `<InputFile accept=...>` list aligned with `src/AspireApp.Web\Controllers\FileUploadController.cs`; otherwise UI tests can silently fail even when backend upload support is correct.
- The Postgres regression proof remains split: `OperationalUploadStoreTests` verifies upload metadata lands in `files`, while the UI flow proves the Web→Python processing handoff with a low-cost document.

## Core Context

**Key architectural learnings from active development (Feb-Apr 2026):**

- **Aspire orchestration pattern:** Name database resources cleanly (`DefaultConnection`), inject with `.WithReference()`, let projects read via `GetConnectionString()`. Works well at scale.
- **Provider migration pattern:** SQLite → Postgres requires: NuGet swap (Sqlite → Npgsql), `Program.cs` provider change (`UseSqlite` → `AddNpgsqlDbContext`), remove provider-specific helpers (journal-mode interceptor, WAL checkpointing).
- **Cross-service contract testing:** Derive shared infrastructure names (DB names, endpoint URLs) from AppHost config, don't hardcode literals. Prevents test regressions on intentional infrastructure changes.
- **Blazor + FastAPI integration:** File upload executed by Blazor Server (via `IHttpClientFactory`), not browser JavaScript. Playwright can't intercept browser network; resolve document ID from API state instead.
- **Postgres cutover wins:** Eliminated ~400 lines of SQLite-specific boilerplate (path resolution, journal-mode hacks, fresh-connection workarounds, pragma tuning).

**Current state (as of 2026-04-05):**
- Web operational store: Postgres (appdb) via EF Core Npgsql
- Python operational store: Postgres (appdb) via psycopg2  
- Shared schema: `files` + `document_pages` (stable, cross-service compatible)
- Test pattern: Contract tests derive DB name from AppHost

**Next phase (BRAIN pivot):**
- ApiService repurposing as Interface Service / API Gateway
- Python service decomposition (Ingestion/Knowledge/Validation internal packages)
- Semantic Kernel for agent orchestration (currently chat-only)

---

### 2026-04-05 — Web Upload Store Postgres Cutover

**Status:** Complete (Jeff)

**Key paths:**
- `src/AspireApp.AppHost/AppHost.cs` — the operational upload store now comes from Aspire Postgres via `AddDatabase("DefaultConnection")`, and the Web frontend consumes it through `WithReference(uploadStore)`.
- `src/AspireApp.Web/Program.cs` — `UploadDbContext` now uses `UseNpgsql(...)`; the SQLite path resolution and journal-mode interceptor were removed.
- `src/AspireApp.Web/Shared/FileStorageService.cs` — SQLite WAL checkpoint logic was removed; upload metadata writes are provider-agnostic again.
- `src/AspireApp.Web/appsettings.json` — local default connection string now points at PostgreSQL instead of the old SQLite file.
- `src/AspireApp.WebTest/Fixtures/TestFixture.cs`, `src/AspireApp.WebTest/Tests/OperationalUploadStoreTests.cs` — test fixture captures the injected upload-store connection string and the new integration test proves the upload API persists rows into Postgres.

**Patterns learned:**
- In this repo, the clean Aspire cutover pattern is: name the database resource `DefaultConnection`, inject it with `.WithReference(...)`, and let the Web project keep using `GetConnectionString("DefaultConnection")`.
- For this migration phase, keep Python’s legacy `ASPIRE_DB_PATH` wiring alive in AppHost while the Web project moves first; that preserves current Python startup behavior while making the Web upload store cut over cleanly.
- The most direct regression proof for the .NET half is an upload API test that verifies both the stored file on disk and the corresponding `files` row in Postgres through an `NpgsqlConnection`.

### 2025-02-21 — Deep .NET Analysis

**Build:** Clean, 0 warnings. Target `net10.0` preview, SDK 10.0.200-preview via `global.json`.

**Key Paths:**
- Orchestration: `src/AspireApp.AppHost/AppHost.cs` — 6 services (apiservice, ollama, graph-db, python-service, lightrag, webfrontend)
- Blazor app: `src/AspireApp.Web/Program.cs` — EF Core SQLite, SemanticKernel, MVC controllers mixed in
- API: `src/AspireApp.ApiService/Program.cs` — Only weatherforecast demo endpoint
- Shared: `src/AspireApp.ServiceDefaults/Extensions.cs` — OTel, health, resilience, service discovery
- DB entities: `src/AspireApp.Web/Data/DocumentEntities.cs` — FileMetadata + legacy Document/ProcessedDocument
- DB context: `src/AspireApp.Web/Shared/UploadDbContext.cs`
- File storage: `src/AspireApp.Web/Shared/FileStorageService.cs`
- Upload controller: `src/AspireApp.Web/Controllers/FileUploadController.cs`
- Chat AI: `src/AspireApp.Web/Components/Pages/Chat.razor.cs` — SemanticKernel streaming via Ollama
- Speech: `src/AspireApp.Web/Components/Shared/SpeechService.cs` — JS interop
- Warmup: `src/AspireApp.Web/Services/OllamaWarmupService.cs` — Background model keep-alive
- Config: `src/AspireApp.Web/Components/Pages/HomeConfigurations.cs` — Static env-var config
- AI state: `src/AspireApp.Web/Components/Shared/AiInfoStateService.cs` — Singleton DI-based

**Configuration Keys:**
- AppHost injects `AI-Chat-Model` and `AI-Endpoint` as env vars to webfrontend
- Web reads `AI-Model` (not `AI-Chat-Model`) in AiInfoStateService — key mismatch
- HomeConfigurations reads Aspire connection strings: `ConnectionStrings__ollama`, `ConnectionStrings__chat`
- SQLite at `../../database/data-resources.db` relative to Web project

**Package Versions (as of analysis):**
- Aspire SDK: 13.1.0, Ollama hosting: 13.1.1
- SemanticKernel: 1.71.0, SK.Connectors.Ollama: 1.68.0-alpha (mismatched)
- EF Core Sqlite: 10.0.3, OpenTelemetry: 1.15.0

**Known Issues:**
- Two `ServiceDiscoveryUtilities` classes in different namespaces (root vs Pages)
- ApiService /health only mapped in Development mode via MapDefaultEndpoints()
- LightRAG and Ollama have no health checks but are in WaitFor chains
- Console.WriteLine used instead of ILogger in several places
- Redundant IConfiguration singleton registration in Web/Program.cs
- Legacy entities (Document, ProcessedDocument) still in DbContext with [Obsolete]

### 2026-02-21 — Cross-Agent Findings

**From Bob:**
- Processing pipeline blocked by ~10 missing DatabaseService methods in Python
- Status casing bug ("Uploaded" vs "uploaded") prevents file discovery
- ApiService vestigial, should be removed or given real purpose

**From Jarvis:**
- Save_document_page() signature mismatch will crash during processing
- FK column name conflict (file_id vs document_id) creates data integrity risk
- Requirements.txt unpinned — non-reproducible builds

**From Buster:**
- Zero automated tests — high regression risk on schema changes
- Console.WriteLine used 35+ times in Chat.razor.cs alone
- Cross-service contract tests critical to prevent JSON field name drift

### 2026-02-22 — Squad Orchestration Complete

**Status:** All four agents completed independent reviews; findings merged into shared decisions.md.

**Jeff's Action Items (Ready to Execute):**
1. Status casing fix: FileUploadController line 123 `"Uploaded"` → `"uploaded"` (30 min, P0) — **BLOCKER for Jarvis P0.2 validation**
2. Config key align: AppHost `AI-Chat-Model` → `AI-Model` (30 min, P0)
3. LightRAG health check or remove from WaitFor (1 hr, P0)
4. ApiService removal decision: Remove entire project (1 day, P1)
5. SemanticKernel version sync: Update Connectors.Ollama to 1.71.0 (30 min, P1)
6. Duplicate ServiceDiscoveryUtilities consolidation (1-2 hrs, P1)
7. Console.WriteLine → ILogger replacement (high-impact files first)

**Dependencies:**
- Status casing fix must land before Python tests can pass
- ApiService removal needs full grep verification of references
- All P0 items gate Sprint 1 completion

### 2026-02-27 — Cross-Agent Update: Jarvis P0.2 Lands

**Status:** Jarvis completed P0.2 (save_document_page fix) at commit `e9d90ea`

**Impact on Jeff:**
- P0.2 is now ✅ COMPLETE. Method invocation corrected, FK value fixed.
- **P0 Item 1 (status casing fix) is still BLOCKING** — files won't be discovered until Jeff's change lands
- **Coordination:** Once Jeff lands status casing fix, full file discovery pipeline can be validated end-to-end with Jarvis's fix in place
- Next step for Jeff: Prioritize P0.3 (status casing) to unblock integration testing

### 2026-02-21 — Jeff Full .NET Codebase Review

**Status:** Build clean (0 warnings), Aspire orchestration solid, 3 critical blockers, 7 code quality issues

**Critical Blockers (P0):**
1. Status casing: C# writes "Uploaded", Python queries "uploaded" → file discovery broken
2. AI model env var: AppHost passes "AI-Chat-Model", Web reads "AI-Model" → config mismatch
3. LightRAG: Registered with no health check, webfrontend waits for it → startup can hang

**Code Quality (P1):**
- Duplicate ServiceDiscoveryUtilities class (2 namespaces, 1 logic)
- Console.WriteLine instead of ILogger in 7 files (Chat.razor.cs: 35+ instances)
- OllamaWarmupService creates raw HttpClient (bypasses IHttpClientFactory)
- SemanticKernel version skew (1.71.0 core vs 1.68.0-alpha connectors)

**Tech Debt (P2):**
- Redundant IConfiguration registration in Program.cs
- ApiService health check dev-only (production readiness issue)
- README outdated (.NET 9 ref, should say .NET 10)

**Strategic (P3):**
- LightRAG functional role unclear (replace or supplement custom RAG?)
- ApiService decision pending (keep/remove/merge)
- Test infrastructure foundation (3-5 days when ready)

**Deliverable:** DOTNET_REVIEW.md with 90+ priority actions and validation checklist

### 2026-02-27 — DocumentPage FK Column Fix

**Change:** Fixed `[Column("document_id")]` → `[Column("file_id")]` on `DocumentPage.FileId` property in `DocumentEntities.cs` line 183. Updated corresponding comment and index name in `UploadDbContext.cs` (`idx_pages_document_id` → `idx_pages_file_id`).

**Why:** Python `database_service.py` creates the `document_pages` table with a column named `file_id` (FK to `files(id)`). The C# EF Core model had `[Column("document_id")]` which would map to a non-existent column, causing data integrity failures when both services access the same SQLite database. The C# property name `FileId` was already correct — only the column mapping attribute was wrong.

**Scope:** 2 files, 3 lines changed. `ProcessedDocument.FileId` (line 259, separate table) was intentionally left unchanged.

**Commit:** `6e5b34b` on `feature/doc-upload`

### 2025-11-02 — P0 Item 2 Complete: DocumentPage FK Column Final Alignment

**Status:** Complete (parallel work with Jarvis)  
**Commits:** Jeff: `6e5b34b` | Jarvis: `77db074`

**Jeff's Scope (C#):**
- Changed `[Column("document_id")]` → `[Column("file_id")]` on `DocumentPage.FileId` property in `DocumentEntities.cs` line 183
- Updated `UploadDbContext.cs` index name: `idx_pages_document_id` → `idx_pages_file_id`
- Build verified clean (0 errors, 0 warnings)

**Jarvis's Parallel Scope (Python):**
- Updated `DocumentPage` Pydantic model: `processed_document_id` → `file_id`
- Updated `fix_database.py` and `diagnose_database.py` CREATE TABLE statements
- Updated `README.md` schema documentation

**Result:** C#↔Python schema alignment complete. Both services now agree on FK column name `file_id` referencing `files(id)`. P0 Item 2 closed.

### 2025-11-02 — P0 Item 4 Complete: Upload Status Casing Normalization

**Status:** Complete (Coordinator-assisted, lightweight mode)  
**Commit:** `62ee545`

**Jeff's Scope (C#):**
- Changed `"Uploaded"` → `"uploaded"` on line 123 of `FileUploadController.cs`
- Build verified clean (0 errors, 0 warnings)
- No schema changes required; no cross-service contract updates needed

**Result:** FileUploadController now writes lowercase `"uploaded"` status, matching Python's file discovery queries (`WHERE status = 'uploaded'`) and other status values (processing, processed, error). File discovery pipeline unblocked. P0 Item 4 closed.

### 2026-03-20 — Python Footprint Minimization Follow-Through

**Status:** Complete (Jeff revision)

**Key paths:**
- `src/AspireApp.PythonServices/app/services/database_service.py` — removed legacy sync/document wrappers, added canonical `files`-based projections and status lookup
- `src/AspireApp.PythonServices/app/routers/documents.py` — document endpoints now project directly from `files`
- `src/AspireApp.PythonServices/app/routers/processing.py` — processing endpoints now use canonical file/status methods
- `src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` — regression gate now asserts the canonical surface and absence of removed sync shims
- `src/AspireApp.PythonServices/README.md`, `docs/CROSS_SERVICE_CONTRACT.md`, `docs/MIGRATION_GUIDE.md` — docs updated to describe only the live `files` + `document_pages` footprint
- `migrate_database.py`, `test_database_schema.py`, `src/AspireApp.PythonServices/diagnose_database.py`, `src/AspireApp.PythonServices/fix_database.py`, `src/AspireApp.PythonServices/scripts/fix_schema.py`, `src/AspireApp.PythonServices/scripts/test_concurrent_access.py` — support helpers rewritten around the canonical schema

**Patterns learned:**
- Keep the SQLite source of truth in `files`; if the API still wants `Document`/`ProcessingStatus`, project those models from `files` rather than maintaining bridge/sync methods.
- Status values in the Python surface should stay canonical (`uploaded`, `processing`, `processed`, `error`) even when exposed through legacy-shaped response models.
- Stdlib validation is enough to guard this footprint: `python src/AspireApp.PythonServices/tests/test_p0_contract_audit.py` plus `python test_database_schema.py` caught the contract cleanup without needing pytest installed.

### 2026-03-20 — P0 Decision Merge Complete

**Status:** All P0 work merged into shared decisions.md and approved by squad.

**Work Summary Across Squad:**
- **Jeff (this agent):** Finished Python footprint cleanup by removing sync shims and updating canonical contract methods. Fixed status casing + FK column alignment in earlier phases.
- **Jarvis:** Implemented upload path fix + endpoint/method pruning.
- **Bob:** Post-QA revision work. Converted audit tests from `expectedFailure` to live regression. Aligned CROSS_SERVICE_CONTRACT.md.
- **Buster:** QA gates (3 phases). Initial rejection, then approvals post-Bob and post-Jeff.

**Inbox → Decisions.md:** 6 files merged. Jeff's final footprint decision now part of permanent squad record.

**Orchestration Log Created:** Scribe created one per agent documenting spawn phases and context for successors.

**Session Log Created:** Scribe created brief summary of P0 completion status.

**Next Phase:** Continue with P1 items (version pinning, Neo4j batching, etc.) or new features. Jeff to maintain canonical Python contract surface methods and docs going forward.

### 2026-03-21 — Aspire Dashboard Test Auth Capture

**Status:** Complete (Jeff)

**Key paths:**
- `src/AspireApp.WebTest/Fixtures/TestFixture.cs` — enables the Aspire dashboard in test runs, waits for the `aspire-dashboard` resource, and reads dashboard auth data from the resource snapshot environment variables.
- `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs` — stores the dashboard base URL, browser token, and computed authenticated login URL.
- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` — navigates with the authenticated login URL and asserts the redirect leaves `/login` and lands on the resources dashboard.

**Patterns learned:**
- For Aspire dashboard UI tests, use `app.ResourceNotifications.WaitForResourceHealthyAsync("aspire-dashboard")` and read `DASHBOARD__FRONTEND__PUBLICURL` plus `DASHBOARD__FRONTEND__BROWSERTOKEN` from `Snapshot.EnvironmentVariables` instead of scraping the console log output.
- `app.GetEndpoint("aspire-dashboard", "http")` is a good fallback for the dashboard base URL, but the browser token only shows up in the dashboard resource snapshot.
- The dashboard title is runtime-specific (`AspireApp resources` here), so tests should assert the authenticated redirect and a `"resources"` title substring rather than an exact `"Aspire Resources"` string.

### 2026-03-21 — Aspire Dashboard Test Redirect/Title Poll Revision (Bob → Rejection → Fix)

**Status:** Complete (Bob revision after Buster rejection)

**Context:** Jeff's initial artifact captured dashboard URL and token correctly and populated `AppHostMappingModel.AspireDashboardLoginUri`, but Buster rejected the test because the assertion on title failed: after navigating to the login URI, `document.title` was empty when polled.

**Root Cause Race Condition:**
1. `await page.GotoAsync(aspireDashboardLoginUri)` returns after landing on login page
2. Dashboard auth handler validates token and triggers Blazor-driven redirect: `NavigateTo("/", forceLoad: true)`
3. Between redirect start and page fully load, the document transitions: login page → new page (empty title) → Blazor hydration (title set by `<PageTitle>` component)
4. Original test immediately polled `document.title` on the new page before `<PageTitle>` component ran

**Fix Applied (Bob):**
1. After `GotoAsync`, added explicit gate: `await page.WaitForURLAsync(url => !url.Contains("/login"), new PageWaitForURLOptions { Timeout = 120_000 });`
2. Added explicit timeout to title poll: `await page.WaitForFunctionAsync("() => document.title", new PageWaitForFunctionOptions { Timeout = 60_000 });`
3. Changed title assertion from exact match to substring: `Contains("resources", StringComparison.OrdinalIgnoreCase)`

**Validation:**
- `dotnet build AspireApp.sln` ✅ — 0 warnings, 0 errors
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter BasicAspireAppHostTests.AspireDashboardLoads` ✅ — 1 passed, ~40s wall clock

**Pattern Learning:** When testing Blazor Server UI redirects via Playwright, always:
1. Gate subsequent assertions on the URL settling (no "/login" in URL)
2. Use explicit long timeouts (60s+) for title polls to account for SignalR cold-start
3. Assert on flexible conditions (substring/contains) rather than exact matches

**Approved:** Buster accepted revised artifact. Dashboard test harness pattern documented in decisions.md for team adoption.

### 2025-11-02 — Deep Trace: Upload Flow & Ingestion Pipeline Integration Points

**Status:** Complete (read-only review, no code changes)

**Context:** User asked: "FlowEndToEnd test gets file uploaded and in table. Where does Docling parsing trigger? What is the clear ingestion process?"

**Answer — The Gap Identified:**

1. **Web UI Upload Flow (Lines 160–276 in UploadData.razor.cs):**
   - Blazor component reads file from browser
   - POSTs multipart data to `/api/FileUpload` endpoint
   - FileUploadController writes bytes to `./data/{timestamp}_{uuid}.pdf` on host filesystem
   - Writes FileMetadata row to SQLite with status="uploaded"
   - Returns 200 OK to UI, UI refreshes file table
   - **Test stops here** — it sees the file row and assumes ingestion continues

2. **What .NET Actually Does (FileUploadController.cs lines 110–123):**
   - `File.CopyToAsync(stream)` writes bytes to disk (line 113)
   - `FileStorageService.AddFileAsync()` inserts one row into `files` table (line 117)
   - Returns response (line 131)
   - **No background job, no HTTP call, no trigger of any kind**

3. **Aspire Bind Mount Visibility (AppHost.cs lines 73–74):**
   - `./data` ↔ `/app/data` (bidirectional)
   - `./database` ↔ `/app/database` (bidirectional)
   - File written by .NET is **immediately visible** to Python container
   - SQLite database shared in real-time

4. **What Python Service **Expects** (processing.py lines 128–203):**
   - Endpoint: `POST /processing/process-all`
   - Queries `files WHERE status='uploaded'`
   - Launches background tasks per file
   - Calls `docling.process_document()`, creates Neo4j nodes, updates status → "processed"
   - **But this endpoint is never called by .NET**

5. **The Missing Piece:**
   - **No orchestrated trigger** connects upload completion to processing initiation
   - Current architecture is entirely manual: user must either:
     - Call `/processing/process-all` via curl/Postman after upload
     - UI must add a "Process Now" button
     - Background polling service must exist (does not)
   - Test validation cannot go beyond "file appears in table" without manually calling Python endpoint

**Patterns & Decisions:**

- **Status quo:** Ingestion is **pull-based** (Python polls or waits for manual trigger), not **push-based** (Web triggers processing)
- **Next step for testing:** After upload, test must explicitly call Python `/processing/process-all` endpoint and poll file status until it reaches "processed"
- **Next step for UX:** Add "Process Now" button to UI or implement background polling service in .NET
- **Cross-service contract:** File status enum is canonical: "uploaded" (initial), "processing", "processed", "error"

**Documented in:** `.squad/decisions/inbox/jeff-ingestion-trigger-gap.md` for team review

### 2026-03-26 — FlowEndToEnd FastAPI Proof Harness

**Status:** Harness updated; live validation exposed a Python-side integration bug.

**What changed:**
- `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs` now clears stale copies of the sample document through the Web API, uploads through the UI, resolves the new file row from API-backed state, and then calls the Python trigger/status endpoints directly.

### 2026-04-21 — Web UI Readiness Status Display + Schema Tolerance

**Status:** COMPLETE — Upload table now displays per-document readiness state; backward-compatible with pre-polling schemas.

**What was built:**
- Extended `UploadData` record with optional `IndexingStatus` property (nullable); safe deserialization for pre-polling docs
- Web UI Upload table rows now display readiness labels: queued (spinner), indexing (progress), ready (checkmark), error (alert)
- `UploadDataService` interprets `indexing_status` state → human-readable labels
- No breaking changes to ApiService contract or gateway routing

**Why it matters:**
- Users see honest progress: documents appear in Upload list before they're queryable (not silent waiting)
- Schema tolerance prevents version mismatch errors when rolling out `indexing_status` polling
- UI gracefully degrades if `indexing_status` absent (treats as legacy "processed" semantics)

**Key files:**
- `src/AspireApp.Web/Components/UploadDataService.cs` — label interpretation
- `src/AspireApp.Web/Data/UploadData.cs` — contract extension (nullable field)
- `src/AspireApp.WebTest/Tests/UploadDataTests.cs` — optional-field compat tests (28/28 passing)

**Test coverage:**
- UploadDataTests: optional field deserialization, all readiness states (28/28 passing)
- BasicAspireAppHostTests: end-to-end compat checks (passing)
- No ApiService changes required

**Handoff to next phase:**
- Jarvis: Polling loop feeding `indexing_status` updates via API responses
- Buster: E2E browser test validates UI label updates as documents transition states

- The test now fails with readable diagnostics for trigger failures, status contract problems, processing errors, or polling timeouts.
- `BasicAspireAppHostTests.PythonServiceOpenAPILoads` still passes, so this work separates "Swagger loads" from "processing actually works."

**Key learning:**
- In this repo, the upload POST is executed by Blazor Server code (`UploadData.razor.cs` via `IHttpClientFactory`), not by browser JavaScript, so Playwright cannot wait on a browser `/api/FileUpload` response to capture the document ID.
- The reliable pattern is: upload via UI → resolve the new row from Web API state → call Python `/processing/process-document/{id}` → poll Python `/processing/status/{id}`.

**Live finding for Jarvis:**
- The revised FlowEndToEnd test currently fails because the uploaded file row exists in the Web API state, but Python returns `404 {"detail":"Document not found"}` for `POST /processing/process-document/{id}`.
- That points to a Python-side shared database visibility/path issue rather than a Swagger/OpenAPI issue.

---

### 2026-04-05 — Postgres Cutover Coordination & BRAIN Pivot Context

**Status:** Postgres cutover complete. Joined BRAIN pivot decision consolidation session.

**What Happened:**
1. **Postgres Upload Store Cutover (completed in parallel with Jarvis):**
   - Web now uses uilder.AddNpgsqlDbContext<UploadDbContext>("appdb") instead of SQLite
   - AppHost injects via .WithReference(postgres); Web reads GetConnectionString("appdb")
   - Removed DeleteJournalModeInterceptor, CheckpointDatabaseAsync, connection-string resolution helpers (~100 lines eliminated)
   - Manual AppHost tuning by Eric ensured connection string wiring works correctly
   
2. **Regression Detection & Coordination:**
   - WebTest failed due to stale fixture expectations (hardcoded DefaultConnection instead of ppdb)
   - Buster diagnosed this as test/harness regression, not product issue
   - Coordination: All three surfaces (AppHost, Web, Python) now use canonical ppdb name
   - Pattern: Future contract tests must derive DB names from AppHost source, not hardcode literals

3. **BRAIN Pivot Context:**
   - Kujan review: BRAIN requires service decomposition, new Validation/Reasoning layers, clear contracts
   - Verbal strategy: MVP should focus on one evidence-backed agentic slice (QA intelligence recommended)
   - Eric decision: Pivot approved. BRAIN is the product; chat is one interface, not architecture
   - Timeline: New phase sequence proposed (Phase 0: Reframe, Phase 1: Contracts, Phase 2-3: First slice)
   - ApiService repurposed: No longer vestigial weather stub; becomes BRAIN Interface Service / API Gateway

**Key Decisions for Web/C# Work Going Forward:**
- Postgres is now canonical for Web operational store (no more SQLite workarounds)
- Multi-service architecture is stabilizing; Aspire orchestration pattern proven sound
- Next BRAIN phase requires Interface Service (C# Minimal API) as API gateway + interface layer
- Consider Semantic Kernel agents or Microsoft.Extensions.AI for BRAIN Reasoning integration (currently only used for chat)

**Contract Alignment:**
- Postgres ppdb is the shared upload store name across all services
- docs/CROSS_SERVICE_CONTRACT.md needs update: "Shared Database" section SQLite → PostgreSQL
- Web's iles table schema unchanged; Python now writes to same Postgres tables

**Related Agent Work:**
- **Jarvis:** Python Postgres cutover completed in parallel; derives contract from AppHost
- **Buster:** Updated WebTest fixture expectations; established pattern for future contract tests
- **Kujan:** Architecture review points to Python service decomposition as next major work
- **Verbal:** Strategy review recommends deferring multi-tenancy until MVP proof

**Orchestration Log:** Created for session context at 20260405T143735Z-jeff.md

---



## Core Context (Summarized from 2025-11-02 — 2026-03-26)

**Foundational Work:**
- **2025-11-02:** Auth UX assessment + blueprint (identified need for unauthenticated landing + mock providers; designed pluggable IAuthProvider abstraction)
- **2026-02-21:** Deep .NET analysis of codebase structure, cross-agent findings, squad orchestration completion
- **2026-02-27:** Cross-agent coordination during P0: upload path normalization, Python footprint minimization, DocumentPage FK fixes
- **2026-03-20:** P0 completion: upload status casing, Python footprint, Aspire dashboard test harness
- **2026-03-21:** Aspire dashboard auth capture and redirect/title poll refinement (Bob's rejection → Jeff's fix)
- **2026-03-26:** FlowEndToEnd FastAPI proof harness creation; detailed trace of upload flow + ingestion pipeline
- **2026-04-05:** Postgres cutover coordination; BRAIN pivot context established

**Key Learnings from Core Context:**
1. Pluggable auth pattern: Separate interface (IAuthProvider) from implementation; inject via DI for swappability
2. Session-scoped auth sufficient for dev/testing phases; persistence (JWT/cookies) deferred to Phase 6
3. TenantContextService works well; tenant selection UI is functional end-to-end
4. Mock providers reduce scope; real OAuth integrates without component rewrites
5. Playwright tests require explicit page closure; keep WebTest fixtures lightweight
6. Aspire dashboard integration supports auth capture for acceptance gate testing

---

### 2026-04-05 — Auth UX Blueprint Designed (Cross-Agent Consensus)

**Agent Assessment:** Jeff designed concrete Blazor UX + services for mock auth.  
**Cross-Agent Inputs:** Bob (architecture/IAuthProvider seams), Buster (acceptance gates).  

**Key Decisions:**
- **AuthenticationContext (scoped)** mirrors TenantContextService pattern ✅
- **IAuthProvider interface** enables pluggable backends (mock, Microsoft, Google) ✅
- **Blazor components:** Landing, SignIn, Dashboard, UserMenu ✅
- **Provider buttons** styled like real OAuth for UX fidelity ✅
- **Buster alignment:** 5-layer test gates defined; E2E flow validations ✅

**Deliverables:**
1. AuthenticationContext.cs — scoped service, holds CurrentUser + OnAuthChanged event
2. IAuthProvider + MockAuthProvider — pluggable backend, hardcoded mock users
3. Landing.razor — public, hero + sign-in CTA
4. SignIn.razor — public, three sign-in options (mock, Microsoft stub, Google stub)
5. Dashboard.razor — protected via <AuthorizeView>
6. UserMenu.razor — avatar + logout in NavMenu

**Test Coverage:** Playwright E2E (login flow, tenant auto-select, logout), xUnit unit tests (provider state transitions), cross-service tenant audit (Python validation)

**Next:** Eric approval + sprint estimation + implementation by Jeff

### 2026-04-05 — Scribe: Auth documentation and decisions merged (18 inbox files)

**Session:** Post-spawn consolidation after Jeff (auth doc creation) and Warden (security audit)

**What Scribe Did:**
- Created orchestration logs for both agents documenting spawn context and work completed
- Created session log summarizing auth doc completion and ready state
- Merged 18 inbox decisions into decisions.md (5 from Jeff: auto-select, setup-guide, auth-seam, microsoft-signin, mock-shell)
- Consolidated overlapping decisions (e.g., Bob + Buster's multi-gate recommendations merged into single "Mock Pluggable Auth Slice" decision)
- Updated Jeff and Warden history.md with cross-agent context propagation
- Deleted all .squad/decisions/inbox/* files after merge

**Decisions Captured (Jeff's 5):**
1. **Auto-Select Live Microsoft When Configured** — Web auth seam defaults to uto, resolves to Microsoft-only when creds present
2. **Authentication Setup Guide** — Created docs/AUTHENTICATION_SETUP.md (20 KB, ~650 lines) for local Microsoft testing and Google prep
3. **Microsoft Entra Auth Uses Existing IAuthService Seam** — Keep abstraction, plug Entra in behind it
4. **Microsoft Sign-In as Hosted Redirect** — Direct link to /auth/microsoft/signin, not demo picker flow
5. **Mock Auth Shell Uses Blazor Auth Primitives** — Scoped AuthenticationContext + AppAuthenticationStateProvider

**Cross-Agent Context:**
- Warden validated security posture of auth implementation (APPROVED)
- Warden corrected 4 security-critical accuracy issues in documentation (ports, Google API, OIDC guidance)
- Together, Jeff + Warden delivered production-ready auth docs and security-hardened implementation
- All 23 regression tests passing with Microsoft integration in place
- Ready for Eric to test with real Microsoft credentials

**Status:** ✅ Documentation ready for user testing. Auth implementation approved by security specialist. All decisions merged and inbox cleared.


### 2026-04-09 — Tenant Slice Session: Core Implementation

**Role:** .NET Dev (Core Tenant Model)

**Outcome:** Implemented persisted tenant model with default-tenant protection and upload authorization hardening. 28 targeted tests passing; ready for merge.

**What Jeff Did:**
1. Created tenants and tenant_memberships tables with proper constraints
2. Implemented TenantManagementService.EnsureTenantAccessAsync() for idempotent default-tenant recovery
3. Hardened FileUploadController to validate X-Tenant-Id membership; reject 403 for unmembered tenants
4. Added tenant-scoped duplicate detection and file deletion
5. Updated TenantSelector.razor to render user's actual memberships (not hardcoded list)
6. Implemented add-member by username with generic success/failure response
7. Created /tenants management page with protected badge for original tenant
8. Added test coverage for provisioning, protection, and authorization paths

**Coordination:**
- Warden identified add-member exception handling gap; Jeff broadened catch block
- Buster required direct recovery tests; Jeff added 6 direct tests
- Specialist added UploadUrl tenant-isolation regression test

**Key Decisions Contributed:**
- Tenant Core Implementation — persisted model + cached DefaultTenantId pointer
- Tenant UI Implementation — single management page + protected badge UI
- Tenant Upload Authorization Enforcement — X-Tenant-Id validation on every operation

**Status:** Slice complete; security approved; tests passing; merged to decisions.md

### 2026-04-17 — P2-B Confidence Fail-Closed Fix (Cross-Domain Reviewer Lockout)

**Status:** Complete. Delivered as Jeff under reviewer lockout (Jarvis rejected revision).

**Context:**
- Buster rejected Jarvis's P2-B confidence enrichment implementation because `retrievers.py` still assigned `DEFAULT_CONFIDENCE = 0.5` when Neo4j enrichment returned None.
- Required fix: unresolved scores must fail closed (force semantic fallback) instead of being guessed.

**Implementation:**
1. Modified `_build_item()` to return `None` when confidence cannot be resolved (lines 49-50), preventing KnowledgeItem creation with missing confidence.
2. Modified `_extract_items()` fallback path to return empty list when confidence is unresolved (lines 358-359), forcing semantic retriever fallback.
3. Updated list comprehensions to filter out `None` items (lines 328-333, 411-415).
4. Updated tests to validate fail-closed behavior: `test_lightrag_retriever_fails_closed_when_neo4j_returns_none`, `test_lightrag_retriever_without_neo4j_service_fails_closed`, `test_lightrag_retriever_fails_closed_on_unscored_response_text`.
5. Updated `roadmap/Tasks.md` to reflect P2-B completion.

**Key Insight:**
- Fail-closed pattern is cleaner than guessing: when LightRAG cannot provide or enrich confidence, filter the result out entirely.
- `BrainKnowledgeRetriever` orchestrator then naturally falls back to `SemanticKnowledgeRetriever`, which retrieves real confidence from Neo4j Claim/Page nodes.
- This preserves "LightRAG-first" behavior while ensuring semantic fallback handles unresolved cases.

**Key Paths:**
- `src/AspireApp.PythonServices/app/brain/knowledge/retrievers.py` — fail-closed logic in `_build_item()` and `_extract_items()`.
- `src/AspireApp.PythonServices/tests/test_lightrag_retriever.py` — fail-closed test coverage.
- `src/AspireApp.PythonServices/tests/test_knowledge_retriever.py` — fallback response test updated.
- `roadmap/Tasks.md` — P2-B marked complete.

**Validation:**
- All 25 Python retriever tests pass (14 in `test_lightrag_retriever.py`, 11 in `test_knowledge_retriever.py`).
- `BasicAspireAppHostTests.BrainQueryReturnsConfidenceEnrichedResults` should continue to pass (validates no DEFAULT_CONFIDENCE=0.5 in results).

**Cross-Domain Notes:**
- Took narrow Python fix under C# ownership due to Jarvis lockout — honored reviewer verdict exactly.
- Did not pair with or reuse Jarvis's rejected work per charter guardrails.
- Documented decision in `.squad/decisions/inbox/jeff-failclose-lightrag-confidence.md`.



---

### 2026-04-15 — Fixed Event Loop Starvation in Python Processing Service

**Task:** Fix BasicAspireAppHostTests.FlowEndToEnd timeout during processing status polling.

**Status:** ✅ COMPLETE — 8/8 tests passing

**Problem Analysis:**
- Upload succeeds but test times out while polling GET /processing/status/{id}
- Root cause: Python process_document_task is an sync FastAPI background task with synchronous implementation
- Sync-heavy document processing (Docling extraction, Neo4j writes, embedding calls) monopolizes the event loop
- Status polling requests hit client timeouts during active processing

**Solution Implemented:**
- Kept process_document_task public API async for backward compatibility
- Moved heavy processing body to thread-pool worker via syncio.to_thread(...)
- Restores FastAPI event-loop responsiveness for status/health polling during active processing

**Key Changes:**
- src\AspireApp.PythonServices\app\routers\processing.py — Wrapped sync processing in syncio.to_thread()
- src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs — Updated to treat transient timeouts as retryable within polling window
- src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs — Updated expectations to match async dispatch model

**Trade-offs & Notes:**
- Each queued document consumes a thread-pool worker during blocking processing
- Acceptable for current Aspire smoke/integration workflow
- If concurrency grows materially, revisit dedicated worker queue or external job runner
- No API surface changes; existing callers unaffected

**Decision Recorded:**
- "Keep Python processing off the FastAPI event loop" — Captures pattern for future maintainers

**Validation:**
- dotnet test ... --filter "FullyQualifiedName~BasicAspireAppHostTests.FlowEndToEnd|FullyQualifiedName~FileUploadControllerTests" — **8/8 passed**
- Python processing regression tests clean
- No regressions in other background task patterns

**Lessons for Future Work:**
- FastAPI background tasks must be truly async; sync-heavy work blocks the event loop
- Thread-pool workers are appropriate for CPU-bound tasks within an async context
- Client-side timeouts during service startup are transient; polling helpers should be resilient

### 2026-04-15 — ChatConversationPersistenceTests Test Intermittency Due to AI Response Timing

**Status:** Diagnosed; test works but is flaky under slow AI conditions.

**Problem:**
- SignedInUserCanSaveRenameResumeAndDeleteConversation passed on first run after clean build, but failed on second run with "The chat send button stayed disabled longer than expected."
- Test uses 90-second timeout waiting for send button to re-enable after AI response (WaitForControlEnabledAsync at line 205 of test helper).
- Send button is controlled by IsAIResponsing flag in Chat.razor.cs, which is properly managed in inally block (line 1040) to always reset.
- AI response has 3-minute internal timeout (line 977), so button can legitimately stay disabled for up to 180 seconds.

**Root Cause:**
- Test timeout (90s) is shorter than the AI's internal response timeout (3min), creating a race condition.
- When Ollama is slow (model loading, GPU contention, system load), the test times out before the legitimate response completes.
- This is environmental flakiness, not a product bug—IsAIResponsing management is correct.

**Recommendation:**
- Test timing should be addressed by Buster:
  1. Increase WaitForControlEnabledAsync timeout to match or exceed AI response timeout (180s+), or
  2. Mock AI responses in test scenarios to eliminate timing variability, or
  3. Add explicit AI warmup phase before conversation tests run.

**Key Insight:**
- Product code correctly manages button state through exception handlers and finally block.
- All data-testid hooks are properly in place (validated: chat-send, chat-conversations-shell, chat-conversation-list, etc.).
- Test infrastructure requires timing alignment with production AI behavior or controlled test doubles.

**Key Paths:**
- src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs line 516-530 — WaitForControlEnabledAsync with 90s timeout
- src\AspireApp.Web\Components\Pages\Chat.razor.cs lines 972-1046 — CallBackgroundAI with 3min timeout and proper finally cleanup
- src\AspireApp.Web\Components\Pages\Chat.razor line 884 — send button disabled condition includes IsAIResponsing

**Validation:**
- First run: Passed after clean build (1m 48s runtime)
- Second run: Failed at 91s with button still disabled (AI still responding)
- Product behavior is correct; test needs timing adjustment

**Cross-Agent Handoff:**
- Jeff validated product code correctness and testid infrastructure
- Buster should address test timing strategy (increase timeout or add mocks)



## 2026-04-15T17-41-59 — Chat Persistence Test Investigation & P2-C AppHost Config (Scribe collaboration)

**Role:** .NET dev (ChatConversationPersistenceTests analysis + P2-C embedding orchestration)
**Outcome:** No product defect; test timeout race identified; P2-C embedding config complete
**Output:**
- Analyzed Chat.razor.cs timing assumptions vs Ollama response latency  
- Product code verified correct (AI response management sound)
- Identified 90s test timeout races with legitimate slow AI responses
- Approved P2-C embedding infrastructure (vector indexes, Neo4j integration)
- Delegated test strategy hardening to Buster

**Learning:** Large language model response times are variable; test suites must account for this in E2E scenarios.

**Cross-Agent:** Collaborated with Buster (timing investigation), Bob (architecture), Jarvis (embedding pipeline).

**Files:**
- .squad/orchestration-log/2026-04-15T17-41-59-jeff.md (session log)
- .squad/decisions/inbox/jeff-*.md → merged to decisions.md


### 2026-04-22 — Critique mode now preserves selected mode on first message

**Status:** Implemented and validated.

**Implementation Results:**
- ✅ `Chat.razor.cs` now passes `SelectedChatMode` into `StartConversationAsync`, so new conversations persist the selected mode before the first backend call.
- ✅ Critique-mode tests can send messages without stub exceptions, and the BrainChat client receives "critique" when the toggle is set.

**Key Paths:**
- `src\AspireApp.Web\Components\Pages\Chat.razor.cs`
- `src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs`

**Validation Notes:**
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~ChatCritiqueModeTests" -v q`

### 2026-04-15T19:37:41Z — Critique Mode UI Product Layer: Complete and Validated

**Status:** Implemented, tested, and approved for Phase 3b integration.

**Work Completed:**
- Enabled Blazor Critique-mode toggle (removed disabled attribute from radio button)
- Rendered reasoning/progress details using framework-agnostic CSS classes (.reasoning-panel, .reasoning-step, etc.)
- Wired SelectedChatMode to BrainChatClient.ChatAsync call path (mode reaches gateway)
- Maintained framework agnosticism: UI consumes generic BrainChatResponse.ReasoningSteps, not PydanticAI types

**Test Harness Fix (Blocker Resolution):**
- Initial issue: ChatCritiqueModeTests.cs used unsupported RemoveAll() on Bunit's BunitServiceProvider
- Root cause: Bunit doesn't allow service replacement after context creation
- Solution: Implemented Option A (parameterized factory) — CreateTestContext() now accepts optional service overrides
- Tests now compile and all 9 targeted tests passing (9/9)

**Validation Results:**
- ✅ Critique toggle enabled and persists mode
- ✅ Reasoning steps render with step title + reasoning + optional tool badge + result
- ✅ Mode reaches gateway correctly
- ✅ Regular mode unchanged (no regression)
- ✅ Mode hint text updates based on selection
- ✅ Conversation persistence preserves mode

**Cross-Team Updates:**
- **Buster (QA):** Reviewed harness fix and approved test suite; 9/9 tests passing
- **Jarvis (Python):** Critique reasoning pipeline ready to feed reasoning steps into UI
- **Bob (Architecture):** Swappable agent framework validated in C# layer

**Residual Risk Noted:**
- No dedicated test yet exercises mode switching after loading an existing conversation (manual spot-check recommended for Phase 3b polish)

**Key Files Modified:**
- src\AspireApp.Web\Components\Pages\Chat.razor
- src\AspireApp.Web\Components\Pages\Chat.razor.cs
- src\AspireApp.Web\Components\Pages\Chat.razor.css
- src\AspireApp.WebTest\Tests\ChatCritiqueModeTests.cs (test harness fix)

**Related Decisions Merged:**
- Critique Mode UI Implementation (Jeff, 2026-04-22)
- Critique-Mode UI Test Coverage Strategy (Buster, 2026-04-22)
- Critique-Mode UI Test Blocker + Resolution (Buster, 2026-04-22, resolved)
- Critique-Mode Harness Revision Approved (Buster, 2026-04-23)

**Orchestration Log:** .squad/orchestration-log/2026-04-15T19-37-41Z-jeff.md

**Session:** Critique Mode UI Batch (2026-04-15T19:37:41Z, log: .squad/log/2026-04-15T19-37-41Z-critique-ui-batch.md)
---

## Cross-Agent Coordination — Scribe Merge (2026-04-15T20:25:34Z)

**Session:** Planning Doc Reconcile & Test Failure Triage

**Work:** Jeff synced planning documents, analyzed auth/upload test failures, and coordinated cross-service fixes.

**Coordination Points:**
- Verbal recommended Phase 3 beta reframing; Jeff updated roadmap to reflect foundation reality
- Bob reconciled branch state; verified Phase 1/2 gates closed; locked Phase 3 critical path
- Buster identified chat-mode regression coverage gap; Jeff incorporated into Phase 3b roadmap with honest wording
- Jarvis analyzed Python processing timeout; Jeff coordinated orchestration/infrastructure debugging
- Warden hardened auth test selectors; Jeff/Buster confirmed split-brain pattern (endpoint wiring issue)

**Key Outcomes:**
- Planning docs synchronized: Plan.md, Tasks.md, Roadmap.md, identity/now.md all reflect Phase 1/2 completion and Phase 3b next milestone
- Upload status race fix already implemented (background fire-and-forget processing); next step is Buster test assertion update
- Auth cookie hydration decision documented; hard-navigation proof pattern recommended for future tests
- WebTest fixture improvements: isolated per-run state, graceful Aspire health-check failures

**Related:** Orchestration logs created. Session log at .squad/log/2026-04-15T20-25-34Z-planning-doc-reconcile.md. 17 inbox decisions merged into .squad/decisions.md.

### 2026-04-15 — BRAIN Gateway/Web HTTP Error Preservation + No-Retry Policy for Unsafe Methods

**Problem:**
- Critique mode configuration failures in Python returned deterministic 503 + ProblemDetails, but:
  1. Gateway client collapsed 503 → generic 502, obscuring root cause
  2. Both gateway and Web clients retried unsafe POST requests, amplifying same deterministic failure

**Fix:**
- Updated BrainBackendClient in src/AspireApp.ApiService/Services/ to preserve downstream HTTP status codes and read ProblemDetails responses
- Updated BrainChatClient in src/AspireApp.Web/Services/ to disable resilience retries on POST operations
- Updated Chat.razor.cs to parse and display ProblemDetails errors instead of generic retry feedback

**Result:**
- Configuration-driven HTTP failures now surface with accurate status codes to Blazor UI
- Deterministic 503 errors no longer amplified by retry policies
- Chat UI displays actionable error messages (e.g., "Critique model not available") instead of generic "try again"
- Focused tests: 30/30 passed (ChatCritiqueModeTests + BrainGatewayPhase2Tests)
- Full build: Success (no regressions)

**Cross-Agent Impact:**
- **Jarvis (Python):** Provider fix now flows through unmodified; HTTP status codes correctly forwarded.
- **Buster (QA):** Gateway error preservation enables HTTP client validation; combined with provider fix + saved conversation reload tests for three-seam regression coverage.

**Key Pattern:**
- **Error transparency:** Preserve and surface downstream errors instead of collapsing to generic status. Enables operators to diagnose and fix configuration issues quickly.
- **Unsafe method resilience:** Disable retries for POST/PUT/DELETE on deterministic faults. Only retry for transient I/O failures.

**Key file paths:**
- src/AspireApp.ApiService/Services/BrainBackendClient.cs (error preservation)
- src/AspireApp.ApiService/Services/BrainBackendClientServiceCollectionExtensions.cs (policy config)
- src/AspireApp.Web/Services/BrainChatClient.cs (error preservation + retry disable)
- src/AspireApp.Web/Components/Pages/Chat.razor.cs (error parsing + display)
- src/AspireApp.WebTest/Tests/BrainGatewayPhase2Tests.cs (validation)
- .squad/decisions.md (full decision details + validation)
- .squad/orchestration-log/2026-04-15T21-17-30Z-jeff.md (session details)

### 2026-04-16 — MVP Achieved: P3b Critique UI on Track, P1-Immediate Post-MVP Tasks Queued

**Scope:** Cross-agent session confirming MVP milestone and elevating two post-MVP fixes for Phase 3c.

**What Happened (Summary for Jeff):**
- MVP is **officially declared functional** (gateway-routed chat Regular mode works end-to-end)
- Documentation updated across README + roadmap to reflect this milestone
- Two post-MVP fixes identified by user feedback and elevated to **P1-immediate** status:
  1. **Conversation context not passed on follow-ups** (affects you + Jarvis: session lifecycle + Python routing)
  2. **Gateway evidence not persisted** (affects you + Buster: message storage + UI validation)
- P3b critique UI remains on track (no blocking gates)
- Both post-MVP fixes blocked on P3b completion (2026-04-30 target)

**What This Means for Jeff:**
- Continue P3b critique UI work without interruption
- Post-MVP context memory task will be your lead work in Phase 3c (alongside Jarvis)
  - Scope: Investigate current session lifecycle in Chat.razor.cs + BrainChatClient
  - Goal: Pass multi-turn context on follow-up queries (user pain point)
- Evidence persistence task is secondary (Buster lead, you supporting): persisted backend results + UI display

**Coordination Notes:**
- Coordinator SQL-tracked both tasks; queued pending P3b gate closure
- Verbal confirmed prioritization is user-driven and high-ROI
- No architectural decisions needed; purely implementation scope

**Key Files to Know (post-MVP phase):**
- Session management: `src/AspireApp.Web/Services/BrainChatClient.cs` (context passing)
- Chat component: `src/AspireApp.Web/Components/Pages/Chat.razor.cs` (UI state)
- Python gateway: `src/AspireApp.ApiService/Services/BrainBackendClient.cs` (request routing)

**Status:** MVP locked; post-MVP priorities ordered; ready for Phase 3c investigation kickoff after P3b (2026-04-30)


## Learnings

### 2026-04-16 - UploadData web-source display semantics
- Keep stored source taxonomy explicit: `url`, `youtube_video`, and `youtube_channel` remain distinct persisted values.
- Treat those three source types as a shared WEB display family in the UploadData table via `UrlSourceTypeClassifier.IsWebSourceType()` plus local UI helpers in `src\\AspireApp.Web\\Components\\Pages\\UploadData.razor(.cs)`.
- Focused regression coverage lives in `src\\AspireApp.WebTest\\Tests\\UploadDataTests.cs` and asserts WEB badge, globe icon, and URL-cell rendering for all web-backed source types.

### 2026-04-16 - UploadData URL refresh should reuse cleanup + existing processing start
- URL-backed rows in `src\\AspireApp.Web\\Components\\Pages\\UploadData.razor(.cs)` now expose a `Refresh` action, but uploaded file rows keep their existing delete-only behavior.
- The refresh path lives in `src\\AspireApp.Web\\Shared\\FileStorageService.cs`: for URL-backed rows it reuses `cleanup-document`, resets persisted processing artifacts/state back to `uploaded`, and then reuses the existing `processing/process-document/{id}` trigger instead of adding a second backend API.
- Regression coverage in `src\\AspireApp.WebTest\\Tests\\UploadDataTests.cs` now protects both the URL-only button rendering seam and the cleanup/reset/requeue behavior for processed web sources.

### 2026-04-24 - Upload readiness must separate processing-complete from retrieval-ready
- `src\\AspireApp.Web\\Data\\DocumentEntities.cs` maps shared `files.indexing_status` so Web can distinguish LightRAG readiness from the primary `status` lifecycle.
- `src\\AspireApp.Web\\Components\\Pages\\UploadData.razor(.cs)` should keep legacy/non-LightRAG rows on the existing processed semantics (`null` / `not_requested` / `ready` still render as processed) but override the badge for `queued`, `indexing`, `failed`, and `timed_out`.
- `src\\AspireApp.Web\\Shared\\FileStorageService.cs` is the right .NET seam for tolerant shared-table bootstrap work: ensure missing columns exist before EF reads the `files` table, and reset retrieval readiness back to `not_requested` when a source is re-queued.



### 2026-04-21 — Timeout Stabilization Session: Non-Blocking Upload Dispatch + Explicit Timeout Translation

**Session Work:** .NET side timeout traced to synchronous upload dispatch and opaque cancellation handling.

**Jeff Learning:** Upload surfaces must not block on background processing dispatch; return queued immediately. Gateway must translate OperationCanceledException into clear TimeoutException. Both fixes implemented in UI layer, gateway clients, and dependency wiring.

**Session Log:** See .squad/log/20260421-timeout-stabilization.md
**Decision File:** .squad/decisions.md (merged: Non-Blocking Upload Dispatch + Explicit Gateway Timeouts)

