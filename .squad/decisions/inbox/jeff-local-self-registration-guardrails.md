# Local Auth Self-Registration Guardrails — Jeff — 2026-04-06

**Status:** Implemented
**Scope:** Local username/password sign-in inside the existing Blazor + cookie auth seam

## Decision

Keep local self-registration inside `LocalAccountAuthenticator` rather than introducing a separate registration endpoint or switching to ASP.NET Identity.

## Why

- The existing seam already centralizes local credential lookup, password verification, and the generic invalid-credentials behavior.
- Warden required username-only auto-create, early password-floor checks, and duplicate-race handling; those rules are safest when enforced before `Program.cs` decides whether to issue a cookie.
- Development needed the feature immediately, but production defaults needed to stay conservative.

## Implementation Notes

- Added `Authentication:Local:AllowSelfRegistration` with default `false`; enabled it only in `src\AspireApp.Web\appsettings.Development.json`.
- Auto-create runs only for trimmed username-shaped identifiers that match `[A-Za-z0-9._-]{3,100}` and only after the 12-character password floor passes.
- Email-shaped identifiers remain lookup-only; failures still collapse to the existing `invalid-credentials` redirect.
- Auto-created users get synthetic local-only emails in the form `{NORMALIZED_USERNAME}@local.aspireai` and default tenant `default`.

## Key Paths

- `src\AspireApp.Web\Services\LocalAccountAuthenticator.cs`
- `src\AspireApp.Web\Services\LocalAuthenticationOptions.cs`
- `src\AspireApp.Web\Program.cs`
- `src\AspireApp.Web\appsettings.Development.json`
- `src\AspireApp.WebTest\Tests\LocalAccountSelfProvisioningTests.cs`
