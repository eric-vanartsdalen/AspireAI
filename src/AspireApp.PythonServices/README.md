# AspireAI Document Processing Service

This service provides document processing capabilities using [docling](https://github.com/DS4SD/docling) and Neo4j for Retrieval Augmented Generation (RAG) functionality.

## Features

- **Document Processing**: Automatic processing of uploaded documents using docling
- **Page Extraction**: Individual page content extraction and storage
- **Graph Database**: Neo4j integration for advanced relationship modeling
- **RAG Functionality**: Search and retrieval capabilities across processed documents
- **REST API**: Complete FastAPI-based REST API for all operations
- **Optimized Builds**: Multi-stage Docker builds with persistent caching for fast development

## Quick Start

### Option 1: Local Development (Fastest)

```bash
# Set up local virtual environment (one-time setup)
cd src/AspireApp.PythonServices
python setup_dev_env.py

# Activate virtual environment
.venv\Scripts\activate  # Windows
source .venv/bin/activate  # Linux/Mac

# Run the service locally
uvicorn app.fastapi:app --host 0.0.0.0 --port 8000 --reload
```

### Option 2: Docker Development with Caching

```bash
cd src/AspireApp.PythonServices
docker-compose -f docker-compose.dev.yml up
```

### Option 3: Full Aspire Orchestration

```bash
# Run from solution root
dotnet run --project src/AspireApp.AppHost
```

## Performance Optimizations

### ?? Build Speed Improvements

The Python service has been optimized for fast builds and development:

1. **Multi-Stage Docker Build**: Separates dependency installation from code changes
2. **Layer Caching**: Dependencies are cached unless requirements.txt changes
3. **Persistent Volumes**: pip cache and virtual environment persist across builds
4. **Optimized .dockerignore**: Reduces build context size
5. **Development Environment**: Local .venv setup for rapid iteration

### ?? Performance Comparison

| Method | Cold Build | Warm Build (code change) | Hot Reload |
| ------ | ---------- | ------------------------ | ---------- |
| Original | ~3-5 min | ~3-5 min | N/A |
| Optimized Docker | ~3-5 min | ~30 sec | ~5 sec |
| Local .venv | ~2 min | N/A | ~1 sec |

### ?? Cache Persistence

**Docker Volumes**: The following are persisted across container rebuilds:

- `python-pip-cache`: pip download cache (`/root/.cache/pip`)
- `python-venv`: Virtual environment (`/opt/venv`)
- Application data and database bindings

**Local Development**: Uses standard Python `.venv` which persists naturally.

## Architecture

```text
???????????????????    ????????????????????    ???????????????????
?   Blazor Web    ?    ?  Python FastAPI  ?    ?     Neo4j       ?
?   Frontend      ??????     Service      ??????  Graph Database ?
?                 ?    ?                  ?    ?                 ?
???????????????????    ????????????????????    ???????????????????
                                ?
                                ?
                       ????????????????????
                       ?  SQLite Database ?
                       ?   (Documents)    ?
                       ????????????????????
                                ?
                                ?
                       ????????????????????
                       ?   File Storage   ?
                       ? (Processed Docs) ?
                       ????????????????????
```

## API Endpoints

### Documents

- `GET /documents/` - List all documents
- `GET /documents/unprocessed` - List unprocessed documents
- `GET /documents/{document_id}` - Get specific document
- `GET /documents/{document_id}/status` - Get document processing status

### Processing

- `POST /processing/process-document/{document_id}` - Process a specific document
- `POST /processing/process-all` - Process all unprocessed documents
- `GET /processing/status/{document_id}` - Get processing status
- `GET /processing/processed-documents` - List all processed documents

### RAG (Retrieval)

- `GET /rag/search-documents?query={query}&limit={limit}` - Search document content
- `GET /rag/document-context/{document_id}` - Get full document context
- `GET /rag/page-content/{document_id}/{page_number}` - Get specific page content
- `GET /rag/surrounding-pages/{document_id}/{page_number}` - Get surrounding pages
- `POST /rag/semantic-search` - Advanced semantic search
- `GET /rag/health` - Check RAG services health

### System

- `GET /` - Service information
- `GET /health` - Health check
- `GET /docs` - Interactive API documentation

## Document Processing Flow

1. **Upload**: Documents uploaded through Blazor frontend to `/app/data/uploads/`
2. **Detection**: Service detects new documents in SQLite database
3. **Processing**: Docling processes document and extracts pages
4. **Storage**:
   - Processed document saved to `/app/data/processed/documents/{doc_id}/`
   - Page content stored individually
   - Metadata and structure preserved
5. **Graph Creation**: Document and pages added to Neo4j as nodes with relationships
6. **Search Ready**: Content becomes searchable through RAG endpoints

## File Structure

```text
/app/
??? database/
?   ??? data-resources.db          # SQLite database
??? data/
?   ??? uploads/                   # Temporary upload storage
?   ??? processed/                 # Processed documents
?       ??? documents/
?           ??? {doc_id}/
?               ??? document.json  # Full docling document
?               ??? metadata.json  # Processing metadata
?               ??? pages/         # Individual pages
?                   ??? page_001.json
?                   ??? page_002.json
?                   ??? ...
??? app/
    ??? fastapi.py                 # Main FastAPI app
    ??? models/
    ?   ??? models.py              # Pydantic models
    ??? services/
    ?   ??? database_service.py    # SQLite operations
    ?   ??? docling_service.py     # Document processing
    ?   ??? neo4j_service.py       # Graph operations
    ??? routers/
        ??? documents.py           # Document management
        ??? processing.py          # Document processing
        ??? rag.py                 # RAG functionality
```

## Environment Variables

- `NEO4J_URI`: Neo4j connection URI (default: `bolt://localhost:7687`)
- `NEO4J_USER`: Neo4j username (default: `neo4j`)
- `NEO4J_PASSWORD`: Neo4j password (default: `neo4j@secret`)
- `PIP_CACHE_DIR`: pip cache directory (default: `/root/.cache/pip`)

## Development Workflow

### ?? Recommended Development Cycle

1. **Initial Setup**: Run `python setup_dev_env.py` once
2. **Code Changes**: Use local .venv for rapid iteration
3. **Testing**: Use docker-compose.dev.yml for integration testing
4. **Production**: Use full Aspire orchestration

### ??? Development Tools

The development environment includes:

- **pytest**: Unit and integration testing
- **black**: Code formatting
- **flake8**: Code linting
- **mypy**: Type checking
- **httpx**: HTTP client for API testing

### ?? Testing

```bash
# Run all tests
pytest

# Run with coverage
pytest --cov=app

# Test specific module
pytest tests/test_services.py

# Integration tests
python demo_processing.py
```

## Database Schema

### SQLite Tables

```sql
-- Original uploaded documents
CREATE TABLE documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    filename TEXT NOT NULL,
    original_filename TEXT NOT NULL,
    file_path TEXT NOT NULL,
    file_size INTEGER,
    mime_type TEXT,
    upload_date DATETIME DEFAULT CURRENT_TIMESTAMP,
    processed BOOLEAN DEFAULT FALSE,
    processing_status TEXT DEFAULT 'pending'
);

-- Document processing results
CREATE TABLE processed_documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id INTEGER REFERENCES documents(id),
    docling_document_path TEXT NOT NULL,
    total_pages INTEGER,
    processing_date DATETIME DEFAULT CURRENT_TIMESTAMP,
    processing_metadata TEXT,
    neo4j_node_id TEXT
);

-- Individual document pages
CREATE TABLE document_pages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_id INTEGER NOT NULL,
    page_number INTEGER NOT NULL,
    content TEXT NOT NULL,
    page_metadata TEXT,
    neo4j_page_node_id TEXT,
    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE,
    UNIQUE(file_id, page_number)
);
```

### Neo4j Graph Schema

```cypher
// Node types
(:Document {id, filename, original_filename, ...})
(:Page {id, document_id, page_number, content, ...})

// Relationships
(Document)-[:CONTAINS]->(Page)
(Page)-[:PRECEDES]->(Page)
```

## Usage Examples

### Processing Documents

```python
import requests

# Process all unprocessed documents
response = requests.post("http://localhost:8000/processing/process-all")
print(response.json())

# Check processing status
response = requests.get("http://localhost:8000/processing/status/1")
print(response.json())
```

### Searching Content

```python
# Search for content
response = requests.get("http://localhost:8000/rag/search-documents?query=machine learning&limit=5")
results = response.json()
print(f"Found {results['count']} results")

# Get document context
response = requests.get("http://localhost:8000/rag/document-context/1")
context = response.json()
print(f"Document has {len(context['pages'])} pages")
```

### Advanced Search

```python
# Semantic search with filters
search_data = {
    "query": "artificial intelligence",
    "document_ids": [1, 2, 3],
    "limit": 10,
    "similarity_threshold": 0.7
}
response = requests.post("http://localhost:8000/rag/semantic-search", json=search_data)
print(response.json())
```

## Testing

Run the demo script to test functionality:

```bash
python demo_processing.py
```

Run service tests:

```bash
python test_services.py
```

## Troubleshooting

### Build Issues

- **Slow builds**: Ensure Docker BuildKit is enabled and use the optimized Dockerfile
- **Dependency errors**: Clear Docker cache with `docker builder prune`
- **Permission issues**: Use the non-root user configuration in Dockerfile

### Runtime Issues

- **Neo4j connection**: Check health endpoint and ensure Neo4j is running
- **File access**: Verify volume mounts in Aspire configuration
- **Memory issues**: Increase Docker memory limits for large document processing

## Future Enhancements

- **Vector Embeddings**: Add semantic similarity search with embeddings
- **Entity Extraction**: Extract and link named entities across documents
- **GraphRAG**: Implement Microsoft's GraphRAG approach
- **Incremental Updates**: Only process changed content
- **Batch Processing**: Parallel processing for large document sets
- **Citation Networks**: Model document references and citations

## Dependencies

- **docling**: Document processing and conversion
- **neo4j**: Graph database driver
- **fastapi**: Web framework
- **pydantic**: Data validation
- **sqlite3**: Database operations
- **aiofiles**: Async file operations
