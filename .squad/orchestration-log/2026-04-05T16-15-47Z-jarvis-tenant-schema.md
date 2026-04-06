### 2026-04-05T16-15-47Z — Tenant Schema Gap Fix - Python/Postgres Alignment

| Field | Value |
|-------|-------|
| **Agent routed** | Jarvis (Python / Data Dev) |
| **Why chosen** | After Bob's UI revision, runtime uploads to Python failed (HTTP 500) because Python DatabaseService schema was missing tenant_id column. Required Python schema alignment with C# contract. |
| **Mode** | sync |
| **Why this mode** | Schema synchronization affects cross-service data persistence. Must be validated immediately by Buster's contract audit before proceeding. |
| **Files authorized to read** | src/AspireApp.Web/Data/DocumentEntities.cs; src/AspireApp.Web/Shared/UploadDbContext.cs; src/AspireApp.PythonServices/app/services/database_service.py |
| **File(s) agent must produce** | src/AspireApp.PythonServices/app/services/database_service.py (tenant_id column, indexes, column definitions); decision document in .squad/decisions/inbox/ |
| **Outcome** | ✅ Completed. Python schema now includes tenant_id in CREATE TABLE (line 235), column definitions (line 88), and two indexes (idx_files_tenant, idx_files_tenant_status). Schema matches C# contract. Contract audit tests ready for validation. |

---

**Details:** Added `tenant_id TEXT NOT NULL DEFAULT 'default'` to Python schema initialization and migration repair logic. Matches C# FileMetadata entity. Indexes support both single-tenant queries and composite tenant+status filtering.
