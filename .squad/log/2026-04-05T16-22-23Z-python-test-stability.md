# Python Test Stability Fix — 2026-04-05

## What Happened

Jarvis diagnosed and fixed failing Python smoke test and skipped/not-run regression tests. Buster audited the fixes and validated test discovery alignment.

## Root Causes Fixed

1. **Dependency-Tolerant Imports**: DatabaseService now handles missing neo4j package at import time instead of failing.
2. **Test Bootstrap Paths**: Corrected venv path resolution in test entrypoints.
3. **VS Test Discovery**: pyproj now includes regression test files.
4. **Utility Script Masquerade**: Removed test_* function from non-test utility scripts.

## Validation

- 14 regression + contract tests collected and passing in VS.
- Smoke gate bootstrap dependencies complete.
- CLI pytest and VS Test Explorer workflows aligned.

## Team Updates

- Jarvis: Updated `.github/instructions/python.instructions.md` with async/robustness guidance.
- Buster: Documented discovery/project-inclusion decisions in `.squad/decisions/inbox/buster-python-test-discovery-smoke.md`.

