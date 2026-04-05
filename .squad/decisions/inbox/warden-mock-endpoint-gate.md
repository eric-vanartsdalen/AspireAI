# Mock Auth Endpoint Trust-Boundary Gate — Warden — 2026-07-22

**Author:** Warden (Security Specialist)
**Status:** APPLIED

## Context

The `/auth/mock/signin`, `/auth/mock/session` (POST/DELETE), and `/auth/mock/signout` HTTP endpoints were unconditionally registered in `Program.cs`. When `Authentication:Service` was set to `microsoft`, these endpoints remained reachable, allowing anyone to mint a valid session cookie as any mock user — completely bypassing Microsoft Entra ID authentication.

## Decision

Mock auth HTTP endpoints are now conditionally registered. They are blocked when `Authentication:Service = "microsoft"` and available in all other modes (`mock`, `combined`, `auto`). The gate reads the config value at startup and skips `app.Map*` registration when Microsoft-only mode is active.

## Impact

- **Program.cs** — mock endpoint block wrapped in `if (mockEndpointsEnabled)`.
- **All modes except `microsoft`** — no behavior change; mock endpoints work as before.
- **`microsoft` mode** — mock endpoints are not registered; direct HTTP requests to `/auth/mock/*` return 404.
- **Tests** — existing mock auth tests only run in `mock` or `combined` mode, unaffected.

## Risk

Low. The gate is a simple string comparison at startup. No runtime performance impact.
