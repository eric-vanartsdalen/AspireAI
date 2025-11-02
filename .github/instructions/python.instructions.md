---
description: 'Python coding conventions for AspireAI services'
applyTo: '**/*.py'
---

# AspireAI Python Practices

## Scope
- Applies to FastAPI services, ingestion scripts, and tooling located in `src/AspireApp.PythonServices`.
- Align with repo-wide decisions first; follow maintainer guidance if conflicts arise.

## Style & Documentation
- Follow PEP 8 formatting via `ruff format` or `black` (79/88 character wrap guidelines are suggestions, not hard limits).
- Annotate public functions and FastAPI route handlers with type hints; include docstrings when behavior is not obvious.
- Prefer descriptive function names over comments explaining intent.

## Structure & Dependencies
- Keep FastAPI routers modular—group related endpoints in the same module and register via `include_router`.
- Encapsulate database or Neo4j interactions behind dedicated helper modules to prevent incidental coupling.
- Use environment variables (surfaced by Aspire) for secrets and connection details; centralize loading in a settings module.

## Error Handling & Logging
- Raise `HTTPException` with meaningful status codes for API errors.
- Log operational failures using the project logging helper; include document IDs or request identifiers where possible.
- Avoid bare `except` clauses; catch specific exceptions and rethrow with additional context only when needed.

## Testing Guidance
- Place pytest tests under `src/AspireApp.PythonServices/tests` (existing pattern); mirror module names for clarity.
- Cover happy path, validation errors, and integration with external services (mocked where necessary).
- Use `pytest-asyncio` for async route/unit testing.

## Decision Log
- Pending maintainer choice on preferred SQL/graph client abstractions; keep Neo4j helpers lightweight until confirmed.
