# Pluggable OIDC auth in Blazor Server

## When to use

- A Blazor Server shell already has a mock auth seam and needs a real external provider without rewriting UI components
- The app should keep demo/mock sign-in for local regression testing

## Pattern

1. Keep the UI bound to a single auth abstraction (`IAuthService` here).
2. Add a provider-specific OIDC implementation that only knows how to start challenge/sign-out navigation.
3. Let ASP.NET Core cookie + OpenID Connect middleware handle the actual Microsoft flow.
4. Map external claims into the app's existing authenticated-user model during `OnTokenValidated`.
5. If you need live + demo providers side-by-side, add a small composite auth service instead of hardwiring the UI.
6. When the provider should immediately leave the app (for example Microsoft hosted sign-in), render a direct link to the challenge endpoint from the Razor page instead of depending solely on a Blazor click callback.
7. Keep demo providers explicitly labeled as demos so mixed-mode testing never looks like a real IdP flow.
8. For Microsoft, allow `TenantId` to be optional when the authority builder already falls back to `common`; this keeps personal-account scenarios viable.

## AspireAI reference

- `src/AspireApp.Web/Services/MicrosoftEntraAuthService.cs`
- `src/AspireApp.Web/Services/CompositeAuthService.cs`
- `src/AspireApp.Web/Services/AuthenticationServiceCollectionExtensions.cs`
- `src/AspireApp.Web/Program.cs`
- `src/AspireApp.Web/Components/Shared/SignInPanel.razor`
- `src/AspireApp.Web/Services/MicrosoftEntraAuthenticationOptions.cs`
