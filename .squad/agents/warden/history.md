# Warden History

## Project Context

- **Owner:** Eric Van Artsdalen
- **Project:** AspireAI
- **Stack:** C# (.NET 9/10), Blazor, Minimal API, Python FastAPI, Neo4j, Ollama, Docker, Aspire
- **Description:** AI-powered document processing and RAG platform with multi-tenant foundations, PostgreSQL operational storage, and an evolving authenticated UX
- **Added:** 2026-04-05T16:47:47Z

## Learnings

- Joined to own security-sensitive work across auth, tenant isolation, and secure application behavior.
- Current active concern: mock auth UX foundation was rejected twice and needs a secure, pluggable revision with clean automated proof before real Microsoft/Google auth is attempted.
- Mock auth now resolves through a config-driven `AuthServiceFactory` and `AddAspireAppAuthentication(...)`, so `Program.cs` no longer hardcodes the mock path.
- The Blazor auth shell depends on interactive routing (`Components\Routes.razor` and `Components\Pages\SignIn.razor`) plus `AppAuthenticationStateProvider` notifications to keep protected UX and sign-in state aligned.
- Service-level auth coverage lives in `src\AspireApp.WebTest\Tests\AuthServiceFactoryTests.cs` and `src\AspireApp.WebTest\Tests\MockAuthServiceTests.cs`; end-to-end auth proof remains in `src\AspireApp.WebTest\Tests\AuthUxFoundationTests.cs`.
- **2026-07-22 — Microsoft Entra ID integration hardened.** Key security decisions:
  - OIDC scheme is registered **conditionally** — only when `Authentication:Microsoft:TenantId`, `ClientId`, and `ClientSecret` are all present. In mock-only mode, no OIDC handler exists; no callback paths are exposed.
  - PKCE enabled (`UsePkce = true`) as defense-in-depth on the authorization code flow.
  - Cookie hardened: `SecurePolicy=SameAsRequest`, 8-hour sliding expiration, `SlidingExpiration=true`.
  - `OnTokenValidated` uses `context.Fail()` (not `throw`) so the OIDC handler returns a proper error response.
  - The `/auth/microsoft/signin` endpoint is only mapped when the OIDC scheme is registered. The universal `/auth/signout` clears the cookie first, then attempts federated sign-out only if the scheme exists.
  - `ClientSecret` removed from committed `appsettings.json` — secrets live in `dotnet user-secrets` only.
  - MainLayout sign-out uses `<a href>` (not `<button @onclick>`) so sign-out works even when the Blazor SignalR circuit is degraded.
  - Trust boundary: Entra `tid` claim is the Azure AD tenant, NOT the application tenant. `ResolveTenantSeed` maps external identity → app tenant via `UserTenantSeeds`/`DomainTenantSeeds` config, defaulting to `"default"`.
  - Three `Authentication:Service` modes: `mock` (demo only), `microsoft` (live Entra only), `combined` (both, via `CompositeAuthService`).
  - Key files: `MicrosoftEntraAuthService.cs`, `MicrosoftEntraAuthenticationOptions.cs`, `CompositeAuthService.cs`, `AuthenticatedUserClaims.cs`.
- **2026-07-22 — Mock endpoint trust-boundary hardened.**
  - `/auth/mock/*` HTTP endpoints now gated behind service mode check in `Program.cs`. When `Authentication:Service` = `microsoft`, mock endpoints are not registered — prevents session-cookie bypass.
  - Jeff's `auto` mode (resolves to `combined` or `mock` at runtime via `AuthServiceFactory.ResolveServiceKey`) is correctly wired and the factory already handles it. The `SignInPanel` direct-sign-in for external providers (no two-click) was already fixed in Jeff's rescue pass.
  - README updated with Azure app registration steps, explicit service mode table, and corrected documentation.
- **2026-07-22 — Authentication `auto` mode security audit: APPROVED.**
  - User reported "UI only allows 2 users to login, so it seems like it's still using the Mock" — this is **correct behavior** because no Microsoft credentials are configured.
  - Verified: `dotnet user-secrets list` is empty; `appsettings.json` Microsoft section has empty strings for TenantId/ClientId/ClientSecret.
  - `AuthServiceFactory.ResolveServiceKey()` correctly falls back to `MockService` when `MicrosoftEntraAuthenticationOptions.IsConfigured = false`.
  - Security assessment: **No code vulnerabilities detected.** Jeff's implementation is secure and defensive:
    - OIDC handler only registers when credentials exist (prevents runtime errors)
    - Mock endpoints gated by service mode (prevents auth bypass)
    - Factory resolution fails safe to mock when config invalid
    - No secrets in committed configuration files
  - User action required: Follow README.md Azure app registration steps and add credentials via `dotnet user-secrets` to enable real Microsoft auth.
  - Outcome: **No code changes authorized.** System is working as designed. User must configure Microsoft credentials to exit mock-only mode.
  - Decision log: `.squad/decisions/inbox/warden-auth-mode-auto-insecure-requires-client-secrets.md`
- **2025-07-24 — AUTHENTICATION_SETUP.md security review.**
  - Fixed fabricated port numbers (`7123`/`5123`) that don't match any configuration. Aspire assigns webfrontend ports dynamically; guide now instructs users to check the Aspire dashboard and register actual ports in Azure.
  - Removed deprecated Google+ API enablement step (shut down 2019). Replaced with correct OAuth consent screen configuration flow.
  - Removed unnecessary "Authorized JavaScript origins" from Google setup — not needed for server-side OIDC.
  - Corrected post-sign-in redirect destination (goes to `/`, not "dashboard").
  - Fixed smoke test checklist: mock providers only appear in `auto`/`combined` mode, not when Service is `microsoft`.
  - Added Google testing-mode caveat: only test users can sign in while app is in Google's "Testing" status.
  - Key files: `docs/AUTHENTICATION_SETUP.md`, `MicrosoftEntraAuthenticationOptions.cs`, `AuthenticationServiceCollectionExtensions.cs`, `Program.cs`.
