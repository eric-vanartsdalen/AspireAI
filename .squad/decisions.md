# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-04-05T21:33:20Z):** Merged 18 inbox decisions from auth documentation and QA validation (Jeff, Warden, Bob, Buster). Consolidated Bob's UX revision + Buster's multi-gate approval into a single "Mock Pluggable Auth Slice" decision. No duplicates found. Inbox cleared.
> **Note (2026-04-06T18:38:14Z):** Merged local auth password floor relaxation decision (Warden approval + Jeff implementation). Relax minimum from 12 to 10 characters; add visible UI hint; confirm case-insensitive username uniqueness already implemented; defer password reset. All implementation work complete and tested.

<!-- Decisions are appended below. Each entry starts with ### -->

## Local Auth Password Floor 12→10 + Visible UI Requirement — Warden, Jeff — 2026-04-06

**Authors:** Warden (Security Specialist), Jeff (.NET Dev)  
**Status:** IMPLEMENTED  
**Scope:** Local authentication minimum password length, UI hint visibility, case-insensitive username uniqueness confirmation, password-reset deferral.

### Context

Eric requested reducing the local auth password minimum from 12 to 10 characters and adding a visible hint on the sign-in form. Warden reviewed the cryptographic implications; Jeff implemented the changes and updated tests/docs.

### Decision

**Relax local password floor to 10 characters; add visible UI constraint hint; confirm case-insensitive username uniqueness via existing normalized-key path; defer password reset to later phase.**

#### Password Minimum: 12 → 10

- **Rationale:** NIST 800-63B recommends floor of 8 characters for memorized secrets. ASP.NET Core `PasswordHasher<T>` on .NET 10 uses PBKDF2-HMAC-SHA512 with 600k iterations, making brute-force infeasible at 10 characters.
- **Risk Profile:** Acceptable for local-dev/single-operator product stage.
- **Revisit Trigger:** If auth path becomes production-facing or internet-exposed; conduct entropy/policy review.
- **Implementation:** `LocalAuthenticationOptions.MinimumPasswordLength` now 10.

#### Username Case-Insensitive Uniqueness: Already Implemented

- **Current Path:** `LocalAuthValueNormalizer.Normalize()` applies `ToUpperInvariant()`. The `ux_local_auth_users_normalized_username` unique index enforces uniqueness on normalized column.
- **Lookup:** `LocalAccountAuthenticator.AuthenticateAsync` queries against `NormalizedUsername`.
- **Matches:** ASP.NET Identity convention for case-insensitive identity resolution.
- **Code Change Required:** None. Confirmation only.

#### UI Constraint Visibility

- **Requirement:** Show 10-character minimum directly in `SignInPanel.razor`.
- **Implementation:** Added `minlength="10"` to password `<input>` and helper text below: "Password must be at least 10 characters".
- **Security Note:** Server-side validation remains the authoritative gate; UI hint is UX quality, not security bypass.

#### Password Reset: Deferred

- **Status:** Remains in explicit deferral list from self-registration security gate (see "Mock Pluggable Auth Slice" decision).
- **No Security Gap:** Admin can reset via direct DB update if needed.
- **Future Work:** Implement password reset workflow in dedicated phase when self-service registration is added.

### Implementation Checklist (Complete)

- [x] Change `MinimumPasswordLength` from `12` to `10` in `LocalAuthenticationOptions.cs`
- [x] Add `minlength="10"` to password `<input>` in `SignInPanel.razor`
- [x] Add helper text: "Password must be at least 10 characters"
- [x] Update tests: 10-char boundary, UI hint validation, mixed-case username handling
- [x] Update `docs\AUTHENTICATION_SETUP.md` with new floor, uniqueness explanation, password-reset deferral

### Key Paths

- `src\AspireApp.Web\Services\LocalAuthenticationOptions.cs`
- `src\AspireApp.Web\Components\Shared\SignInPanel.razor`
- `src\AspireApp.Web\Services\LocalAccountAuthenticator.cs`
- `src\AspireApp.Web\Services\LocalAuthValueNormalizer.cs`
- `src\AspireApp.Web\Shared\UploadDbContext.cs`
- `docs\AUTHENTICATION_SETUP.md`

### Test Results

- ✓ 10-character minimum boundary validated
- ✓ UI hint displays and functions correctly
- ✓ Mixed-case username reuse prevented
- ✓ No duplicate local users created
- ✓ All regression tests passing

### Relationship to Other Decisions

- **Upstream:** "Mock Pluggable Auth Slice" (2026-04-05) — This work is a targeted refinement within the local auth seam, not affecting mock provider abstraction.
- **Impact Scope:** Local authentication only. No impact on OAuth placeholder, API auth, or Python service auth (deferred to Phase 6).

### Future: Production Auth Policy

When local auth becomes production-facing:
1. Revisit password entropy policy (history, expiry, complexity)
2. Implement password reset workflow
3. Add rate limiting on failed sign-in attempts
4. Move to centralized secret storage (Azure Key Vault, etc.)

---

## Mock Pluggable Auth Slice — Unauthenticated Landing + Provider Abstraction — Bob, Jeff, Buster — 2026-04-05

**Authors:** Bob (Lead / Architect), Jeff (.NET Dev), Buster (QA / Tester)  
**Status:** RECOMMENDED — Pending Eric approval  
**Scope:** Next UX infrastructure leg after tenant-context. Mock authentication with pluggable provider abstraction, unauthenticated landing, protected views, multi-layer test gates.

### Context

Tenant-context UI is complete and passing tests. The TenantSelector dropdown renders but has no user identity behind it — nobody "logs in," so tenant selection is arbitrary and untethered to any user concept. The BRAIN roadmap places real auth at Phase 6, but the UX seams and abstractions need to exist earlier so that:

1. The app feels like a real product (login page, user identity, personalized experience)
2. Tenant association is driven by user identity, not manual dropdown
3. Real Microsoft/Google OAuth slots in later without rewriting the UI or service layer
4. UI tests validate auth-gated flows now, not after the fact

### Decision

**Build a Mock Pluggable Auth Slice as the next UI infrastructure leg.**

This is a UX and abstraction layer — not a security implementation. It establishes the shapes that real auth will fill.

#### Architecture

`
IAuthenticationStateProvider (abstraction)
├── MockAuthProvider (development — hardcoded users, provider picker UI)
└── (future) OAuthAuthProvider (real Microsoft/Google via ASP.NET Identity)

AuthenticatedUser (model)
├── UserId, DisplayName, Email, AvatarUrl
├── Provider ("mock-microsoft", "mock-google", etc.)
└── TenantId (links to TenantContextService)
`

#### In-Scope (By Layer)

| Layer | Deliverable | Owner | Status |
|-------|-------------|-------|--------|
| **UX** | Landing.razor (/) — hero + sign-in CTA | Jeff | Blueprint ready |
| **UX** | SignIn.razor (/signin) — provider picker | Jeff | Blueprint ready |
| **UX** | Dashboard.razor (/dashboard) — protected | Jeff | Blueprint ready |
| **UX** | User display in top-row / nav | Jeff | Blueprint ready |
| **Service** | IAuthStateProvider interface + MockAuthProvider | Bob, Jeff | Design ready |
| **Service** | AuthenticatedUser model | Jeff | Defined |
| **Service** | AuthenticationContext (scoped, mirrors TenantContextService) | Jeff | Blueprint ready |
| **Logic** | Mock login page (select provider → sign in) | Jeff | Blueprint ready |
| **Logic** | Mock user profiles (3-4 hardcoded per provider) | Jeff | Blueprint ready |
| **Logic** | Wire TenantContextService to AuthenticatedUser.TenantId | Jeff | Design ready |
| **Logic** | <AuthorizeView> gating on main layout | Jeff | Standard Blazor pattern |
| **Testing** | Playwright UI tests (login flow, tenant auto-select, logout) | Buster | Strategy defined |
| **Testing** | Unit tests for MockAuthProvider state transitions | Buster | Strategy defined |
| **Testing** | 5-layer acceptance gates (UI → Component → Integration → Service → Contract) | Buster | Gates defined |

#### Out-of-Scope (Explicitly Deferred)

| Item | Why | Target Phase |
|------|-----|--------------|
| Real OAuth (Microsoft Identity, Google) | This slice builds the seam they plug into | Phase 6 |
| Token management, refresh, JWT | Not needed for mock | Phase 6 |
| API authorization middleware | Gateway auth is Phase 6 | Phase 6 |
| Role-based access control (RBAC) | No roles needed yet; user identity is sufficient | Later |
| Persistent user sessions (DB-backed) | Mock state lives in Blazor circuit memory | Phase 6 |
| User registration / self-service | Mock users are hardcoded | Phase 6 |
| Password-based authentication | Mock has no passwords | Phase 6 |
| Python service auth | Python doesn't need auth awareness until Phase 6 | Phase 6 |

### Acceptance Gates (5 Required)

| Gate | Criteria | Validation |
|------|----------|-----------|
| **AUTH-A** | Unauthenticated user sees landing page with sign-in CTA at / | LandingPageTests.cs — HTTP 200, sign-in buttons visible |
| **AUTH-B** | Clicking a provider button on /login signs in and redirects to Home | AuthFlowE2ETests.cs — Playwright E2E flow |
| **AUTH-C** | Authenticated user sees their name/avatar in the top bar | AuthFlowE2ETests.cs — Element inspection |
| **AUTH-D** | Tenant auto-selects to the signed-in user's default tenant | AuthFlowE2ETests.cs — Tenant dropdown state |
| **AUTH-E** | Sign-out returns to landing page; nav links are inaccessible | AuthFlowE2ETests.cs + LandingPageTests.cs — Route protection |
| **AUTH-F** | Provider pluggability: config-only swap (no code recompile) | AuthProviderFactoryTests.cs — Factory loads provider from config |
| **AUTH-G** | 5-layer test suite passes (UI → Component → Integration → Service → Contract) | All gate tests pass; cross-service tenant propagation verified |

### What This Unlocks

1. **Real OAuth integration (Phase 6):** Swap MockAuthProvider DI registration for OAuthAuthProvider. Login page adds real redirects. AuthenticatedUser model stays the same. Zero UI component rewrites.
2. **Tenant-user binding:** Tenant selection becomes meaningful — it's the user's workspace, not an anonymous dropdown.
3. **BRAIN Gateway auth:** When the Gateway exists (Phase 3+), the AuthenticatedUser.TenantId propagates as the 	enant_id header on Gateway requests.
4. **Chat history per user:** Conversation persistence gains a real user identity to key on.
5. **UI test coverage for auth flows:** Playwright tests validate login/logout/redirect patterns that real OAuth will also follow.

### Implementation Notes

- Use Blazor's built-in AuthenticationStateProvider and <AuthorizeView> — this is the framework's supported extensibility point
- MockAuthProvider returns a ClaimsPrincipal with mock claims, so all downstream [Authorize] attributes and AuthorizeView work correctly
- Mock login page should visually resemble real OAuth provider buttons for UX fidelity
- Switching providers on the mock login page signs out the current user and signs in as the new provider's mock user
- CascadingAuthenticationState wraps the router in Routes.razor
- AuthenticationContext (scoped) mirrors TenantContextService pattern for consistency

### Risk Assessment

| Risk | Level | Mitigation |
|------|-------|-----------|
| Scope creep into real OAuth | Low | Explicit "Out-of-Scope" list + feature-gate config |
| UX doesn't match real providers later | Low | Mock buttons visually styled like real providers now |
| Tenant selector regression | Low | Buster regression test validates tenant dropdown still works |
| Auth state leaks across sessions | Low | Mock state dies with Blazor circuit (intended) |

### Relationship to BRAIN Roadmap

This work is **orthogonal to BRAIN phases** — it's UX infrastructure that any product needs. It doesn't block or depend on Phase 0-3. It directly enables Phase 6 (auth enforcement) by establishing the seam.

**Suggested placement:** Pre-Phase 0 UX infrastructure or standalone "UX Foundation" epic.

### Team Responsibilities

| Role | Responsibility |
|------|-----------------|
| **Bob (Architect)** | IAuthStateProvider interface design, AuthProviderFactory, DI registration seams |
| **Jeff (.NET Dev)** | AuthenticationContext, MockAuthProvider, Blazor components (Landing/SignIn/Dashboard), Program.cs wiring |
| **Buster (QA)** | Acceptance gates (5 tiers), Playwright E2E, regression tests, contract specs |
| **Jarvis (Python)** | Future: tenant ID propagation in API headers (Phase 6) |

---

### Decision Summary (Consolidated)

**Three agents (Bob, Jeff, Buster) independently audited auth readiness and unanimously recommended:**

1. **Bob's Recommendation** (Architect): Mock pluggable auth slice with provider abstraction, unauthenticated landing, protected views. Real OAuth deferred to Phase 6 via DI swappability. Enables BRAIN gateway integration later.

2. **Jeff's Recommendation** (.NET Dev): Concrete Blazor UX (Landing, SignIn, Dashboard). AuthenticationContext (scoped) + MockAuthProvider (pluggable). Three sign-in options: Mock demo, Microsoft stub, Google stub. Tenant auto-selection on login. Mirror TenantContextService pattern.

3. **Buster's Recommendation** (QA): 5-layer acceptance gates (UI → Contract) before implementation. Multi-tier validation: Unauthenticated landing, mock endpoint contract, cross-service tenant isolation, E2E sign-in flow, provider pluggability via config-only swap. Gates must pass before real OAuth attempted.

**Status:** All three recommendations converge on same direction. Awaiting Eric approval for sprint assignment.



---

## Mock Auth Route Foundation — Bob — 2026-04-05

**Author:** Bob (Lead / Architect)
**Status:** IMPLEMENTED
**Scope:** Auth routing and foundation for Blazor implementation

### Decision

Build the mock auth UX using Blazor's built-in route authorization flow:

1. Keep / public as the unauthenticated landing page
2. Redirect anonymous access to [Authorize] routes through AuthorizeRouteView into /signin?returnUrl=...
3. Keep the mock provider picker in a reusable shared component so both landing page and /signin use the same auth seam

### Why

- Landing page and protected-route experience serve different jobs; collapsing them muddies both
- QA needs deterministic redirect behavior after sign-out and direct navigation to protected routes
- Framework primitives (AuthenticationStateProvider, AuthorizeRouteView, AuthorizeView) keep auth within standard patterns

### Consequences

- Stable test hooks live on reusable sign-in surface and authenticated shell
- Real OAuth later swaps auth service/provider registration, not route structure
- Post-logout UX becomes predictable: / for explicit sign-out, /signin for protected-route interception

---

## Auth UI Test Hooks — Buster — 2026-04-05

**Author:** Buster (QA / Tester)
**Status:** IMPLEMENTED
**Scope:** Playwright acceptance gates for mock auth UX

### Decision

The mock auth UX slice exposes stable Playwright hooks on the Blazor shell instead of forcing brittle text-only selectors.

### Required Hooks

- data-testid="auth-landing" — Unauthenticated landing container
- data-testid="auth-sign-in-cta" — Landing sign-in call-to-action
- data-testid="auth-provider-*" — Provider choices (e.g., uth-provider-mock-microsoft)
- data-testid="auth-user-select" — Account picker before sign-in completes (if needed)
- data-testid="auth-submit-sign-in" — Second-step sign-in submit (if needed)
- data-testid="auth-user-display" — Signed-in identity surface
- data-testid="auth-sign-out" — Sign-out control
- data-testid="auth-current-tenant" or data-auth-tenant="{tenantId}" — Signed-in tenant display

### Why

src\AspireApp.WebTest\Fixtures\TestFixture.cs is the correct Aspire/AppHost test bed. These hooks let QA bind Playwright to stable UX seams while Jeff adjusts wording, layout, and route polish without breaking acceptance tests.

---

## Auth QA Rejection: Incomplete Closure — Buster — 2026-04-05

**Author:** Buster (QA / Tester)
**Status:** REJECTED for approval (Warden revision owner)
**Scope:** Mock auth UX revision review after Bob's independent pass

### Decision

Reject this revision for approval. The auth shell now exists in Blazor UI, but implementation misses required closure on agreed acceptance gates.

### Why (Incomplete Gates)

1. **AUTH-F incomplete** — Program.cs only resolves MockAuthService and throws for non-mock modes. Not the config-swappable provider abstraction agreed by team.
2. **AUTH-G incomplete** — Only Playwright-style auth flow exists. Promised service/factory/contract tiers not present.
3. **Automation not clean** — Focused WebTest execution during review did not produce clean auth run. Broader browser suite regresses in BasicAspireAppHostTests.FlowEndToEnd.

### Enforcement

- Next revision owner must not be Bob (independent follow-up pass already attempted)
- Route to different owner with explicit focus on AUTH-F/AUTH-G completion and clean automation

---

## Auth UX Revision: Post-Logout Route Protection — Buster — 2026-04-05

**Author:** Buster (QA / Tester)
**Status:** REJECTED for approval
**Scope:** Warden auth UX revision

### Decision

Do not approve Warden's auth UX slice. Next revision owner must be someone other than Warden.

### Why

1. **Focused gate not closed:** AuthUxFoundationTests.SignOutReturnsToLandingAndReprotectsAppAreas fails. Direct navigation to /chat remains reachable after sign-out instead of redirecting to sign-in.
2. **Critical regression dirty:** BasicAspireAppHostTests.FlowEndToEnd fails. UI upload path never surfaces processing-smoke.pdf in API-backed upload state after sign-in.

### Required for Next Owner

- Fix post-logout route protection for direct visits to [Authorize] pages
- Re-run focused auth set (AuthServiceFactoryTests, MockAuthServiceTests, AuthUxFoundationTests) clean
- Re-run BasicAspireAppHostTests.FlowEndToEnd clean before requesting approval

---

## Auto-Select Live Microsoft When Configured — Jeff — 2026-04-05

**Author:** Jeff (.NET Dev)
**Status:** IMPLEMENTED
**Scope:** Web auth seam default behavior

### Decision

Web auth seam defaults to Authentication:Service = auto and resolves to live Microsoft-only mode whenever Microsoft client settings are present. combined remains available as explicit mixed-mode choice for local testing.

### Why

Previous shape allowed valid Microsoft OIDC configuration to exist while UI still rendered only mock providers (because auth service pinned to mock). Made real login journey feel broken even though OIDC handler and challenge endpoint were wired correctly.

### Outcome

- Landing/sign-in expose real Microsoft path automatically when configured
- Clicking Microsoft card starts hosted Microsoft login immediately with no demo-user picker
- Demo/mock providers remain available only when live config absent or Authentication:Service explicitly set to mock/combined

---

## Authentication Setup Guide — Jeff — 2026-04-05

**Author:** Jeff (.NET Dev)
**Status:** COMPLETE
**Scope:** Developer documentation for local auth testing

### Summary

Created docs/AUTHENTICATION_SETUP.md — comprehensive guide for:
1. Microsoft Entra ID consumer authentication (implemented, working)
2. Google OAuth setup (credentials prep for future implementation)
3. Local user-secrets configuration and troubleshooting

### Key Design Decisions

- **Explicit current state:** ✅ Microsoft ready, ❌ Google future work (prevents confusion)
- **User secrets as primary config** — Matches .NET conventions, prevents secret commits
- **Dynamic port guidance** — Check Aspire dashboard for actual webfrontend port
- **Smoke test checklist** — Actionable manual steps covering success and error scenarios
- **Troubleshooting section** — Common issues with concrete fixes

### Files Updated

- **Created:** docs/AUTHENTICATION_SETUP.md (20 KB, ~650 lines)

---

## Microsoft Entra Auth Uses Existing IAuthService Seam — Jeff — 2026-04-05

**Author:** Jeff (.NET Dev)
**Status:** IMPLEMENTED
**Scope:** Blazor Web auth integration

### Decision

Keep existing IAuthService abstraction as only UI-facing auth seam. Plug real Microsoft Entra ID in behind it with provider-specific implementation plus optional combined provider service.

### Why

- Preserves working mock/demo regression surface
- Lets Eric manually test live Microsoft sign-in without deleting local demo flows
- Keeps cookie/OIDC responsibilities in ASP.NET Core middleware instead of bespoke token code

### Implementation Notes

- MicrosoftEntraAuthService issues challenge through /auth/microsoft/signin
- CompositeAuthService exposes live Microsoft + mock providers in one picker when Authentication:Service=combined
- Program.cs owns provider-aware sign-out so both mock and OIDC sessions leave shell coherently

---

## Microsoft Sign-In as Hosted Redirect — Jeff — 2026-04-05

**Author:** Jeff (.NET Dev)
**Status:** IMPLEMENTED
**Scope:** Sign-in UX and Microsoft auth activation

### Decision

Treat live Microsoft sign-in as direct hosted redirect from sign-in page. Reserve dropdown chooser exclusively for explicitly labeled demo providers.

### Why

- Users landed on Microsoft-looking demo picker instead of exercising real OIDC challenge
- Personal Microsoft accounts (@hotmail.com) don't always have tenant-specific values, so requiring TenantId was unnecessarily blocking
- Plain link to /auth/microsoft/signin is more reliable than interactive Blazor button before external auth

### Implementation Notes

- Live Microsoft activates when ClientId and ClientSecret present; blank TenantId uses existing common authority fallback
- SignInPanel.razor renders hosted providers as direct links; keeps demo providers on in-app chooser
- Demo provider labels renamed to Microsoft demo / Google demo to avoid mixed-mode ambiguity

---

## Mock Auth Shell Uses Blazor Auth Primitives — Jeff — 2026-04-05

**Author:** Jeff (.NET Dev)
**Status:** PROPOSED
**Scope:** Auth foundation for Web app

### Decision

Use scoped AuthenticationContext plus AppAuthenticationStateProvider as Web app's auth foundation. Keep provider behavior behind IAuthService.

### Why

- Blazor already gives us AuthorizeRouteView, AuthorizeView, CascadingAuthenticationState; using them keeps shell aligned with real ASP.NET Core auth later
- DI seam (IAuthService) lets current mock Microsoft/Google/demo experience be replaced without rewriting page/layout code
- Tenant remains separate from identity, but sign-in can initialize TenantContextService from selected user's default tenant

### Consequences

- Protected pages use framework auth attributes instead of custom route logic
- Auth UX components preserve stable data-testid hooks (existing WebTest suite depends on them)
- Real OAuth later swaps service implementation and reuses same shell/layout surfaces

---

## Configurable Auth Provider Factory — Warden — 2026-04-05

**Author:** Warden (Security Specialist)
**Status:** IMPLEMENTED
**Scope:** Mock auth UX foundation

### Decision

Use config-driven AuthServiceFactory behind AddAspireAppAuthentication(...) so active auth implementation selected by configuration, while implementations explicitly registered in DI.

### Why

- Removes hardwired mock branch from Program.cs
- Keeps provider selection on allowlisted registration path
- Gives later Microsoft/Google work registration-plus-config swap instead of UI rewrite

---

## Microsoft Entra ID OIDC Security Defaults — Warden — 2026-04-05

**Author:** Warden (Security Specialist)
**Status:** IMPLEMENTED
**Scope:** Secure defaults for Microsoft Entra ID integration

### Decisions

1. **OIDC scheme registered conditionally** — Only when Authentication:Microsoft:TenantId, ClientId, ClientSecret all present. In mock-only mode: no OIDC handler, no callback paths exposed, no metadata retrieval.

2. **PKCE enabled** — UsePkce = true on OIDC handler as defense-in-depth for auth code interception.

3. **Cookie hardened** — SecurePolicy = SameAsRequest, ExpireTimeSpan = 8 hours, SlidingExpiration = true.

4. **Proper error handling** — context.Fail() over 	hrow in OnTokenValidated so OIDC handler returns proper error.

5. **Endpoint guards** — /auth/microsoft/signin only mapped when OIDC scheme registered. /auth/signout clears cookie first, then attempts federated sign-out only if scheme exists.

6. **No secrets in committed config** — ClientSecret removed from ppsettings.json. Secrets live in dotnet user-secrets only.

7. **Sign-out via \<a href>\** — MainLayout sign-out uses link (not \<button @onclick>\) so sign-out works when Blazor SignalR circuit degraded.

8. **Tenant trust boundary** — Entra 	id claim is Azure AD tenant, NOT app tenant. External identities mapped to app tenants via UserTenantSeeds/DomainTenantSeeds config, defaulting to "default". This mapping must be explicit — never auto-trust external tenant claims.

### Rationale

These defaults reduce attack surface in mock-only deployments, harden cookie for live deployments, prevent information leakage from misconfigured OIDC endpoints. Existing mock regression suite (5 unit tests) passes unchanged.

---

## Mock Auth Endpoint Trust-Boundary Gate — Warden — 2026-04-05

**Author:** Warden (Security Specialist)
**Status:** IMPLEMENTED
**Scope:** Authentication endpoint security

### Context

/auth/mock/signin, /auth/mock/session (POST/DELETE), and /auth/mock/signout HTTP endpoints were unconditionally registered in Program.cs. When Authentication:Service = "microsoft", these endpoints remained reachable, allowing anyone to mint valid session cookie as any mock user — completely bypassing Microsoft Entra ID.

### Decision

Mock auth HTTP endpoints now conditionally registered. They are blocked when Authentication:Service = "microsoft" and available in all other modes (mock, combined, uto). Gate reads config value at startup and skips pp.Map* registration when Microsoft-only mode active.

### Impact

- **Program.cs** — Mock endpoint block wrapped in if (mockEndpointsEnabled)
- **All modes except microsoft** — No behavior change; mock endpoints work as before
- **microsoft mode** — Mock endpoints not registered; direct HTTP requests to /auth/mock/* return 404
- **Tests** — Existing mock auth tests only run in mock/combined modes, unaffected

---

## Authentication Setup Guide — Security-Sensitive Corrections — Warden — 2026-04-05

**Author:** Warden (Security Specialist)
**Status:** COMPLETED
**Scope:** docs/AUTHENTICATION_SETUP.md accuracy corrections

### Corrections Made

1. **Dynamic ports, not hardcoded** — Guide used fabricated port numbers (7123/5123) that don't match configuration. Aspire assigns webfrontend ports dynamically. Updated to instruct users to check Aspire dashboard for actual ports.

2. **Google+ API removed** — Guide instructed enabling Google+ API (shut down 2019). Caused confusion during setup. Replaced with current OAuth consent screen workflow.

3. **JavaScript origins removed** — Removed "Authorized JavaScript origins" from Google setup (only needed for client-side flows, not server-side OIDC).

### Why This Matters

Incorrect redirect URIs are the #1 cause of "it doesn't work" OAuth setup failures. Telling users to register wrong ports would waste hours of debugging and might lead to insecure workarounds.

---

## User Directive: No Live Microsoft Test Automation — Eric — 2026-04-05T19:49:26Z

**Author:** Eric VanArtsdalen (via Copilot)
**Status:** ACKNOWLEDGED
**Scope:** Testing strategy

### What

Do not spend effort automating live Microsoft-user authentication with real accounts. User will manually validate real login flow. Automated coverage should stay focused on non-live regression behavior.

### Why

User request — captured for team memory.

---

## Authentication Auto Mode Security Audit: APPROVED — Warden — 2026-07-22

**Author:** Warden (Security Specialist)
**Status:** APPROVED
**Severity:** N/A (Approved)

### Executive Summary

No security vulnerabilities detected. The authentication system is functioning exactly as designed.

### Context

User reported: "UI only allows 2 users to login, so it seems like it's still using the Mock — please allow the UI to do a real authentication flow."

Verified: dotnet user-secrets list is empty; ppsettings.json Microsoft section has empty strings. This is **correct behavior** because no Microsoft credentials are configured.

### Root Cause (Correct)

1. Configuration state: Authentication:Service = "auto", Microsoft section has empty values
2. Factory resolution: AuthServiceFactory.ResolveServiceKey() correctly returns MockService when MicrosoftEntraAuthenticationOptions.IsConfigured = false
3. Result: UI shows only mock providers (2 demo users) — this is safe defensive programming

### Security Assessment: APPROVED

✅ OIDC conditional registration — Only registered when credentials exist (prevents runtime errors)
✅ Factory resolution — uto mode safely falls back to mock when Microsoft not configured
✅ Mock endpoint gating — Mock routes disabled when service mode is explicitly microsoft
✅ Composite service delegation — Routes to appropriate provider based on providerId
✅ No session bypass — Mock endpoints disabled when real auth is only configured option

**No code changes required.** System is working as designed.

### Required User Action

To enable real Microsoft authentication:

1. Create Azure App Registration with redirect URI: https://localhost:{port}/signin-oidc-microsoft
2. Create client secret
3. Configure via dotnet user-secrets:
   ```powershell
   dotnet user-secrets set "Authentication:Microsoft:TenantId" "<your-tenant-id>"
   dotnet user-secrets set "Authentication:Microsoft:ClientId" "<your-client-id>"
   dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "<your-client-secret>"
   ```
4. Restart application
5. Verify Microsoft button appears on /signin

---
