# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-03-25):** Archived pre-2026-02-27 entries (6 decisions, ~12 KB) to `decisions-archive.md` due to file size (30.75 KB → 18.5 KB target). Merged 2 inbox decisions: bob-roadmap-tracking-2026-03-25.md, copilot-directive-2026-03-25T14-07-58Z.md. Inbox cleared.


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

## Decision: Roadmap Status Tracking & Challenge Log

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

**Action**: Scribe merges this decision into `.squad/decisions.md` after approval.


---

### 2026-03-25T14:07:58Z: User directive
**By:** Eric VanArtsdalen (via Copilot)
**What:** Keep `roadmap/Tasks.md` updated as work progresses so status does not get lost, and track challenges or revisit-later items that may become important in future implementation.
**Why:** User request — captured for team memory



---

## Decision: Roadmap Status Tracking & Challenge Log

**Date:** 2026-03-25  
**By:** Bob (Lead/Architect)  
**Triggered by:** User directive via Copilot  

### What Changed
Updated oadmap/Tasks.md to enforce status tracking discipline and surface emerging challenges:

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

**Action**: Scribe merges this decision into .squad/decisions.md after approval.

---

## User Directive — Roadmap Maintenance — 2026-03-25

**By:** Eric VanArtsdalen (via Copilot)  
**What:** Keep oadmap/Tasks.md updated as work progresses so status does not get lost, and track challenges or revisit-later items that may become important in future implementation.  
**Why:** User request — captured for team memory
