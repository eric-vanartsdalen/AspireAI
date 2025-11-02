---
post_title: "Copilot Instruction Improvement Plan"
author1: "GitHub Copilot"
post_slug: "copilot-instruction-plan"
microsoft_alias: "na"
featured_image: ""
categories: ["internal"]
tags: ["copilot", "instructions"]
ai_note: "Generated with GitHub Copilot."
summary: "Plan to streamline .github guidance for AspireAI."
post_date: "2025-11-03"
---

## Purpose
- This document tracks the active and future workstreams for improving Copilot's guidance and prompt library for the AspireAI repository.

## Current State
- **Core Instructions**: `copilot-instructions.md` is the primary entry point, providing a repository overview, setup checklist, and a lookup table for specialized instructions.
- **Instruction Set**: The `instructions/` directory contains scoped guidance for different domains (e.g., Blazor, Python, C#).
- **Prompt Library**: The `prompts/` directory contains a curated set of prompts with metadata for various tasks.
- **Workstreams**: All major refactoring workstreams (WS1-WS4) are complete. The final step is verification.

## Active Workstreams

### WS5 – Verification & Adoption
- **Goal**: Ensure the entire instruction set is consistent, functional, and free of loops or contradictions.
- **Status**: In Progress
- **Tasks**:
  1.  Perform a dry-run Copilot session using the updated instructions to validate the directive hierarchy.
  2.  Share the updated instruction plan with maintainers for final approval.
  3.  Document validation steps (build, Aspire run, lint) within `copilot-instructions.md` to ensure future updates remain testable.

## Completed Workstreams
- **WS1 – Core Instruction Rewrite**: Streamlined `copilot-instructions.md`.
- **WS2 – Instruction Pruning & Consolidation**: Refreshed guidance in the `instructions/` directory.
- **WS3 – Prompt Library Refresh**: Updated all prompts with metadata and examples.
- **WS4 – Task & Memory Bank Alignment**: Aligned memory/task handling processes.

## Next Steps
1.  Complete WS5 by running a full validation session.
2.  Submit all documentation changes for maintainer review and approval.

Last updated: 2025-11-03 (Plan finalized to reflect completed work)
