description: 'Tune one-off SQL scripts or imports used alongside AspireAI without over-optimizing for production relational workloads.'
agent: 'agent'
tools: ['read_file', 'grep_search', 'apply_patch', 'run_in_terminal']
owner: '@eric-vanartsdalen'
audience: 'Data Maintainers'
dependencies: ['SQL Client Tools', 'Database Access Credentials']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Spot-checking batch imports, optimizing staging scripts before moving data to Neo4j, advising on ad-hoc analytics queries.
- **Prerequisites**: Access to query text plus representative data volume (CSV dump or staging database).
- **Sample Inputs**: Bulk insert scripts, ETL staging queries, slow path analysis from partner teams.
- **Related Instructions**: `../instructions/sql-sp-generation.instructions.md`, `../../docs/DATABASE_MANAGEMENT.md`.

# SQL Optimization Playbook

Aim for improvements that can be proven quickly: reduced runtime, fewer locks, or lower I/O. AspireAI keeps relational usage limited, so prefer clear fixes over deep vendor-specific tuning.

## 1. Profiling First
- Run `EXPLAIN` / `EXPLAIN ANALYZE` before changing code; capture baseline duration and row estimates.
- Check index usage via DMVs (`pg_stat_statements`, `sys.dm_db_index_usage_stats`).
- Log parameter values causing poor plans so you can replay.

## 2. Query Structure
- Replace `SELECT *` with explicit column lists to shrink payloads.
- Push filters into `WHERE` clauses early; swap correlated subqueries for window functions or JOINs.
- Ensure joins use equality predicates on indexed columns and avoid implicit cross joins.
- For pagination, recommend keyset pagination (`WHERE Id > @LastId`) instead of deep `OFFSET` usage.

```sql
SELECT Id, Title
FROM Documents
WHERE UpdatedAt >= @Since
ORDER BY UpdatedAt DESC
FETCH FIRST 100 ROWS ONLY;
```

## 3. Index & Storage Guidance
- Request indexes only when predicates appear in high-volume workloads; list key columns + include columns when the engine supports it (SQL Server `INCLUDE`, PostgreSQL covering indexes).
- Suggest dropping overlapping or unused indexes noted by DMVs.
- For staging tables, consider column order that matches bulk copy operations to reduce page splits.

## 4. Batch Operations
- Prefer set-based statements (`INSERT ... SELECT`, `MERGE`) over row-by-row loops.
- Chunk large operations using primary key ranges or timestamps to avoid long locks.
- Wrap related DML in explicit transactions with savepoints so reruns can resume cleanly.

```sql
BEGIN TRANSACTION;
    INSERT INTO DocumentsArchive (Id, Payload)
    SELECT Id, Payload
    FROM Documents
    WHERE UpdatedAt < @Cutoff;
    DELETE FROM Documents WHERE UpdatedAt < @Cutoff;
COMMIT;
```

## 5. Engine-Specific Tips
- **PostgreSQL**: Analyze tables after bulk loads (`ANALYZE`), watch out for bloated tables, and use partial indexes for common filters.
- **SQL Server**: Capture actual execution plans, enable `SET STATISTICS IO, TIME ON` during tuning, and watch tempdb spill warnings.
- **SQLite (tests)**: Use transactions for bulk inserts, trust `WITHOUT ROWID` only when primary keys are natural, and enable WAL mode for concurrency.

## 6. Validation & Reporting
- Re-run profiling after changes; include before/after metrics in the review summary.
- List follow-up automation (e.g., schedule index maintenance, add query monitoring) if the workload persists.
- If improvements rely on environment variables or session settings, document them explicitly.

## Output Template
```
## [Priority] Optimization: [Short description]

**Baseline**: runtime X ms, rows Y
**Change**: [Index added / query rewritten / batch chunked]
**Result**: runtime X' ms, rows Y'
**Next**: [Additional monitoring or clean-up]
```

Keep recommendations pragmatic and linked to measurable wins so maintainers can validate them before committing to long-term relational support.
