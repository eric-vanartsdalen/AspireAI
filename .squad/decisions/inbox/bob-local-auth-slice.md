# Local Username/Password Auth — First Slice Recommendation — Bob — 2026-07-29

**Author:** Bob (Lead / Architect)
**Status:** RECOMMENDED — Pending Eric approval
**Scope:** Smallest viable first slice for managed local username/password login within the existing pluggable auth architecture.

## Context

Eric requested a "classic managed basic username and password login." The existing auth architecture already has a clean provider seam: `IAuthService` → `AuthServiceFactory` → `AuthServiceRegistration`, with Mock, Microsoft Entra, and Composite providers registered. The question is whether local auth fits this seam cleanly and what the minimum viable slice looks like.

## Decision

**Add a `LocalAuthService : IAuthService` that validates username/password credentials against config-provisioned users, then issues the same ASP.NET Core cookie ticket the mock and Microsoft providers already use.**

### 1. Provider Seam Fit — Yes, Clean Addition

The existing registration pattern handles this without modification:

- New `LocalAuthService` with `ServiceKey = "local"`
- Registered via `AddAuthServiceRegistration<LocalAuthService>("local")`
- New `LocalAuthenticationOptions` bound from `Authentication:Local` config section
- `CompositeAuthService` must become dynamic (accept all registered `IAuthService` providers) rather than hardcoding Mock + Microsoft. This is the one structural change needed.

No teardown of Mock or Microsoft auth. All three coexist.

### 2. No ASP.NET Core Identity — Stay in the Custom Seam

ASP.NET Core Identity brings its own DbContext, UserManager, SignInManager, role stores, and token providers. It would fight every existing abstraction:

- `AuthenticatedUser` vs `IdentityUser`
- `AppAuthenticationStateProvider` vs Identity's `AuthenticationStateProvider`
- `AuthenticationContext` scoped state vs Identity's cookie/session management

**Instead:** Use `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (standalone, no full Identity dependency) or `BCrypt.Net-Next` for password hashing. Validate credentials in the local auth endpoint, then call `httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ...)` — identical pattern to mock auth endpoints in Program.cs.

### 3. Sign-In Only — No Self-Service Registration

Pre-provisioned local users defined in `appsettings.json`. No registration flow.

**Rationale:** Registration requires email validation, password strength UI, duplicate detection, confirmation flow — massive scope creep. The immediate value is proving credential validation through the existing provider seam. Registration is a separate slice after the auth path is proven.

**Config shape:**
```json
"Authentication": {
  "Local": {
    "DefaultAppTenantId": "default",
    "Users": [
      {
        "Username": "admin",
        "DisplayName": "Admin User",
        "Email": "admin@aspireai.local",
        "PasswordHash": "$2a$12$...",
        "DefaultTenantId": "default"
      }
    ]
  }
}
```

Password hashes are generated offline (CLI tool or startup helper). Never store plaintext passwords in config.

### 4. Tenant Assignment — Same as Mock Auth

Each pre-provisioned user gets a `DefaultTenantId` in their config record. The existing `TenantContextService.InitializeForUser(user.DefaultTenantId)` call in the sign-in flow handles tenant hydration. No new mechanism needed.

### 5. Red Flags for Implementation

| Risk | Mitigation |
|------|------------|
| **Don't import ASP.NET Core Identity** | Use standalone `PasswordHasher<T>` or BCrypt. Full Identity would require rewriting the auth layer. |
| **Don't add a new DbContext for users yet** | Config-based users keep the first slice purely additive. DB-backed users is a separate migration. |
| **Don't modify `IAuthService` interface** | The existing `SignInAsync(providerId, userId)` contract works. Password validation happens in the server-side endpoint, not in the Blazor component. |
| **`SignInPanel.razor` needs a new rendering mode** | Local auth needs a username+password form, not a user picker dropdown. Add a `RequiresCredentials` flag to `AuthProviderOption` (or equivalent) to trigger the form. Don't overload `RequiresUserSelection`. |
| **`CompositeAuthService` is hardcoded to 2 providers** | Make it accept all registered providers dynamically via constructor injection of `IEnumerable<IAuthService>` or a provider registry. Don't keep adding if/else branches per provider. |
| **Password hashes in config** | Acceptable for first slice (same risk profile as connection strings). Mark as secret in Aspire parameters. Move to DB-backed storage when user management evolves. |
| **Don't expose password validation in Blazor interactive mode** | The username/password form submits to a server-side POST endpoint (like `/auth/local/signin`), not an interactive Blazor call. Credentials must not travel over SignalR. |

## Implementation Outline (for Jeff)

1. **`LocalAuthenticationOptions`** — options class bound to `Authentication:Local`, with `Users` list and `DefaultAppTenantId`
2. **`LocalAuthService : IAuthService`** — returns one `AuthProviderOption` with `RequiresCredentials = true`, validates username against config catalog
3. **`AuthProviderOption`** — add `bool RequiresCredentials` property (default false, non-breaking)
4. **`CompositeAuthService`** — refactor to accept providers dynamically instead of hardcoding Mock + Microsoft
5. **`SignInPanel.razor`** — new rendering branch for `RequiresCredentials` providers showing username/password form
6. **`Program.cs`** — new `POST /auth/local/signin` endpoint: validate credentials → hash check → issue cookie → redirect
7. **`AuthenticationOptions`** — add `const string LocalService = "local"` and update `ResolveEffectiveService` auto-resolution logic
8. **Config** — add `Authentication:Local` section to `appsettings.json` with 2 pre-provisioned users
9. **Offline hash tool** — simple console command or startup log that generates BCrypt hashes for initial provisioning

## What This Unlocks

- Real credential-based login alongside existing mock and Microsoft auth
- Proves the provider seam is genuinely extensible (not just mock → Microsoft)
- Foundation for DB-backed user management and self-service registration in a later slice
- Local development without needing Microsoft Entra configuration
