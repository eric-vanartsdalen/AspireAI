# Session Log — File Hash Schema Startup Bug
**Date:** 2025-11-02  
**Agents:** Jarvis (Python / Data Dev), Buster (QA / Tester)  
**Outcome:** ✅ Fix accepted; regression gates established

## Summary
A stale SQLite schema (`file_hash` column missing) combined with incorrect default database path preference ordering caused startup failures on Windows. The fix adds pre-index schema migration, corrects path resolution ordering, and establishes a smoke-test regression gate.

## Root Cause Analysis

### Issue
```
sqlite3.OperationalError: no such column: file_hash
```
Occurred on ApplicationHost startup when:
1. DatabaseService attempted to create index on `files.file_hash`
2. Schema was stale (column did not exist)
3. Default local database path preference was incorrect, masking which database was being used

### Contributing Factors
- **No Schema Migration Before Index Creation:** DatabaseService did not validate or create the `file_hash` column before attempting index creation
- **Incorrect Path Ordering:** Local `database/` directory was checked before Aspire-provisioned volume, leading to confusion about which database instance was in use
- **Missing Startup Diagnostics:** No clear indication of which database path was being used or why it might be stale

## Solution

### Changes Made

#### 1. Schema Migration (Jarvis)
**File:** `src/AspireApp.PythonServices/app/services/database_service.py`

- Added migration to create `file_hash` column if missing
- Migration runs **before** any index creation attempts
- Prevents OperationalError on stale schemas
- Logs migration execution for observability

#### 2. Test Suite Rewrite (Jarvis)
**File:** `src/AspireApp.PythonServices/test_services.py`

- Converted from isolated unit tests → real smoke suite
- Tests exercise actual DatabaseService initialization against SQLite
- Coverage includes:
  - Schema migration execution
  - Index creation after migration
  - Startup diagnostics
  - Database path ordering
- Regression gate prevents schema evolution bugs

#### 3. Roadmap Update (Jarvis)
- Documented fix pattern
- Added smoke test as ongoing regression vector
- Marked issue as resolved with integration pattern clear

### Verification

#### Reproduction (Buster)
- ✅ Windows path failure reproduced
- ✅ Schema mismatch confirmed

#### Validation (Buster)
- ✅ Schema migration creates missing column
- ✅ Index creation succeeds after migration
- ✅ Local database path ordering correct
- ✅ Startup diagnostics output validated
- ✅ Smoke tests cover regression scenarios

#### QA Gates Passed
- [x] **First gate (Rejected):** Insufficient coverage; isolated tests only
- [x] **Second gate (Rejected):** Incomplete path validation; weak diagnostics
- [x] **Final gate (Accepted):** Full smoke suite with strong regression coverage

## Impact

### Reliability
- Startup no longer fails on stale schemas
- Automatic schema evolution prevents future version skew
- Clear diagnostics on database path selection

### Maintainability
- Smoke test as regression gate prevents schema-evolution bugs
- Pattern documented for future schema additions
- Test suite exercises real startup path, not isolated stubs

### Observability
- Diagnostic output clarifies which database is being used (local vs. Aspire-provisioned)
- Migration logs indicate schema changes applied
- Error messages now point to correct database path

## Decision Points

1. **Schema Migration Approach:** Pre-index validation + automatic column creation (vs. manual migration scripts)
   - **Rationale:** Reduces operational burden; works across environments; no separate migration runner
   
2. **Test Strategy:** Smoke suite over isolated unit tests
   - **Rationale:** Real startup path validation prevents false confidence; better regression coverage
   
3. **Diagnostics:** Log database path and migration execution
   - **Rationale:** Operators can see which database is in use; aids debugging

## Files Changed
- `src/AspireApp.PythonServices/app/services/database_service.py` — Added migration
- `src/AspireApp.PythonServices/test_services.py` — Rewrote test suite
- `roadmap/Tasks.md` — Documented fix and pattern

## Artifacts
- Regression tests: `test_services.py` (smoke suite)
- Fix validation: Buster QA review (multi-pass gate)
- Diagnostics: Database path and migration logging

## Status → Ready for Merge
