description: 'Automate filling in a form using Playwright for regression or smoke testing'
agent: 'agent'
tools: ['run_in_terminal', 'read_file', 'apply_patch']
owner: '@eric-vanartsdalen'
audience: 'QA Contributors'
dependencies: ['Node.js 20+', 'Playwright CLI']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Automating web form filling for testing or data entry using Playwright.
- **Dependencies**: Playwright library installed, access to the form URL, form field details.
- **Sample Inputs**: Form URL, field names and values to fill (e.g., text fields, dates, file uploads).
- **Related Instructions**: See `../instructions/blazor.instructions.md` for UI interaction patterns and `../instructions/memory-recall.instructions.md` for automation workflow guardrails.

# Automating Filling in a Form with Playwright MCP

Your goal is to automate the process of filling in a form with data supplied at runtime while keeping tests deterministic.

## Specific Instructions

1. Confirm the target URL, test data source, and expected submission behavior. Never assume placeholder links—ask the requester for the correct environment (local, staging, production).
2. Generate a Playwright script (TypeScript preferred) that:
	- Navigates to the provided form URL using project credentials or test users.
	- Waits for key fields using resilient locators (data-test-id, `aria-label`, etc.).
	- Fills each field from a structured payload (JSON or fixture file) so the script is reusable.
	- Uploads files via `page.setInputFiles` when required.
	- Captures a screenshot before submission and pauses for reviewer confirmation.
3. Request explicit approval before triggering `page.click` on the submit action. Expose a helper (`await page.pause()` or CLI flag) so reviewers can inspect the filled form first.
4. Add assertions (text presence, validation messages) to guard against layout drift.
5. Document any environment variables or secrets the script needs.

## Example Commands
- `npx playwright codegen https://localhost:5001/test-form`
- `npx playwright test tests/form-fill.spec.ts --project=chromium`
- `npx playwright show-trace trace.zip`
