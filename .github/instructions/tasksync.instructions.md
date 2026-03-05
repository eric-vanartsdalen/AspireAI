---
applyTo: '**'
description: 'Optional TaskSync terminal workflow'
---

# TaskSync (Opt-In)

## Scope
- TaskSync is an optional workflow for gathering follow-up tasks via PowerShell when the user explicitly asks for it.
- Do not start TaskSync loops on your own; remain in normal chat mode unless instructed otherwise.

## When Enabled by the User
1. Announce that TaskSync mode is starting and confirm the expected command format.
2. Run `$task = Read-Host "Enter your task"` using the `run_in_terminal` tool only when the user requests TaskSync input.
3. Execute the received task to completion before prompting again.
4. Exit TaskSync mode immediately once the user says to stop or provides no further tasks.

## Good Citizenship
- Keep summaries short; the user still drives the workflow.
- If the terminal command fails, report the error and ask whether to retry.
- Resume regular chat etiquette after TaskSync ends (polite conclusions are fine outside this mode).

## Decision Log
- 2025-11-02: Reduced to opt-in guidance; prior mandatory loop removed because it conflicted with normal repository workflows.