---
description: 'Neo4j graph database integration patterns for AspireAI'
applyTo: '**/*Neo4j*.cs,**/*Neo4j*.py,**/neo4j*.conf,**/cypher/**'
---

# Neo4j Integration Guidance

## Scope
- Graph database patterns for document processing and RAG workflows in AspireAI
- Python Neo4j driver usage in FastAPI services
- Schema design for documents, pages, chunks, and relationships
- Query patterns and performance optimization

## Core Principles
- **Graph-first modeling**: Documents → Pages → Chunks with typed relationships
- **Constraint enforcement**: Unique IDs via Neo4j constraints at startup
- **Connection pooling**: Reuse driver instances; avoid per-request connections
- **Async operations**: Use async patterns in Python for concurrent queries
- **Idempotent schemas**: Ensure constraints/indexes can run safely on restart

## Connection Management

### Python Driver Setup
```python
from neo4j import GraphDatabase, Driver
import os

class Neo4jService:
    def __init__(self):
        self.uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
        self.user = os.getenv("NEO4J_USER", "neo4j")
        self.password = os.getenv("NEO4J_PASSWORD", "neo4j@secret")
        self._driver: Optional[Driver] = None

    def get_driver(self) -> Driver:
        if self._driver is None:
            self._driver = GraphDatabase.driver(
                self.uri, 
                auth=(self.user, self.password)
            )
            self._ensure_constraints()
        return self._driver

    def close(self):
        if self._driver:
            self._driver.close()
```

**Best Practices**:
- Lazy-initialize driver on first use
- Store single driver instance per service
- Close driver on service shutdown
- Read credentials from environment variables only

## Schema Design

### Document Graph Structure
```
(Document)-[:CONTAINS]->(Page)-[:PRECEDES]->(Page)
(Page)-[:HAS_CHUNK]->(Chunk)
(Chunk)-[:SIMILAR_TO {score}]->(Chunk)
```

### Node Types
- **Document**: `{id, filename, original_filename, file_path, file_size, mime_type, upload_date, processed}`
- **Page**: `{id, document_id, page_number, content, metadata}`
- **Chunk**: `{id, page_id, chunk_index, content, embedding, metadata}`

### Constraints (Created at Startup)
```cypher
CREATE CONSTRAINT doc_id IF NOT EXISTS 
FOR (d:Document) REQUIRE d.id IS UNIQUE;

CREATE CONSTRAINT page_id IF NOT EXISTS 
FOR (p:Page) REQUIRE p.id IS UNIQUE;

CREATE CONSTRAINT chunk_id IF NOT EXISTS 
FOR (c:Chunk) REQUIRE c.id IS UNIQUE;
```

**Best Practices**:
- Create constraints with `IF NOT EXISTS` for idempotency
- Use unique constraints on primary identifiers
- Composite IDs: `{document_id}_{page_number}` for pages
- Run constraint creation in `_ensure_constraints()` method

## Query Patterns

### Creating Document Graphs
```python
def create_document_node(self, document: Document) -> str:
    with self.get_driver().session() as session:
        result = session.run("""
            CREATE (d:Document {
                id: $id,
                filename: $filename,
                upload_date: $upload_date,
                processed: $processed
            })
            RETURN elementId(d) as node_id
        """, document.dict())
        return result.single()["node_id"]
```

**Best Practices**:
- Use parameterized queries (never string interpolation)
- Return `elementId()` for relationships, not property IDs
- Batch page/chunk creation in transactions
- Use `MERGE` for idempotent updates

### Creating Relationships
```python
def create_relationships(self, doc_node_id: str, page_node_ids: List[str]):
    with self.get_driver().session() as session:
        for page_node_id in page_node_ids:
            session.run("""
                MATCH (d:Document), (p:Page)
                WHERE elementId(d) = $doc_id AND elementId(p) = $page_id
                CREATE (d)-[:CONTAINS]->(p)
            """, {"doc_id": doc_node_id, "page_id": page_node_id})
```

**Optimization**: Batch relationship creation with `UNWIND` for large datasets

### Querying with Context
```python
def get_surrounding_pages(self, document_id: int, page_number: int, 
                          context_range: int = 2) -> List[Dict]:
    with self.get_driver().session() as session:
        result = session.run("""
            MATCH (d:Document {id: $document_id})-[:CONTAINS]->(p:Page)
            WHERE p.page_number >= $start_page 
              AND p.page_number <= $end_page
            RETURN p.content, p.page_number, p.metadata
            ORDER BY p.page_number
        """, {
            "document_id": document_id,
            "start_page": max(1, page_number - context_range),
            "end_page": page_number + context_range
        })
        return [dict(record) for record in result]
```

### Text Search (Basic)
```cypher
MATCH (p:Page)
WHERE p.content CONTAINS $query
MATCH (p)<-[:CONTAINS]-(d:Document)
RETURN p.content, p.page_number, d.filename
ORDER BY p.page_number
LIMIT $limit
```

**Future Enhancement**: Replace `CONTAINS` with full-text indexes or vector similarity

## Health Checks

```python
def health_check(self) -> bool:
    try:
        with self.get_driver().session() as session:
            result = session.run("RETURN 1 as test")
            return result.single()["test"] == 1
    except Exception:
        return False
```

**Integration**: Called from FastAPI `/health` endpoint and Aspire dashboard monitoring

## Configuration (neo4j.conf)

### Key Settings for Document Processing
```properties
# Memory (adjust based on workload)
dbms.memory.heap.initial_size=512M
dbms.memory.heap.max_size=2G
dbms.memory.pagecache.size=1G

# Transaction limits
dbms.transaction.timeout=60s
dbms.transaction.concurrent.maximum=1000

# Text search optimization
dbms.index.fulltext.default_analyzer=standard-no-stop-words

# APOC support (for advanced operations)
dbms.security.procedures.unrestricted=apoc.*
apoc.import.file.enabled=true
```

**Tuning Guidelines**:
- Heap: 25-50% of available RAM (max 31GB for optimal JVM)
- Page cache: Remaining RAM after heap allocation
- Increase transaction timeout for large document imports

## Maintenance Patterns

### Adding New Node Types
```python
# 1. Define constraint
def _ensure_constraints(self):
    constraints = [
        # Existing constraints...
        "CREATE CONSTRAINT entity_id IF NOT EXISTS "
        "FOR (e:Entity) REQUIRE e.id IS UNIQUE"
    ]
    # Apply constraints...

# 2. Create nodes with new label
def create_entity_node(self, entity_data: Dict) -> str:
    with self.get_driver().session() as session:
        result = session.run("""
            CREATE (e:Entity {id: $id, name: $name, type: $type})
            RETURN elementId(e) as node_id
        """, entity_data)
        return result.single()["node_id"]
```

### Updating Schema
```cypher
-- Add property to existing nodes
MATCH (p:Page)
WHERE NOT EXISTS(p.processed_date)
SET p.processed_date = datetime()

-- Add index for new property
CREATE INDEX page_processed_date IF NOT EXISTS 
FOR (p:Page) ON (p.processed_date)
```

**Migration Strategy**:
- Script schema changes in `src/AspireApp.PythonServices/scripts/`
- Test on development data first
- Use `IF NOT EXISTS` for indexes/constraints
- Document breaking changes in migration comments

### Adding Relationships
```python
# New relationship type: Pages to extracted Entities
def link_page_entities(self, page_node_id: str, entity_node_ids: List[str]):
    with self.get_driver().session() as session:
        session.run("""
            MATCH (p:Page), (e:Entity)
            WHERE elementId(p) = $page_id 
              AND elementId(e) IN $entity_ids
            MERGE (p)-[:MENTIONS]->(e)
        """, {"page_id": page_node_id, "entity_ids": entity_node_ids})
```

**Use `MERGE`** for idempotent relationship creation (safe to run multiple times)

### Query Optimization
```cypher
-- Before (slow: full label scan)
MATCH (p:Page)
WHERE p.document_id = $doc_id
RETURN p

-- After (fast: use index)
CREATE INDEX page_document_id IF NOT EXISTS 
FOR (p:Page) ON (p.document_id);

MATCH (p:Page {document_id: $doc_id})
RETURN p
```

**Optimization Checklist**:
- Profile queries with `PROFILE` or `EXPLAIN`
- Add indexes on frequently filtered properties
- Avoid cartesian products (ensure relationships in MATCH)
- Use `LIMIT` on unbounded queries
- Batch writes in transactions (reduces commit overhead)

### Clearing Test Data
```cypher
-- Remove all nodes and relationships (dev only)
MATCH (n) DETACH DELETE n

-- Remove specific document graph
MATCH (d:Document {id: $doc_id})-[r*]->(related)
DETACH DELETE d, related
```

**Warning**: `DETACH DELETE` removes nodes and all relationships; use carefully

## Error Handling

```python
from neo4j.exceptions import ServiceUnavailable, ConstraintError

try:
    result = session.run(query, params)
except ServiceUnavailable:
    logger.error("Neo4j unavailable - check connection")
    raise HTTPException(503, "Graph database unavailable")
except ConstraintError as e:
    logger.warning(f"Constraint violation: {e}")
    raise HTTPException(409, "Duplicate resource")
except Exception as e:
    logger.error(f"Unexpected Neo4j error: {e}")
    raise HTTPException(500, "Database operation failed")
```

**Best Practices**:
- Catch `ServiceUnavailable` for connection issues
- Catch `ConstraintError` for duplicate keys
- Log with context (document ID, operation)
- Return appropriate HTTP status codes from FastAPI

## Testing

### Unit Tests (Mocked Driver)
```python
from unittest.mock import MagicMock

def test_create_document():
    mock_driver = MagicMock()
    service = Neo4jService()
    service._driver = mock_driver
    
    # Test logic without real Neo4j connection
```

### Integration Tests (Real Neo4j)
```python
import pytest

@pytest.fixture
def neo4j_service():
    service = Neo4jService(
        uri="bolt://localhost:7687",
        user="neo4j",
        password="test"
    )
    yield service
    service.close()

def test_document_creation(neo4j_service):
    doc = Document(id=1, filename="test.pdf")
    node_id = neo4j_service.create_document_node(doc)
    assert node_id is not None
```

**Best Practices**:
- Use separate test database instance
- Clean test data between runs
- Test constraint enforcement
- Verify relationship creation

## Aspire Integration

### Environment Variables (from AppHost.cs)
```csharp
.WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
.WithEnvironment("NEO4J_USER", neo4jUser.Resource)
.WithEnvironment("NEO4J_PASSWORD", neo4jPass.Resource)
```

### Service Dependencies
```csharp
var pythonServices = builder.AddDockerfile("python-service", ...)
    .WaitFor(neo4jDb);  // Ensure Neo4j starts first
```

## Performance Tuning

### Connection Pool Settings
```python
from neo4j import GraphDatabase

driver = GraphDatabase.driver(
    uri,
    auth=(user, password),
    max_connection_pool_size=50,
    connection_timeout=30.0
)
```

### Batch Operations
```python
def batch_create_pages(self, pages: List[PageContent], batch_size: int = 100):
    with self.get_driver().session() as session:
        for i in range(0, len(pages), batch_size):
            batch = pages[i:i + batch_size]
            session.run("""
                UNWIND $pages as page
                CREATE (p:Page {
                    id: page.id,
                    content: page.content,
                    page_number: page.page_number
                })
            """, {"pages": [p.dict() for p in batch]})
```

**Recommendation**: Batch size 100-1000 depending on node complexity

## Troubleshooting

### Connection Failures
- Verify Neo4j container is running in Aspire dashboard
- Check `NEO4J_URI` format: `bolt://host:7687` (not http)
- Confirm credentials match AppHost parameters
- Test connection: `curl http://localhost:7474` should return Neo4j browser

### Slow Queries
- Run `PROFILE` to see execution plan
- Add indexes on filtered properties
- Check page cache size in `neo4j.conf`
- Review relationship cardinality (avoid fan-out)

### Constraint Violations
- Check for duplicate IDs before insert
- Use `MERGE` instead of `CREATE` for idempotent operations
- Verify constraint names don't conflict

## Decision Log
- **2025-11-02**: Initial creation based on Python Neo4jService implementation
- **2025-11-02**: Document graph schema (Document→Page→Chunk)
- **2025-11-02**: Constraint enforcement at service initialization
- **Future**: Add vector embeddings for semantic search

## Related Instructions
- `aspire-orchestration.instructions.md` - Neo4j container setup
- `python.instructions.md` - FastAPI service patterns
- `dotnet-architecture-good-practices.instructions.md` - Cross-service contracts

Update this file when schema changes, new query patterns emerge, or performance issues are identified.
