# Tenant Edge-Case Revision — Warden — 2026-04-07

**Scope:** Surgical fixes for the two gaps Buster rejected.

## Decisions

### 1. Broadened save-failure catch in `AddMemberByUsernameAsync`

The original code only caught `DbUpdateException`. Any other exception during
`SaveChangesAsync` (e.g. `InvalidOperationException`, transient infra errors)
would bubble up unhandled, potentially leaking implementation details through
the Blazor error boundary.

**Change:** Widened the catch to `Exception` (excluding `OperationCanceledException`)
so every save failure collapses to `return false` with a warning log. This
preserves the generic "couldn't update access" UX contract and prevents
information leakage.

### 2. Direct `EnsureTenantAccessAsync` test coverage

Existing tests exercised the recovery logic only through `LocalAuthBootstrapper`
and `LocalAccountAuthenticator`. Added six direct tests against
`TenantManagementService.EnsureTenantAccessAsync`:

- No memberships → creates protected default tenant
- Has memberships but none default → backfills a new default
- Multiple defaults → resolves to single (protected wins)
- Unprotected default → promoted to protected
- Empty user ID → `ArgumentException` guard
- Generic save failure on add-member → returns `false`

No schema, model, or UI changes required.

---

**Status:** INBOX — Awaiting Scribe merge to `.squad/decisions.md`
