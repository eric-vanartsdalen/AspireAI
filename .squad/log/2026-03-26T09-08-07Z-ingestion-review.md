# Session Log — Ingestion Review Spike — 2026-03-26T09:08:07Z

## Context

User Eric requested team investigation into document ingestion pipeline to understand why uploaded files don't automatically trigger processing. Full squad deployed: Bob (architecture), Jeff (.NET), Jarvis (Python), Buster (QA).

## Findings Summary

### Upload-to-Processing Trigger Gap (P1 Blocker)

**Problem:** FileUploadController writes file + DB row, returns 200, **stops**. No automatic trigger to Python processing.

**Root Cause:** Pull architecture (Python pulls when told), not push architecture (Web pushes trigger).

**Solution (Two-Phase):**
1. **Phase 1 (Immediate):** UI button "Process Uploaded Files" (Jeff)
2. **Phase 2 (Next Sprint):** Auto-trigger POST after upload via IHttpClientFactory (Jeff)

**Impact:** Unblocks manual workflows; makes gap visible in UI; enables test instrumentation.

### LightRAG Architecture Clarification

**Assumption (wrong):** Drop Docling output into directory, LightRAG auto-pickups.

**Reality:** LightRAG's `INPUT_DIR` watches raw files only (PDF, DOCX, TXT), not pre-parsed markdown. Explicit API handoff required.

**Correct Architecture:** Docling → Python stages markdown → explicit POST `/documents/scan` → LightRAG ingests.

**Runtime Proof:** Live Aspire run validated seeded document → processing → LightRAG scan → Neo4j storage → query ✅ (caveat: one merge-stage embedding failure, non-blocking).

### Test Regression Vector

**Finding:** `BasicAspireAppHostTests.FlowEndToEnd` passes but proves only upload persistence, not ingestion pipeline.

**Risk:** If Python/Docling/Neo4j/LightRAG goes offline, test still passes (false confidence).

**Solution:** Rewrite test with 5 checkpoint assertions:
1. Trigger: POST /processing/process-all
2. Poll: GET /processing/status/{id}
3. DB: document_pages rows created
4. FS: Markdown staged
5. Neo4j: Document node created

## Decisions Merged

10 inbox decisions consolidated and deduplicated in `.squad/decisions.md`:

- **Bob:** 3 decisions (trigger strategy, LightRAG architecture, P1 narrowing)
- **Jeff:** 1 decision (gap analysis)
- **Jarvis:** 3 decisions (explicit trigger, LightRAG handoff, runtime proof)
- **Buster:** 3 decisions (test audit, proof closure criteria, QA gate)

## Roadmap Impact

- **P1 LightRAG:** Marked as partial progress; closure criteria documented
- **P1 Testing:** FlowEndToEnd rewrite queued (same phase)
- **Phase 2 Queued:** Auto-trigger implementation (next sprint)
- **Challenges Logged:** Volume mounts, LightRAG story, config propagation, test coverage, Neo4j profiling

## Orchestration Logs

Created per-agent logs in `.squad/orchestration-log/`:
- `2026-03-26T09-08-07Z-bob.md` — Architecture analysis, trigger strategy, roadmap updates
- `2026-03-26T09-08-07Z-jeff.md` — Upload path trace, gap identification, Phase 1/2 readiness
- `2026-03-26T09-08-07Z-jarvis.md` — Python verification, LightRAG proof, Neo4j URI validation
- `2026-03-26T09-08-07Z-buster.md` — Test audit, closure criteria, QA gate

## Inbox Cleanup

Deleted 10 inbox decision files after merge:
- `bob-ingestion-trigger-decision.md`
- `bob-lightrag-architecture-2026-03-25.md`
- `bob-lightrag-p1-partial.md`
- `buster-ingestion-test-audit.md`
- `buster-lightrag-proof-gate.md`
- `buster-p1-lightrag-handoff-qa.md`
- `jarvis-ingestion-trigger-clarified.md`
- `jarvis-lightrag-explicit-ingest.md`
- `jarvis-lightrag-runtime-proof.md`
- `jeff-ingestion-trigger-gap.md`

## Next Actions (Team)

- **Jeff:** Phase 1 UI button + Phase 2 auto-trigger implementation
- **Buster:** FlowEndToEnd test rewrite with 5 checkpoint assertions
- **Jarvis:** Optional /processing/status endpoint if needed for observability
- **Bob:** Monitor roadmap P1 LightRAG progress tracking

---

**Completed by:** Scribe (background agent)  
**Time:** 2026-03-26 09:08:07 UTC  
**All decisions merged, inbox cleared, memory updated.**
