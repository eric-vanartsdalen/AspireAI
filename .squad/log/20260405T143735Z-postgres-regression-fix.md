# Session Log — Postgres Regression Fix & Pivot Decisions

**Date:** 2026-04-05  
**Session Duration:** Multi-agent spawn (Jeff, Jarvis, Buster) + Kujan adversarial review + Verbal strategy synthesis  
**Coordinator:** Eric Van Artsdalen  

---

## Executive Summary

This session consolidated three completed agent spawns (Web Postgres cutover, Python Postgres cutover, regression verdict) and synthesized two major strategic documents (architecture review and product pivot decisions) into the decisions inbox.

**Outcome:** Postgres upload-store migration is verified complete. BRAIN pivot strategy approved and documented.

---

## What This Session Accomplished

### 1. Postgres Upload-Store Migration Completion ✅

**Status:** All three surfaces (AppHost, Web, Python) now use Postgres `appdb` for operational upload/processing state.

**Jeff (Web):**
- Swapped EF Core from SQLite to Npgsql
- Removed SQLite-specific boilerplate (DeleteJournalModeInterceptor, CheckpointDatabaseAsync, path resolution)
- Connected via Aspire `.WithReference(postgres)`
- Manual AppHost tuning required; now functional

**Jarvis (Python):**
- Replaced sqlite3 with psycopg2 connection pool
- Removed multi-candidate path resolution and fresh-read workarounds
- Updated contract audit to derive database name from AppHost instead of hardcoding
- All 30 tests pass

**Buster (QA):**
- Diagnosed regression tests as stale (false negatives, not product breaks)
- Updated WebTest fixture expectations
- Established pattern: contract tests derive values from source (AppHost), not hardcoded literals

**What This Eliminates:**
- ~400 lines of SQLite-specific complexity across Web + Python
- Journal-mode conflicts, WAL synchronization issues, multi-candidate path resolution
- CheckpointDatabaseAsync calls, pragma tuning, fresh-connection workarounds

### 2. Decisions Inbox Consolidation

Eight decision files prepared for merging into `decisions.md`:

| File | Scope | Status |
|------|-------|--------|
| `bob-postgres-cutover.md` | Architectural decision, execution order, schema mapping | ✅ Ready |
| `jeff-postgres-cutover.md` | Web-specific implementation, NuGet/connection changes | ✅ Ready |
| `jarvis-postgres-cutover.md` | Python-specific implementation, connection config | ✅ Ready |
| `jarvis-postgres-contract-check.md` | Contract audit refactor (AppHost-derived naming) | ✅ Ready |
| `buster-regression-verdict.md` | Regression root cause (test harness, not product) | ✅ Ready |
| `kujan-arch-review.md` | Adversarial architecture review (28 KB); Gap analysis, extensibility, service boundaries | ✅ Ready |
| `brain-pivot-decisions.md` | Product pivot: BRAIN is core, 9 key decisions, superseded old roadmap | ✅ Ready |
| `verbal-strategy-review.md` | Strategic product critique, MVP definition, scope warnings, new phase sequence | ✅ Ready |

### 3. Cross-Agent Coordination Context

**Note:** Postgres cutover spawns are complete. BRAIN pivot decisions now activated:

- Postgres migration is **foundational infrastructure** for the pivot (Aspire orchestration, multi-service deployment, clean contracts).
- Old roadmap phases (4-8: Flat RAG, LightRAG, plugin ecosystem) are **superseded** by BRAIN phase sequence.
- Next immediate work: Define BRAIN core contracts (CanonicalDocument, KnowledgeResult, ReasonResponse) and build first vertical slice.

---

## Decisions Merged into Inbox

### Postgres Cutover Chain (5 files)
1. **bob-postgres-cutover.md** — Architectural decision, schema mapping, execution order
2. **jeff-postgres-cutover.md** — Web implementation (NuGet, Program.cs, AppHost wiring)
3. **jarvis-postgres-cutover.md** — Python implementation (psycopg2, env config, schema init)
4. **jarvis-postgres-contract-check.md** — Contract audit pattern (AppHost-derived names)
5. **buster-regression-verdict.md** — Regression root cause (test harness, not product)

**Cross-Cutting:** All five decisions reference the same architectural migration. No exact duplicates; some overlap in rationale. Merge strategy: Keep separate sections, consolidate rationale where edges touch.

### BRAIN Pivot Chain (3 files)
6. **kujan-arch-review.md** — Adversarial review (28 KB). Gaps: Validation, Reasoning, Application layers; service decomposition; extensibility vs. monolith.
7. **brain-pivot-decisions.md** — 9 key decisions: BRAIN is core, domain-agnostic MVP, LightRAG behind abstraction, Python/C# split, tenancy by design, vector store, internal packages first, feature branch, proactive core.
8. **verbal-strategy-review.md** — Strategic critique: vision-roadmap misalignment, scope risks, MVP definition (QA intelligence slice), multi-tenant deferral, risk register, new phase sequence.

**Cross-Cutting:** Kujan provides gap analysis; Verbal provides strategic recommendations; brain-pivot-decisions applies them. All three are complementary (not conflicting). No exact duplicates.

---

## Deduplication Notes

**Postgres decisions:** Minor overlap in rationale (e.g., both Jeff and Jarvis explain why Postgres is better than SQLite). These are implementation-specific; keep separate sections.

**BRAIN decisions:** Kujan, Verbal, and brain-pivot provide different lenses (architecture audit, strategic critique, decision statements). No exact duplicates. Each adds distinct value.

**No full duplicates found** across all eight inbox files. Merge is straightforward consolidation.

---

## Squad Context

### Completed Spawns
- ✅ **Jeff (web-postgres-cutover):** Web upload store now uses Aspire-managed Postgres
- ✅ **Jarvis (jarvis-postgres-contract-check):** Python contract audit updated; derives DB name from AppHost
- ✅ **Buster (buster-regression-verdict):** Ruled recent failures as test/harness regressions; updated WebTest fixture

### New Coordination
- **Kujan (adversarial review):** Provided architecture gap analysis for BRAIN pivot
- **Verbal (strategy review):** Provided strategic critique and MVP/scope recommendations
- **Eric (manual AppHost tuning):** Ensured AppHost loads correctly after initial Postgres wiring

### Next Phase
- **Bob (cross-service contracts):** Update `CROSS_SERVICE_CONTRACT.md` to reflect Postgres as canonical (not SQLite)
- **BRAIN Core:** Define CanonicalDocument, KnowledgeResult, ReasonResponse contracts and first vertical slice
- **Domain Selection:** Choose first domain (QA intelligence recommended by Verbal)

---

## Decision Merge Strategy

1. Merge all 5 Postgres decisions as dated sections in `decisions.md`, ordered by decision date (oldest first)
2. Merge Kujan adversarial review as its own section (large, structured, but coherent)
3. Merge BRAIN pivot chain (kujan → verbal → brain-pivot-decisions) as consecutive sections to preserve analytical flow
4. Delete all 8 inbox files after merge
5. Commit `.squad/decisions/` changes to git

---

## Decisions to Archive

Current `decisions.md` is ~8.5 KB (within target). After merge, expect ~35-40 KB. Archive entries older than 30 days to `decisions-archive.md` to keep current file manageable.

**Candidates for archive:**
- SQLite startup schema self-repair (2025-11-02)
- SQLite startup QA gate (2025-11-02)
- FastAPI proof surface (2025-11-02)
- FastAPI proof gate (2025-11-02)
- FlowEndToEnd API-backed upload state (2025-11-02)
- Python service startup path resolution (2026-03-27)
- Legacy schema test update (2026-03-27)
- Optional docling smoke coverage (2026-03-28)
- Docling smoke gate alignment (2026-03-28)

Total: 9 entries, ~7 KB. Archive these to `decisions-archive.md` with a dated section.

---

## Orchestration Logs Created

Four orchestration logs created for agent context:

1. `20260405T143735Z-jeff.md` — Web Postgres cutover
2. `20260405T143735Z-jarvis.md` — Python Postgres cutover
3. `20260405T143735Z-buster.md` — Regression verdict
4. *(Leadership/strategic documents not part of orchestration log chain; recorded in decisions/inbox)*

---

## Next Steps for Scribe

1. ✅ Create orchestration logs (3 files: Jeff, Jarvis, Buster)
2. ⏳ Create session log (this file)
3. ⏳ Merge 8 inbox decision files into `decisions.md`
4. ⏳ Deduplicate `decisions.md`
5. ⏳ Archive old decisions (9 entries, ~7 KB) to `decisions-archive.md`
6. ⏳ Append cross-agent updates to Jeff, Jarvis, Buster histories
7. ⏳ Git commit .squad/ changes

---

**Session Recorded:** 2026-04-05 14:37:35Z
