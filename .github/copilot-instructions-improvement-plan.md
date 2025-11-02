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
- **Master Instructions**: `.github/copilot-instructions.md` duplicates README setup details, carries encoding noise, and lacks a quick index that points to topical instructions or prompts.
- **Sub-instruction Drift**: `dotnet-framework.instructions.md` and `tasksync.instructions.md` impose obsolete or impossible behaviors (e.g., C# 7.3 only, forced terminal loops) that clash with the .NET 9 + Aspire reality and higher-priority directives.
- **Markdown Policy Mismatch**: `markdown.instructions.md` demands blog-style front matter and 80-character hard wraps, which is overkill for internal docs and discourages updates.
- **Prompt Library**: `.github/prompts/*.prompt.md` focuses on Playwright and EF scenarios but misses Aspire-specific flows (e.g., orchestrator debugging, Neo4j primers, Python ingestion tips) and lacks metadata describing when to use each prompt.
- **Tasks & Memory Bank**: `.github/tasks/` is empty, while `memory-bank.instructions.md` references files that do not exist, causing noise during Copilot sessions.

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

## Workstreams
- **WS1 – Core Instruction Rewrite**
  - Draft a new outline for `copilot-instructions.md` covering repo overview, run/build checklist, troubleshooting index, and quick links.
  - Validate facts against current solution (projects, Docker needs, AI endpoints) and remove duplicate guidance already handled in README.
  - Add an explicit “Use these sub-instructions when…” table mapping file globs to instruction filenames.
- **WS2 – Instruction Pruning & Consolidation**
  - Audit each file in `instructions/` for accuracy; delete or rewrite `dotnet-framework`, `tasksync`, and other non-applicable directives.
  - Collapse overlapping content (e.g., C# guidance duplicated across files) into modular sections referenced from the master index.
  - Introduce short “Decision Logs” for areas that need future input (e.g., whether to keep DDD mandates).
- **WS3 – Prompt Library Refresh**
  - Catalogue existing prompts with owner, prerequisites, and sample invocation text.
  - Author new prompts for Aspire dashboard troubleshooting, Neo4j cypher prototyping, Python ingestion debugging, and AI evaluation scripts.
  - Add usage metadata headers (audience, prerequisites, related instructions) to each prompt; enforce <200 lines.
- **WS4 – Task & Memory Bank Alignment**
  - Either provision the referenced `memory-bank` files (project brief, active context, etc.) or simplify the instruction so Copilot is not forced to read non-existent files.
  - Populate `.github/tasks/` with example templates and a README describing when maintainers should add task files.
  - Define a guardrail process to archive obsolete plans and avoid stale instructions.
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
- Begin WS1 & WS2 discovery: inventory instruction files, log keep/merge/remove decisions, and collect any missing facts from code owners.
- Prototype the new `copilot-instructions.md` outline in a draft branch, keeping a change log for future revertability.
- Schedule follow-up to design prompt metadata schema before rewriting existing prompt files.


Last updated: 2025-11-02
