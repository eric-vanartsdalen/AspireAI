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

### 2026-04-05 — Scribe: Auth documentation and decisions merged (18 inbox files)

**Session:** Post-spawn consolidation after Jeff (auth doc creation) and Warden (security audit)

**What Scribe Did:**
- Created orchestration logs for both agents documenting spawn context and work completed
- Created session log summarizing auth doc completion and ready state
- Merged 18 inbox decisions into decisions.md (4 from Warden: provider-factory, oidc-defaults, endpoint-gate, setup-corrections)
- Consolidated overlapping decisions across team (Bob/Jeff/Buster/Warden) — no duplicates found
- Updated Jeff and Warden history.md with cross-agent context propagation
- Deleted all .squad/decisions/inbox/* files after merge

**Decisions Captured (Warden's 4):**
1. **Configurable Auth Provider Factory** — Config-driven AuthServiceFactory, removes hardwired mock from Program.cs
2. **Microsoft Entra ID OIDC Security Defaults** — 8 hardening decisions: conditional OIDC, PKCE, hardened cookies, proper errors, endpoint guards, no secrets in config, sign-out via link, tenant trust boundary
3. **Mock Auth Endpoint Trust-Boundary Gate** — Conditionally register /auth/mock/* endpoints; blocked when Authentication:Service = "microsoft"
4. **Authentication Setup Guide — Security-Sensitive Corrections** — Fixed 3 critical accuracy issues: dynamic ports (not fabricated), Google+ API removed (shut down 2019), JavaScript origins removed (not needed for server-side OIDC)

**Cross-Agent Context:**
- Jeff produced auth documentation and implementation decisions
- Warden validated security posture (APPROVED — no vulnerabilities detected)
- Warden corrected documentation accuracy (ports, deprecated APIs, OIDC guidance)
- Together, Jeff + Warden delivered secure, accurate, production-ready auth setup
- All 23 regression tests passing with Microsoft Entra integration in place
- Mock endpoint gating prevents auth bypass when live Microsoft is configured
- OIDC conditional registration prevents metadata failures in unconfigured environments

**Team Coordination:**
- Bob's provider abstraction and DI seam informed Warden's factory pattern
- Buster's 5-layer acceptance gates informed Warden's mock endpoint gating strategy
- All decisions converge on pluggable, secure-by-default authentication that supports both mock (dev) and live (production) modes

**Status:** ✅ Security audit passed. Documentation corrected. All decisions merged and inbox cleared. Ready for Eric's manual test with real Microsoft credentials.
- **2025-07-25 — Local username/password auth security floor defined.**
  - Approved managed local auth as a new `IAuthService` provider (`ServiceKey = "local"`) slotting into the existing `AuthServiceFactory` pattern.
  - Password storage: ASP.NET Core `PasswordHasher<T>` (PBKDF2-HMAC-SHA512, 600k iterations in .NET 10). No custom hashing, no third-party bcrypt. Framework-native only.
  - New `local_users` table in existing PostgreSQL database via `UploadDbContext`. Columns: id, username, email, display_name, password_hash, default_tenant_id, failed_login_count, last_failed_at, created_at, updated_at.
  - Minimum protections required: 12-char password minimum, generic login failure messages (no user enumeration), no passwords in logs, constant-time comparison (built into PasswordHasher), failed-attempt tracking columns for future lockout.
  - Endpoint gating: `/auth/local/*` conditionally registered, same gate pattern as `/auth/mock/*`. Blocked when `Authentication:Service` = `microsoft`.
  - Tenant isolation: `default_tenant_id` column on local_users, flows through `TenantContextService.InitializeForUser()` identically to mock/Microsoft paths.
  - Explicitly deferred: self-registration, password reset, email verification, account lockout enforcement, MFA, password complexity UI, RBAC.
  - Anti-patterns called out: no custom hashing, no passwords in config files, no reuse of MockAuthCatalog, no bypassing the factory seam, no user-enumeration endpoints.
  - Decision logged: `.squad/decisions/inbox/warden-local-auth-floor.md`
  - Key files: `AuthServiceFactory.cs`, `AuthenticationOptions.cs`, `AuthenticationServiceCollectionExtensions.cs`, `Program.cs`, `UploadDbContext.cs`.
- **2025-07-25 — Self-registration security gate: APPROVED with guardrails.**
  - User explicitly requested: "if username doesn't exist, create the user with hashed password, then log them in."
  - Approved on-the-fly self-provisioning with these mandatory constraints:
  - Config gate: `AllowSelfRegistration` boolean on `LocalAuthenticationOptions` (default `false`). Self-create only active when explicitly enabled.
  - Username-only auto-create: identifiers containing `@` are email lookups only — never auto-create from email-shaped input (prevents unverified email claims).
  - Password floor: enforce 12-char minimum on the auto-create path. Reject early (before DB lookup) for ALL sign-in attempts when password < 12 chars to avoid leaking user existence.
  - Username validation: alphanumeric + hyphens + underscores + periods only, 3–100 chars. Reject invalid shapes with generic error.
  - Derived fields: Email = `{normalized_username}@local.aspireai` (synthetic), DisplayName = submitted identifier (trimmed), DefaultTenantId = `TenantContextService.DefaultTenantId` ("default").
  - Generic errors: all failure paths (duplicate, invalid chars, short password) return the same `BuildInvalidLocalCredentialResult` redirect. No user enumeration.
  - DB-level safety: existing unique index on `normalized_username` prevents race-condition duplicates.
  - Anti-create on duplicate: if INSERT fails (unique constraint), return generic error — do NOT reveal the username is taken.
  - Deferred: rate limiting, CAPTCHA, email verification, account lockout. Acceptable for current product stage with config gate.
  - Decision logged: `.squad/decisions/inbox/warden-self-registration-guardrails.md`
  - Key files: `LocalAccountAuthenticator.cs`, `LocalAuthenticationOptions.cs`, `LocalAuthBootstrapper.cs`, `Program.cs`, `SignInPanel.razor`.

- **2025-07-25 — Password floor 12→10 review: APPROVED for current slice.**
  - User requested relaxing minimum from 12 to 10 characters. NIST 800-63B floor is 8; 10 is above that. PBKDF2-HMAC-SHA512 @ 600k iterations remains the hashing floor. Acceptable for local-dev/single-operator stage.
  - Username case-insensitive uniqueness already implemented: `LocalAuthValueNormalizer.Normalize()` uses `ToUpperInvariant()`, `ux_local_auth_users_normalized_username` unique index enforces it. No code changes needed for uniqueness.
  - UI password hint missing: `SignInPanel.razor` credential form has no `minlength` or helper text. Jeff should add both.
  - Password reset deferred: already explicitly deferred in prior security gate. Acceptable — user acknowledged "this can wait."
  - Decision logged: `.squad/decisions/inbox/warden-password-floor-relaxation.md`

### 2026-04-09 — Tenant Slice Session: Security Review & Revision

**Role:** Security Specialist (Tenant Edge Cases)

**Outcome:** Approved core security model; identified and fixed add-member exception handling + direct recovery test gaps.

**What Warden Did:**
1. Reviewed tenant isolation model: default-tenant protection, upload authorization, membership enforcement
2. Approved default-tenant protection mechanism (is_default immutable, delete rejection)
3. Approved upload authorization hardening (403 for unmembered tenants)
4. Identified gap: AddMemberByUsernameAsync only catches DbUpdateException; other transient failures bubble unhandled
5. Identified gap: No direct tests for EnsureTenantAccessAsync recovery logic
6. Created revision decision with fixes:
   - Broaden exception catch to all failures (excluding OperationCanceledException)
   - Add 6 direct tests: no memberships, multiple defaults, save failure, etc.
7. Coordinated with Buster on final test audit

**Key Decisions Contributed:**
- Tenant Isolation, Default-Tenant Protection & Add-Member Security Requirements — comprehensive security model
- Tenant Edge-Case Revision — save-failure catch broadening + direct recovery tests

**Status:** Approved; revision implemented; security gate passed
- **2026-04-10 — Chat persistence privacy review: REJECTED pending independent revision.**
  - `ChatConversationService.cs` correctly scopes list/get/add/rename/delete operations by `owner_user_id`, so shared tenant membership does not widen conversation access by itself.
  - The actual `/chat` UI in `src\AspireApp.Web\Components\Pages\Chat.razor` and `Chat.razor.cs` still does not render the saved-conversation shell or call `IChatConversationService`, so rename/delete/resume privacy is not proven in the product yet.
  - `src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs` passes service-layer owner-isolation coverage, but it still lacks a direct unauthorized `AddMessageAsync` assertion for another user attempting to continue an existing conversation.
  - `src\AspireApp.WebTest\Tests\ChatConversationPersistenceTests.cs` is written as the intended end-to-end privacy gate, but it explicitly skips when the conversation shell hooks are absent; live execution here was also blocked because Docker/Aspire was unhealthy.
  - Recommended revision owner: Bob for an independent follow-up pass that wires the real chat shell and closes the privacy proof.

- **2026-04-10 — Auth/Upload UX Test Failures Security Review: APPROVED — No Vulnerabilities Detected.**
  - User reported: `AuthenticatedUploadUxTests`, `AuthUxFoundationTests`, `CompositeAuthServiceTests` are failing.
  - Warden security verdict: **No auth shortcuts or insecure patterns detected.** All security controls properly in place:
    - Mock endpoints correctly gated by service mode (line 147–150, Program.cs)
    - OIDC handler conditionally registered only when Microsoft config present
    - Session cookies hardened (HttpOnly, SameSite=Lax, 8-hour sliding, SecurePolicy=SameAsRequest)
    - Tenant isolation enforced (403 for unmembered tenants in FileUploadController)
    - No secrets committed; credentials in dotnet user-secrets only
  - **Root cause of failures: Integration/timing issues, not auth bypass risks:**
    - `SuccessfulMockSignInTransitionsIntoAuthenticatedShell` fails because `MockAuthService.SignInAsync()` uses `forceLoad: true`, causing Blazor circuit rebuild mid-flight. Test assertion checks URL immediately after, catching transient `/signin` in redirect chain.
    - `SignOutReturnsToLandingAndReprotectsAppAreas` fails with `net::ERR_ABORTED` navigating to `/chat` — suggests Aspire testhost unhealthy or chat component crash, not auth layer failure.
  - **Approved fix direction:** Adjust test timing/assertions to account for Blazor circuit cycles; verify `/chat` endpoint health in testhost. Do NOT bypass mock endpoint gating, OIDC conditional registration, or tenant isolation.
  - **Anti-patterns explicitly rejected:** Test-only backdoors, opt-in tenant checks, removal of forceLoad from SignIn flow.
  - Decision logged: `.squad/decisions/inbox/warden-auth-test-failures-no-vulnerabilities.md`
  - Key files: `MockAuthService.cs`, `Program.cs` (lines 147–225), `FileUploadController.cs` (lines 345–390), `AuthenticationServiceCollectionExtensions.cs` (lines 22–34).
   - **Cross-agent coordination:** Buster diagnosed fixture orchestration root cause (shared storage corruption); Jeff applied 3 app-level fixes. All 13 originally failing tests now passing.
