# Local Managed Auth QA Contract — Buster — 2026-04-06

**Author:** Buster (QA / Tester)  
**Status:** APPROVED  
**Scope:** Regression coverage for the managed local username/password auth slice

## Decision

Treat the local auth test gate as a layered seam contract:

1. **Config + factory:** `AuthenticationOptions` and `AuthServiceFactory` must prove `auto`, `local`, `microsoft`, and `combined` resolution without hardcoded UI assumptions.
2. **Provider metadata:** `AuthProviderOption.RequiresCredentials` and `SignInPath` are part of the contract. If either regresses, the sign-in UI no longer knows whether to render a user picker, a credential form, or a hosted redirect.
3. **Component UX:** `SignInPanel` must select the managed local provider from the query string, render a plain HTML credential form that posts to `/auth/local/signin`, and show only the generic invalid-credentials message (`"We couldn't sign you in with those credentials."`).
4. **Operational store logic:** `LocalAccountAuthenticator` and `LocalAuthBootstrapper` must run against `UploadDbContext` in unit tests. `Microsoft.EntityFrameworkCore.InMemory` is the approved harness for that coverage inside `AspireApp.WebTest`.
5. **Regression floor:** mock and Microsoft flows must stay green in the same run; local auth coverage does not excuse breakage in existing providers.

## Why

This keeps QA honest without requiring live Microsoft Entra credentials or a running PostgreSQL instance for every auth-unit scenario. It also proves the new local slice is using the existing pluggable seam instead of special-casing the UI or bypassing the operational store model.

## Validation

- Focused auth/local test pass in `AspireApp.WebTest`
- Full repo `dotnet test --no-restore` pass
- No provider-specific credential leakage in the sign-in component error path
