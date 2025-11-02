---
post_title: "Copilot Instruction Improvement Plan"
author1: "GitHub Copilot"
post_slug: "copilot-instruction-plan"
microsoft_alias: "na"
featured_image: ""
categories: ["internal"]
tags: ["copilot", "instructions"]
ai_note: "Generated with GitHub Copilot (GPT-5-Codex Preview)."
summary: "Plan to streamline .github guidance for AspireAI."
post_date: "2025-11-02"
---

## Purpose
- Define a concise roadmap to rebuild the `.github` guidance so every Copilot session begins with a focused `copilot-instructions.md` and accesses only the context it needs.

## Current State Assessment
- **Solution Overview**: Aspire-hosted orchestration (`src/AspireApp.AppHost`) bootstraps the Blazor UI (`src/AspireApp.Web`), minimal API (`src/AspireApp.ApiService`), Python FastAPI workers (`src/AspireApp.PythonServices`), Neo4j container, and Ollama model; shared data lives under `data/` and `database/`.
- **Master Instructions** *(2025-11-02 update)*: Rewritten `copilot-instructions.md` now provides a concise overview, run/test checklist, troubleshooting guide, and instruction lookup table within ~200 lines.
- **Sub-instruction Drift** *(2025-11-02 update)*: All files in `.github/instructions/` audited. Legacy directives (e.g., `dotnet-framework`, `tasksync`) re-scoped; C#, Blazor, Markdown, Python, and architecture guidance harmonized with current tooling and include decision logs.
- **Prompt Library**: `.github/prompts/*.prompt.md` focuses on Playwright and EF scenarios but misses Aspire-specific flows (e.g., orchestrator debugging, Neo4j primers, Python ingestion tips) and lacks metadata describing when to use each prompt.
- **Tasks & Memory Bank**: `.github/tasks/` is empty; `memory-recall.instructions.md` now documents an opt-in workflow and clarifies that no memory files exist unless requested.

## Objectives
- Keep every guidance file below 400 lines while preserving enough context for day-to-day work.
- Establish a single entry-point `copilot-instructions.md` that summarizes repo facts, explains how to resolve queries, and links to topic files via a consistent index.
- Reconcile or retire contradictory instructions so Copilot follows realistic processes aligned with AspireApp tooling.
- Curate prompts and optional task templates that map cleanly to product areas (Blazor UI, API, Python ingestion, Neo4j, orchestration, testing).

## Target Information Architecture
- **`copilot-instructions.md`**: 200-line cap; include repo snapshot, quick-start checklist, toolchain health checks, and a directory of specialized guidance (instructions, prompts, tasks).
- **`instructions/`**: Group by domain (`platform`, `dotnet`, `blazor`, `python`, `data`, `docs`); retire or merge legacy files; add short scope statements and mutual exclusivity notes.
- **`prompts/`**: Prefix with functional area (e.g., `aspire-`, `blazor-`, `python-`); embed front-matter-style metadata (use cases, dependencies, sample inputs); ensure each prompt references matching instruction sections.
- **`tasks/`**: Introduce lightweight templates for feature work, bug triage, and research spikes; align with any future memory-bank process without forcing unnecessary bureaucracy.
- **`memory-bank/`**: Create the referenced core documents or adjust instructions to reflect the actual workflow.

## Progress
- **2025-11-02**: WS1 completed—`copilot-instructions.md` reorganized with quick-start checklist, troubleshooting cheatsheet, and instruction index.
- **2025-11-02**: WS2 completed—`instructions/` directory pruned and rewritten with scoped guidance, optional TaskSync flow, and legacy notes.
- **2025-11-02**: WS3 metadata retrofit underway—prompt headers normalized with owner/audience/dependency fields, architecture prompt trimmed, and command examples added to Aspire, Python, AI, and Playwright helpers.
- **2025-11-02**: WS4 completed—memory bank files provisioned with dated sections, task templates added, backlog aligned, and guardrails documented.

## Workstreams
- **WS1 – Core Instruction Rewrite** ✅
  - Output: streamlined `copilot-instructions.md` matching target information architecture with instruction lookup table.
- **WS2 – Instruction Pruning & Consolidation** ✅
  - Output: refreshed architecture, C#, Blazor, Markdown, Python, SQL, TaskSync, and memory guidance; legacy .NET Framework instructions quarantined.
- **WS3 – Prompt Library Refresh** 
  - Catalogue existing prompts with owner, prerequisites, and sample invocation text.
  - Author new prompts for Aspire dashboard troubleshooting, Neo4j cypher prototyping, Python ingestion debugging, and AI evaluation scripts.
  - Add usage metadata headers (audience, prerequisites, related instructions) to each prompt; enforce <200 lines.
  - Cross-check each prompt against the instruction index and ensure related guidance links resolve.
  - Document WS3 acceptance criteria so maintainers know when the library is “green”.

  **Inventory – 2025-11-02**
  | Prompt | Focus | Metadata Coverage | Notes |
  |--------|-------|-------------------|-------|
  | `ai-evaluation-scripts.prompt.md` | Ollama evaluation scripts | ✅ Use cases, dependencies, inputs, related instructions | Sample command block added (2025-11-02). |
  | `architecture-blueprint-generator.prompt.md` | Repo architecture blueprint | ✅ Detailed config variables and metadata | Trimmed to <200 lines with Aspire-focused guidance (2025-11-02). |
  | `aspire-dashboard-troubleshooting.prompt.md` | Aspire orchestration triage | ✅ | Command examples added for dashboard triage (2025-11-02). |
  | `csharp-async.prompt.md` | Async best practices | ✅ | Tool list realigned with available helpers (2025-11-02). |
  | `csharp-docs.prompt.md` | XML documentation guidance | ✅ | Tool list realigned with available helpers; sample content still mirrors Microsoft conventions. |
  | `ef-core.prompt.md` | EF Core patterns | ✅ | Add link to any EF migration scripts if created. |
  | `neo4j-cypher-prototyping.prompt.md` | Cypher query prototyping | ✅ | Reference actual schema once stabilized. |
  | `playwright-automation-fill-in-form.prompt.md` | Playwright form automation | ✅ | Updated to demand explicit environments and include command snippets (2025-11-02). |
  | `playwright-explore-website.prompt.md` | Playwright exploratory testing | ✅ | Exploration workflow rewritten with explicit tooling steps (2025-11-02). |
  | `playwright-generate-test.prompt.md` | Playwright test generation | ✅ | Adds command examples; align with repo test harness once finalized. |
  | `python-ingestion-debugging.prompt.md` | FastAPI ingestion debugging | ✅ | Command examples added for pytest and diagnostics (2025-11-02). |
  | `sql-code-review.prompt.md` | SQL review guidance | ✅ | Condensed to Aspire-focused checklist with security/perf template (2025-11-02). |
  | `sql-optimization.prompt.md` | SQL tuning | ✅ | Trimmed to pragmatic tuning steps for staging/import scripts (2025-11-02). |

  **Schema Status – 2025-11-02**
  - Created `prompts/README.md` documenting required front matter fields (`owner`, `audience`, `dependencies`, `last_reviewed`, etc.) and maintenance checklist.
  - Next action: retrofit existing prompts to the schema once priorities below are addressed.

  **Follow-up Priorities**
  1. ✅ Trim `architecture-blueprint-generator.prompt.md` to stay under 200 lines while preserving configuration matrix (2025-11-02).
  2. ✅ Add concrete command examples to `aspire-dashboard-troubleshooting.prompt.md`, `python-ingestion-debugging.prompt.md`, and `ai-evaluation-scripts.prompt.md` (2025-11-02).
  3. ✅ Align tool identifiers in C# prompts (`csharp-async`, `csharp-docs`) with available helper names (2025-11-02).
  4. ✅ Dry-run SQL review/optimization prompts against `docs/DATABASE_MANAGEMENT.md` schema; captured index and constraint follow-ups (2025-11-02).
  5. (On-Hold - In progress) Coordinate with the Blazor team on a shared Playwright test harness and prepare Playwright prompt updates ahead of the suite launch.
  6. ✅ Spot-check prompt metadata links (`Related Instructions`) for accuracy; log fixes inline or in this plan. *(2025-11-02: Related instruction targets normalized to `../instructions/` and `../copilot-instructions.md` paths.)*
  7. ✅ Define WS3 completion checklist (metadata, commands, instruction links, <200 lines) and share with maintainers for sign-off.
- **WS3 Completion Checklist** *(2025-11-02 draft)*
  - Every prompt contains top-level metadata keys (`description`, `agent`, `tools`, `owner`, `audience`, `dependencies`, `last_reviewed`).
  - `## Metadata` section includes **Use Cases**, **Dependencies/Prerequisites**, **Sample Inputs**, and **Related Instructions** with working relative links.
  - Prompt body documents goal statement, actionable steps, and validation commands targeted to Aspire environments.
  - All related instruction references resolve to `../instructions/` or `../copilot-instructions.md`; external docs use relative repo paths where possible.
  - `last_reviewed` date updated when content changes and never older than the most recent retrofit cycle.
  - File length remains under 200 lines and avoids redundant context already covered in `copilot-instructions.md`.
  - Validation log captured in this plan (what changed, date) before marking the prompt complete.
- **WS3-5 Playwright Harness Plan**  (On-Hold - In progress)
  - Confirm Blazor team point of contact and harness owner; capture target ship date and supported browsers (ETA: 2025-11-04).
  - Gather current Blazor UI regression scenarios to seed shared Playwright suites and note environment prerequisites (Aspire host, seeded data, auth state).
  - Draft harness acceptance criteria covering command entry points, CI hooks, and reporting so prompts can reference stable workflows.
  - Prepare updates for `playwright-generate-test.prompt.md`, `playwright-automation-fill-in-form.prompt.md`, and `playwright-explore-website.prompt.md`, adding harness usage steps and dependency notes.
  - Schedule prompt retrofit review once the harness ships; include validation steps (run suite locally, capture sample output) before marking priority 5 complete.
- **WS4 – Task & Memory Bank Alignment** ✅
  - Either provision the referenced `memory-bank` files (project brief, active context, etc.) or simplify the instruction so Copilot is not forced to read non-existent files.
  - Populate `.github/tasks/` with example templates and a README describing when maintainers should add task files.
  - Define a guardrail process to archive obsolete plans and avoid stale instructions.
  - **2025-11-02 update**: Memory bank directory audited; added `Last reviewed` stamps, dated sections, and README guardrails (append new entries beneath the marker). Created task templates (`feature`, `bug`, `research`), aligned `_index.md` with the active backlog (added TASK002 & TASK003) with owner/milestone placeholders, and documented archive expectations in `tasks/README.md`.
  - **2025-11-02 completion check**: Maintainers confirmed templates, guardrails, and backlog updates meet WS4 acceptance criteria.
- **WS5 – Verification & Adoption** 
  - Run a dry-run Copilot session using the new instructions to ensure directive hierarchy is consistent and no loops remain.
  - Share the plan with maintainers for approval, track decisions, then execute updates in manageable PRs.
  - Document validation steps (build, Aspire run, lint) inside the master instructions so future updates remain testable.

## Dependencies & Risks
- **Stakeholder Alignment**: Need confirmation from maintainers on whether strict DDD and financial compliance rules remain requirements.
- **Process Tooling**: If memory-bank automation is expected, scope a lightweight script or acknowledge manual upkeep.
- **Change Fatigue**: Large instruction rewrites can confuse contributors; plan incremental merges and communicate changes in README or changelog.
- **Validation Coverage**: Ensure prompts referencing external services (Ollama, Neo4j) document prerequisites to prevent run failures.

## Immediate Next Steps
- Review this plan with project owners and capture approvals or edits.
- Start WS3 discovery: inventory existing prompts, draft metadata schema, and identify gaps tied to Aspire orchestration, Neo4j, and Python ingestion.
- Evaluate appetite for `.github/tasks/` templates before making structural changes (feeds into WS4).


Last updated: 2025-11-02 (WS3 completion checklist drafted; WS3-5 Playwright harness plan drafted; WS4 task & memory templates added; prompt metadata links audited)
