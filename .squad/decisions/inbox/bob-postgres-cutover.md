# Postgres Cutover — Operational Data Migration

**Author:** Bob (Lead / Architect)  
**Date:** 2026-07-26  
**Status:** APPROVED — Ready for execution  
**Scope:** Replace SQLite shared-file pattern with Postgres for `files` and `document_pages` tables

---

## Context

The Web UI (C#/Blazor) and Python processing service currently share a single SQLite file (`data-resources.db`) via Docker bind mounts. This works but has caused recurring operational pain:

- WAL vs DELETE journal-mode conflicts across the Windows host / Linux container boundary
- Stale-read workarounds in Python (`_should_prefer_fresh_reads`, fresh connection fallbacks)
- Multi-candidate path resolution logic (8+ code paths to find the right `.db` file)
- `DeleteJournalModeInterceptor` hack in C# to force journal mode on every connection
- SQLite `CheckpointDatabaseAsync` calls after every write in FileStorageService
- Bind-mount file visibility issues between services

**Postgres is already provisioned in AppHost** (`builder.AddPostgres("postgres")` with `appdb` database, pgWeb, bind mount, user/pass parameters). Both services already `WaitFor(postgres)` and receive `POSTGRES_USER`/`POSTGRES_PASSWORD` environment variables. Neither service actually connects to Postgres yet.

---

## Decision

### 1. Keep the same `files` + `document_pages` schema in Postgres

The schema is stable and well-documented in `docs/CROSS_SERVICE_CONTRACT.md`. Both sides agree on column names, types, and writer/reader ownership. No structural redesign needed.

**DDL changes (SQLite → Postgres):**

| SQLite | Postgres |
|--------|----------|
| `INTEGER PRIMARY KEY AUTOINCREMENT` | `SERIAL PRIMARY KEY` (or `GENERATED ALWAYS AS IDENTITY`) |
| `DATETIME` | `TIMESTAMPTZ` |
| `DEFAULT CURRENT_TIMESTAMP` | `DEFAULT NOW()` |
| `TEXT` (for JSON columns) | `JSONB` for `page_metadata`; `TEXT` for everything else |
| Placeholder `?` | Placeholder `%s` (psycopg2) |

**Indexes and constraints transfer directly.** The `UNIQUE(file_id, page_number)` and FK cascade behavior are standard SQL.

### 2. C# Web Changes (Jeff owns)

| What | Action |
|------|--------|
| **NuGet packages** | Remove `Microsoft.EntityFrameworkCore.Sqlite`. Add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` |
| **Program.cs** | Replace `AddDbContext<UploadDbContext>(options.UseSqlite(...))` with `builder.AddNpgsqlDbContext<UploadDbContext>("appdb")`. Remove `ResolveSqliteConnectionString`, `GetSqliteDataSource`, `ShouldResolveAgainstContentRoot` helpers. Remove `DeleteJournalModeInterceptor` class entirely |
| **FileStorageService** | Delete `CheckpointDatabaseAsync()` and all calls to it. Remove `Microsoft.Data.Sqlite` import |
| **UploadDbContext** | Replace `HasDefaultValueSql("CURRENT_TIMESTAMP")` with `HasDefaultValueSql("NOW()")` in legacy entity config. Primary table config is attribute-driven and works cross-provider |
| **DocumentEntities.cs** | No changes needed — `[Column]` attributes are provider-agnostic |
| **AppHost.cs (webfrontend)** | Add `.WithReference(postgres)` to webfrontend. Remove `ConnectionStrings__DefaultConnection` env var (Aspire injects it via `WithReference`) |

### 3. Python Changes (Jarvis owns)

| What | Action |
|------|--------|
| **requirements.txt** | Add `psycopg2-binary` (sync) or `psycopg[binary]` (async-capable). Remove: nothing (sqlite3 is stdlib) |
| **Dockerfile** | No change needed — `psycopg2-binary` has no native build deps |
| **DatabaseService class** | Replace `sqlite3` connection pool with `psycopg2.pool.ThreadedConnectionPool`. Remove `ConnectionPool` class. Remove all SQLite pragma logic. Remove multi-candidate path resolution. Remove fresh-connection workaround methods. SQL: `?` → `%s`, `AUTOINCREMENT` → `SERIAL`, add `RETURNING id` to inserts |
| **Connection config** | Read `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` from env. AppHost must pass these (see below) |
| **Schema init** | `_ensure_database_schema()` keeps `CREATE TABLE IF NOT EXISTS` + `CREATE INDEX IF NOT EXISTS` — standard Postgres DDL. Remove `_ensure_required_columns` ALTER TABLE migration logic (fresh Postgres, no legacy schemas to heal) |

### 4. AppHost.cs Changes (Jeff owns, but affects both)

**Add to Python service env vars:**
```
.WithEnvironment("POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
.WithEnvironment("POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
.WithEnvironment("POSTGRES_DB", "appdb")
```

Or, simpler: `.WithReference(postgres)` and read the Aspire-injected connection string. For a Dockerfile-based service the explicit env vars are cleaner since Python won't use Aspire service discovery natively.

**Remove from AppHost:**
- SQLite file setup block (lines 17-31): `sharedDatabaseFileName`, `sharedDatabaseFile`, `sharedDatabaseConnectionString`, `Directory.CreateDirectory`, `File.Create`
- `ASPIRE_DB_PATH` env var from Python service
- `/app/docs-database` bind mount from Python service (keep `/app/data` mount for file storage)
- `ConnectionStrings__DefaultConnection` env var from webfrontend

**Keep:** `sharedDatabasePath` directory creation and bind mount for the postgres data directory (already wired).

### 5. Cross-Service Contract Update

`docs/CROSS_SERVICE_CONTRACT.md` section "Shared Database (SQLite)" becomes "Shared Database (PostgreSQL)". The table schema, status lifecycle, writer/reader ownership, and processing trigger contract all remain unchanged. Remove the journal-mode paragraph and path-resolution section.

---

## What This Eliminates

- `ConnectionPool` class (150+ lines of SQLite workarounds)
- `DeleteJournalModeInterceptor` class
- `CheckpointDatabaseAsync` method
- `_should_prefer_delete_journal` / `_should_prefer_fresh_reads` / `_fetch_*_from_fresh_connection` methods
- Multi-candidate database path resolution (~100 lines)
- SQLite pragma tuning (WAL, synchronous, mmap, cache_size, busy_timeout)
- All stale-read workarounds
- Journal-mode conflicts between host and container
- SQLite file creation at AppHost startup

**Net reduction:** ~400+ lines of SQLite-specific complexity across both services.

---

## What Gets Deferred (Do NOT Do Now)

| Item | Why Later |
|------|-----------|
| Legacy entity removal (`Document`, `ProcessedDocument` classes) | Unrelated to Postgres; separate cleanup task |
| EF Core Migrations framework | `EnsureCreated()` works fine for a development-stage project; add migrations when schema stabilizes further |
| Python diagnostic scripts (`init_database.py`, `fix_schema.py`, `diagnose_database.py`) | Utility scripts, not runtime. Rewrite when someone needs them |
| Test infrastructure updates | Buster's scope — existing tests use SQLite in-memory/temp files. Update after core cutover lands |
| `test_p0_contract_audit.py` rewrite | Tests exercise DatabaseService which will change. Buster updates after Jarvis's Python cutover |
| Data migration from existing SQLite | Development databases are disposable. No production data to migrate |

---

## Execution Order

1. **Jeff: AppHost.cs** — Wire Postgres connection details to both services, remove SQLite plumbing
2. **Jeff: Web project** — NuGet swap, Program.cs provider change, remove SQLite helpers
3. **Jarvis: Python DatabaseService** — psycopg2 backend, remove SQLite pool and workarounds
4. **Bob: Contract doc** — Update `CROSS_SERVICE_CONTRACT.md`
5. **Buster: Tests** — Update Python tests to work against Postgres (can use testcontainers or in-memory mock)

Steps 1-2 can run in parallel with step 3. Step 4 after 1-3 land. Step 5 after all.

---

## Validation

- `dotnet build` passes
- AppHost starts, Aspire dashboard shows all services green
- Upload a document via Blazor → verify `files` row appears in pgWeb
- Trigger processing via Python → verify status transitions and `document_pages` rows in pgWeb
- Existing FlowEndToEnd test passes (once test infrastructure is updated)

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| EF Core `EnsureCreated()` doesn't match Python DDL | Both sides create the same tables; first service to start creates them. Use `IF NOT EXISTS` on both sides |
| Sequence/serial ID conflicts | Both sides use auto-generated IDs. C# writes files, Python reads them by ID — no cross-write conflicts on the same table's PK |
| Postgres container startup delay | Already handled by `WaitFor(postgres)` on both services |
| Connection string format differences | Aspire handles C# injection; Python reads explicit env vars — no ambiguity |
