# Skill: Shared Postgres Contract Audit

## When to use

Use this when Aspire, Web, and Python all depend on the same operational Postgres store and a regression test needs to prove those surfaces still agree after infrastructure changes.

## Pattern

1. Read the active database name from `src/AspireApp.AppHost/AppHost.cs` (`postgres.AddDatabase("...")`).
2. Assert AppHost passes that same name to Python via `POSTGRES_DATABASE`.
3. Assert Web resolves that same name via `GetConnectionString("...")`.
4. Keep separate assertions for provider choice (`UseNpgsql`, `psycopg-pool`) and for absence of SQLite-only code paths.

## Why this works

The contract is shared alignment, not a legacy literal. Deriving the name from AppHost preserves strong drift detection while allowing legitimate renames during platform fixes.
