# Blazor Mock Auth Shell

## Purpose

Add a UX-first authentication foundation to a Blazor app without introducing real OAuth, cookies, or JWTs yet.

## Use When

- You need an unauthenticated landing page before protected app areas.
- You want mock Microsoft/Google/demo sign-in now, but a clean seam for real providers later.
- You need tenant or workspace context to initialize from user identity while staying a separate service.

## Pattern

1. Create a scoped session model (`AuthenticationContext`) that holds the current user.
2. Bridge it into Blazor with a custom `AuthenticationStateProvider`.
3. Register `AddAuthorizationCore()` and `AddCascadingAuthenticationState()`.
4. Put provider logic behind an interface (`IAuthService`) and keep the current implementation mock.
5. Use `AuthorizeRouteView` in `Routes.razor` and `[Authorize]` on protected pages.
6. Keep the public landing/sign-in UI reusable (`SignInPanel.razor`) and add stable `data-testid` hooks for UI tests.
7. Let sign-in initialize a separate tenant/workspace service from the selected user's default tenant.

## AspireAI Reference

- `src/AspireApp.Web/Services/AuthenticationContext.cs`
- `src/AspireApp.Web/Services/AppAuthenticationStateProvider.cs`
- `src/AspireApp.Web/Services/IAuthService.cs`
- `src/AspireApp.Web/Services/MockAuthService.cs`
- `src/AspireApp.Web/Components/Shared/SignInPanel.razor`
- `src/AspireApp.Web/Components/Routes.razor`
