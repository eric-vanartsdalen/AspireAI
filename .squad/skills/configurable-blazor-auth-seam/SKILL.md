# Configurable Blazor auth seam

## When to use

- You need a mock auth slice now, but real providers later.
- The UI should stay stable while provider selection moves to configuration.

## Pattern

1. Keep the UI on a single auth abstraction (`IAuthService`).
2. Register allowed auth implementations explicitly in DI.
3. Resolve the active implementation through a factory bound to configuration, and centralize the effective-mode resolver so the HTTP route surface follows the same decision.
4. Let `auto` resolve to the default provider composition for the current slice, and keep that decision in one resolver so the UI, endpoint surface, and DI selection stay aligned.
5. When the sign-in page intentionally mixes providers, build the combined catalog from registered auth services instead of hardcoding constructor pairs/triples.
6. Bridge user state into Blazor authorization with a custom `AuthenticationStateProvider`.
7. Add service-level tests for provider selection and sign-in/sign-out state transitions before relying on E2E proof.
8. When a provider needs a server-posted credential form instead of a hosted redirect or demo picker, extend provider metadata (for example `RequiresCredentials` + `SignInPath`) so the shared sign-in surface can render the form without bypassing the auth abstraction.
9. For dev-seeded local accounts, accept only precomputed password hashes and insert missing rows instead of overwriting existing database users.
10. When local first-use registration is needed, keep it inside the credential verifier (`LocalAccountAuthenticator`) behind config, enforce password floors before lookup, treat email-shaped identifiers as lookup-only, and collapse all failures back to the same invalid-credentials redirect.
11. If usernames only need case-insensitive uniqueness, keep that rule on normalized identifier columns and unique indexes instead of rewriting the auth storage model; add regression tests that prove different casing maps to the same account.
12. Surface local password floors through the shared sign-in component using the same constant the server enforces (`minlength` + helper text) so UX copy and server validation do not drift.

## AspireAI example

- Registration: `src\AspireApp.Web\Services\AuthenticationServiceCollectionExtensions.cs`
- Effective mode resolver: `src\AspireApp.Web\Services\AuthenticationOptions.cs`
- Selection: `src\AspireApp.Web\Services\AuthServiceFactory.cs`
- Route gating: `src\AspireApp.Web\Program.cs`
- Provider implementations: `src\AspireApp.Web\Services\MockAuthService.cs`, `LocalAuthService.cs`, `MicrosoftEntraAuthService.cs`
- Local credential verifier: `src\AspireApp.Web\Services\LocalAccountAuthenticator.cs`
- Shared sign-in UI: `src\AspireApp.Web\Components\Shared\SignInPanel.razor`
- Tests: `src\AspireApp.WebTest\Tests\AuthServiceFactoryTests.cs`, `AuthenticationOptionsTests.cs`, `CompositeAuthServiceTests.cs`, `LocalAuthServiceTests.cs`, `LocalAccountAuthenticatorTests.cs`, `LocalAuthBootstrapperTests.cs`
