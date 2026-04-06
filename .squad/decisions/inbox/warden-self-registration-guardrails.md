# Self-Registration Security Guardrails — Warden — 2025-07-25

**Author:** Warden (Security Specialist)
**Status:** APPROVED with mandatory guardrails
**Scope:** On-the-fly user self-provisioning via local auth sign-in form

## Context

User explicitly requested that when a username is submitted on the local sign-in form and it doesn't exist in the database, the system should auto-create the user with the hashed password and then proceed with login. The prior local-auth slice was admin-provisioned only (seed users).

## Decision

**APPROVE** self-registration with the following mandatory constraints Jeff must implement:

### 1. Config Gate
- Add `AllowSelfRegistration` boolean to `LocalAuthenticationOptions` (default: `false`).
- Auto-create path is only active when explicitly enabled. Existing deployments are unaffected.

### 2. Username-Only Auto-Create
- If the submitted identifier contains `@`, it is treated as an email lookup ONLY — never auto-create from email-shaped input.
- Reason: creating a DB record claiming an email address without verification is an unverifiable identity assertion.
- Username-shaped identifiers (no `@`) that don't match an existing user trigger auto-create.

### 3. Password Floor
- Enforce 12-character minimum password length.
- Apply this check early (before DB lookup) on ALL sign-in attempts to avoid timing-based user enumeration.
- Reject with the same generic error as any other failure.

### 4. Username Validation
- Allowed characters: alphanumeric, hyphens, underscores, periods.
- Length: 3–100 characters.
- Invalid usernames return the same generic error — no shape-specific feedback.

### 5. Derived Fields for Auto-Created Users
- `Username`: submitted identifier (trimmed)
- `NormalizedUsername`: via existing `LocalAuthValueNormalizer.Normalize()`
- `Email`: `{normalizedUsername}@local.aspireai` (synthetic, clearly non-deliverable)
- `NormalizedEmail`: normalize the synthetic email
- `DisplayName`: submitted identifier (trimmed)
- `DefaultTenantId`: `TenantContextService.DefaultTenantId` ("default")
- `IsActive`: `true`

### 6. Generic Error Behavior
- All failure paths (user not found with self-reg disabled, duplicate username, invalid chars, password too short, DB constraint violation) must return the same `BuildInvalidLocalCredentialResult` redirect.
- No endpoint or response may reveal whether a username is taken or available.
- The existing unique index on `normalized_username` prevents race-condition duplicates; catch the constraint exception and return generic error.

### 7. Mandatory Tests
- Auto-create succeeds for valid username + valid password when `AllowSelfRegistration = true`
- Auto-create user can immediately log in on subsequent attempt
- Auto-create assigns `DefaultTenantId = "default"`
- Email-shaped identifier (`contains @`) with no existing match returns generic error (no auto-create)
- Password shorter than 12 chars returns generic error
- Duplicate username returns generic error
- Invalid username characters return generic error
- Existing login behavior unchanged when `AllowSelfRegistration = false`

## Explicitly Deferred
- Rate limiting / CAPTCHA (acceptable risk at current product stage behind config gate)
- Email verification
- Account lockout enforcement
- Password complexity beyond length minimum
- Self-registration via email identifiers

## Rationale
The config gate limits blast radius. Username-only creation avoids unverified email claims. The 12-char floor and generic errors maintain the security baseline established in the local-auth-floor decision. DB unique indexes provide race-condition safety without application-level locking.
