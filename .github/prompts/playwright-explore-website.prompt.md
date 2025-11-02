agent: 'agent'
description: 'Website exploration for testing using Playwright'
tools: ['run_in_terminal', 'read_file', 'apply_patch']
owner: '@eric-vanartsdalen'
audience: 'QA Contributors'
dependencies: ['Node.js 20+', 'Playwright CLI']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Exploring websites to identify functionalities and generate test cases using Playwright.
- **Dependencies**: Playwright library, access to the website URL.
- **Sample Inputs**: Website URL to explore.
- **Related Instructions**: See `../instructions/blazor.instructions.md` for Aspire UI context and `../instructions/memory-recall.instructions.md` for testing workflow checks.

# Website Exploration for Testing

Your goal is to explore the website and identify key functionalities.

## Specific Instructions

1. Confirm the environment (local, staging, production) and authentication expectations before launching Playwright.
2. Use `npx playwright codegen <url>` or an exploratory script to navigate the requested flows; prefer data-test identifiers over brittle CSS selectors.
3. Capture 3–5 critical user journeys, recording steps, locators, and expected outcomes in a structured table.
4. Flag any blockers (auth prompts, network errors) and gather console/network traces where relevant.
5. Close the browser context or stop tracing once exploration finishes to free resources.
6. Summarize findings and outline candidate automated tests, noting prerequisites or fixtures each test needs.

## Example Commands
- `npx playwright codegen https://localhost:5001`
- `npx playwright show-trace trace.zip`
- `npx playwright test --ui`
