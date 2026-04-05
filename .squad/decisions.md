# Decisions

> Shared decision log. All agents read this before starting work.
> Scribe merges new decisions from `.squad/decisions/inbox/` after each session.
> **Note (2026-04-05):** Merged 8 inbox decisions from Postgres cutover (Jeff, Jarvis, Buster) and BRAIN pivot (Kujan, Verbal, Eric). Archived 9 decisions from 2025-11-02 and 2026-03-27/28 (~7 KB) to `decisions-archive.md` to maintain ~20 KB target. Inbox cleared.

<!-- Decisions are appended below. Each entry starts with ### -->
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



