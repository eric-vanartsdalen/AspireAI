# Local Username/Password Auth — Security Floor

**Author:** Warden (Security Specialist)
**Date:** 2025-07-25
**Status:** APPROVED with constraints
**Scope:** Managed local username/password auth slice for AspireAI

## Context

Eric requested a classic managed username/password login. The app already has a pluggable `IAuthService` / `AuthServiceFactory` pattern with config-driven mode selection (`mock`, `microsoft`, `combined`, `auto`). A new `local` provider must slot into this factory without weakening existing trust boundaries.

## Decision: Security Floor for First Slice

### 1. Password Storage — REQUIRED

- **Use ASP.NET Core Identity's `PasswordHasher<T>`** (PBKDF2-HMAC-SHA512, 600k iterations in .NET 10, auto-upgrades). It ships with the framework — no third-party dependency needed.
- **Do NOT** roll a custom hash, use raw SHA-256/MD5, or store plaintext.
- Store the hash in a new `local_users` table in the existing PostgreSQL operational database (`UploadDbContext`).

### 2. Explicitly OUT OF SCOPE (first slice)

| Deferred | Reason |
|----------|--------|
| Self-registration / sign-up UI | Admin seeds users; reduces attack surface |
| Password reset / forgot-password | Requires email infrastructure not yet present |
| Email verification | No email service wired |
| Account lockout after N failures | Good to add later; first slice is admin-only users in a dev/demo context |
| MFA / TOTP | Layer on after base local auth is stable |
| Password complexity policy UI | Enforce a minimum (12 chars) in code; no config UI yet |
| RBAC / roles | User identity is sufficient for now (existing pattern) |

### 3. Minimum Protections — REQUIRED even in a small first slice

1. **Constant-time comparison** — `PasswordHasher<T>.VerifyHashedPassword()` already handles this.
2. **Minimum password length: 12 characters** — enforce at the service boundary before hashing.
3. **No password in logs** — never log the password value, even at Debug level.
4. **Generic error on login failure** — return "Invalid username or password" for both wrong-user and wrong-password. Do not disclose which is wrong.
5. **Rate awareness** — log failed attempts with username and timestamp. Full lockout is deferred, but the data must exist for future lockout implementation. A simple `failed_login_count` and `last_failed_at` column is sufficient.
6. **HTTPS only in production** — already enforced by `UseHsts()` + `UseHttpsRedirection()` in Program.cs.
7. **Cookie settings unchanged** — reuse the existing hardened cookie (HttpOnly, SameSite=Lax, SecurePolicy=SameAsRequest, 8h sliding). The local auth service issues the same `aspireapp-auth` cookie via the same `CookieAuthenticationDefaults.AuthenticationScheme`.
8. **Endpoint gating** — `/auth/local/*` endpoints must be conditionally registered, same pattern as `/auth/mock/*`. When `Authentication:Service` = `microsoft`, local endpoints are not mapped.

### 4. Tenant Isolation for Local Users

- Each local user row has a `default_tenant_id` column (non-nullable, default `"default"`).
- On sign-in, `TenantContextService.InitializeForUser(user.DefaultTenantId)` is called — identical to the mock and Microsoft paths.
- No cross-tenant user sharing in the first slice. One user → one default tenant.
- Admin-seeded users get their tenant assignment at creation time.

### 5. Approaches Jeff MUST AVOID

| Anti-pattern | Why |
|--------------|-----|
| **Custom password hashing** (raw SHA, bcrypt NuGet, hand-rolled PBKDF2) | Framework `PasswordHasher<T>` is audited, auto-upgrades iteration count, and handles versioning. No reason to go outside it. |
| **Storing passwords in `appsettings.json` or user-secrets** | Passwords are user credentials — they go in the database, hashed. Config files are for service secrets. |
| **Reusing `MockAuthCatalog` with cleartext passwords** | Mock catalog is an in-memory demo fixture. Local auth needs a real persisted store with hashed credentials. |
| **Skipping the `IAuthService` factory seam** | The local provider MUST register as a new `AuthServiceRegistration` (e.g., `ServiceKey = "local"`) and be resolvable via `AuthenticationOptions.Service`. No special-casing in `Program.cs`. |
| **Exposing a user-enumeration endpoint** | No `/auth/local/users` list endpoint. Sign-in takes username + password; nothing else. |

## Implementation Shape (not code — just the seam contract)

- New `Authentication:Service` value: `"local"` (and update `auto` resolution logic if local credentials exist)
- New class: `LocalAuthService : IAuthService` with `ServiceKey = "local"`
- New EF entity: `LocalUser` (id, username, email, display_name, password_hash, default_tenant_id, failed_login_count, last_failed_at, created_at, updated_at)
- New table: `local_users` in PostgreSQL via `UploadDbContext`
- New endpoints: `POST /auth/local/signin` (username + password form), `GET /auth/local/signout` (redirect to sign-out)
- Seed mechanism: EF migration seed or a CLI/admin command — NOT self-registration
- `AuthProviderOption` for local: `RequiresUserSelection = false` (user types credentials; no dropdown)
- `CompositeAuthService` updated to include local when both local and other providers are active

## Acceptance Criteria (for Buster)

- [ ] `PasswordHasher<T>.VerifyHashedPassword()` is the only verification path
- [ ] No password value appears in any log output at any level
- [ ] Login failure returns identical error for wrong-user and wrong-password
- [ ] `/auth/local/*` endpoints are not mapped when `Authentication:Service` = `microsoft`
- [ ] `local_users.password_hash` column stores only hashed values (no plaintext, no reversible encoding)
- [ ] Minimum 12-character password enforced at sign-in creation / seed time
- [ ] Tenant isolation flows through `TenantContextService` identically to other providers
