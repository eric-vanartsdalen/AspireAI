---
applyTo: '**'
description: 'Memory recall and behavior guidelines for AI assistant'
---

# Memory & Context Guidance

## Scope
- Use this guidance to stay oriented between sessions. AspireAI currently has no automated memory-bank tooling—treat these steps as lightweight best practices.
- If the user supplies a memory-bank archive or asks to maintain one, follow their instructions first.

## Core Principles
1. **User directives win**: Always follow explicit user commands, even if they differ from these defaults.
2. **Stay factual**: Verify repository details with the available tools instead of relying on stale assumptions.
3. **Keep responses concise**: Explain what you are doing and why, without unnecessary filler.

## Working Session Routine
- At the start of a task, skim `copilot-instructions.md` and any relevant files the user mentions.
- Summarize the current goal in your own words before making changes. This becomes your lightweight “active context.”
- After each major change, note the outcome in chat so future sessions understand what was done and why.

## Memory Bank Files
- `.github/memory-bank/` contains ready-to-edit Markdown files (`projectbrief.md`, `activeContext.md`, `progress.md`, plus supporting context notes).
- Only update these files when the user explicitly requests persistent memory tracking.
- Add a `Last reviewed: YYYY-MM-DD` line when making edits and keep sections concise (bullets or short paragraphs).
- Archive stale content by moving older sections to the bottom of the file under a dated heading; avoid deleting historical decisions.
- When new updates are required, append a fresh dated block immediately below the `Last reviewed` line instead of rewriting prior entries, then update the date and push older sections downward.

## Task Tracking
- `.github/tasks/` now includes a README, templates (`feature-template.md`, `bug-template.md`, `research-template.md`), and sample entries.
- When formal tracking is requested, duplicate the closest template, add it to `_index.md`, and keep `Status`, `Added`, and `Updated` fields current.
- Log material progress in the task file after each development milestone; link to PRs or commits for traceability.
- Archive completed tasks older than three months by moving them to a dated subfolder under `.github/tasks/archive/` (create the folder on first use).

## Decision Log
- 2025-11-02: Full memory-bank workflows disabled until maintainers supply supporting files or automation.