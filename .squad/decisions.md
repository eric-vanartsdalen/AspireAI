# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-04-15T06:58:08Z):** Merged 3 inbox decisions from failing test fix session (Buster, Jeff, Eric). Key outcome: Fixed upload/e2e test failures identified as test-side timing assumptions; Python processing moved off FastAPI event loop via `asyncio.to_thread()`; user directive "no GitHub Issues" recorded. All tests passing (8/8). Consolidated into 3 decisions: (1) User Directive — No GitHub Issues, (2) Upload Test Scaffolding Async Dispatch, (3) Python Processing Off Event Loop. Status: Tests clean, inbox cleared, decisions merged.
> **Note (2026-04-17T23:50:00Z):** Merged 2 inbox decisions from roadmap/Tasks.md update session (Bob, Buster). Key outcome: P2-B gate closure confirmed (confidence fail-closed + Neo4j enrichment verified); P2-C unblocked (embedding infrastructure identified as blocker, not code); Phase 3 critical path locked (agent framework selection is BLOCKING GATE with 2026-04-24 decision deadline). Contradiction detection deferred to Phase 3 Critic Agent integration. No exact duplicates found. Inbox cleared.
> **Note (2026-04-05T21:33:20Z):** Merged 18 inbox decisions from auth documentation and QA validation (Jeff, Warden, Bob, Buster). Consolidated Bob's UX revision + Buster's multi-gate approval into a single "Mock Pluggable Auth Slice" decision. No duplicates found. Inbox cleared.
> **Note (2026-04-06T18:38:14Z):** Merged local auth password floor relaxation decision (Warden approval + Jeff implementation). Relax minimum from 12 to 10 characters; add visible UI hint; confirm case-insensitive username uniqueness already implemented; defer password reset. All implementation work complete and tested.
> **Note (2026-04-09T15:39:08Z):** Merged 6 tenant slice decisions (Jeff, Warden, Buster, Bob). Tenant isolation via persisted tenants/memberships, default-tenant protection + backfill, upload authorization hardening, add-member edge-case revision, and local-auth-slice foundation recommendation. 28 targeted tests passing. No duplicates found. Inbox cleared.
> **Note (2026-04-09T15:39:12Z):** Added Upload Authentication Regression decision (Jeff, Buster): FileStorageService scoped injection removes HTTP self-call pattern in UploadData; tenant context preserved in-circuit. Regression coverage tightened. Build success. No inbox files to merge.
> **Note (2026-04-10T07:48:03Z):** Merged 9 inbox decisions from chat persistence & rename focus work (Jeff, Warden, Eric, Buster). Consolidated chat history tests audit, persistence audit, service implementation, rename focus fix, upload auth test gap closure, privacy review notes, and user privacy directive. No exact duplicates; privacy review rejected prematurely (Warden flagged incomplete UI wiring, not design flaw). All implementation work complete. Inbox cleared.
> **Note (2026-04-11T18:38:10Z):** Merged 1 inbox decision from chat persistence QA validation session (Buster). "Chat privacy tests should not wait on full AI completion" — acceptance seam is owner message persistence + owner-only visibility, not Ollama response completion. 7/7 focused tests passing. Inbox cleared.
> **Note (2026-04-13T15:18:35Z):** Merged 5 inbox decisions from P1 Docling-to-LightRAG-to-Neo4j audit session (Jarvis, Bob, Buster, Verbal, Jeff). Key outcomes: Items 1 & 4 fully covered; items 2 & 3 require integration test gates (Phase 2); three roadmap items should be reworded to "foundation-only" to reduce Phase 2 execution risk. No duplicates found. Inbox cleared.
> **Note (2026-04-14T06:17:03Z):** Merged 1 inbox decision from Phase 0 gate closeout session (Bob). "Phase 0 Gate Closeout: BRAIN Pivot Decision Recording Complete" — Decision-recording gate closed; BRAIN pivot recorded; Docker validation caveat noted as outstanding quality gate (not blocking Phase 1 parallel work). No duplicates found. Inbox cleared.
> **Note (2026-04-17T23:55:30Z):** Merged 0 new inbox decisions from roadmap/Tasks.md cleanup session (Bob, Buster). Session summary: Bob updated Tasks.md and highlighted Phase 2/3 sequencing; Buster verified honesty against test evidence and approved; Bob performed surgical cleanup (removed duplicate contradiction-detection entry and stale outstanding Phase 2 proof item); Buster rechecked for internal consistency and approved final state. Roadmap now internally consistent. No inbox files generated. See session log `20260417-roadmap-cleanup.md` for details.
> **Note (2026-04-17T23:50:00Z):** Merged 2 inbox decisions from roadmap/Tasks.md update session (Bob, Buster). Key outcome: P2-B gate closure confirmed (confidence fail-closed + Neo4j enrichment verified); P2-C unblocked (embedding infrastructure identified as blocker, not code); Phase 3 critical path locked (agent framework selection is BLOCKING GATE with 2026-04-24 decision deadline). Contradiction detection deferred to Phase 3 Critic Agent integration. No exact duplicates found. Inbox cleared.
> **Note (2026-04-15T07:42:56Z):** Merged 2 inbox decisions from chat focus + LightRAG round-trip regression fix session (Jeff, Jarvis). Key outcome: (1) Chat focus seam uses explicit render-time flags (`ShouldFocusQuestionInput`/`ShouldFocusConversationTitleInput`) instead of eager autofocus in `OnAfterRenderAsync`, preventing rename-typing focus theft. (2) LightRAG retriever updated to handle multiple response shapes (`contexts` + `/query/data` chunks) and recover provenance from filename parsing (e.g., `000007-guide.md` → document ID). Both regression tests passing (ChatFocusTests 7/7, LightRAG tests 27/27, LiveLightRagNeo4jQueryRoundTrip ✅). No duplicates found. Inbox cleared.
> **Note (2026-04-15T17:41:59Z):** Merged 9 inbox decisions from chat persistence test investigation + P2-C embedding phase (Buster, Jeff, Bob, Jarvis, Scribe). Key outcomes: (1) ChatConversationPersistenceTests issue is environmental (missing Playwright Chromium) + timing-dependent (90s timeout races slow AI), not product regression. (2) Playwright setup must be documented. (3) P2-C embedding population active. (4) Ollama workload serialization implemented. (5) Vector infrastructure review approved. (6) All P2-C work consolidated. Inbox cleared.
> **Note (2026-04-15T18:38:37Z):** Merged 4 inbox decisions from PydanticAI framework selection + Critique pipeline implementation session (Bob, Jarvis, Eric directive). Key outcomes: (1) User directive captured: Use PydanticAI for Phase 3b, design for swappability. (2) Bob defined architecture boundary with `IAgentProvider` protocol abstraction, enabling zero-refactor framework swaps via env-var config. (3) Jarvis implemented Python-side seam: `PydanticAIProvider`, `CritiquePipeline` orchestrator, 33 targeted tests passing. (4) Jeff confirms C# gateway ready (no changes needed). (5) 6 acceptance gates (P3b-A through P3b-F) defined; 4 already met. Decision deadline 2026-04-24 confirmed. No duplicates found. Inbox cleared. See orchestration logs for details.
> **Note (2026-04-15T19:37:41Z):** Merged 4 inbox decisions from Critique Mode UI product layer + test coverage batch (Jeff, Buster). Key outcomes: (1) Critique-mode toggle enabled in `Chat.razor`; `disabled` attribute removed. (2) Reasoning steps render with progress details using framework-agnostic CSS classes. (3) Mode wiring to `BrainChatClient.ChatAsync` confirmed working. (4) UI/product test coverage (8 tests + 1 persistence test) now all passing (9/9 after Jeff's test harness fix). (5) Residual risk noted: no dedicated test yet exercises mode switching after loading conversation (non-blocking, for Phase 3b polish). No exact duplicates found. Inbox cleared.

<!-- Decisions are appended below. Each entry starts with ## -->

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

## User Directive: Do Not Use GitHub Issues — Eric — 2026-04-15T06:32:36Z

**Author:** Eric VanArtsdalen (via Copilot)  
**Status:** ACKNOWLEDGED  
**Scope:** Team workflow & issue tracking

### What

Do not use GitHub Issues for this repository.

### Why

User request — captured for team memory.

---

## Test Decision: Upload Test Scaffolding Must Respect Async Dispatch — Buster — 2026-04-15

**Author:** Buster (QA / Tester)  
**Status:** IMPLEMENTED  
**Scope:** Test infrastructure & async patterns

### Decision

Treat these upload failures as test-scaffolding issues, not app regressions:

1. `FileUploadControllerTests` must wait for background queue dispatch instead of asserting synchronous completion.
2. `BasicAspireAppHostTests.FlowEndToEnd` must treat transient Python status-call timeouts as retryable within the existing overall processing poll window.

### Why

- `FileUploadController.UploadFile()` intentionally starts automatic processing on a delayed fire-and-forget task so the upload response preserves the initial `uploaded` state.
- The FlowEndToEnd failure was a raw `HttpClient.Timeout` during `processing/status/{id}` polling, not a business assertion failure. The smoke should fail only after the full processing timeout window is exhausted.

### References

- `src\AspireApp.Web\Controllers\FileUploadController.cs`
- `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs`
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`

---

## Implementation Decision: Keep Python Processing Off the FastAPI Event Loop — Jeff — 2026-04-15

**Author:** Jeff (.NET Dev)  
**Status:** IMPLEMENTED  
**Scope:** Python FastAPI service & background processing

### Context

- `BasicAspireAppHostTests.FlowEndToEnd` was uploading successfully, then timing out while polling `GET /processing/status/{id}`.
- The Python processing router exposed `process_document_task` as an `async` FastAPI background task, but the implementation body was synchronous document-processing work (Docling, Neo4j writes, embedding calls, file output).
- In that shape, the background task could monopolize the FastAPI event loop long enough for status polling requests to hit client timeouts.

### Decision

- Keep the public `process_document_task` entrypoint async for existing callers and tests.
- Move the heavy processing body onto a worker thread via `asyncio.to_thread(...)`.
- Treat controller-side automatic processing as eventual background queueing in unit tests rather than a synchronous side effect of the upload response.

### Why

- This preserves the existing API surface while restoring responsiveness for status and health endpoints during active processing.
- It is smaller and safer than introducing a new queue abstraction during a regression fix.

### Trade-offs

- Each queued document now consumes a thread-pool worker while its blocking processing runs.
- That is acceptable for the current Aspire smoke/integration workflow, but if concurrency grows materially we should revisit a dedicated worker queue or external job runner.

### Key Paths

- `src\AspireApp.PythonServices\app\routers\processing.py`
- `src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs`
- `src\AspireApp.WebTest\Tests\FileUploadControllerTests.cs`

---


## Chat Focus: Explicit Render-Time Flags (No Eager Autofocus) — Jeff — 2026-04-15

**Author:** Jeff (.NET Dev)  
**Status:** IMPLEMENTED  
**Scope:** Blazor chat component focus seam, rename-title workflow stability

### Decision

Use explicit render-time focus flags in src\AspireApp.Web\Components\Pages\Chat.razor.cs for both the question input and the conversation-title input. OnAfterRenderAsync should only call ocusElement when a user action has queued that focus change (via ShouldFocusQuestionInput or ShouldFocusConversationTitleInput flags). Never attempt to autofocus question-input on every non-edit render.

### Why

The previous OnAfterRenderAsync behavior could attempt to refocus the question box on any non-edit render, which makes Blazor rerenders a potential focus thief. This breaks the rename-title workflow: user types a new name, then a render happens (e.g., another component updates state), and the focus jumps back to the question input, interrupting the edit.

Matching the existing title-input pattern with dedicated ShouldFocusQuestionInput flag keeps rename typing stable and makes the regression easy to test with bUnit and JSInterop call counts.

### Implementation

1. **Chat.razor.cs:**
   - Add boolean flags: ShouldFocusQuestionInput and ShouldFocusConversationTitleInput (default: alse)
   - In OnAfterRenderAsync, only call ocusElement if the corresponding flag is 	rue
   - Consume the flag (set to alse) after focus call completes
   - Set flags explicitly from user actions (conversation selected, rename exited)

2. **Chat.razor:**
   - Update @ref="questionInput" to check ShouldFocusQuestionInput flag
   - Keep title-input focus directive unchanged (already follows this pattern)

3. **Tests:**
   - ChatFocusTests.RenameTitleInput_DoesNotRefocusQuestionInputWhileTyping now passes ✅
   - ChatConversationServiceTests validates focus queuing (7/7 tests passing)

### Key Paths

- src\AspireApp.Web\Components\Pages\Chat.razor.cs
- src\AspireApp.Web\Components\Pages\Chat.razor
- src\AspireApp.WebTest\Tests\ChatFocusTests.cs
- src\AspireApp.WebTest\Tests\ChatConversationServiceTests.cs

### Validation

- ✓ Rename-title typing no longer interrupted by question-input refocus
- ✓ Question-input focus only happens on explicit user action (conversation select, rename exit)
- ✓ JSInterop focus call counts stable under rename workflow
- ✓ Regression test passing

### Relationship to Other Decisions

- **Related:** Chat persistence fixes (2026-04-10) — This work complements message history stability by adding input focus stability.
- **No Impact:** OAuth, auth, tenant isolation, Python services

---

## LightRAG Query Provenance Compatibility (Multi-Shape + Filename Parsing) — Jarvis — 2026-04-15

**Author:** Jarvis (Python/Neo4j specialist)  
**Status:** IMPLEMENTED  
**Scope:** Python LightRAG retrieval seam, knowledge contract compatibility, provenance enrichment

### Decision

Support both newer LightRAG \contexts\ payloads and legacy \/query/data\ chunk payloads in \LightRagRetriever\, and recover provenance from \ile_path\ / \source_doc\ when explicit document IDs are absent. Use filename parsing (e.g., \ 00007-guide.md\ → document ID 7) to enrich confidence scores via Neo4j lookup.

### Why

The live LightRAG round-trip test passed ingestion and Neo4j growth but returned empty Python retrieval results because:

1. **Response Shape Variance:** LightRAG now surfaces retrieval data in multiple formats. Some responses return \contexts\ (structured); others return \/query/data\ chunk rows.
2. **Missing Score Fields:** The legacy \/query/data\ format may carry only \ile_path\ + \eference_id\ without an explicit score field.
3. **Provenance Gap:** When a chunk lacks a document ID, we can still recover it by parsing the staged filename pattern.

This update bridges the gap: retriever accepts both shapes, extracts provenance flexibly, and enriches confidence from Neo4j when available.

### Implementation

1. **LightRagRetriever** (pp\services\lightrag_query_service.py):
   - Check for \contexts\ field first; if present, iterate over contexts
   - Fall back to \/query/data\ chunk list if \contexts\ absent
   - Extract \ile_path\ / \source_doc\ for each chunk
   - Use filename parsing to recover document ID when needed

2. **BrainKnowledgeRetriever** (pp\brain\knowledge\retrievers.py):
   - Parse filename patterns like \ 00007-guide.md\ → extract numeric prefix as document ID
   - Query Neo4j for document metadata by ID
   - Enrich confidence score from Neo4j doc data if available

3. **Tests:**
   - \	est_lightrag_retriever.py\ validates response shape flexibility (unit tests)
   - \	est_knowledge_retriever.py\ validates provenance recovery and confidence enrichment
   - \	est_processing_pipeline_regression.py\ end-to-end ingest + query round-trip

### Key Paths

- \src\AspireApp.PythonServices\app\services\lightrag_query_service.py\
- \src\AspireApp.PythonServices\app\brain\knowledge\retrievers.py\
- \src\AspireApp.PythonServices\tests\test_lightrag_retriever.py\
- \src\AspireApp.PythonServices\tests\test_knowledge_retriever.py\
- \src\AspireApp.WebTest\Tests\BasicAspireAppHostTests.cs\ (LiveLightRagNeo4jQueryRoundTrip)

### Validation

- ✓ LiveLightRagNeo4jQueryRoundTrip test passing (C# / Aspire integration)
- ✓ Python LightRAG retrieval tests all passing (27/27)
- ✓ Provenance recovered from filename parsing (document ID + filename → confidence)
- ✓ Response shape flexibility confirmed (both \contexts\ and \/query/data\ handled)

### Consequences

- Python retrieval stays compatible with multiple LightRAG response shapes.
- Confidence enrichment can use Neo4j by parsing staged filenames.
- Future LightRAG upgrades should preserve this multi-shape seam unless contract tests intentionally narrow it.
- If LightRAG changes filename pattern, update parsing logic in \BrainKnowledgeRetriever.parse_document_id_from_path()\

### Relationship to Other Decisions

- **Related:** P1 Docling-to-LightRAG-to-Neo4j audit (2026-04-13) — This work closes the retrieval gap identified in that audit.
- **Upstream:** LightRAG integration basics — This decision assumes LightRAG handoff and query services already functional.
- **No Impact:** C# API, Blazor, chat, auth, tenant isolation

---


# Decision: Roadmap Cleanup After P2-B Knowledge Layer Closure

**Date:** 2025-11-02
**Owner:** Bob (Lead Architect)
**Context:** Post-P2-B cleanup of `roadmap/Tasks.md` following consolidated LightRAG confidence session (2026-04-17)

## Problem

After P2-B knowledge layer closure (confidence scoring + live proof via `BasicAspireAppHostTests.BrainQueryReturnsConfidenceEnrichedResults`), roadmap contained:

1. **Duplicate contradiction detection entries** in Validation Layer section:
   - Line 196: Original P2 Outstanding (marked non-blocking, low priority)
   - Line 200: Revised entry marked DEFERRED to Phase 3 Critic Agent (better context)

2. **Stale outstanding item** in Cross-Layer Integration:
   - Line 206: "Add live Aspire/WebTest proof that `/brain/query` can surface claim-backed confidence without DEFAULT_CONFIDENCE=0.5"
   - Status: Now **COMPLETE** (live proof exists; P2-B gate closed 2026-04-17)

## Decision

**Rationale:**
- Keep one honest contradiction detection entry: the Phase 3-contextualized version from line 200 (now repositioned as single entry)
- Remove stale "add live proof" item; P2-B already proven with `BrainQueryReturnsConfidenceEnrichedResults` test
- Preserve Bob/Buster-approved sequencing for Phase 2 and Phase 3 items

**Changes:**
- **Validation Layer section:** Remove duplicate. Rewrite single contradiction item as `[P3 Outstanding → Phase 3 Critic Agent]` to clarify it's Phase 3 work, not P2 blocker
- **Cross-Layer Integration section:** Remove line 206 (stale proof requirement); keep two legitimate P2 documentation items
- No other items modified; sequencing preserved

## Impact

- Roadmap now reflects true P2-B closure (confidence scoring + live proof)
- Eliminates confusion about contradiction detection priority/timing
- Single source of truth for Phase 3 validation layer roadmap
- Team context: Contradiction detection is Phase 3 work (Critic Agent scope), not P2 blocker

## Approval

✅ Ready for merge by Bob (architect decision authority)



# Ollama Contention: Serialize Pipeline Workloads — Bob — 2026-04-18

**Author:** Bob (Lead / Architect)
**Status:** IMPLEMENTED
**Scope:** Processing pipeline ordering in `process_document_task`

## Context

FlowEndToEnd and LiveLightRagNeo4jQueryRoundTrip tests were timing out during processing. Root cause: `process_document_task` triggered LightRAG ingestion (which calls Ollama for LLM + embeddings) *before* completing its own Ollama embedding work (page + claim vectors). Both consumers competed for a single Ollama instance configured with `MAX_ASYNC=1`. The serial queuing pushed total processing time past the 2-minute test polling window.

## Decision

**Defer LightRAG handoff until after all Python-side Ollama embedding work completes.** This is a pure operation reorder — no logic or interface changes. The metadata dict still accumulates identically; it's persisted to disk slightly later in the pipeline.

## Rationale

- Ollama serves one request at a time; concurrent consumers create a serial queue.
- Each embedding batch call has a 60-second timeout; queuing behind LightRAG LLM calls can exceed this.
- Sequencing eliminates the contention window entirely.

## Architectural Rule

When multiple pipeline stages share a single-instance AI model server (Ollama), orchestrate them sequentially. This applies to any future processing step that calls Ollama — do not add concurrent Ollama consumers without increasing `MAX_ASYNC` or adding model-level isolation.

## Files Changed

- `src/AspireApp.PythonServices/app/routers/processing.py`



# P2-C Vector Infrastructure Review — APPROVED

**Author:** Buster (QA/Tester)  
**Date:** 2026-04-17  
**Status:** APPROVED  

## Context

Review of uncommitted P2-C working tree changes for correctness and roadmap honesty. Focus areas: AppHost embedding config, Neo4j vector indexes, embedding service, and test coverage.

## Decision

**P2-C vector infrastructure foundation is honestly scoped and correctly implemented.**

### What Was Delivered

1. **AppHost embedding config** (Jeff): Python services receive `OLLAMA_ENDPOINT`, `EMBEDDING_MODEL`, `EMBEDDING_DIM` via environment variables; wait for Ollama + embedding model before starting
2. **Neo4j vector indexes** (Jarvis): `page_content_vector` and `claim_text_vector` created via `_ensure_vector_indexes()` using Neo4j 5.x syntax with `IF NOT EXISTS` (idempotent)
3. **Vector search methods** (Jarvis): `search_claims_vector()` and `search_pages_vector()` use `db.index.vector.queryNodes()` with cosine similarity
4. **EmbeddingService** (Jarvis): Ollama-first with local sentence-transformers fallback; graceful degradation when dependencies unavailable
5. **Test coverage** (Jarvis): 11/11 tests passing in `test_vector_infrastructure.py`; all related tests still pass (28/28)

### Why This Is Honest

- **Roadmap status:** "🟡 IN PROGRESS" instead of "✅ COMPLETE" — accurate signal
- **Explicit remaining work:** Tasks.md line 173 states "Populate embeddings ... wire vector search into retrievers"
- **Foundation-first approach:** Infrastructure (indexes, helpers, config) implemented before population pipeline
- **No overclaim:** Does NOT claim vector retrieval is live; only that infrastructure is ready

### Correctness Validation

- ✅ Config wiring matches Aspire parameter patterns
- ✅ Dependency ordering correct (wait for Ollama + embedding model)
- ✅ Vector index syntax uses Neo4j 5.x conventions
- ✅ Embedding dimension (1024 for bge-m3) matches model output
- ✅ Search methods return standard result shape compatible with existing retrievers
- ✅ All tests pass (build, pytest suite)

### Contrast With P2-B Review

- **P2-B (2026-11-02):** Rejected for marking items "done" when blocker existed
- **P2-C (2026-04-17):** Approved because roadmap honestly states "foundation complete, population pending"

### Recommendation

**APPROVED for merge.** P2-C gate can remain "🟡 IN PROGRESS" until embedding population and retriever integration are complete. Foundation work enables parallel progress on Phase 3 agent selection while embedding pipeline is built.

## Impact

- Unblocks embedding population work
- Validates vector search contracts before integration
- Enables honest roadmap tracking (foundation vs. full feature)



# Decision: Playwright Browser Installation Required for WebTest Suite

**Date:** 2025-02-05  
**Author:** Buster (QA/Tester)  
**Status:** Active

## Context

`AspireApp.WebTest` uses Playwright for end-to-end testing of the Blazor UI. Playwright requires browser binaries (Chromium, Firefox, or WebKit) to be installed locally, which are **not** committed to the repository.

## Problem

After a fresh clone or environment change, tests fail with:
```
Microsoft.Playwright.PlaywrightException : Driver not found: 
C:\Users\...\AspireApp.WebTest\bin\.playwright\node\win32_x64\node.exe
```

This manifests as test crashes in the fixture initialization (`TestFixture.InitializeAsync`).

## Decision

**All developers must install Playwright browsers before running WebTest suite.**

### Required Setup Steps

1. Install Playwright CLI (once per machine):
   ```powershell
   dotnet tool update --global Microsoft.Playwright.CLI
   ```

2. Install Chromium browser (required for tests):
   ```powershell
   playwright install chromium
   ```

### Why Not Automate This?

- Playwright browser binaries are ~200MB and not suitable for repository storage
- Playwright's design expects local installation per environment
- CI/CD pipelines already handle this via GitHub Actions or Azure DevOps tasks
- Manual install is one-time setup, acceptable for local development

## Verification

After installation, verify with:
```powershell
dotnet test src\AspireApp.WebTest\AspireApp.WebTest.csproj --filter "FullyQualifiedName~SignedInUserCanSaveRenameResumeAndDeleteConversation"
```

Test should pass in ~107 seconds (depending on Aspire startup time).

## Documentation Location

This should be added to:
- `README.md` — Prerequisites section
- `docs/development-setup.md` — If such a file exists
- CI/CD documentation — Already handled by pipeline scripts

## Related

- Playwright documentation: https://playwright.dev/dotnet/docs/browsers
- GitHub Actions setup: Uses `mcr.microsoft.com/playwright/dotnet` Docker image with browsers pre-installed



# P2-C Embedding Population Finish

**Date:** 2026-04-18  
**Owner:** Jarvis  
**Status:** Implemented

## Context
Embedding population during ingestion needed a real-path regression proof, and per-item embedding calls were adding unnecessary overhead.

## Decision
Batch page and claim embeddings with `EmbeddingService.embed_batch` inside `process_document_task`, then persist them with `Neo4jService.populate_page_embedding` and `Neo4jService.populate_claim_embedding`. Add regression coverage that exercises the real processing flow with faked collaborators to prove batch calls and persistence.

## Implementation
- `src/AspireApp.PythonServices/app/routers/processing.py`
- `src/AspireApp.PythonServices/tests/test_processing_pipeline_regression.py`

## Validation
- `python -m pytest -q tests/test_processing_pipeline_regression.py tests/test_embedding_population_pipeline.py`



# P2-C Vector Index Infrastructure — Foundation Complete

**Date:** 2026-04-17  
**Author:** Jarvis  
**Status:** ✅ Implemented  
**Phase:** P2-C (Knowledge Layer — Vector Search Readiness)

## Context

P2-C gate requires Neo4j vector indexes to be queryable for semantic search. Current text-based search (`CONTAINS` matching) is limited in retrieval quality. Vector similarity search will enable semantic matching and improve relevance ranking.

Jeff has already configured Aspire to pass embedding configuration via environment variables (`OLLAMA_ENDPOINT`, `EMBEDDING_MODEL`, `EMBEDDING_DIM`). The next step is to create the vector indexes and search infrastructure in Python.

## Decision

Implement vector index infrastructure in three parts:

1. **Vector index creation** — Idempotent index creation in `Neo4jService._ensure_vector_indexes()`
2. **Vector search methods** — `search_claims_vector()` and `search_pages_vector()` using Neo4j 5.x vector similarity syntax
3. **Embedding service** — `EmbeddingService` with sentence-transformers support and graceful degradation

## Implementation

### Vector Indexes Created

- **`page_content_vector`**: Index on `Page.content_embedding` (384 dimensions, cosine similarity)
- **`claim_text_vector`**: Index on `Claim.text_embedding` (384 dimensions, cosine similarity)

Both indexes created with `IF NOT EXISTS` for idempotency. Runs on every Neo4j service initialization.

### Vector Search Methods

```python
def search_claims_vector(
    self, 
    query_embedding: List[float], 
    limit: int = 10,
    similarity_threshold: float = 0.7
) -> List[Dict[str, Any]]:
    """Vector-based semantic search over Claim nodes."""
    # Uses db.index.vector.queryNodes('claim_text_vector', ...)
    
def search_pages_vector(
    self, 
    query_embedding: List[float], 
    limit: int = 10,
    similarity_threshold: float = 0.7
) -> List[Dict[str, Any]]:
    """Vector-based semantic search over Page nodes."""
    # Uses db.index.vector.queryNodes('page_content_vector', ...)
```

Both methods return standard result shape matching text-based search for easy integration into `SemanticKnowledgeRetriever`.

### Embedding Service

`EmbeddingService` provides:
- Lazy-loaded sentence-transformers model
- Batch encoding support
- Graceful degradation when model unavailable
- Configurable via `EMBEDDING_MODEL` and `EMBEDDING_DIMENSION` env vars

## Rationale

**Why sentence-transformers as default?**
- Proven, lightweight, easy to install (`pip install sentence-transformers`)
- all-MiniLM-L6-v2 model: 384 dimensions, good quality/speed tradeoff
- Can switch to Ollama embeddings later without changing search infrastructure

**Why create indexes at service startup?**
- Simplifies deployment (no separate migration scripts)
- Idempotent with `IF NOT EXISTS` — safe to run repeatedly
- Fails gracefully if Neo4j version doesn't support vector indexes

**Why separate vector search methods from text search?**
- Different query patterns (vector similarity vs. text CONTAINS)
- Different tuning parameters (similarity threshold vs. keyword matching)
- Easier to benchmark and A/B test retrieval quality
- Can use both in fallback chain: vector-first → text-fallback

**Why 0.7 similarity threshold?**
- Conservative default; filters low-quality matches
- Tunable per-query for different use cases
- Prevents semantic drift (returning unrelated content with low similarity)

## Consequences

**Positive:**
- ✅ Vector search infrastructure ready for use
- ✅ No blocking dependencies — can populate embeddings in parallel with Phase 3 agent work
- ✅ Test coverage proves correctness without requiring live embeddings
- ✅ Foundation supports both sentence-transformers and Ollama embeddings

**Neutral:**
- Vector indexes consume Neo4j storage (minimal until embeddings populated)
- Embedding model adds ~90MB to Python container size (sentence-transformers)

**Remaining work:**
- Populate `content_embedding` and `text_embedding` during ingestion
- Wire vector search into `SemanticKnowledgeRetriever`
- Consider switching to Ollama embeddings if Jeff configures it

## Alternatives Considered

**1. Use full-text indexes instead of vector indexes**
- Rejected: Full-text matching is keyword-based, not semantic
- Vector similarity captures meaning, not just word overlap

**2. Defer vector infrastructure to Phase 3**
- Rejected: P2-C gate explicitly requires vector indexes queryable
- Foundation work enables parallel development of agent framework

**3. Require Ollama embeddings from day one**
- Rejected: Creates deployment dependency; harder to test locally
- sentence-transformers works standalone; Ollama integration can follow

**4. Use dedicated vector DB (Qdrant, Pinecone)**
- Deferred: Neo4j 5.x vector indexes sufficient for MVP
- Can swap implementation behind `IKnowledgeRetriever` if performance degrades

## Testing

All infrastructure validated with `test_vector_infrastructure.py` (8/8 tests passing):
- Vector index creation (idempotent, both Page and Claim indexes)
- Vector search methods (correct query structure, parameter passing)
- Embedding service (model loading, batch encoding, graceful degradation)

No live Neo4j or embeddings required for test suite.

## Files Modified

- `src/AspireApp.PythonServices/app/services/neo4j_service.py` — vector index creation, search methods
- `src/AspireApp.PythonServices/app/services/embedding_service.py` — new file, embedding generation
- `src/AspireApp.PythonServices/tests/test_vector_infrastructure.py` — new file, 8 tests
- `roadmap/Tasks.md` — P2-C status updated to "Infrastructure Complete"
- `.squad/agents/jarvis/history.md` — learning entry added

## Next Steps

1. **Embedding population pipeline** — Generate embeddings during ingestion, store in `Page.content_embedding` and `Claim.text_embedding`
2. **Wire into SemanticKnowledgeRetriever** — Use vector search first, fall back to text search
3. **Benchmark retrieval quality** — Compare vector vs. text search on sample queries
4. **Consider Ollama switch** — If Jeff configures Ollama embedding endpoint, update `EmbeddingService`

## Related

- **P2-B** — Confidence scoring (now complete)
- **P3-A** — Agent framework selection (next priority)
- **Skill:** `.squad/skills/neo4j-confidence-enrichment/SKILL.md` — confidence enrichment pattern
- **Decision:** `.squad/decisions/inbox/jarvis-lightrag-confidence-enrichment.md` — P2-B decision



# Chat Persistence Test Timing Strategy

**Date:** 2025-01-08  
**Author:** Jeff (.NET Dev)  
**Status:** Proposed — awaiting Buster's test strategy decision

## Context

`ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation` exhibits intermittent failures due to timing mismatch between test expectations and AI response behavior.

## The Issue

- **Test timeout:** 90 seconds waiting for send button to re-enable (`WaitForControlEnabledAsync`)
- **AI timeout:** 180 seconds (3 minutes) for response completion (`CallBackgroundAI`)
- **Failure mode:** Test fails at 91s when AI legitimately needs more time
- **Success case:** Test passes in ~108s when AI responds quickly

## Product Code Status

✅ **Correct** — `IsAIResponsing` management is sound:
- Set to `true` before AI call (line 594)
- Reset to `false` in `finally` block (line 1040)
- Handles all exception paths properly
- All required `data-testid` hooks present and validated

## Recommendations for Buster (QA Lead)

Choose one strategy:

### Option A: Align Test Timeout with Product Behavior
```csharp
private static async Task WaitForControlEnabledAsync(ILocator locator, string description)
{
    var timeoutAt = DateTime.UtcNow.AddSeconds(210); // Was 90, now 210 (3.5min)
    // ... rest unchanged
}
```
**Pros:** Tests real production timing  
**Cons:** Slow test runs; still fails if AI takes 4+ minutes

### Option B: Mock AI Responses in Test Scenarios
Add test-mode AI mock that returns instantly:
```csharp
// In test setup
services.Configure<HomeConfigurations>(opts => 
{
    opts.AIEndpoint = "http://mock-ollama";
});
services.AddSingleton<BrainChatClient>(sp => new MockBrainChatClient());
```
**Pros:** Fast, deterministic tests  
**Cons:** Doesn't validate real AI integration timing

### Option C: Separate Integration vs. E2E Tests
- **Unit/Integration:** Mock AI, fast validation of conversation CRUD
- **E2E (nightly only):** Real AI with generous timeouts
**Pros:** Best of both worlds  
**Cons:** More test infrastructure

## Jeff's Position

Product code is correct. Test strategy is Buster's domain. I'm available to add test hooks or timing configuration if needed, but the fix belongs in test infrastructure, not in `Chat.razor.cs`.

## Files
- `src/AspireApp.WebTest/Tests/ChatConversationPersistenceTests.cs` — test timing
- `src/AspireApp.Web/Components/Pages/Chat.razor.cs` — product behavior (validated correct)

## Next Steps

1. Buster reviews timing strategy options
2. If Option B or C chosen, Jeff can add mock infrastructure
3. Update test suite accordingly



---
date: 2026-04-17
author: Jeff
status: Implemented
scope: AppHost orchestration, Python service configuration
---

# Decision: P2-C Embedding Configuration via Aspire Environment Variables

## Context

Phase 2, Gate C (P2-C) requires Neo4j vector indexes on `Page.content` and `Claim.text` properties. This depends on:
1. Python services being able to generate embeddings via Ollama
2. Embedding model configuration reaching the Python container runtime
3. Startup ordering ensuring Ollama + embedding model are ready before Python services

The embedding model was already defined in AppHost configuration (`AI-Embedding-Model: bge-m3:latest`) and loaded into Ollama, but Python services had no access to this config.

## Decision

Wire embedding infrastructure to Python services via three new Aspire environment variables:
- `OLLAMA_ENDPOINT` — Dynamic Ollama HTTP endpoint from service discovery
- `EMBEDDING_MODEL` — Model name from `AI-Embedding-Model` parameter (e.g., `bge-m3:latest`)
- `EMBEDDING_DIM` — Fixed embedding dimension for the model (1024 for bge-m3)

Add startup dependencies:
- Python service waits for Ollama container (`WaitFor(ollama)`)
- Python service waits for embedding model load (`WaitFor(embeddingmodel)`)

## Rationale

1. **Consistency with existing patterns:** LightRAG container already receives similar config (`EMBEDDING_BINDING_HOST`, `EMBEDDING_MODEL`, `EMBEDDING_DIM`). Python services follow the same shape.

2. **Dynamic endpoint resolution:** Using `ollama.GetEndpoint("http")` instead of hardcoded URLs ensures service discovery works across environments (dev, staging, Docker networks).

3. **Aspire-first orchestration:** Startup dependencies guarantee embedding model availability before Python worker starts processing documents—prevents runtime failures during cold starts.

4. **Configuration surface minimalism:** Only three variables added; embedding dimension is static since it's model-intrinsic and rarely changes.

5. **Unblocks Jarvis cleanly:** Python implementation now has all required environment context to build embedding service wrapper and populate vector indexes without further AppHost changes.

## Alternatives Considered

1. **Python reads config from shared file:** Rejected—breaks Aspire service discovery patterns and creates hidden dependencies.

2. **Python calls back to C# Gateway for config:** Rejected—adds HTTP round-trip during startup; violates separation of concerns.

3. **Hardcode Ollama endpoint in Python:** Rejected—couples Python to specific network topology; fails in multi-environment deployments.

## Implementation

**File:** `src/AspireApp.AppHost/AppHost.cs`

**Changes:**
- Lines 145-153: Added environment variables to Python service registration
- Lines 153-154: Added `.WaitFor(ollama)` and `.WaitFor(embeddingmodel)` dependencies

**Roadmap Update:**
- `roadmap/Tasks.md` line 170-173: Marked AppHost config complete, ownership transferred to Jarvis for vector index implementation

## Validation

- ✅ Build succeeds: `dotnet build`
- ✅ AppHost starts without errors
- ✅ Python service receives all three environment variables (verify via Aspire dashboard)
- ✅ Embedding model loads before Python startup (verify startup sequence in dashboard logs)

## Next Steps

1. **Jarvis:** Implement `EmbeddingService` wrapper in Python (`app/services/embedding_service.py`)
2. **Jarvis:** Create Neo4j vector index schema (Cypher `CREATE VECTOR INDEX` syntax)
3. **Jarvis:** Wire embedding service into document ingestion pipeline to populate indexes on Page/Claim creation
4. **Buster:** Add integration test validating embedding service can reach Ollama and generate vectors

## Impact

- Python services: New environment variables available; no code changes yet
- Neo4j: No changes; vector index creation deferred to Python implementation
- LightRAG: No changes; already has own embedding config
- Web/Gateway: No changes; vector search queries deferred to Phase 3

## Cross-References

- **History:** `.squad/agents/jeff/history.md` (2026-04-17 entry)
- **Roadmap:** `roadmap/Tasks.md` lines 167-178 (P2-C gate)
- **Related Config:** `AppHost.cs` lines 186-193 (LightRAG embedding config)
- **Related Pattern:** `aspire-orchestration.instructions.md` — environment variable best practices



# Decision: P2-C Embedding Population Phase — Active Work Begins

**Date:** 2026-04-17T23:55:00Z  
**Recorded by:** Scribe (Copilot)  
**Topic:** P2-C Phase Transition from Infrastructure to Active Implementation  
**Status:** RECORDED (for merge into `.squad/decisions.md`)  

## Decision

The team transitions from **P2-C vector foundation waiting** (Ollama infrastructure setup) into **active P2-C work: embedding population during document ingestion** for Page and Claim nodes.

### Scope (Specific)
- **What:** Generate and store embeddings for Page and Claim nodes during ingest pipeline
- **When:** Parallel with Phase 3 agent framework selection (no blocking dependencies)
- **Who:** Jarvis (Python embedding implementation), supported by Bob (architecture review), Jeff (UI preparation)

### What This Means
- Ollama embedding service is now operational (infrastructure ready)
- **P2-C is not deferred** — it is actively proceeding into the next honest step
- Embedding vectors stored in Neo4j enable retrieval patterns for Phase 3 agents
- Vector index creation (current P2-C work) unblocks Phase 3 retrieval and chat flows

### Out of Scope (Explicitly)
- Contradiction detection (P2-C secondary goal) — deferred to Phase 3 Critic Agent
- Vector similarity search endpoint (P3-A dependency; Jeff handles after embeddings populated)
- Multi-model embedding selection — hardcoded to single Ollama model per configuration

## Rationale

1. **Previous gate completion (P2-B)** unblocks this work; no external blockers remain
2. **Infrastructure readiness** (Ollama running) validates this is the next honest step
3. **Phase 3 parallel execution** enabled: Embedding generation does not depend on agent framework selection
4. **Critical path clarity** ensures team focus on highest-value sequential work, not speculative features

## Implementation Notes

- **Ingest flow modification:** `app.routers.ingest_document` → call Ollama embedding service → store in Neo4j
- **Validation:** Embeddings queryable via vector similarity search (test harness; not UI-facing yet)
- **Performance monitoring:** Track bulk ingest time; if embedding adds >10s per doc, consider async queuing
- **Schema consistency:** Coordinate embedding dimension/format between Python code and Neo4j storage

## What This Unblocks

- ✅ Vector search foundation for retrieval patterns (Phase 3-A dependency)
- ✅ Jeff can design embedding-aware Blazor chat UI components
- ✅ Jarvis can proceed without waiting for agent framework decision
- ✅ Bob can focus on framework selection without coordination overhead

## Cross-Team Handoff

| Role | Responsibility |
|------|---|
| **Jarvis** | Implement embedding generation + Neo4j storage during ingest |
| **Jeff** | Design Blazor chat UI for embedding-aware retrieval results |
| **Bob** | Verify embedding schema; finalize agent framework selection by 2026-04-24 |

## Risk Assessment

| Risk | Likelihood | Mitigation |
|------|-----------|-----------|
| Ollama latency during bulk ingest | Medium | Monitor; batch requests; consider async if >10s per doc |
| Vector dimension mismatch (multiple models) | Low | Hardcode model name; validate at store time |
| Neo4j vector search performance (1000+ embeddings) | Medium | Add vector index; profile queries early |
| Embedding storage bloats Neo4j | Low | Monitor disk; consider separate vector store if >GB |

## Related Decisions

- **P2-B Completion** (2026-04-17): LightRagRetriever confidence enrichment verified
- **Phase 3 Framework Selection** (pending 2026-04-24): ✅ **DECIDED: PydanticAI** with swappable architecture (2026-04-15)
- **P2-C Vector Foundation** (2026-04-17): Embedding infrastructure identified as blocker, now resolved

## Decision Timeline

- **2026-04-17T23:55:00Z:** P2-C embedding population phase recorded
- **2026-04-18 (est):** Jarvis begins embedding generation implementation
- **2026-04-15 (COMPLETED):** ✅ Agent framework selection finalized: PydanticAI with `IAgentProvider` abstraction
- **2026-04-24 (deadline CLOSED):** Phase 3b unblocked; Python implementation ready; awaiting C# gateway confirmation

## Files Updated

- `.squad/log/2026-04-17T23-55-00Z-p2c-embedding-population-phase.md` ✅
- `.squad/log/2026-04-15T18-38-37-pydanticai-framework-selection.md` ✅ (NEW)
- `.squad/orchestration-log/2026-04-15T18-38-37-bob.md` ✅ (NEW)
- `.squad/orchestration-log/2026-04-15T18-38-37-jarvis.md` ✅ (NEW)
- `.squad/identity/now.md` ✅
- `.squad/decisions/inbox/` — CLEARED (4 files merged + deduped)

---

## PydanticAI Agent Framework Selection + Swappable Architecture — Bob — 2026-04-22

**Author:** Bob (Lead / Architect)  
**Status:** PROPOSED — Pending Eric approval (Decision Deadline: 2026-04-24)  
**Scope:** Agent framework selection for Phase 3b Critique mode, abstraction layer for framework replaceability

### Context

Phase 3b Critique Mode requires multi-agent orchestration (Planner → Retriever → Synthesizer → Critic). Eric directed: **use PydanticAI, but design for swappability** in case a better framework emerges. This decision unblocks Phase 3b while protecting against framework lock-in.

### Decision

**Adopt PydanticAI as the Phase 3b agent framework, abstracted behind a provider interface to enable zero-refactor swaps.**

#### Why PydanticAI

1. **Pydantic Native**: Built on Pydantic v2, aligning with existing contract models (`BrainChatRequest`, `ReasonResponse`, `Evidence`)
2. **Lightweight**: Minimal abstraction overhead vs LangGraph or CrewAI
3. **Type Safety**: First-class Python typing — agents type-checked at development time
4. **Tool Integration**: Native function-calling support for structured LLM interactions
5. **Model Agnostic**: Works with Ollama (current LLM) and other providers without vendor lock-in

#### Swappable Architecture: The Agent Provider Abstraction

**Core Principle:** The BRAIN reasoning layer orchestrates agents through **contracts**, not framework-specific APIs.

**Layer Boundaries:**
```
BrainChatRequest (user input)
    ↓
/brain/chat endpoint (FastAPI router)
    ↓
AgentOrchestrator (framework-agnostic coordinator)
    ↓
IAgentProvider interface (abstraction seam)
├── PydanticAIProvider (current implementation)
├── LangGraphProvider (future swap candidate)
└── CustomProvider (fallback if needed)
    ↓
ReasonResponse (output contract)
```

#### Key Interfaces

**`IAgentProvider` (Abstract Base Class)**
- `async def reason(request, knowledge_context) -> ReasonResponse` — Execute multi-agent reasoning pipeline
- `def get_provider_name() -> str` — Return provider identifier

**`AgentOrchestrator` (Framework-Neutral Coordinator)**
- Depends only on `IAgentProvider`, not specific framework
- Coordinates knowledge retrieval + agent reasoning
- Returns contract-shaped `ReasonResponse`

#### Dependency Injection Pattern

**Factory for Provider Selection:**
```python
def create_agent_provider() -> IAgentProvider:
    provider_name = os.getenv("AGENT_PROVIDER", "pydantic-ai")
    if provider_name == "pydantic-ai":
        return PydanticAIProvider(...)
    elif provider_name == "langgraph":
        return LangGraphProvider(...)  # Future
    else:
        raise ValueError(f"Unknown provider: {provider_name}")
```

#### Framework Swap Example: PydanticAI → LangGraph

**Step 1:** Implement `LangGraphProvider(IAgentProvider)`  
**Step 2:** Update `agent_factory.py` to handle `AGENT_PROVIDER=langgraph`  
**Step 3:** Set env var in `AppHost.cs` — `.WithEnvironment("AGENT_PROVIDER", "langgraph")`  
**Step 4:** Restart Aspire. **No code changes** in routers, orchestrator, or contracts.

### What This Protects Against

1. **Framework Abandonment**: If PydanticAI stalls, swap to LangGraph with minimal effort
2. **Performance Issues**: If PydanticAI proves slow, benchmark alternatives behind same interface
3. **Vendor Lock-In**: Agent logic lives in our contracts, not framework-specific types
4. **API Breaking Changes**: PydanticAI updates isolated to provider class

### Implementation Ownership

#### Jarvis (Python Extension Points)

1. `app/brain/reasoning/agent_provider.py` — `IAgentProvider` ABC
2. `app/brain/reasoning/pydantic_ai_provider.py` — `PydanticAIProvider` implementation
3. `app/brain/reasoning/orchestrator.py` — `AgentOrchestrator` class
4. `app/brain/reasoning/agent_factory.py` — `create_agent_provider()` factory
5. `app/routers/brain.py` — Wire Critique mode to orchestrator
6. `requirements.txt` — Add `pydantic-ai==0.0.14`

#### Bob (Documentation & Coordination)

- [x] Write this decision document
- [ ] Update roadmap/Plan.md Phase 3b with PydanticAI selection
- [ ] Review Jarvis's implementation for contract adherence

#### Jeff (C# Gateway)

- [ ] No changes required — Gateway already expects `ReasonResponse`
- [ ] Optional: Add `X-Agent-Provider` response header for observability

#### Buster (Testing)

- [ ] Unit tests for `IAgentProvider` contract compliance
- [ ] Integration test: Critique mode E2E
- [ ] Mock swap test: Replace provider without code changes
- [ ] Performance benchmark: Critique vs Regular mode

### Acceptance Gates (Phase 3b)

| Gate | Criteria | Status |
|------|----------|--------|
| **P3b-A** | `PydanticAIProvider` implements `IAgentProvider` | ✅ DONE |
| **P3b-B** | `/brain/chat` (mode=critique) returns `ReasonResponse` with reasoning steps | ⏳ PENDING |
| **P3b-C** | Factory allows env-var-based provider swap | ✅ DONE |
| **P3b-D** | Mock swap test: Replace provider without code changes | ✅ DONE |
| **P3b-E** | Unit tests pass for agent provider interface | ✅ DONE |
| **P3b-F** | Critique mode E2E test passes | ⏳ PENDING |

### Risk Assessment

| Risk | Mitigation |
|------|-----------|
| PydanticAI is young (v0.0.x) | Interface abstraction allows quick swap; evaluate Phase 4 |
| Limited agent ecosystem vs LangGraph | PydanticAI simpler = less complexity; we control orchestration |
| Ollama-only initially | PydanticAI supports multiple models; expand Phase 6 |
| Performance unknowns | Benchmark Phase 4; contract allows side-by-side testing |

### Decision Rationale

**Why not LangGraph?** Graph DSL adds complexity without clear benefit for sequential pipeline.  
**Why not CrewAI?** Role-based abstraction doesn't map cleanly to BRAIN contracts.  
**Why not Custom?** Reinventing orchestration is effort-intensive.  
**Why PydanticAI?** Best Pydantic alignment, lightweight, type-safe, fastest path to Phase 3b.

---

## User Directive: PydanticAI with Swappable Design — Eric VanArtsdalen — 2026-04-15T18:26:47Z

**By:** Eric VanArtsdalen (via Copilot)  
**Status:** CAPTURED — For team memory  
**What:** Use PydanticAI for the agentic Critique-mode implementation, but design and implement it so the framework can be swapped out later if needed.  
**Why:** User request — explicit directive to guide Phase 3b decision-making

### Impact

- Unblocks Bob's architecture boundary definition (see "PydanticAI Agent Framework Selection" decision)
- Drives Jarvis's Python implementation via `IAgentProvider` abstraction
- Accepted by team: Architecture ensures framework replaceability without code refactoring
- Decision deadline: 2026-04-24 (Phase 3b gates closure)

---

## PydanticAI Selection with Swappable Abstraction — Jarvis — 2026-04-24 (IMPLEMENTED)

**Author:** Jarvis (Python / Data Dev)  
**Status:** IMPLEMENTED — Targeted tests passing  
**Scope:** Framework-agnostic agent orchestration, PydanticAI provider implementation, critique pipeline foundation

### Context

Phase 3b requires multi-agent orchestration. Eric requested PydanticAI with swappable design. Bob defined architecture boundary. Jarvis implemented the Python seam behind protocol abstraction.

### Decision

**Implement PydanticAI behind `AgentProvider` protocol, enabling low-friction provider replacement.**

#### Implementation Details

**Files created:**
- `app/brain/reasoning/agent_provider.py` — Protocol + response model
- `app/brain/reasoning/pydantic_ai_provider.py` — PydanticAI adapter
- `app/brain/reasoning/critique_pipeline.py` — Agent orchestration
- `app/routers/brain.py` — Critique mode routing

**DI wiring:**
```python
def get_agent_provider(llm: LlmChatService = Depends(...)) -> PydanticAIProvider:
    return PydanticAIProvider(model_name=llm.model_name, endpoint=llm.endpoint)

def get_critique_pipeline(
    agent_provider: PydanticAIProvider = Depends(get_agent_provider),
    retriever: BrainKnowledgeRetriever = Depends(get_brain_retriever),
) -> CritiquePipeline:
    return CritiquePipeline(agent_provider=agent_provider, knowledge_retriever=retriever)
```

**Agent roles:**
- **Planner**: Decomposes complex questions into sub-queries
- **Retriever**: Handled by `BrainKnowledgeRetriever` (not PydanticAI agent)
- **Synthesizer**: Merges knowledge sources into coherent draft
- **Critic**: Validates quality, checks contradictions, scores confidence

### Test Coverage

**33 targeted tests, all passing:**

- 13 critique pipeline tests (provider availability, orchestration, sub-query extraction, deduplication, confidence extraction, protocol conformance)
- 20 brain chat tests (routing, Regular mode unchanged, Critique mode provider detection, mock isolation)

### Acceptance Gates Status

- ✅ **P3b-A**: `PydanticAIProvider` implements `IAgentProvider` contract
- ✅ **P3b-C**: Factory pattern ready for env-var swap
- ✅ **P3b-D**: Mock swap test confirms swappability
- ✅ **P3b-E**: Unit tests passing
- ⏳ **P3b-B**: Awaiting C# gateway for full validation
- ⏳ **P3b-F**: E2E test pending UI integration

### Future Enhancements

1. Tool integration for agent function calls
2. Parallel agent execution for sub-queries
3. Proactive Monitor for contradiction detection
4. Framework benchmarking (PydanticAI vs LangGraph vs custom)
5. Agent prompt tuning

### Relationship to Other Decisions

- Upstream: Bob's swappable architecture design (2026-04-22)
- Upstream: Eric's user directive (2026-04-15)
- Coordination: Jarvis implementation validates Bob's abstraction boundaries
- Impact: Unblocks Phase 3b UI wiring + acceptance gate P3b-B closure

---

## Chat Conversation Persistence Test Timeout Alignment — Jeff — 2026-04-18

**Author:** Jeff (.NET Dev)  
**Status:** IMPLEMENTED  
**Scope:** Test infrastructure timeout configuration for chat persistence tests

### Context

`ChatConversationPersistenceTests.SignedInUserCanSaveRenameResumeAndDeleteConversation` test failed intermittently when AI responses exceeded 90 seconds. Not a product bug—application correctly handles slow AI responses—but test infrastructure issue where helper timeouts didn't align with legitimate AI response times.

### Problem

- `AppHostMappingModel.Options.Timeout` is 180 seconds (3 minutes) to accommodate slow AI
- Test helpers `WaitForTranscriptToContainAsync` and `WaitForControlEnabledAsync` used 90-second timeouts
- When Ollama responses took 90-180 seconds (legitimate), tests failed even though app behavior was correct
- Created flaky tests failing due to insufficient wait time, not bugs

### Decision

**Align test helper timeouts with infrastructure capabilities: increase from 90s to 180s to match AppHostMappingModel timeout.**

#### Changes Made

1. **`WaitForTranscriptToContainAsync`** (line 365-383)
   - Timeout: 90s → 180s
   - Comment: "AI responses can take up to 180s under load; align with AppHostMappingModel.Options.Timeout"

2. **`WaitForControlEnabledAsync`** (line 516-530)
   - Timeout: 90s → 180s
   - Comment: "Send button disabled during AI response; allow 180s (matches infrastructure timeout)"

#### Rationale

- Test timeouts should reflect infrastructure, not ideal-case expectations
- AI-integrated features need patience for legitimate slow responses
- Product behavior correct; test infrastructure must be patient enough
- No assertions weakened—all persistence/rename/resume/delete validations intact

### Validation

**Test Results:**
- ✅ Passes reliably at ~160-165 seconds (within 180s timeout)
- ✅ No assertion weakening
- ✅ Multiple runs confirm stability
- ✅ Build succeeded

### Design Principle

**Test Infrastructure Should Match Reality:**
- External services (AI, APIs, databases) require timeouts reflecting actual response characteristics
- Fast-path optimization good, but tests shouldn't fail on legitimate slow paths
- Infrastructure config should guide test timeout selection
- Prefer patience over flakiness — longer test better than unreliable

### Future Considerations

- Check similar patterns in other AI-integrated tests
- Consider extracting shared constant for AI test timeouts
- Monitor if responses consistently exceed 180s (infrastructure investigation needed)

### Relationship to Other Decisions

- Upstream: Buster validated scenario passes end-to-end once Playwright Chromium installed
- Prior diagnosis: Product correct; test infrastructure needed hardening
- Scope: Test infrastructure only — no application code changes

