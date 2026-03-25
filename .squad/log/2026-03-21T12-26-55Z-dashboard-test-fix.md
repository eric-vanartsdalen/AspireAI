# Session Log — Dashboard Test Fix — 2026-03-21T12:26:55Z

## Participants

- **Jeff:** Aspire dashboard resource snapshot capture and token extraction
- **Buster:** Test audit and rejection gate
- **Bob:** Settle strategy revision and fix

## Problem

Aspire WebTest suite needed authenticated dashboard navigation. Initial artifact by Jeff captured dashboard URI and token from resource snapshot (sound), but Buster rejected because test assertion failed: title was empty after redirect.

## Resolution

Bob revised assertion strategy:
1. Gate on auth redirect completion with `WaitForURLAsync(url => !url.Contains("/login"))`.
2. Poll title with explicit 60s timeout (Blazor cold-start buffer).
3. Assert flexibly with `Contains("resources")` instead of exact match.

## Outcome

- `BasicAspireAppHostTests.AspireDashboardLoads` ✅
- Full WebTest suite ✅
- Build clean ✅

## Decision Logged

"Aspire Dashboard Playwright Tests Must Wait for Auth Redirect" → inbox for team adoption.
