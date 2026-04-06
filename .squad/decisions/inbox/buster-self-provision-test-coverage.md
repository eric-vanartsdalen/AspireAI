# Self-Provisioning Test Coverage for Local Auth

**Author:** Buster (QA / Tester)  
**Date:** 2026-04-06  
**Status:** Test suite complete, awaiting implementation

## Summary

Created comprehensive test coverage for self-provisioning local authentication. Tests document expected behavior when unknown credentials are submitted: create account automatically, then sign in. Coverage includes duplicate prevention, existing-user login, error paths, and contract stability.

## Test Coverage Created

**File:** `src\AspireApp.WebTest\Tests\LocalAccountSelfProvisioningTests.cs`

### Test Categories

1. **New User Creation (3 tests)**
   - Unknown username creates user
   - Unknown email creates user
   - Password hashed securely

2. **Duplicate Handling (4 tests)**
   - Existing username rejects duplicate
   - Existing email rejects duplicate
   - Case-insensitive duplicate prevention
   - Normalization prevents bypass

3. **Existing User Login (3 tests)**
   - Correct password authenticates
   - Wrong password rejects
   - Email identifier works

4. **Error Paths (5 tests)**
   - Empty username rejected
   - Empty password rejected
   - Whitespace credentials rejected
   - Disabled local auth blocks creation
   - Invalid tenant ID rejects

5. **Contract Stability (4 tests)**
   - Default tenant assignment
   - Display name generation
   - Timestamps recorded
   - Identifier normalization

6. **Integration (2 tests)**
   - End-to-end creation + sign-in flow
   - Duplicate handling with wrong password

## Implementation Requirements

When Warden implements self-provisioning in `LocalAccountAuthenticator.AuthenticateAsync()`:

1. **Check for existing user first** (by normalized username OR email)
2. **If user exists:**
   - Validate password with PasswordHasher
   - Return AuthenticatedUser on success, null on failure
3. **If user doesn't exist:**
   - Validate credentials are non-empty
   - Generate display name from identifier
   - Derive email if identifier is username (e.g., `username@aspire.local`)
   - Derive username if identifier is email (e.g., prefix before `@`)
   - Hash password securely
   - Assign default tenant ID (first available or "default")
   - Set timestamps (CreatedAt, UpdatedAt)
   - Persist to database
   - Return AuthenticatedUser

## Critical Edge Cases

- **Duplicate race condition:** Two simultaneous requests with same unknown username
  - Database unique constraints prevent actual duplicate
  - Second request fails at INSERT, should retry lookup + password validation
- **Email vs username ambiguity:** `john.doe` could be username or invalid email
  - Solution: Treat as username unless contains `@` character
- **Normalization consistency:** `JohnDoe`, `johndoe`, `JOHNDOE` must resolve to same user
  - Use `LocalAuthValueNormalizer.Normalize()` before lookups and saves

## Adjustments If Behavior Changes

If Warden decides self-provisioning should NOT happen automatically:

1. Keep tests as-is but mark with `[Fact(Skip = "Self-provisioning deferred")]`
2. Invert logic: test that unknown credentials return null
3. Add separate registration endpoint tests instead

If self-provisioning requires additional validation (email verification, password strength):

1. Add new test section for validation rules
2. Update error path tests to cover new rejection cases
3. Document minimum password requirements

## Contract Violations to Watch

These tests protect against breaking changes:

- **Form field name drift:** `identifier` parameter must match across SignInPanel → Program.cs → LocalAccountAuthenticator
- **Tenant assignment changes:** Default tenant must be valid per `TenantContextService.GetAvailableTenants()`
- **Normalization changes:** Username/email normalization must remain case-insensitive, consistent
- **Password rehash triggers:** Existing tests verify `PasswordVerificationResult.SuccessRehashNeeded` updates hash

## Related Tests

- `LocalAccountAuthenticatorTests.cs` — Existing user authentication
- `LocalAuthEndpointContractTests.cs` — Form → endpoint contract
- `LocalAuthBootstrapperTests.cs` — Seed user logic (won't conflict with self-provision)
- `SignInPanelTests.cs` — UI rendering (no change needed)

## Decision Log Entry Required

This test suite documents expected behavior. Warden should confirm or adjust the self-provisioning strategy before implementing. If the behavior changes significantly, notify Buster to update tests.
