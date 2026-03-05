agent: 'agent'
tools: ['run_in_terminal', 'read_file', 'grep_search', 'apply_patch']
description: 'Debug Python FastAPI ingestion services for document processing and Graph-RAG'
owner: '@eric-vanartsdalen'
audience: 'Python Maintainers'
dependencies: ['Python 3.12', 'Docker Desktop']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Debugging FastAPI endpoints, document ingestion failures, Graph-RAG processing errors, Docker container issues.
- **Dependencies**: Python 3.12, FastAPI, Docker, access to `data/` and `database/` volumes.
- **Sample Inputs**: Error logs from Python services, API request/response examples, file processing failures.
- **Related Instructions**: See `../instructions/python.instructions.md` for FastAPI guidelines and maintenance patterns; reference `../instructions/neo4j-integration.instructions.md` for graph database operations; see `../instructions/testing.instructions.md` for debugging test patterns.

# Python Ingestion Debugging Guide

Your goal is to troubleshoot issues in the Python FastAPI backend for document ingestion and Graph-RAG experiments.

## Common Issues

### API Failures
- Check endpoint definitions in `app/main.py`.
- Verify request validation with Pydantic models.
- Ensure async handling for I/O operations.

### Document Processing
- Validate file uploads to `data/uploads/`.
- Check processing logic for text extraction, chunking.
- Monitor Graph-RAG integration with Neo4j.

### Container Problems
- Verify `Dockerfile` and `requirements.txt`.
- Check bind mounts for `data/` and `database/`.
- Enable BuildKit: `DOCKER_BUILDKIT=1 docker build`.

### Performance
- Profile async operations.
- Check for blocking calls in FastAPI routes.
- Optimize database connections.

When debugging, suggest logging improvements, error handling, and targeted fixes based on symptoms.

## Example Commands
- `python -m pip install -r src/AspireApp.PythonServices/requirements.txt`
- `python -m pytest src/AspireApp.PythonServices/test_services.py`
- `python src/AspireApp.PythonServices/diagnose_database.py`
- `docker ps --filter "name=python" --format "table {{.Names}}\t{{.Status}}"`