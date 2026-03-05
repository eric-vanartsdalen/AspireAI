---
description: 'Cross-service contract management for C# and Python integration'
applyTo: '**/models/**,**/Models/**,**/DTOs/**,**/*Client.cs,**/routers/**/*.py'
---

# Cross-Service Contract Guidance

## Scope
- Data contracts between C# (Blazor, API) and Python (FastAPI) services
- Request/response model synchronization
- Breaking change management across service boundaries
- Maintenance-first: evolving contracts safely, then creation patterns

## Core Principles
- **Explicit contracts**: Define shared models in both languages; avoid runtime discovery
- **Version tolerance**: Design for forward/backward compatibility with optional fields
- **Test at boundaries**: Verify serialization/deserialization across the wire
- **Document changes**: Breaking changes require coordination and version bumps

## Maintenance Patterns

### Adding Fields to Existing Models

**Python Side** (Non-Breaking):
```python
# app/models/models.py - BEFORE
class Document(BaseModel):
    id: int
    filename: str
    upload_date: datetime
    processed: bool

# AFTER - Adding optional field
class Document(BaseModel):
    id: int
    filename: str
    upload_date: datetime
    processed: bool
    processing_priority: Optional[int] = 0  # New field with default
```

**C# Side** (Mirror the change):
```csharp
// Before
public record Document(
    int Id,
    string Filename,
    DateTime UploadDate,
    bool Processed
);

// After - Add optional property
public record Document(
    int Id,
    string Filename,
    DateTime UploadDate,
    bool Processed,
    int? ProcessingPriority = 0  // Nullable with default
);
```

**Validation**:
1. Deploy Python service first (backward compatible)
2. Update C# consumers to use new field
3. Test serialization: `JsonSerializer.Serialize(doc)` matches Python `doc.model_dump_json()`

### Changing Field Types (Breaking)

**Coordination Required**:
```python
# Python - Phase 1: Add new field, deprecate old
class ProcessingStatus(BaseModel):
    completed_at: Optional[str]  # Deprecated
    completed_at_v2: Optional[datetime] = None  # New field
    
    @validator('completed_at_v2', pre=True, always=True)
    def migrate_completed_at(cls, v, values):
        if v is None and 'completed_at' in values:
            # Parse old string format
            return datetime.fromisoformat(values['completed_at'])
        return v
```

```csharp
// C# - Phase 1: Accept both formats
public record ProcessingStatus
{
    [JsonPropertyName("completed_at")]
    public string? CompletedAtLegacy { get; init; }
    
    [JsonPropertyName("completed_at_v2")]
    public DateTime? CompletedAt { get; init; }
    
    // Helper to get actual value
    public DateTime? ActualCompletedAt => 
        CompletedAt ?? (CompletedAtLegacy != null 
            ? DateTime.Parse(CompletedAtLegacy) 
            : null);
}
```

**Phase 2** (after all consumers updated):
```python
# Remove deprecated field
class ProcessingStatus(BaseModel):
    completed_at: Optional[datetime] = None  # Rename v2 back
```

### Renaming Fields

**Use JSON Property Names** (prevents breaking):
```python
# Python - rename internal, keep wire format
class Document(BaseModel):
    id: int
    file_name: str = Field(alias="filename")  # Internal: file_name, JSON: filename
    
    class Config:
        populate_by_name = True  # Accept both names
```

```csharp
// C# - match wire format
public record Document(
    int Id,
    [property: JsonPropertyName("filename")] string FileName  // Property: FileName, JSON: filename
);
```

### Removing Fields (Deprecation)

**Step 1 - Mark Deprecated**:
```python
# Python - keep field, mark deprecated
class Document(BaseModel):
    id: int
    filename: str
    legacy_field: Optional[str] = None  # Deprecated: remove in v2.0
```

**Step 2 - Update Consumers**:
```csharp
// C# - stop using deprecated field
var doc = await client.GetFromJsonAsync<Document>($"/documents/{id}");
// No longer access doc.LegacyField
```

**Step 3 - Remove After Migration Period**:
```python
# Python - safe to remove after all consumers updated
class Document(BaseModel):
    id: int
    filename: str
    # legacy_field removed
```

### Updating Nested Objects

**Python Side**:
```python
# Before
class ProcessedDocument(BaseModel):
    document_id: int
    total_pages: int
    metadata: Optional[Dict[str, Any]] = None

# After - Add structured metadata
class ProcessingMetadata(BaseModel):
    extraction_method: str
    processing_duration_ms: int
    warnings: List[str] = []

class ProcessedDocument(BaseModel):
    document_id: int
    total_pages: int
    metadata: Optional[Dict[str, Any]] = None  # Legacy
    processing_metadata: Optional[ProcessingMetadata] = None  # New
```

**C# Side**:
```csharp
// Mirror structure
public record ProcessingMetadata(
    string ExtractionMethod,
    int ProcessingDurationMs,
    List<string> Warnings
);

public record ProcessedDocument(
    int DocumentId,
    int TotalPages,
    Dictionary<string, object>? Metadata,  // Legacy
    ProcessingMetadata? ProcessingMetadata  // New
);
```

## Contract Testing

### Python Side Tests
```python
# tests/test_contracts.py
import json
from app.models.models import Document
from datetime import datetime

def test_document_serialization():
    """Ensure Document matches C# expectations"""
    doc = Document(
        id=1,
        filename="test.pdf",
        upload_date=datetime.now(),
        processed=False
    )
    
    json_str = doc.model_dump_json()
    json_obj = json.loads(json_str)
    
    # Verify field names match C# expectations
    assert "id" in json_obj
    assert "filename" in json_obj
    assert "upload_date" in json_obj
    assert "processed" in json_obj
    
    # Verify types
    assert isinstance(json_obj["id"], int)
    assert isinstance(json_obj["filename"], str)
    assert isinstance(json_obj["processed"], bool)

def test_datetime_format():
    """Ensure datetime format matches C# parsing"""
    doc = Document(
        id=1,
        filename="test.pdf",
        upload_date=datetime(2025, 11, 2, 14, 30, 0),
        processed=False
    )
    
    json_obj = json.loads(doc.model_dump_json())
    # C# expects ISO 8601
    assert "2025-11-02" in json_obj["upload_date"]
```

### C# Side Tests
```csharp
// Tests/ContractTests.cs
[Fact]
public async Task Document_Deserializes_From_Python()
{
    // Arrange - JSON from Python service
    var pythonJson = """
        {
            "id": 1,
            "filename": "test.pdf",
            "upload_date": "2025-11-02T14:30:00",
            "processed": false
        }
        """;
    
    // Act
    var doc = JsonSerializer.Deserialize<Document>(pythonJson);
    
    // Assert
    Assert.NotNull(doc);
    Assert.Equal(1, doc.Id);
    Assert.Equal("test.pdf", doc.Filename);
    Assert.False(doc.Processed);
}

[Fact]
public async Task Document_Serializes_For_Python()
{
    // Arrange
    var doc = new Document(
        Id: 1,
        Filename: "test.pdf",
        UploadDate: new DateTime(2025, 11, 2, 14, 30, 0),
        Processed: false
    );
    
    // Act
    var json = JsonSerializer.Serialize(doc);
    var jsonObj = JsonSerializer.Deserialize<JsonElement>(json);
    
    // Assert - verify field names Python expects
    Assert.True(jsonObj.TryGetProperty("id", out _));
    Assert.True(jsonObj.TryGetProperty("filename", out _));
    Assert.True(jsonObj.TryGetProperty("upload_date", out _));
}
```

## Creation Patterns

### Defining New Shared Models

**Python Model** (Define first):
```python
# app/models/models.py
from pydantic import BaseModel, Field
from typing import Optional, List
from datetime import datetime

class DocumentAnalysis(BaseModel):
    """Analysis results for processed document"""
    document_id: int
    entity_count: int
    keyword_summary: List[str]
    confidence_score: float = Field(ge=0.0, le=1.0)
    analyzed_at: datetime
    
    class Config:
        json_schema_extra = {
            "example": {
                "document_id": 1,
                "entity_count": 15,
                "keyword_summary": ["contract", "agreement"],
                "confidence_score": 0.95,
                "analyzed_at": "2025-11-02T14:30:00"
            }
        }
```

**C# Model** (Mirror structure):
```csharp
// Models/DocumentAnalysis.cs
public record DocumentAnalysis(
    [property: JsonPropertyName("document_id")] int DocumentId,
    [property: JsonPropertyName("entity_count")] int EntityCount,
    [property: JsonPropertyName("keyword_summary")] List<string> KeywordSummary,
    [property: JsonPropertyName("confidence_score")] double ConfidenceScore,
    [property: JsonPropertyName("analyzed_at")] DateTime AnalyzedAt
);
```

**FastAPI Endpoint**:
```python
@router.get("/{document_id}/analysis", response_model=DocumentAnalysis)
async def get_document_analysis(document_id: int) -> DocumentAnalysis:
    """Get analysis for document"""
    analysis = await analyze_document(document_id)
    return analysis
```

**C# Client**:
```csharp
public class DocumentClient(HttpClient httpClient)
{
    public async Task<DocumentAnalysis?> GetAnalysisAsync(
        int documentId, 
        CancellationToken ct = default)
    {
        return await httpClient.GetFromJsonAsync<DocumentAnalysis>(
            $"/documents/{documentId}/analysis", ct);
    }
}
```

### Collection Responses

**Python Pattern**:
```python
class PaginatedResponse(BaseModel, Generic[T]):
    total: int
    skip: int
    limit: int
    items: List[T]

@router.get("/", response_model=PaginatedResponse[Document])
async def list_documents(
    skip: int = 0, 
    limit: int = 100
) -> PaginatedResponse[Document]:
    docs = get_documents()
    return PaginatedResponse(
        total=len(docs),
        skip=skip,
        limit=limit,
        items=docs[skip:skip+limit]
    )
```

**C# Pattern**:
```csharp
public record PaginatedResponse<T>(
    int Total,
    int Skip,
    int Limit,
    List<T> Items
);

public async Task<PaginatedResponse<Document>> ListDocumentsAsync(
    int skip = 0, 
    int limit = 100)
{
    return await httpClient.GetFromJsonAsync<PaginatedResponse<Document>>(
        $"/documents?skip={skip}&limit={limit}");
}
```

## Versioning Strategies

### URL Versioning
```python
# Python - version in route
@router.get("/v1/documents", response_model=List[Document])
@router.get("/v2/documents", response_model=PaginatedResponse[Document])

# C# - version in client
public class DocumentClientV1(HttpClient client) { }
public class DocumentClientV2(HttpClient client) { }
```

### Content Negotiation
```python
# Python - version in header
@router.get("/documents")
async def list_documents(request: Request):
    version = request.headers.get("API-Version", "1.0")
    if version == "2.0":
        return PaginatedResponse(...)
    return simple_list(...)
```

```csharp
// C# - send version header
httpClient.DefaultRequestHeaders.Add("API-Version", "2.0");
```

## Error Response Contracts

**Python Standard Error**:
```python
class ErrorDetail(BaseModel):
    detail: str
    error_type: str
    field: Optional[str] = None

@router.post("/upload")
async def upload_document(file: UploadFile):
    if file.size > MAX_SIZE:
        raise HTTPException(
            status_code=413,
            detail=ErrorDetail(
                detail="File too large",
                error_type="validation_error",
                field="file"
            ).model_dump()
        )
```

**C# Error Handling**:
```csharp
public record ErrorDetail(
    string Detail,
    [property: JsonPropertyName("error_type")] string ErrorType,
    string? Field
);

try
{
    await client.PostAsync("/upload", content);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
{
    var errorJson = await response.Content.ReadAsStringAsync();
    var error = JsonSerializer.Deserialize<ErrorDetail>(errorJson);
    // Handle error.Detail
}
```

## DateTime Handling

**Python Best Practice**:
```python
from datetime import datetime, timezone

class Document(BaseModel):
    upload_date: datetime
    
    class Config:
        json_encoders = {
            datetime: lambda v: v.isoformat()
        }

# Always use UTC
doc = Document(
    id=1,
    filename="test.pdf",
    upload_date=datetime.now(timezone.utc)
)
```

**C# Best Practice**:
```csharp
public record Document(
    int Id,
    string Filename,
    DateTime UploadDate  // Deserializes ISO 8601 automatically
);

// Store as UTC
var doc = new Document(
    Id: 1,
    Filename: "test.pdf",
    UploadDate: DateTime.UtcNow
);
```

## Enum Synchronization

**Python Enums**:
```python
from enum import Enum

class ProcessingStatus(str, Enum):
    PENDING = "pending"
    PROCESSING = "processing"
    COMPLETED = "completed"
    ERROR = "error"

class Document(BaseModel):
    id: int
    status: ProcessingStatus
```

**C# Enums**:
```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Error
}

public record Document(
    int Id,
    ProcessingStatus Status
);
```

**Validation**: Ensure string values match exactly (case-insensitive in C# by default)

## Documentation

### OpenAPI/Swagger Alignment
```python
# Python - ensure descriptions match
@router.get(
    "/{document_id}",
    response_model=Document,
    summary="Get document by ID",
    description="Retrieves a single document with all metadata"
)

# C# - generate matching XML docs
/// <summary>
/// Get document by ID
/// </summary>
/// <param name="documentId">Document identifier</param>
/// <returns>Document with all metadata</returns>
public async Task<Document?> GetDocumentAsync(int documentId);
```

### Contract Change Log
Maintain `CONTRACTS.md` in repo root:
```markdown
# Contract Change Log

## 2025-11-02 - Document Model
- Added `processing_priority` field (optional, default: 0)
- Breaking: None
- Migration: Update C# models to include nullable int

## 2025-10-15 - ProcessingStatus Model
- Changed `completed_at` from string to datetime
- Breaking: Yes
- Migration: Deploy Python v2 first, update C# consumers within 7 days
```

## Troubleshooting

### Serialization Mismatches
```python
# Python debug: Print actual JSON
print(doc.model_dump_json(indent=2))

# C# debug: Inspect deserialized object
var json = await response.Content.ReadAsStringAsync();
Console.WriteLine(json);
var doc = JsonSerializer.Deserialize<Document>(json);
```

### DateTime Parsing Failures
- Ensure Python uses `.isoformat()`
- Verify C# uses ISO 8601 compatible format
- Always use UTC timestamps
- Test with timezones in contract tests

### Missing Fields
- Check for typos in property names
- Verify `JsonPropertyName` attributes match Python `Field(alias=...)`
- Ensure optional fields have defaults

## Decision Log
- **2025-11-02**: Initial creation focusing on maintenance patterns
- **2025-11-02**: Established deprecation and versioning strategies
- **2025-11-02**: Documented contract testing requirements

## Related Instructions
- `python.instructions.md` - Pydantic model patterns
- `csharp.instructions.md` - C# serialization and DTOs
- `aspire-orchestration.instructions.md` - Service discovery and endpoints

Update this file when new cross-service contracts are established or breaking changes are planned.
