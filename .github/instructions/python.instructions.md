---
description: 'Python coding conventions for AspireAI services'
applyTo: '**/*.py'
---

# AspireAI Python Practices

## Scope
- FastAPI services, document processing workers, and tooling in `src/AspireApp.PythonServices`
- Integrates with Neo4j, Aspire orchestration, and C# services
- Maintenance-first approach: updating existing code safely, then creation patterns

## Maintenance Patterns

### Updating Dependencies

**requirements.txt Management**:
```txt
# Pin major.minor for stability, allow patch updates
fastapi==0.104.*
uvicorn==0.24.*
neo4j==5.14.*

# Document processing (avoid CUDA for faster builds)
docling-core==1.2.*
pypdf2==3.0.*
```

**Update Process**:
1. Test updates in isolated environment first
2. Run `pip list --outdated` to identify candidates
3. Update `requirements.txt` with new pins
4. Rebuild Docker image: `docker build -t test-image .`
5. Test with Aspire before committing

**Breaking Changes**:
- Check migration guides for major version bumps
- Update type hints if library signatures change
- Test Neo4j driver compatibility after updates
- Coordinate with C# team for shared DTOs

### Modifying FastAPI Routes

**Adding Endpoint**:
```python
# app/routers/documents.py
@router.post("/batch-process")
async def batch_process_documents(
    file_ids: List[int],
    db: DatabaseService = Depends(get_database_service)
) -> Dict[str, Any]:
    """Batch process multiple documents"""
    try:
        results = []
        for file_id in file_ids:
            result = await process_document(file_id, db)
            results.append(result)
        return {"processed": len(results), "results": results}
    except Exception as e:
        logger.error(f"Batch processing error: {e}")
        raise HTTPException(status_code=500, detail=str(e))
```

**Updating Existing Endpoint**:
```python
# Before - returns simple list
@router.get("/", response_model=List[Document])
async def list_documents(db: DatabaseService = Depends()):
    return db.get_all_documents()

# After - adds pagination
@router.get("/", response_model=Dict[str, Any])
async def list_documents(
    skip: int = 0, 
    limit: int = 100,
    db: DatabaseService = Depends(get_database_service)
) -> Dict[str, Any]:
    """Get paginated document list"""
    documents = db.get_all_documents()
    total = len(documents)
    return {
        "total": total,
        "skip": skip,
        "limit": limit,
        "documents": documents[skip:skip+limit]
    }
```

**Best Practices**:
- Keep backward compatibility when possible
- Version breaking changes (`/v2/documents`)
- Update OpenAPI docs with clear descriptions
- Test with actual C# client calls

### Updating Service Dependencies

**Adding Database Field**:
```python
# 1. Update schema in database_service.py
cursor.execute("""
    ALTER TABLE files 
    ADD COLUMN processing_priority INTEGER DEFAULT 0
""")

# 2. Update query methods
def get_unprocessed_files(self) -> List[Dict[str, Any]]:
    cursor.execute("""
        SELECT id, file_name, ..., processing_priority
        FROM files 
        WHERE status = 'uploaded'
        ORDER BY processing_priority DESC, uploaded_at ASC
    """)
```

**Adding Service Method**:
```python
# Add to existing class
class DatabaseService:
    def set_processing_priority(self, file_id: int, priority: int) -> None:
        """Update file processing priority (higher = sooner)"""
        try:
            with self._pool.get_connection() as conn:
                cursor = conn.cursor()
                cursor.execute("""
                    UPDATE files SET processing_priority = ? WHERE id = ?
                """, (priority, file_id))
                conn.commit()
        except Exception as e:
            logger.error(f"Error setting priority for file {file_id}: {e}")
            raise
```

### Updating Pydantic Models

**Adding Optional Field** (non-breaking):
```python
# models/models.py
class Document(BaseModel):
    id: int
    filename: str
    # ... existing fields ...
    processing_priority: Optional[int] = 0  # New field with default
```

**Changing Field Type** (breaking):
```python
# Before
class ProcessingStatus(BaseModel):
    completed_at: Optional[str]

# After - coordinate with C# consumers
class ProcessingStatus(BaseModel):
    completed_at: Optional[datetime]
    
    class Config:
        json_encoders = {
            datetime: lambda v: v.isoformat()
        }
```

**Best Practices**:
- Use `Optional[T]` with defaults for new fields
- Version models for breaking changes
- Test serialization/deserialization
- Document changes in model docstrings

### Async Pattern Updates

**Converting Sync to Async**:
```python
# Before - blocking operation
def process_document(file_id: int) -> Dict:
    with neo4j_service.get_driver().session() as session:
        result = session.run(query, params)
        return dict(result.single())

# After - async for concurrency
async def process_document(file_id: int) -> Dict:
    async with neo4j_service.get_async_driver().session() as session:
        result = await session.run(query, params)
        return dict(await result.single())
```

**Concurrent Processing**:
```python
import asyncio

async def batch_process(file_ids: List[int]) -> List[Dict]:
    """Process multiple files concurrently"""
    tasks = [process_document(fid) for fid in file_ids]
    results = await asyncio.gather(*tasks, return_exceptions=True)
    
    # Handle mixed success/failure
    processed = []
    for file_id, result in zip(file_ids, results):
        if isinstance(result, Exception):
            logger.error(f"File {file_id} failed: {result}")
        else:
            processed.append(result)
    return processed
```

**Best Practices**:
- Use async for I/O operations (Neo4j, file reads, HTTP)
- Keep CPU-bound work synchronous (or use thread pool)
- Always use `asyncio.gather` for concurrent tasks
- Handle exceptions in each task gracefully

## Creation Patterns

### FastAPI Router Structure

**New Router Module**:
```python
# app/routers/analytics.py
from fastapi import APIRouter, HTTPException, Depends
from typing import List, Dict, Any
import logging

from ..services.database_service import DatabaseService
from ..models.models import AnalyticsResult

router = APIRouter(prefix="/analytics", tags=["analytics"])
logger = logging.getLogger(__name__)

def get_database_service():
    """Dependency injection for database service"""
    return DatabaseService()

@router.get("/document-stats", response_model=AnalyticsResult)
async def get_document_statistics(
    db: DatabaseService = Depends(get_database_service)
) -> AnalyticsResult:
    """Get aggregated document statistics"""
    try:
        stats = db.get_statistics()
        return AnalyticsResult(**stats)
    except Exception as e:
        logger.error(f"Analytics error: {e}")
        raise HTTPException(status_code=500, detail=str(e))
```

**Register in main app**:
```python
# app/fastapi.py
from .routers import analytics

app.include_router(analytics.router)
```

### Service Class Pattern

**New Service**:
```python
# app/services/embedding_service.py
import logging
from typing import List, Optional
import numpy as np

logger = logging.getLogger(__name__)

class EmbeddingService:
    """Generate embeddings for document chunks"""
    
    def __init__(self, model_name: str = "sentence-transformers/all-MiniLM-L6-v2"):
        self.model_name = model_name
        self._model = None  # Lazy load
    
    def _load_model(self):
        """Lazy-load embedding model"""
        if self._model is None:
            from sentence_transformers import SentenceTransformer
            self._model = SentenceTransformer(self.model_name)
            logger.info(f"Loaded embedding model: {self.model_name}")
        return self._model
    
    def embed_text(self, text: str) -> List[float]:
        """Generate embedding vector for text"""
        try:
            model = self._load_model()
            embedding = model.encode(text)
            return embedding.tolist()
        except Exception as e:
            logger.error(f"Embedding error: {e}")
            raise
    
    def embed_batch(self, texts: List[str]) -> List[List[float]]:
        """Batch embed multiple texts efficiently"""
        try:
            model = self._load_model()
            embeddings = model.encode(texts, batch_size=32)
            return [emb.tolist() for emb in embeddings]
        except Exception as e:
            logger.error(f"Batch embedding error: {e}")
            raise
```

### Pydantic Model Design

**Request/Response Models**:
```python
# models/models.py
from pydantic import BaseModel, Field, validator
from typing import Optional, List
from datetime import datetime

class DocumentUploadRequest(BaseModel):
    """Request model for document upload"""
    filename: str = Field(..., min_length=1, max_length=255)
    content_type: str
    size_bytes: int = Field(..., gt=0)
    
    @validator('content_type')
    def validate_content_type(cls, v):
        allowed = ['application/pdf', 'application/msword', 
                   'application/vnd.openxmlformats-officedocument.wordprocessingml.document']
        if v not in allowed:
            raise ValueError(f'Unsupported content type: {v}')
        return v

class DocumentResponse(BaseModel):
    """Response model for document data"""
    id: int
    filename: str
    status: str
    uploaded_at: datetime
    total_pages: Optional[int] = None
    
    class Config:
        orm_mode = True  # Allow from_orm() conversion
        json_encoders = {
            datetime: lambda v: v.isoformat()
        }
```

### Error Handling Patterns

**Specific Exception Handling**:
```python
from neo4j.exceptions import ServiceUnavailable, ConstraintError
from sqlite3 import IntegrityError
from fastapi import HTTPException

@router.post("/process")
async def process_document(file_id: int, db: DatabaseService = Depends()):
    try:
        result = await process_document_workflow(file_id, db)
        return result
    
    except ServiceUnavailable:
        logger.error(f"Neo4j unavailable for file {file_id}")
        raise HTTPException(
            status_code=503, 
            detail="Graph database unavailable"
        )
    
    except IntegrityError as e:
        logger.warning(f"Duplicate file {file_id}: {e}")
        raise HTTPException(
            status_code=409, 
            detail="Resource already exists"
        )
    
    except ValueError as e:
        logger.warning(f"Invalid input for file {file_id}: {e}")
        raise HTTPException(
            status_code=400, 
            detail=str(e)
        )
    
    except Exception as e:
        logger.error(f"Unexpected error processing file {file_id}: {e}", 
                    exc_info=True)
        raise HTTPException(
            status_code=500, 
            detail="Internal processing error"
        )
```

### Structured Logging

**Logger Setup**:
```python
import logging
from python_json_logger import jsonlogger

# Configure in app startup
def setup_logging():
    logger = logging.getLogger()
    handler = logging.StreamHandler()
    
    formatter = jsonlogger.JsonFormatter(
        '%(timestamp)s %(level)s %(name)s %(message)s',
        rename_fields={'levelname': 'level', 'asctime': 'timestamp'}
    )
    handler.setFormatter(formatter)
    logger.addHandler(handler)
    logger.setLevel(logging.INFO)
```

**Contextual Logging**:
```python
logger.info("Processing document", extra={
    "file_id": file_id,
    "filename": filename,
    "operation": "document_processing",
    "stage": "extraction"
})

logger.error("Neo4j connection failed", extra={
    "neo4j_uri": uri,
    "error_type": type(e).__name__,
    "retry_attempt": retry_count
})
```

## Testing

### Unit Tests (Mocked Dependencies)
```python
# tests/test_document_router.py
import pytest
from unittest.mock import Mock, patch
from app.routers import documents

@pytest.fixture
def mock_db_service():
    mock = Mock()
    mock.get_all_documents.return_value = [
        {"id": 1, "filename": "test.pdf", "status": "processed"}
    ]
    return mock

@pytest.mark.asyncio
async def test_list_documents(mock_db_service):
    with patch('app.routers.documents.get_database_service', 
               return_value=mock_db_service):
        result = await documents.list_documents(db=mock_db_service)
        assert len(result) == 1
        assert result[0]["filename"] == "test.pdf"
```

### Integration Tests (Real Services)
```python
# tests/integration/test_neo4j_integration.py
import pytest
from app.services.neo4j_service import Neo4jService

@pytest.fixture(scope="module")
def neo4j_service():
    service = Neo4jService()
    yield service
    service.close()

def test_create_document_node(neo4j_service):
    doc = Document(id=1, filename="test.pdf", file_path="/test")
    node_id = neo4j_service.create_document_node(doc)
    
    assert node_id is not None
    # Cleanup
    with neo4j_service.get_driver().session() as session:
        session.run("MATCH (d:Document {id: $id}) DETACH DELETE d", 
                   {"id": 1})
```

### Async Testing
```python
import pytest
import asyncio

@pytest.mark.asyncio
async def test_concurrent_processing():
    file_ids = [1, 2, 3]
    results = await batch_process(file_ids)
    
    assert len(results) == 3
    assert all(r["status"] == "success" for r in results)
```

## Style & Formatting

### Type Hints
```python
from typing import List, Dict, Optional, Union, Any
from pathlib import Path

def process_file(
    file_path: Path,
    options: Optional[Dict[str, Any]] = None
) -> Dict[str, Union[str, int]]:
    """Process file and return results"""
    pass

async def fetch_documents(
    limit: int = 100
) -> List[Dict[str, Any]]:
    """Fetch documents asynchronously"""
    pass
```

### Docstrings
```python
def calculate_similarity(text1: str, text2: str) -> float:
    """
    Calculate cosine similarity between two text strings.
    
    Args:
        text1: First text string
        text2: Second text string
    
    Returns:
        Similarity score between 0.0 and 1.0
    
    Raises:
        ValueError: If either text is empty
    """
    if not text1 or not text2:
        raise ValueError("Text inputs cannot be empty")
    # Implementation...
```

### Environment Configuration
```python
# app/config.py
import os
from pydantic import BaseSettings

class Settings(BaseSettings):
    # Neo4j
    neo4j_uri: str = "bolt://localhost:7687"
    neo4j_user: str = "neo4j"
    neo4j_password: str = "password"
    
    # Paths
    data_path: str = "/app/data"
    database_path: str = "/app/database"
    
    # Feature flags
    enable_vector_search: bool = False
    
    class Config:
        env_file = ".env"
        case_sensitive = False

settings = Settings()
```

## Docker Optimization

**Multi-Stage Dockerfile**:
```dockerfile
# Stage 1: Builder
FROM python:3.11-slim as builder
WORKDIR /build
COPY requirements.txt .
RUN pip install --user --no-cache-dir -r requirements.txt

# Stage 2: Runtime
FROM python:3.11-slim
WORKDIR /app
COPY --from=builder /root/.local /root/.local
COPY app ./app
ENV PATH=/root/.local/bin:$PATH
CMD ["uvicorn", "app.fastapi:app", "--host", "0.0.0.0", "--port", "8000"]
```

**Best Practices**:
- Use multi-stage builds for smaller images
- Cache pip packages in volumes (Aspire pattern)
- Pin Python version explicitly
- Use `.dockerignore` to exclude tests, docs

## Decision Log
- **2025-11-02**: Expanded with maintenance-first patterns
- **2025-11-02**: Added async/await best practices
- **2025-11-02**: Documented Pydantic validation patterns
- **2025-11-02**: Added structured logging guidance

## Related Instructions
- `neo4j-integration.instructions.md` - Neo4j driver patterns
- `aspire-orchestration.instructions.md` - Docker and environment setup
- `dotnet-architecture-good-practices.instructions.md` - Cross-service contracts
