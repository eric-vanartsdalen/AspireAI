# Decision: Local Managed Username/Password Auth Seam

**Date:** 2025-11-02  
**Owner:** Jeff (.NET Dev)  
**Status:** Feasibility Assessment Complete — Ready for Implementation

## Summary

Local managed username/password authentication can be cleanly integrated into the existing `IAuthService` abstraction without breaking changes or architectural refactoring.

## Key Decisions

### 1. Storage: UploadDbContext for First Slice
- Add `LocalAuthCredential` table directly to `UploadDbContext`
- Rationale: Appropriate for MVP; can be separated into dedicated `AuthDbContext` later
- Schema: `Id`, `Username` (unique), `PasswordHash`, `Email`, `DisplayName`, `TenantId`, `CreatedAt`
- Password hashing: bcrypt (use `BCrypt.Net-Next` NuGet package)

### 2. Service Implementation Pattern
- Create `LocalAuthService : IAuthService` following `MockAuthService` shape
- `GetProviders()` → single provider: `AuthProviderOption("local", "Local Account", description, css class, requiresUserSelection: true)`
- `GetUsers()` → returns empty (no user picker; form-driven)
- `SignInAsync()` → redirects to username/password form URI
- `SignOutAsync()` → clears cookie session

### 3. Endpoint Pattern
- New endpoint: **POST `/auth/local/signin`**
  - Accept form data: `username`, `password`, `returnUrl`
  - Validate credentials against `LocalAuthCredential` table
  - Create `ClaimsPrincipal` with `AuthenticatedUserClaims`
  - Sign cookie: `CookieAuthenticationDefaults.AuthenticationScheme`
  - Redirect to `returnUrl` or home
- Reuse existing cookie signing pattern from Program.cs (lines 144–195)

### 4. UI Form Strategy
- **Option A (simpler):** Add form section directly in `SignInPanel.razor` for local provider
- **Option B (modular):** Create new `LocalSignInForm.razor` component and render from `SignInPanel` when local provider selected
- Recommend **Option A** for MVP (fewer components)

### 5. Tenant Initialization
- Local users must have valid `TenantId` from hardcoded list in `TenantContextService`
- Seeder strategy: Assign all local users to `"default"` tenant or admin-configurable tenant
- Will not create dynamic tenants from local auth (keep Phase 1 simple)

### 6. Composite Mode Integration
- Wire `LocalAuthService` into `CompositeAuthService` alongside Microsoft
- When multiple providers active, `CompositeAuthService.GetProviders()` returns combined list
- `CompositeAuthService.SignInAsync()` routes to correct handler based on provider ID
- **Gotcha fix:** Update `SignOutAsync()` to detect provider from claims (currently hardcoded to Microsoft)

### 7. Configuration
- Add `"local"` constant to `AuthenticationOptions.cs`
- Register via `AddAuthServiceRegistration<LocalAuthService>("local")`
- No new config keys needed for MVP (use hardcoded test users or seeder)

## Touchpoints (8 files)

### New Files (3)
- `Services/LocalAuthCredential.cs` — EF model
- `Services/LocalAuthService.cs` — IAuthService implementation
- `Services/LocalAuthSeeder.cs` — Optional: seed test accounts on startup

### Modified Files (5)
- `Shared/UploadDbContext.cs` — Add `DbSet<LocalAuthCredential>`
- `Services/AuthenticationServiceCollectionExtensions.cs` — Register `LocalAuthService` + options
- `Services/AuthenticationOptions.cs` — Add `LocalService = "local"` constant
- `Services/CompositeAuthService.cs` — Wire local into combined provider list + routing
- `Program.cs` — Add POST `/auth/local/signin` endpoint + optional seeder init

### Unchanged
- `SignInPanel.razor` — Will auto-discover local provider
- `MockAuthCatalog.cs`, `MockAuthService.cs` — No changes
- `AuthenticationContext.cs`, `AppAuthenticationStateProvider.cs` — No changes

## Implementation Order

1. Create `LocalAuthCredential` model + seed data structure
2. Add table to `UploadDbContext` + migration
3. Implement `LocalAuthService`
4. Add POST `/auth/local/signin` endpoint to `Program.cs`
5. Wire into `AuthenticationServiceCollectionExtensions` + `CompositeAuthService`
6. Add form UI (Option A: SignInPanel section)
7. Test composite flow: mock + local + optional Microsoft
8. Fix `CompositeAuthService.SignOutAsync()` to detect provider

## Validation Checklist

- [ ] Local user can sign in with username/password
- [ ] Cookie session persists after sign-in
- [ ] Tenant is correctly initialized from local user record
- [ ] Local provider appears in SignInPanel alongside mock/Microsoft (if configured)
- [ ] Sign-out clears session for local-authenticated users
- [ ] Composite mode shows all providers correctly
- [ ] No regression in existing mock or Microsoft auth flows

## Future Considerations (Not MVP)

- Password reset / change flow
- User registration (self-service)
- Email verification
- Separate `AuthDbContext` for auth tables
- Two-factor authentication
- Session timeout / refresh token strategy
- User account lockout after N failed attempts
