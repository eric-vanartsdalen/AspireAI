# Playwright Auth UX Contracts

## When to use

- A Blazor auth or onboarding slice is approved conceptually but the UI is still landing.
- QA needs acceptance tests now without creating a second host fixture or hardcoding brittle copy.

## Pattern

1. Reuse the existing Aspire WebTest fixture and browser lifecycle.
2. Add real Playwright acceptance tests for the approved UX gates.
3. Dynamically skip the tests until the target shell exists.
4. Require stable `data-testid` hooks on the UI seams that matter to auth state.
5. Prefer semantic fallbacks (role/name) only for obvious controls like sign-in and sign-out.

## Minimum hooks

- `auth-landing`
- `auth-sign-in-cta`
- `auth-provider-*`
- `auth-user-select` when account selection is multi-step
- `auth-submit-sign-in` when sign-in is multi-step
- `auth-user-display`
- `auth-sign-out`
- `auth-current-tenant` or `data-auth-tenant`

## Why it works

This keeps the QA contract executable, keeps the suite green before the feature lands, and avoids inventing a second test harness that will drift from the live Aspire composition.

## Approval bar

- Do not approve on shell presence alone. A visible landing page, sign-in panel, and sign-out button only prove AUTH-A through AUTH-E style UX seams.
- If the agreed design also requires provider pluggability or multi-layer validation, reject until service/config tests exist alongside the Playwright flow.
- In AspireAI specifically, a DI branch that only resolves `MockAuthService` and throws for any other mode is not a completed pluggable-provider seam.

## Blazor routing note

- For protected Razor routes, prefer `AuthorizeRouteView` + a tiny redirect component that sends anonymous users to `/signin?returnUrl=...`.
- Keep `/` public as the landing page; do not reuse the protected-route prompt as the main landing UX.
- This avoids ambiguous "not authorized" rendering on `/chat` or `/upload` and gives Playwright a deterministic post-logout destination.
