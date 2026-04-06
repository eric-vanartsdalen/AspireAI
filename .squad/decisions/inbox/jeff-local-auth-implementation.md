# Jeff Decision — Managed local auth slice

- **Date:** 2026-04-06
- **Context:** AspireAI needed a first admin-provisioned username/password flow without introducing ASP.NET Core Identity or breaking the existing mock/Microsoft auth seam.

## Decision

- Keep the existing `IAuthService` seam and make `CompositeAuthService` enumerate registered auth services instead of hardcoding specific provider combinations.
- Store managed local accounts in `local_auth_users` inside the existing `UploadDbContext` / Postgres operational store.
- Because the app still relies on `EnsureCreated` instead of EF migrations, add a startup bootstrapper that repairs/creates the `local_auth_users` table for persisted databases before seeding or authenticating.
- Treat `Authentication:Local:SeedUsers` as a dev-only create-missing path that accepts `PasswordHash` values only and never overwrites existing database users.
- Render the local credential experience from the existing `SignInPanel.razor` surface using provider metadata (`RequiresCredentials`, `SignInPath`) and submit it to `POST /auth/local/signin`.

## Consequences

- Local, Microsoft, and demo providers can share the same sign-in page without rewriting the shell.
- The database remains the operational source of truth for local accounts even when development seeds are present.
- Future provider additions only need a new auth service registration plus provider metadata instead of another brittle combined-service constructor edit.
