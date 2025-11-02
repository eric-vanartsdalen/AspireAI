agent: 'agent'
description: 'Generate a Playwright test based on a scenario using Playwright'
tools: ['run_in_terminal', 'read_file', 'apply_patch']
owner: '@eric-vanartsdalen'
audience: 'QA Contributors'
dependencies: ['Node.js 20+', 'Playwright CLI']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Generating Playwright test scripts based on user scenarios, automating test creation and execution.
- **Dependencies**: Playwright library, test scenario description.
- **Sample Inputs**: Test scenario description (e.g., steps to perform on a website).
- **Related Instructions**: See `../instructions/blazor.instructions.md` for front-end structure notes and `../instructions/memory-recall.instructions.md` for Playwright workflow expectations.

# Test Generation with Playwright MCP

Your goal is to generate a robust Playwright test based on the provided scenario and validate it end-to-end.

## Specific Instructions

- Request a scenario outline (preconditions, steps, expected outcomes). If missing, prompt the user to supply it.
- Explore the flow with Playwright tools first to validate selectors and state transitions.
- Generate a Playwright TypeScript test using `@playwright/test` only after interaction steps are confirmed.
- Save the test under `tests/` (or the repo’s specified folder) with consistent naming.
- Run `npx playwright test <file>` and iterate until the test passes and assertions are meaningful.
- Summarize verification results and note any data fixtures or environment configuration needed for CI.

## Example Commands
- `npx playwright codegen https://localhost:5001 --save-storage storage.json`
- `npx playwright test tests/account.spec.ts --project=chromium`
- `npx playwright show-report`
