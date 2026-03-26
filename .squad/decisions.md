# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-03-25):** Archived pre-2026-02-27 entries (6 decisions, ~12 KB) to `decisions-archive.md` due to file size (30.75 KB → 18.5 KB target). Merged 2 inbox decisions: bob-roadmap-tracking-2026-03-25.md, copilot-directive-2026-03-25T14-07-58Z.md. Inbox cleared.
> **Note (2026-03-26):** Merged 10 inbox decisions from ingestion review spike (Bob, Jeff, Jarvis, Buster). Consolidated duplicate entries; deduped roadmap tracking. Inbox cleared.

<!-- Decisions are appended below. Each entry starts with ### -->


## Dashboard Playwright Testing — Jeff, Buster, Bob — 2026-03-21

**Scope:** Aspire Dashboard authenticated navigation in WebTest suite

### Aspire Dashboard Resource Snapshot Capture (Jeff Decision)

**Context:** `AspireApp.WebTest` needed the Aspire dashboard URL and browser token during fixture startup so Playwright can open the authenticated dashboard page reliably.

**Decision:** In Aspire integration tests, wait for the `aspire-dashboard` resource to become healthy and read `DASHBOARD__FRONTEND__PUBLICURL` plus `DASHBOARD__FRONTEND__BROWSERTOKEN` from `dashboardState.Snapshot.EnvironmentVariables`. Use `app.GetEndpoint("aspire-dashboard", "http")` only as fallback for the base dashboard URL.

**Rationale:** The console log line is human-facing and should not be the test contract. The resource snapshot is runtime state owned by the running `DistributedApplication`, so it gives the same values programmatically and avoids log scraping.

**Testing note:** The dashboard title is app-name specific (`AspireApp resources` in this repo), so UI assertions should verify the authenticated redirect leaves `/login` and that the title contains `resources`, not an exact `Aspire Resources` string.

**Affected paths:** `src/AspireApp.WebTest/Fixtures/TestFixture.cs`, `src/AspireApp.WebTest/DataModels/AppHostMappingModel.cs`, `src/AspireApp.WebTest/Tests/BasicAspireAppHostTests.cs`

**Impact:** Infrastructure sound for dashboard test harness. Foundation work complete.

### Aspire Dashboard Playwright Tests Must Wait for Auth Redirect (Bob Decision)

**Context:** Jeff's dashboard test artifact navigated to `/login?t=TOKEN` and immediately polled `document.title`. Buster rejected it because the page title was empty at assertion time. The Blazor-driven redirect flow (token validation → cookie set → `NavigateTo("/", forceLoad: true)`) involves a race: between `GotoAsync` returning and the redirect completing, there's a window where `document.title` reflects the login page, then goes empty on the new page before Blazor hydrates `<PageTitle>`.

**Decision:** All Playwright tests that go through the Aspire Dashboard `/login?t=TOKEN` auth flow must:

1. **Gate on redirect completion**: `WaitForURLAsync(url => !url.Contains("/login"))` after `GotoAsync`.
2. **Poll the title with explicit timeout**: `WaitForFunctionAsync` must specify at least 60s timeout — the default 30s is insufficient for Blazor Server cold-start.
3. **Assert flexibly on title content**: Use `Contains("resources")` (case-insensitive), not an exact match like `"Aspire Resources"`, since the title varies across Aspire versions.

**Rationale:** `WaitForURLAsync` closes the redirect race; 60s timeout accommodates Blazor hydration latency; flexible title assertions avoid version/configuration fragility.

**Implementation Status:**
- `dotnet build AspireApp.sln` ✅
- `dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj` ✅
- Targeted test `BasicAspireAppHostTests.AspireDashboardLoads` ✅

**Impact:**
- **Buster:** Accept the revised test as passing the QA gate.
- **Jeff:** Adopt this pattern for any new Aspire Dashboard Playwright tests.
- **All:** The `TestFixture.cs` token/URL capture infrastructure is confirmed sound — only the assertion strategy needed fixing.



---

## Processing Pipeline Retry & Lifecycle Canonicalization — Jarvis & Buster — 2026-03-25

**Scope:** P1 Item 1 — Stabilize canonical uploaded to processing to processed/error lifecycle; prevent duplicate processing

### Processing Retry Reset (Jarvis Decision)

When a file enters processing, the Python service now resets stale processing artifacts in the same lifecycle step. That includes clearing completion/error fields, clearing docling/Neo4j output columns, and deleting any existing document_pages rows for the file.

**Rationale:** Retries were not safe if a previous attempt partially persisted output before failing. The canonical schema enforces UNIQUE(file_id, page_number), so leaving old page rows behind turns recovery attempts into duplicate-write failures instead of a clean retry.

**Impact:**
- Keeps lifecycle transitions canonical: uploaded to processing to processed / error
- Allows failed rows to re-enter batch processing without manual cleanup
- Preserves processed rows unless an explicit new processing attempt is started

### Processing Retries Stay Canonical (Buster Decision)

Failed files rows must be retry-eligible for the Python processing pipeline. The next processing transition must clear stale failure markers before work resumes.

**QA Expectations:**
- Canonical lifecycle stays uploaded to processing to processed / error
- Failed rows remain discoverable by list_unprocessed_documents()
- Retrying a failed row clears stale processing_error and processing_completed_at
- process_document_task() is covered for both success and failure status updates

**Rationale:** Without this rule, batch reprocessing silently skips failures or reports contradictory state (processing with an old completion timestamp/error). That regression passes happy-path tests and still burns operators.

**Verification Status:** All regression tests passed. Contract audit passed. Database schema validated.

---

---

## Document Ingestion Trigger Strategy — Bob, Jeff, Jarvis, Buster — 2026-03-26

**Scope:** P1 gap closure — Web upload does not trigger Python processing pipeline

### The Problem (Jeff's Finding)

User observed: "File uploads successfully and appears in the table. Then nothing happens. Where does Docling parsing trigger?"

Upon deep trace of the .NET and Python code, the answer is: **Upload stops after database persistence. No automatic trigger exists.**

Current architecture:
- .NET FileUploadController writes file to disk and database row, then returns 200 ✅
- Python `/processing/process-all` endpoint exists and processes files ✅  
- **But no wiring connects them — processing only fires via explicit HTTP call** ❌

### Architecture Decision (Bob)

**Adopt a two-phase approach: UI button (Phase 1) now, auto-trigger (Phase 2) next.**

#### Phase 1 — Immediate (Jeff)
Add a "Process" action per-row or batch "Process All" button on the Upload Documents page. Wire it to call the Python service via `PYTHON_SERVICE_URL` (already in env vars). This unblocks manual user workflows and makes the gap visible in the UI.

#### Phase 2 — Next sprint (Jeff)  
After upload succeeds in `FileUploadController`, fire-and-forget a `POST /processing/process-document/{id}` to the Python service. This is the automatic trigger. Use `IHttpClientFactory` with a named client for the Python service. Failure should log a warning but not fail the upload — processing can always be retried manually.

#### What We Explicitly Reject
- **Background poller:** Adds infrastructure complexity for no benefit over a direct HTTP call.
- **File system watcher:** Fragile across Docker bind mounts and doesn't carry the document ID.
- **Message broker:** Over-engineering for current scale.

### Canonical Ingestion Sequence (for Documentation)

```
1. User uploads file via Web UI
2. FileUploadController saves file to data/ directory
3. FileUploadController writes files row (status: "uploaded")
4. [Phase 2] FileUploadController POSTs to Python /processing/process-document/{id}
5. Python sets status to "processing", clears stale artifacts
6. Python resolves file path via resolve_upload_path()
7. Docling processes document → extracts pages
8. LightRAG handoff: copies markdown to INPUT_DIR, POSTs to lightrag/documents/scan
9. Neo4j nodes created for document + pages
10. document_pages rows written to SQLite
11. Status set to "processed" (or "error" with processing_error)
12. Content queryable via /rag/lightrag-query and /rag/search-documents
```

### Ingestion Trigger Is Explicit (Jarvis Confirmation)

Treat the current document-ingestion contract as explicit and database-driven:

- Upload ends after the file is copied into shared storage and a `files` row is created with `status='uploaded'`.
- Processing starts only when a caller invokes `POST /processing/process-document/{id}` or `POST /processing/process-all`.
- There is no automatic poller, startup worker, or hot-folder watcher in the Python service today.
- Files copied into the shared data directory without a matching `files` row and processing call are unsupported and ignored.

### Impact

- **Jeff:** Implement Phase 1 (UI button) and Phase 2 (post-upload HTTP call)
- **Buster:** Update `FlowEndToEnd` test to include the trigger step and assert `status == "processed"` + `document_pages` rows
- **Jarvis:** No changes needed — Python processing endpoints are already correct
- **Docs:** Update cross-service contract documentation with the canonical trigger sequence

**Status:** Active — awaiting implementation.

---

## LightRAG Integration Architecture — Bob, Jarvis, Buster — 2026-03-25

**Scope:** P1 spike review — clarify LightRAG auto-pickup assumption and integration boundary

### The Problem (Bob's Finding)

Original P1 spike assumed document processing would automatically detect LightRAG capability via directory watching. Testing revealed:

- LightRAG's `INPUT_DIR` watches for raw files (PDF, DOCX, TXT), **not pre-parsed markdown**
- Dropping Docling output into a watched directory won't trigger LightRAG ingestion
- The architecture needs **explicit HTTP handoff**, not directory auto-pickup

### Architecture Decision

**Correct boundary is explicit Python → LightRAG API ingestion, not directory-watching.**

The Python processing pipeline should:

1. Export Docling output as markdown ✅
2. Copy that markdown into the shared LightRAG input directory  
3. Explicitly trigger LightRAG ingestion via `POST /documents/scan` (or other documented LightRAG API)
4. If the scan call fails, keep canonical document processing successful and record the LightRAG handoff failure in processing metadata

### LightRAG Storage Backend Wiring (Bob & Jarvis)

When wiring Neo4j into containerized Python services in this repo:

- **Do NOT** pass Aspire's raw bolt endpoint reference as `NEO4J_URI`. The runtime contract must be an explicit `bolt://{host}:{port}` URI; otherwise LightRAG and the Python Neo4j driver receive `tcp://...` and fail at startup.
- In `AppHost.cs`, explicitly select `LIGHTRAG_GRAPH_STORAGE=Neo4JStorage` and pass LightRAG to Python as an HTTP endpoint.

### Runtime Proof Completed (Jarvis)

Live Aspire run validated:

1. ✅ A seeded document was processed through the Python processing pipeline
2. ✅ The Python service handed the generated markdown to LightRAG and triggered `/documents/scan`
3. ✅ Retrieval succeeded through the Python API route `POST /rag/lightrag-query`
4. ✅ Direct Cypher queries against Neo4j showed LightRAG-created nodes confirming Neo4j-backed storage at runtime

**Caveat:** The proof document hit a LightRAG merge-stage failure for one relationship embedding upsert (`NaN` from Ollama), and LightRAG marked that document `failed`. Even so, the chunk/entity data remained queryable through the Python route, so the integration proof is valid while merge stability remains a follow-up hardening task.

### P1 Narrowed Scope (Bob Revision)

Treat `Docling → LightRAG Ingestion (P1)` as partial progress, not closed:

- Keep the Python handoff at the narrowest verified step: stage exported markdown into LightRAG's shared `INPUT_DIR` and call the documented API endpoint
- Make the AppHost contract explicit by selecting `LIGHTRAG_GRAPH_STORAGE=Neo4JStorage`
- Expose LightRAG to dependent services as an HTTP endpoint, not a generic TCP endpoint  
- Do **not** claim queryability or full P1 completion until a live LightRAG ingest/query round-trip is validated operationally

### QA Closure Criteria (Buster)

**Do not reject** the current work as speculative future work; accept it as **partial P1 progress** only.

**Do reject** any attempt to mark P1 complete without:

1. **Live ingest → query round-trip evidence** — Run the real AppHost/container stack, process a known document, query that same content successfully, showing a document-specific marker so we know retrieval came from the ingested artifact
2. **Python retrieval API remains the orchestration path** — Closing evidence must come through `src\AspireApp.PythonServices\app\routers\rag.py`; direct LightRAG UI/API demo does NOT close the roadmap line
3. **Runtime Neo4j storage proof** — Not only source configuration; closure needs runtime evidence from the running LightRAG container plus corroboration that graph activity is landing in Neo4j
4. **Persisted evidence artifact** — Keep exact commands, responses, and observed results in a durable artifact so QA does not depend on verbal claims

### Impact

- `roadmap/Tasks.md` continues to show P1 LightRAG work as open
- QA can now review a narrower, defensible contract  
- A future implementation pass still needs a live container-backed ingest/query validation before P1 can be closed
- **Affected paths:** `src/AspireApp.AppHost/AppHost.cs`, `src/AspireApp.PythonServices/app/routers/processing.py`, `src/AspireApp.PythonServices/app/services/lightrag_handoff_service.py`, `roadmap/Tasks.md`

---

## FlowEndToEnd Test — Regression Vector Requiring Immediate Rewrite — Buster & Team — 2026-03-25

**Scope:** P1 testing — End-to-end ingestion test coverage

### The Problem (Buster's Audit)

The `BasicAspireAppHostTests.FlowEndToEnd` test passes with flying colors but **proves nothing about the ingestion pipeline**. It is a false-positive confidence issue.

**What it actually proves:**
- ✅ Upload form works, file is stored on disk, database row created, table displays file

**What it completely ignores:**
- ❌ Processing trigger (test never calls Python `/process-all` or `/process-document/{id}`)
- ❌ Docling parsing (no assertion on page extraction)
- ❌ Page persistence (no check of `document_pages` table)
- ❌ Markdown export (no filesystem verification)
- ❌ LightRAG handoff (no HTTP call verification, no staging check)
- ❌ Neo4j ingestion (no graph query verification)
- ❌ Status lifecycle (no polling of processing completion)
- ❌ Error detection (processing can fail silently, test passes anyway)

**Risk:** If Python processing service goes offline, Docling container is deleted, Neo4j wiring is broken, or LightRAG service is unreachable—**the test will still pass**. It has no visibility into any of those failures.

### Solution

#### Immediate (P1): Rewrite the Test

Add these checkpoint assertions in order:

1. **Trigger processing:** `POST /processing/process-all` → assert 200 response
2. **Poll for completion:** `GET /processing/status/{file_id}` every 1s for 30s max → assert status ∈ {processed, error}
3. **Verify pages extracted:** Query SQLite → assert `document_pages` row count > 0 and content length > 100
4. **Verify markdown staged:** Check filesystem → assert `{data}/inputs/{file_id}*.md` exists
5. **Verify Neo4j node:** Query Neo4j → assert `MATCH (d:Document {id: $id})` returns 1 node
6. **Check error path:** If status == "error", assert error message is populated and non-empty

#### Short-term (P1): Add Observability Endpoint

Create a new Python endpoint to consolidate status queries:

```
GET /processing/status/{file_id}
Response: {
  "status": "processed|error|processing",
  "pages_extracted": 42,
  "neo4j_node_id": "node:123",
  "markdown_staged": true,
  "error_message": null,
  "processing_started_at": "2026-03-25T14:30:00Z",
  "processing_completed_at": "2026-03-25T14:30:15Z"
}
```

This collapses all 5 checkpoints into a single API call, making the test simpler and more maintainable.

#### Medium-term (P2): Edge Cases

- **Error path:** Upload → process fails → status == "error" with readable message
- **Timeout path:** Processing takes > 30s (or hangs forever) → test fails fast instead of hanging
- **Concurrent uploads:** Multiple simultaneous uploads don't corrupt shared state
- **Cleanup:** Test deletes uploaded file after assertions (no artifact leaks)

### Implementation Checklist

- [ ] Rewrite `FlowEndToEnd` to add processing trigger: `POST /processing/process-all`
- [ ] Add polling loop: `GET /processing/status/{file_id}` with 1s interval, 30s timeout
- [ ] Add database assertion: `SELECT COUNT(*) FROM document_pages WHERE file_id = ?` > 0
- [ ] Add filesystem check: verify markdown file exists at staging location
- [ ] Add Neo4j check: query graph database for Document node
- [ ] Add error handling: if processing fails, surface error_message in test assertion
- [ ] Optional: Create `/processing/status/{id}` endpoint to consolidate observability
- [ ] Run revised test locally against running Aspire stack to validate all assertions pass
- [ ] Add to CI pipeline so future PRs cannot regress ingestion coverage

### Impact

- **Test Category:** Regression vector → Regression gate
- **Scope:** End-to-end ingestion pipeline (upload → process → retrieve) is now **testable and provable**
- **Team:** Buster maintains this test; Jarvis updates endpoints if observability is added; Jeff validates test passes before merge
- **When:** P1 (same phase as processing pipeline stabilization)

**Rationale:** The ingestion pipeline works in production. But it's currently invisible to the test harness. A test that can't see the pipeline can't prevent regressions. This decision brings the test to parity with the actual runtime behavior and makes the ingestion flow **testable and repeatable**.

---

## Roadmap Status Tracking & Challenge Log — Bob — 2026-03-25

**Date:** 2026-03-25  
**By:** Bob (Lead/Architect)  
**Triggered by:** User directive via Copilot  

### What Changed

Updated `roadmap/Tasks.md` to enforce status tracking discipline and surface emerging challenges:

1. **Maintainer Reminder** — Added blockquote alert at top reminding team to update roadmap during implementation  
2. **Implementation Challenges & Revisit Items** — New section tracking:
   - Infrastructure risks (volume mount validation)
   - Architectural unknowns (LightRAG story)
   - Technical debt signals (config propagation, sparse tests)
   - Performance concerns (Neo4j batch write profiling)

### Why This Matters

- **Information Loss Risk**: Without active tracking, progress gets lost between sessions  
- **Visibility**: Challenges surface early, preventing late-stage surprises  
- **Accountability**: Explicit reminder in the document forces intentional updates, not one-off checkins  

### Process Rule

From now on: roadmap edits should happen **during or immediately after** task completion, not retroactively. The reminder is the nudge.

### Challenges Logged

Five initial challenges captured:
- Gate B may fail silently (volume mount bugs)
- LightRAG integration surface unclear
- Weak environment variable testing
- Test coverage gaps
- Neo4j write performance not yet profiled

---

## User Directive — Roadmap Maintenance — 2026-03-25

**By:** Eric VanArtsdalen (via Copilot)  
**What:** Keep `roadmap/Tasks.md` updated as work progresses so status does not get lost, and track challenges or revisit-later items that may become important in future implementation.  
**Why:** User request — captured for team memory

---
