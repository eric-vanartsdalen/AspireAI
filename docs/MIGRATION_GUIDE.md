# Database Schema Migration Guide

## Quick Start

### Step1: Delete Old Database

```bash
# From repository root
rm database/data-resources.db
```

This ensures a clean start with the new simplified schema.

---

### Step2: Run Application

```bash
# Start Aspire application
dotnet run --project src/AspireApp.AppHost
```

**What happens:**
1. AspireApp.Web starts and auto-creates the new schema via EF Core
2. AspireApp.PythonServices starts and validates schema
3. Both services use the same unified `files` table

---

### Step3: Verify Schema

```bash
# Check the database was created
sqlite3 database/data-resources.db ".tables"
# Expected output: document_pages files
```

---

## Code Changes Required

### C# Code (AspireApp.Web)

#### Updated - Use FileMetadata

```csharp
// In FileStorageService.cs or upload handlers
using AspireApp.Web.Data;

// Create file record
var fileMetadata = new FileMetadata
{
 FileName = uniqueFileName,
 OriginalFileName = originalFileName,
 FilePath = uniqueFileName,
 FileHash = sha256Hash,
 FileSize = fileSize,
 MimeType = contentType,
 Status = "uploaded" // Simple status
};

await _context.Files.AddAsync(fileMetadata);
await _context.SaveChangesAsync();

// Query uploaded files
var uploadedFiles = await _context.Files
 .Where(f => f.Status == "uploaded")
 .OrderBy(f => f.UploadedAt)
 .ToListAsync();

// Check if processed
if (file.IsProcessed)
{
 var pages = await _context.DocumentPages
 .Where(p => p.FileId == file.Id)
 .OrderBy(p => p.PageNumber)
 .ToListAsync();
}
```

#### Deprecated - Old Document entities

```csharp
// Don't use these anymore (marked [Obsolete])
var docs = await _context.Documents.ToListAsync();
var processed = await _context.ProcessedDocuments.ToListAsync();
```

---

### Python Code (AspireApp.PythonServices)

#### Updated - Use new file-based methods

```python
from app.services.database_service import DatabaseService

db = DatabaseService()

# Get unprocessed files
unprocessed = db.get_unprocessed_files()
# Returns: List[Dict] with all file fields

for file in unprocessed:
 file_id = file['id']
 
 # Mark as processing
 db.update_file_status(file_id, 'processing')
 
 try:
 # Process with docling
 result = docling_service.process_document(file)
 
 # Update with results
 db.update_file_processing_results(
 file_id=file_id,
 docling_path=result['docling_path'],
 total_pages=result['total_pages']
 )
 
 # Save pages
 for page in result['pages']:
 db.save_document_page(
 file_id=file_id,
 page_number=page['number'],
 content=page['text'],
 metadata=page.get('metadata')
 )
 
 # Mark complete
 db.update_file_status(file_id, 'processed')
 
 except Exception as e:
 # Mark as error
 db.update_file_status(file_id, 'error', error=str(e))
```

#### Legacy Methods (Still Work)

```python
# These still work but are deprecated
docs = db.get_all_documents() # Returns Document objects
db.save_document(document)
db.update_processing_status(doc_id, 'completed')
```

**Recommendation:** Migrate to new methods when updating code.

---

## Status Value Mapping

### Old -> New

| Old (Documents) | New (Files) | Description |
|----------------|-------------|-------------|
| `pending` | `uploaded` | File uploaded, ready for processing |
| `processing` | `processing` | Currently being processed |
| `completed` | `processed` | Successfully processed |
| `error` / `failed` | `error` | Processing failed |

### C# Status Constants

```csharp
public static class FileStatus
{
 public const string Uploaded = "uploaded";
 public const string Processing = "processing";
 public const string Processed = "processed";
 public const string Error = "error";
}
```

---

## Testing the Migration

### Test1: Upload a File

```csharp
// Via Blazor UI or API
1. Navigate to upload page
2. Select a PDF file
3. Upload

// Check database
sqlite3 database/data-resources.db
SELECT id, file_name, status FROM files;
// Expected:1 row with status='uploaded'
```

---

### Test2: Python Service Processes File

```bash
# Check Python service logs
docker logs <python-service-container-id>

# Should see:
# "Found1 unprocessed files"
# "Processing file1: filename.pdf"
# "Saved page1 for file1"
# "Updated file1 status to 'processed'"
```

```sql
-- Check database
SELECT id, status, total_pages FROM files WHERE id=1;
-- Expected: status='processed', total_pages=N

SELECT COUNT(*) FROM document_pages WHERE file_id=1;
-- Expected: N pages
```

---

### Test3: Query Pages

```csharp
// C# code
var file = await _context.Files
 .Include(f => f.Pages)
 .FirstAsync(f => f.Id ==1);

Console.WriteLine($"File: {file.OriginalFileName}");
Console.WriteLine($"Pages: {file.Pages.Count}");

foreach (var page in file.Pages.OrderBy(p => p.PageNumber))
{
 Console.WriteLine($"Page {page.PageNumber}: {page.Content.Substring(0,50)}...");
}
```

```python
# Python code
pages = db.get_document_pages(file_id=1)
print(f"Retrieved {len(pages)} pages")

for page in pages:
 print(f"Page {page['page_number']}: {page['content'][:50]}...")
```

---

## Rollback (If Needed)

If you need to revert to the old schema:

```bash
#1. Stop application
#2. Restore old database backup (if you have one)
cp database/data-resources.db.backup database/data-resources.db

#3. Revert code changes
git checkout HEAD -- src/AspireApp.Web/Data/
git checkout HEAD -- src/AspireApp.PythonServices/app/services/database_service.py
```

---

## Common Issues

### Issue: "Table 'files' not found"

**Cause:** Old database still exists

**Solution:**
```bash
rm database/data-resources.db
# Restart application - schema will auto-create
```

---

### Issue: EF Core migration errors

**Cause:** EF Core trying to use old migrations

**Solution:**
```bash
# Delete migrations folder (if exists)
rm -rf src/AspireApp.Web/Migrations/

# Let EF Core create schema from scratch via OnModelCreating
```

---

### Issue: Python service can't read files uploaded by C#

**Cause:** Different table names or column names

**Solution:**
- Verify both use lowercase table names (`files`, `document_pages`)
- Check database file permissions
- Verify Docker volume mounts in AppHost.cs

---

## Best Practices

###1. Always Check Status Before Processing

```python
file = db.get_file_by_id(file_id)
if file['status'] == 'uploaded':
 # Safe to process
 db.update_file_status(file_id, 'processing')
```

###2. Use Transactions for Multi-Step Operations

```python
with db._pool.get_connection() as conn:
 try:
 # Update file
 # Save pages
 # Update status
 conn.commit()
 except Exception as e:
 conn.rollback()
 raise
```

###3. Always Set Error Status on Failure

```python
try:
 process_file(file_id)
except Exception as e:
 db.update_file_status(file_id, 'error', error=str(e))
 logger.error(f"Processing failed: {e}")
```

---

## Next Steps After Migration

1. **Verify Schema** - Check tables created correctly
2. **Test Upload Flow** - Blazor upload creates `files` record
3. **Test Processing** - Python service detects and processes
4. **Test Retrieval** - Query pages via both C# and Python
5. **Implement Neo4j Integration** - Add graph node creation
6. **Implement RAG Search** - Query graph for relevant pages
7. **Connect to Chat** - Pull context for chat responses

---

## Support

If you encounter issues:

1. Check database file exists: `ls -la database/data-resources.db`
2. Check schema: `sqlite3 database/data-resources.db ".schema files"`
3. Check application logs for errors
4. Review `docs/DATABASE_SCHEMA.md` for detailed schema documentation

---

## Summary

**What Changed:**
- Old:4+ tables with complex syncing
- New:2 tables (`files`, `document_pages`)

**Benefits:**
- Simpler code (no sync logic needed)
- Clearer status tracking
- Better performance (fewer joins)
- Future-ready (Neo4j fields built-in)

**Migration Path:**
1. Delete old DB
2. Run app (auto-creates schema)
3. Update code to use new methods
4. Test upload -> process -> query flow
