# Database Schema Documentation

## Overview

The AspireAI application uses a **simplified, unified database schema** to track the complete lifecycle of uploaded files from upload through docling processing to Neo4j graph integration.

### Design Philosophy

1. **Single Source of Truth**: One `files` table tracks the entire file lifecycle
2. **Clear State Machine**: Simple status progression: `uploaded` ? `processing` ? `processed` (or `error`)
3. **Future-Ready**: Built-in support for Neo4j node references and future extensions (e.g., website scraping)
4. **Clean Separation**: File metadata vs. extracted page content
5. **SQLite Conventions**: Lowercase table and column names following SQLite best practices

---

## Schema Diagram

```
???????????????????????????????????????????????????????????????????
?                            files                                 ?
???????????????????????????????????????????????????????????????????
? id (PK)                        INTEGER                           ?
? file_name                      TEXT NOT NULL                     ?
? original_file_name             TEXT NOT NULL                     ?
? file_path                      TEXT NOT NULL                     ?
? file_hash                      TEXT DEFAULT ''                   ?
? file_size                      INTEGER DEFAULT 0                 ?
? mime_type                      TEXT                              ?
? uploaded_at                    DATETIME DEFAULT CURRENT_TIMESTAMP?
? status                         TEXT DEFAULT 'uploaded'           ?
? processing_started_at          DATETIME                          ?
? processing_completed_at        DATETIME                          ?
? processing_error               TEXT                              ?
? docling_document_path          TEXT                              ?
? total_pages                    INTEGER                           ?
? neo4j_document_node_id         TEXT                              ?
? source_type                    TEXT DEFAULT 'upload'             ?
? source_url                     TEXT                              ?
???????????????????????????????????????????????????????????????????
                                    ?
                                    ? 1:N
                                    ?
???????????????????????????????????????????????????????????????????
?                        document_pages                            ?
???????????????????????????????????????????????????????????????????
? id (PK)                        INTEGER                           ?
? file_id (FK)                   INTEGER NOT NULL                  ?
? page_number                    INTEGER NOT NULL                  ?
? content                        TEXT NOT NULL                     ?
? page_metadata                  TEXT (JSON)                       ?
? neo4j_page_node_id             TEXT                              ?
?                                                                   ?
? UNIQUE(file_id, page_number)                                     ?
???????????????????????????????????????????????????????????????????
```

---

## Table Definitions

### `files` Table

The core table tracking all uploaded files and their processing lifecycle.

#### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY | Auto-incrementing unique identifier |
| `file_name` | TEXT | NOT NULL | Unique filename (with timestamp/hash suffix) |
| `original_file_name` | TEXT | NOT NULL | User's original filename |
| `file_path` | TEXT | NOT NULL | Relative path to file (e.g., "filename.pdf") |
| `file_hash` | TEXT | DEFAULT '' | SHA256 hash for deduplication |
| `file_size` | INTEGER | DEFAULT 0 | File size in bytes |
| `mime_type` | TEXT | | MIME type (e.g., 'application/pdf') |
| `uploaded_at` | DATETIME | DEFAULT CURRENT_TIMESTAMP | Upload timestamp |
| `status` | TEXT | DEFAULT 'uploaded' | Processing status (see Status Values below) |
| `processing_started_at` | DATETIME | | When processing began |
| `processing_completed_at` | DATETIME | | When processing finished (success or error) |
| `processing_error` | TEXT | | Error message if status='error' |
| `docling_document_path` | TEXT | | Path to docling JSON output |
| `total_pages` | INTEGER | | Number of pages extracted |
| `neo4j_document_node_id` | TEXT | | Neo4j Document node ID (future) |
| `source_type` | TEXT | DEFAULT 'upload' | Source: 'upload', 'website', etc. |
| `source_url` | TEXT | | Source URL for web-scraped content (future) |

#### Status Values

| Status | Description | Next State |
|--------|-------------|------------|
| `uploaded` | File uploaded, ready for processing | `processing` |
| `processing` | Currently being processed by docling | `processed` or `error` |
| `processed` | Successfully processed, pages extracted | (terminal state) |
| `error` | Processing failed (see `processing_error`) | Can retry: ? `processing` |

#### Indexes

```sql
CREATE INDEX idx_files_status ON files(status);
CREATE INDEX idx_files_hash ON files(file_hash);
CREATE INDEX idx_files_uploaded ON files(uploaded_at);
CREATE INDEX idx_files_source_type ON files(source_type);
```

---

### `document_pages` Table

Stores page-level content extracted by docling for RAG retrieval.

#### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY | Auto-incrementing unique identifier |
| `file_id` | INTEGER | NOT NULL, FK ? files(id) | Reference to parent file |
| `page_number` | INTEGER | NOT NULL | Page number (1-indexed) |
| `content` | TEXT | NOT NULL | Extracted page text content |
| `page_metadata` | TEXT | | JSON metadata from docling |
| `neo4j_page_node_id` | TEXT | | Neo4j Page node ID (future) |

#### Constraints

```sql
FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE
UNIQUE(file_id, page_number)
```

#### Indexes

```sql
CREATE INDEX idx_pages_file_id ON document_pages(file_id);
CREATE INDEX idx_pages_file_page ON document_pages(file_id, page_number);
```

---

## Workflow

### 1. File Upload (Blazor ? C#)

```csharp
// AspireApp.Web - FileStorageService
var fileMetadata = new FileMetadata
{
    FileName = uniqueFileName,
    OriginalFileName = originalFileName,
    FilePath = uniqueFileName,
    FileHash = sha256Hash,
    FileSize = fileSize,
    MimeType = contentType,
    UploadedAt = DateTime.UtcNow,
    Status = "uploaded",
    SourceType = "upload"
};

await _context.Files.AddAsync(fileMetadata);
await _context.SaveChangesAsync();
```

**Database State:**
```
files: id=1, status='uploaded', file_name='report_20250120_143022_a1b2c3d4.pdf'
```

---

### 2. Processing Detection (Python Service)

```python
# AspireApp.PythonServices - Check for unprocessed files
unprocessed = db_service.get_unprocessed_files()
# Returns: [{'id': 1, 'status': 'uploaded', 'file_name': '...', ...}]
```

---

### 3. Start Processing (Python Service)

```python
# Update status to 'processing'
db_service.update_file_status(file_id=1, status='processing')
```

**Database State:**
```
files: id=1, status='processing', processing_started_at='2025-01-20 14:30:45'
```

---

### 4. Docling Processing (Python Service)

```python
# Process with docling
docling_service = DoclingService()
result = docling_service.process_document(file_dict)

# Save docling output path and page count
db_service.update_file_processing_results(
    file_id=1,
    docling_path='/app/data/processed/documents/1/document.json',
    total_pages=5
)

# Extract and save pages
for page_num, page_content in enumerate(pages, start=1):
    db_service.save_document_page(
        file_id=1,
        page_number=page_num,
        content=page_content['text'],
        metadata=page_content.get('metadata')
    )

# Mark as processed
db_service.update_file_status(file_id=1, status='processed')
```

**Database State:**
```
files: id=1, status='processed', total_pages=5, 
       docling_document_path='/app/data/processed/documents/1/document.json',
       processing_completed_at='2025-01-20 14:31:15'

document_pages:
  id=1, file_id=1, page_number=1, content='Page 1 text...'
  id=2, file_id=1, page_number=2, content='Page 2 text...'
  ...
```

---

### 5. Error Handling (Python Service)

```python
try:
    # Processing logic
    ...
except Exception as e:
    db_service.update_file_status(
        file_id=1,
        status='error',
        error=str(e)
    )
```

**Database State:**
```
files: id=1, status='error', 
       processing_error='Docling processing failed: ...',
       processing_completed_at='2025-01-20 14:30:50'
```

---

## Future Extensions

### Neo4j Graph Integration (Phase 4)

After processing, link to Neo4j:

```python
# Create Neo4j nodes
neo4j_service = Neo4jService()
doc_node_id = neo4j_service.create_document_node(file_dict)

# Update file with Neo4j node ID
db_service.update_file_processing_results(
    file_id=1,
    docling_path=...,
    total_pages=...,
    neo4j_node_id=doc_node_id  # ?? Link to graph
)

# Create page nodes with relationships
for page in pages:
    page_node_id = neo4j_service.create_page_node(page)
    # Update document_pages.neo4j_page_node_id
```

---

### Website Scraping (Future)

```csharp
// AspireApp.Web - Website scraper
var fileMetadata = new FileMetadata
{
    FileName = "scraped_page_" + guid + ".html",
    OriginalFileName = "Example Page",
    FilePath = relativeFilePath,
    SourceType = "website",  // ?? Different source type
    SourceUrl = "https://example.com/page",  // ?? Original URL
    Status = "uploaded"
};
```

---

## Migration from Legacy Schema

### Legacy Tables (Deprecated)

The previous design used multiple tables:
- `Files` (C# upload tracking)
- `documents` (Python processing tracking)
- `processed_documents` (docling output)
- `file_document_bridge` (sync table)

**These are now deprecated.** The new unified `files` table replaces all of them.

### Backward Compatibility

Legacy entities are maintained in the codebase marked as `[Obsolete]`:
- `Document` (maps to old `documents` table)
- `ProcessedDocument` (maps to old `processed_documents` table)

These will be removed in a future release once all code is migrated.

---

## Database Location

**Development (Docker/Aspire):**
- Path: `/app/database/data-resources.db` (inside Python container)
- Host mount: `{repo}/database/data-resources.db`

**Shared Access:**
- Blazor (C#): Uses EF Core with connection string pointing to `../database/data-resources.db`
- Python service: Direct SQLite access at `/app/database/data-resources.db`

---

## Best Practices

### For C# Developers (AspireApp.Web)

```csharp
// ? Use Files DbSet
var files = await _context.Files
    .Where(f => f.Status == "uploaded")
    .OrderBy(f => f.UploadedAt)
    .ToListAsync();

// ? Don't use deprecated Documents DbSet
[Obsolete] var docs = await _context.Documents.ToListAsync();
```

### For Python Developers (AspireApp.PythonServices)

```python
# ? Use new file-based methods
files = db_service.get_unprocessed_files()
db_service.update_file_status(file_id, 'processing')

# ?? Legacy methods still work but will be removed
docs = db_service.get_all_documents()  # Returns Document objects (deprecated)
```

---

## Testing

### Verify Schema

```bash
# Connect to database
sqlite3 database/data-resources.db

# Check tables
.tables
# Expected: files, document_pages

# Check files table structure
.schema files

# Check for unprocessed files
SELECT id, file_name, status FROM files WHERE status='uploaded';
```

### Sample Queries

```sql
-- Get all uploaded files with page counts
SELECT 
    f.id,
    f.file_name,
    f.status,
    f.total_pages,
    COUNT(p.id) as extracted_pages
FROM files f
LEFT JOIN document_pages p ON f.id = p.file_id
GROUP BY f.id;

-- Find files with processing errors
SELECT id, file_name, processing_error 
FROM files 
WHERE status='error';

-- Get pages for a specific file
SELECT page_number, LEFT(content, 100) as preview
FROM document_pages
WHERE file_id = 1
ORDER BY page_number;
```

---

## Summary

**Key Improvements Over Legacy Schema:**

1. ? **Simplified**: Single `files` table instead of 4+ tables
2. ? **Clear lifecycle**: Obvious status progression
3. ? **No syncing needed**: Eliminated complex bridge logic
4. ? **Future-ready**: Built-in Neo4j and extensibility support
5. ? **Performance**: Focused indexes on actual query patterns
6. ? **Maintainability**: Easy to understand and debug

**Next Steps:**

1. Delete old `database/data-resources.db` to start fresh
2. Run application - schema auto-creates
3. Upload test file via Blazor UI
4. Python service auto-processes new uploads
5. Verify pages extracted in `document_pages` table
