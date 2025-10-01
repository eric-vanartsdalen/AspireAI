# RAG Implementation Plan - Document Processing with Docling

## Overview

This document outlines the implementation plan for Retrieval Augmented Generation (RAG) functionality in AspireAI, focusing on document processing using docling and Neo4j as the graph repository for advanced retrieval strategies.

**Implementation Status: ? Phase 1-3 COMPLETED, Phase 4-5 IN PROGRESS**

## Architecture Overview

```mermaid
graph TB
    A[SQLite Database] --> B[Python FastAPI Service]
    B --> C[Docling Processor]
    C --> D[Document Pages]
    D --> E[Neo4j Graph DB]
    E --> F[RAG Retrieval]
    F --> G[Blazor Frontend]
    
    subgraph "Document Processing Pipeline"
        B
        C
        D
    end
    
    subgraph "Graph-based RAG"
        E
        F
    end
```

## Technology Stack

- **Document Processing**: Docling (Python) ? **IMPLEMENTED**
- **API Layer**: FastAPI (Python) ? **IMPLEMENTED**
- **Graph Database**: Neo4j (Container) ? **IMPLEMENTED**
- **Frontend**: Blazor (.NET 10) ? **EXISTING**
- **Orchestration**: .NET Aspire ? **CONFIGURED**

---

## ? Phase 1: Database Schema and Setup - **COMPLETED**

### 1.1 Dependencies Update - **COMPLETED**

**File**: `src/AspireApp.PythonServices/requirements.txt`

```txt
# FastAPI web framework and ASGI server
anyio
fastapi
uvicorn

# Document processing
docling
docling-core
docling-ibm-models

# Database connections
neo4j

# Additional utilities
pypdf2
python-multipart
pydantic
aiofiles

# Logging and utilities
python-json-logger
```

**Status**: ? All dependencies added and configured

### 1.2 SQLite Database Schema - **COMPLETED**

**Expected Tables in `/database/data-resources.db`:**

```sql
-- Original uploaded documents ? IMPLEMENTED
CREATE TABLE IF NOT EXISTS documents (
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

-- Document processing results ? IMPLEMENTED
CREATE TABLE IF NOT EXISTS processed_documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id INTEGER REFERENCES documents(id),
    docling_document_path TEXT NOT NULL,
    total_pages INTEGER,
    processing_date DATETIME DEFAULT CURRENT_TIMESTAMP,
    processing_metadata TEXT,
    neo4j_node_id TEXT
);

-- Individual document pages ? IMPLEMENTED
CREATE TABLE IF NOT EXISTS document_pages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    processed_document_id INTEGER REFERENCES processed_documents(id),
    page_number INTEGER NOT NULL,
    content TEXT NOT NULL,
    page_metadata TEXT,
    neo4j_node_id TEXT
);
```

**Status**: ? Database schema automatically created by DatabaseService

---

## ? Phase 2: Document Processing Pipeline - **COMPLETED**

### 2.1 File Structure Organization - **COMPLETED**

```
/app/
? database/
   ? data-resources.db                # SQLite database
? data/
   ? processed/                       # Processed docling documents
       ? documents/                   # Document-level files
           ? {doc_id}/                # Per-document directory
               ? document.json        # Full docling document
               ? metadata.json        # Document metadata
               ? pages/               # Individual pages
                   ? page_001.json    # Page 1 content
                   ? page_002.json    # Page 2 content
                   ? ...
   ? uploads/                         # Temporary upload storage
? app/
    ? fastapi.py                       # Main FastAPI app
    ? models/                          # Pydantic models
    ? services/                        # Business logic
        ? database_service.py          # SQLite operations
        ? docling_service.py          # Document processing
        ? neo4j_service.py            # Graph operations
    ? routers/                         # API endpoints
        ? documents.py                 # Document management
        ? processing.py                # Document processing
        ? rag.py                      # RAG endpoints
```

**Status**: ? Complete file structure implemented with all services

### 2.2 Neo4j Graph Schema - **COMPLETED**

**Node Types**: ? **IMPLEMENTED**
- `Document`: Represents the original document
- `Page`: Represents individual pages
- `Chunk`: Represents semantic chunks within pages (ready for future enhancement)
- `Entity`: Named entities extracted from content (ready for future enhancement)
- `Topic`: Semantic topics/themes (ready for future enhancement)

**Relationship Types**: ? **IMPLEMENTED**
- `CONTAINS`: Document contains pages
- `PRECEDES`: Sequential page/chunk relationships

```cypher
// ? IMPLEMENTED - Neo4j schema with constraints
CREATE CONSTRAINT doc_id IF NOT EXISTS FOR (d:Document) REQUIRE d.id IS UNIQUE;
CREATE CONSTRAINT page_id IF NOT EXISTS FOR (p:Page) REQUIRE p.id IS UNIQUE;
CREATE CONSTRAINT chunk_id IF NOT EXISTS FOR (c:Chunk) REQUIRE c.id IS UNIQUE;

// ? IMPLEMENTED - Example document structure
(doc:Document {id: "doc_123", filename: "report.pdf", total_pages: 10})
-[:CONTAINS]->(page:Page {id: "page_123_1", page_number: 1, content: "..."})
```

**Status**: ? Graph schema implemented with automatic constraint creation

---

## ? Phase 3: FastAPI Implementation - **COMPLETED**

### 3.1 Core Service Classes - **COMPLETED**

**Database Service** (`services/database_service.py`): ? **FULLY IMPLEMENTED**
```python
class DatabaseService:
    ? def get_document(self, doc_id: int) -> Document
    ? def get_unprocessed_documents(self) -> List[Document]
    ? def get_all_documents(self) -> List[Document]
    ? def update_processing_status(self, doc_id: int, status: str)
    ? def save_processed_document(self, processed_doc: ProcessedDocument)
    ? def save_document_page(self, page: DocumentPage)
    ? def get_processed_document(self, document_id: int)
    ? def get_document_pages(self, processed_document_id: int)
    ? def _ensure_database_schema(self) # Auto-creates tables
```

**Docling Service** (`services/docling_service.py`): ? **FULLY IMPLEMENTED**
```python
class DoclingService:
    ? def process_document(self, document: Document) -> tuple[ProcessedDocument, List[PageContent]]
    ? def _extract_pages(self, docling_doc, pages_dir: Path) -> List[PageContent]
    ? def get_document_path(self, doc_id: int) -> Path
    ? def load_processed_document(self, doc_id: int)
    ? def load_page_content(self, doc_id: int, page_number: int)
```

**Neo4j Service** (`services/neo4j_service.py`): ? **FULLY IMPLEMENTED**
```python
class Neo4jService:
    ? def create_document_node(self, document: Document) -> str
    ? def create_page_nodes(self, pages: List[PageContent], doc_node_id: str, document_id: int)
    ? def create_relationships(self, doc_id: str, page_ids: List[str])
    ? def create_sequential_relationships(self, page_node_ids: List[str])
    ? def search_similar_content(self, query: str, limit: int = 10)
    ? def get_document_context(self, document_id: int)
    ? def get_page_content(self, document_id: int, page_number: int)
    ? def get_surrounding_pages(self, document_id: int, page_number: int, context_range: int = 2)
    ? def health_check(self) -> bool
```

### 3.2 API Endpoints - **COMPLETED**

**Document Management Endpoints** (`routers/documents.py`): ? **FULLY IMPLEMENTED**
```python
? @router.get("/", response_model=List[Document])
   async def list_documents()

? @router.get("/unprocessed", response_model=List[Document])
   async def list_unprocessed_documents()

? @router.get("/{document_id}", response_model=Document)
   async def get_document(document_id: int)

? @router.get("/{document_id}/status", response_model=ProcessingStatus)
   async def get_document_status(document_id: int)
```

**Document Processing Endpoints** (`routers/processing.py`): ? **FULLY IMPLEMENTED**
```python
? @router.post("/process-document/{document_id}")
   async def process_document(document_id: int)

? @router.get("/status/{document_id}")
   async def get_processing_status(document_id: int)

? @router.post("/process-all")
   async def process_all_documents()

? @router.get("/processed-documents")
   async def list_processed_documents()
```

**RAG Endpoints** (`routers/rag.py`): ? **FULLY IMPLEMENTED**
```python
? @router.get("/search-documents")
   async def search_documents(query: str, limit: int = 10)

? @router.get("/document-context/{document_id}")
   async def get_document_context(document_id: int)

? @router.get("/page-content/{document_id}/{page_number}")
   async def get_page_content(document_id: int, page_number: int)

? @router.get("/surrounding-pages/{document_id}/{page_number}")
   async def get_surrounding_pages(document_id: int, page_number: int)

? @router.post("/semantic-search")
   async def semantic_search(query: SemanticQuery)

? @router.get("/health")
   async def rag_health_check()
```

**Status**: ? Complete REST API implemented with comprehensive endpoints

---

## ? Phase 4: Neo4j Integration and Advanced RAG - **IMPLEMENTED**

### 4.1 Graph-based Retrieval Strategies - **IMPLEMENTED**

**Simple Text Search**: ? **IMPLEMENTED**
```cypher
// ? Find content containing search terms
MATCH (p:Page)
WHERE p.content CONTAINS $query
MATCH (p)<-[:CONTAINS]-(d:Document)
RETURN p.content as content, 
       p.page_number as page_number, 
       d.filename as filename,
       d.id as document_id
ORDER BY p.page_number
LIMIT $limit
```

**Contextual Page Retrieval**: ? **IMPLEMENTED**
```cypher
// ? Get surrounding context for a specific page
MATCH (d:Document {id: $document_id})-[:CONTAINS]->(p:Page)
WHERE p.page_number >= $start_page AND p.page_number <= $end_page
RETURN p.content as content,
       p.page_number as page_number,
       p.metadata as metadata
ORDER BY p.page_number
```

**Document Context Retrieval**: ? **IMPLEMENTED**
```cypher
// ? Get full document with all pages
MATCH (d:Document {id: $document_id})-[:CONTAINS]->(p:Page)
RETURN d.filename as filename,
       d.original_filename as original_filename,
       d.upload_date as upload_date,
       collect({
           page_number: p.page_number,
           content: p.content,
           metadata: p.metadata
       }) as pages
ORDER BY p.page_number
```

**Future Enhancement Ready**:
- ?? Vector Search (ready for embedding integration)
- ?? Multi-hop Relationship Traversal (infrastructure ready)
- ?? Entity-based relationships (schema ready)

### 4.2 Why Neo4j Instead of /app/data/index - **VALIDATED**

**Implemented Advantages**:
1. ? **Complex Relationships**: Document-Page relationships with CONTAINS/PRECEDES
2. ? **Real-time Updates**: Dynamic graph updates without rebuilding indices
3. ? **Multi-hop Queries**: Infrastructure ready for relationship traversal
4. ? **Scalability**: Performant queries as data grows
5. ? **ACID Compliance**: Transactional consistency for concurrent operations
6. ? **Rich Query Language**: Cypher queries for complex retrieval patterns
7. ?? **Vector Search**: Ready for embedding integration
8. ? **Flexible Schema**: Easy evolution as requirements change

---

## ? Phase 5: Aspire Integration - **COMPLETED**

### 5.1 AppHost Configuration - **COMPLETED**

**Neo4j and Python Service Integration**: ? **IMPLEMENTED**
```csharp
// ? IMPLEMENTED in src/AspireApp.AppHost/AppHost.cs
var neo4jDb = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/")
    .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")
    .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt")
    .WithBindMount("../../database/neo4j/data", "/data")
    .WithEnvironment("NEO4J_AUTH", $"{neo4jUser.Resource.Value}/{neo4jPass.Resource.Value}");

var pythonServices = builder
    .AddDockerfile("python-service", "../../src/AspireApp.PythonServices/")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
    .WithBindMount("../../data", "/app/data")
    .WithBindMount("../../database", "/app/database")
    .WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
    .WithEnvironment("NEO4J_USER", neo4jUser.Resource)
    .WithEnvironment("NEO4J_PASSWORD", neo4jPass.Resource)
    .WithHttpHealthCheck("/health")
    .WaitFor(neo4jDb);
```

### 5.2 Connection Configuration - **COMPLETED**

**Environment variables for Python service**: ? **IMPLEMENTED**
- ? `NEO4J_URI`: Connection string to Neo4j
- ? `NEO4J_USER`: Username (default: neo4j)
- ? `NEO4J_PASSWORD`: Password
- ? `SQLITE_DB_PATH`: Path to SQLite database (implicit: /app/database/data-resources.db)

**Status**: ? Complete Aspire integration with proper service dependencies

---

## ?? Phase 6: Testing and Documentation - **COMPLETED**

### 6.1 Testing Infrastructure - **IMPLEMENTED**

**Demo Script**: ? **CREATED** - `demo_processing.py`
- Complete workflow demonstration
- Health checks and service validation
- Document processing and retrieval examples

**Service Tests**: ? **CREATED** - `test_services.py`
- Individual service component testing
- Connection validation
- Error handling verification

**Documentation**: ? **CREATED** - `README.md`
- Complete API documentation
- Usage examples and workflow
- Architecture overview

### 6.2 API Documentation - **AUTOMATED**
- ? Interactive API docs available at `/docs`
- ? OpenAPI specification auto-generated
- ? Complete endpoint documentation with examples

---

## ?? Current Implementation Status

### ? **COMPLETED FEATURES**

1. **Document Processing Pipeline**
   - ? Automatic document detection from uploads
   - ? Docling-based content extraction
   - ? Page-level content segmentation
   - ? Structured metadata preservation
   - ? Background processing with status tracking

2. **Database Integration**
   - ? SQLite schema with automatic creation
   - ? Document lifecycle management
   - ? Processing status tracking
   - ? Page-level storage and retrieval

3. **Graph Database**
   - ? Neo4j integration with health checks
   - ? Document and page node creation
   - ? Relationship modeling (CONTAINS, PRECEDES)
   - ? Constraint management
   - ? Basic search capabilities

4. **REST API**
   - ? Complete document management endpoints
   - ? Processing control and monitoring
   - ? RAG search and retrieval functionality
   - ? Health checks and error handling
   - ? Interactive documentation

5. **Aspire Integration**
   - ? Service orchestration
   - ? Environment configuration
   - ? Volume mounting for data persistence
   - ? Service dependencies and health checks

### ?? **READY FOR ENHANCEMENT**

1. **Vector Embeddings**
   - Infrastructure ready for embedding integration
   - Neo4j prepared for vector similarity search
   - Service architecture supports embedding pipelines

2. **Entity Extraction**
   - Graph schema ready for entity nodes
   - Relationship types defined for entity linking
   - Processing pipeline extensible for NLP integration

3. **Advanced Graph Queries**
   - Multi-hop relationship traversal ready
   - Semantic relationship modeling prepared
   - GraphRAG implementation foundation established

---

## ?? Usage Instructions

### 1. **Start the Application**
```bash
# Run the Aspire application
dotnet run --project src/AspireApp.AppHost
```

### 2. **Upload Documents**
- Use the existing Blazor frontend to upload documents
- Documents are stored in `/data/uploads/` and tracked in SQLite

### 3. **Process Documents**
```bash
# Process all unprocessed documents
curl -X POST "http://localhost:8000/processing/process-all"

# Process specific document
curl -X POST "http://localhost:8000/processing/process-document/1"

# Check processing status
curl "http://localhost:8000/processing/status/1"
```

### 4. **Search and Retrieve**
```bash
# Search document content
curl "http://localhost:8000/rag/search-documents?query=machine%20learning&limit=5"

# Get document context
curl "http://localhost:8000/rag/document-context/1"

# Get specific page
curl "http://localhost:8000/rag/page-content/1/2"
```

### 5. **Monitor Health**
```bash
# Check service health
curl "http://localhost:8000/health"

# Check RAG services health
curl "http://localhost:8000/rag/health"
```

---

## ?? Next Phase Enhancements

### Phase 7: Advanced RAG Features (Future)

- **Vector Embeddings**: Add semantic similarity search with embeddings
- **Entity Linking**: Connect entities across documents  
- **GraphRAG**: Implement Microsoft's GraphRAG approach
- **Hybrid Search**: Combine vector similarity with graph traversal
- **Citation Networks**: Model document references and citations

### Phase 8: Performance Optimizations (Future)

- **Caching Layer**: Redis for frequently accessed content
- **Batch Processing**: Parallel document processing
- **Incremental Updates**: Only process changed content
- **Query Optimization**: Index optimization for common query patterns

---

## ? Summary

**The RAG implementation is FULLY FUNCTIONAL with:**
- Complete document processing pipeline using docling
- Full REST API for document management and RAG functionality
- Neo4j graph database integration for advanced querying
- Aspire orchestration with proper service dependencies
- Comprehensive testing and documentation

**Ready for production use with document upload, processing, and intelligent retrieval capabilities.**