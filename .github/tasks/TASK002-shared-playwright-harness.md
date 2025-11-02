# TASK002 - Stand Up Shared Playwright Harness

**Status:** Planned  
**Owner:** TBD (awaiting Blazor QA assignment)  
**Added:** 2025-11-02  
**Updated:** 2025-11-02

## Summary
Coordinate with the Blazor team to deliver a reusable Playwright test harness that covers Aspire UI regression flows and feeds the prompt library updates tracked in WS3.

## Success Criteria
- [ ] Harness repository and environment setup documented for Aspire contributors
- [ ] Core smoke suite validated against local Aspire dashboard and Web UI
- [ ] Playwright prompts updated with harness usage guidance and validation steps

## Implementation Plan
1. Confirm harness ownership, supported browsers, and target ship date with the Blazor team.
2. Build or adopt the shared Playwright project, including deterministic test data hooks.
3. Pilot the suite against AspireApp.Web locally and document run commands.
4. Update `playwright-*.prompt.md` files with harness workflows and include validation evidence.

## Milestones
- [ ] Stakeholder sync complete and owner confirmed (ETA: TBD)
- [ ] Harness scaffolding committed with documented run instructions (ETA: TBD)
- [ ] Smoke suite passing on Aspire environments and prompts updated (ETA: TBD)

## Validation
- `npx playwright test` passes on the shared suite.
- Aspire dashboard and Web UI smoke tests succeed locally.
- Updated prompts reviewed by Blazor QA owner.

## Notes
Capture coordination decisions and known blockers in the WS3 workstream section of the improvement plan.
