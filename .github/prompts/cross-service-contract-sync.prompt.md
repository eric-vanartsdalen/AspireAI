```prompt
agent: 'agent'
tools: ['read_file', 'grep_search', 'replace_string_in_file', 'run_in_terminal']
description: 'Synchronize data contracts between C# and Python services for API integration'
owner: '@eric-vanartsdalen'
audience: 'Integration Maintainers'
dependencies: ['.NET 10 SDK', 'Python 3.12']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Adding/modifying fields in cross-service DTOs, ensuring C#↔Python serialization compatibility, handling breaking changes in API contracts.
- **Dependencies**: C# records/DTOs, Python Pydantic models, understanding of JSON serialization.
- **Sample Inputs**: Model definitions from `app/models/models.py` (Python) or C# record definitions, API endpoint requirements.
- **Related Instructions**: See `../instructions/cross-service-contracts.instructions.md` for comprehensive contract patterns; reference `../instructions/python.instructions.md` for Pydantic usage; see `../instructions/csharp.instructions.md` for DTO conventions.

# Cross-Service Contract Synchronization

Your goal is to maintain consistent data contracts between C# and Python services, ensuring seamless API integration.

## Contract Mapping Patterns

### Basic Field Types

**Python (Pydantic):**
```python
from pydantic import BaseModel, Field
from typing import Optional
from datetime import datetime

class DocumentMetadata(BaseModel):
    id: str
    title: str
    page_count: int
    uploaded_at: datetime
    tags: Optional[list[str]] = None
```

**C# (Record):**
```csharp
public record DocumentMetadata(
    string Id,
    string Title,
    int PageCount,
    DateTime UploadedAt,
    List<string>? Tags = null
);
```

### Type Correspondence

| Python Type | C# Type | Notes |
|-------------|---------|-------|
| `str` | `string` | Direct match |
| `int` | `int` | Direct match |
| `float` | `double` | Default for decimals |
| `bool` | `bool` | Direct match |
| `datetime` | `DateTime` | Use ISO 8601 format |
| `list[T]` | `List<T>` or `IEnumerable<T>` | Collection types |
| `Optional[T]` | `T?` | Nullable reference types |
| `dict[K,V]` | `Dictionary<K,V>` | Key-value pairs |

## Maintenance Workflows

### Adding a New Field

**1. Update Python model (source of truth for this example):**
```python
# app/models/models.py
class DocumentMetadata(BaseModel):
    id: str
    title: str
    page_count: int
    uploaded_at: datetime
    tags: Optional[list[str]] = None
    author: str = "Unknown"  # ← New field with default
```

**2. Update C# record:**
```csharp
// AspireApp.Web/Data/DocumentMetadata.cs
public record DocumentMetadata(
    string Id,
    string Title,
    int PageCount,
    DateTime UploadedAt,
    List<string>? Tags = null,
    string Author = "Unknown"  // ← New field with default
);
```

**3. Test serialization:**
```python
# Python test
metadata = DocumentMetadata(
    id="123",
    title="Test",
    page_count=10,
    uploaded_at=datetime.now(),
    author="Eric"
)
print(metadata.model_dump_json())
```

```csharp
// C# test
var metadata = new DocumentMetadata(
    Id: "123",
    Title: "Test",
    PageCount: 10,
    UploadedAt: DateTime.UtcNow,
    Author: "Eric"
);
var json = JsonSerializer.Serialize(metadata);
```

### Modifying an Existing Field (Breaking Change)

**Scenario: Change `page_count` from `int` to nullable `int?`**

**1. Update Python model first:**
```python
class DocumentMetadata(BaseModel):
    id: str
    title: str
    page_count: Optional[int] = None  # ← Changed to Optional
    uploaded_at: datetime
```

**2. Update C# record:**
```csharp
public record DocumentMetadata(
    string Id,
    string Title,
    int? PageCount,  // ← Changed to nullable
    DateTime UploadedAt
);
```

**3. Update consuming code:**
- Python: Handle `None` case in processing logic
- C#: Add null checks before using `PageCount`

**4. Test integration:**
```powershell
# Start services
dotnet run --project src/AspireApp.AppHost

# Test document upload with missing page_count
curl -X POST http://localhost:8000/api/documents -F "file=@test.pdf"

# Verify C# service can deserialize response
```

### Removing a Field (Deprecation Strategy)

**Phase 1: Mark as deprecated (maintain backward compatibility)**

```python
# app/models/models.py
class DocumentMetadata(BaseModel):
    id: str
    title: str
    legacy_field: Optional[str] = None  # Deprecated, will be removed in v2
    
    model_config = ConfigDict(
        json_schema_extra={
            "deprecated": ["legacy_field"]
        }
    )
```

**Phase 2: Remove after grace period**
- Remove from Python model
- Remove from C# record
- Update all consumers
- Bump API version if using versioning

## DateTime Handling

**Python FastAPI (UTC recommended):**
```python
from datetime import datetime, timezone

class Event(BaseModel):
    timestamp: datetime
    
    @field_validator('timestamp')
    def ensure_utc(cls, v: datetime) -> datetime:
        if v.tzinfo is None:
            return v.replace(tzinfo=timezone.utc)
        return v.astimezone(timezone.utc)
```

**C# (always use UTC for APIs):**
```csharp
public record Event(
    DateTime Timestamp
)
{
    // Use DateTime.UtcNow when creating, validate on deserialization
    public Event EnsureUtc() => this with 
    { 
        Timestamp = Timestamp.Kind == DateTimeKind.Utc 
            ? Timestamp 
            : Timestamp.ToUniversalTime() 
    };
}
```

**JSON serialization:**
```python
# Python - FastAPI handles automatically
# Output: "2025-11-02T10:30:00Z"
```

```csharp
// C# - configure JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
```

## Enum Synchronization

**Python:**
```python
from enum import Enum

class DocumentStatus(str, Enum):
    PENDING = "pending"
    PROCESSING = "processing"
    COMPLETED = "completed"
    FAILED = "failed"
```

**C#:**
```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
```

**Serialization behavior:**
- Python: `"status": "pending"` (lowercase string)
- C#: Configure to match Python casing:

```csharp
options.SerializerOptions.Converters.Add(
    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
);
```

## Nested Objects

**Python:**
```python
class Address(BaseModel):
    street: str
    city: str
    country: str = "USA"

class User(BaseModel):
    name: str
    address: Address
```

**C#:**
```csharp
public record Address(
    string Street,
    string City,
    string Country = "USA"
);

public record User(
    string Name,
    Address Address
);
```

## Validation and Error Responses

**Python (Pydantic validation):**
```python
from pydantic import BaseModel, Field, field_validator

class UploadRequest(BaseModel):
    filename: str = Field(..., min_length=1, max_length=255)
    content_type: str
    
    @field_validator('content_type')
    def validate_content_type(cls, v: str) -> str:
        allowed = ['application/pdf', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document']
        if v not in allowed:
            raise ValueError(f'Content type must be one of {allowed}')
        return v
```

**C# (Data Annotations or FluentValidation):**
```csharp
public record UploadRequest(
    [StringLength(255, MinimumLength = 1)] string Filename,
    string ContentType
);

// Validation in controller/minimal API
if (!IsValidContentType(request.ContentType))
{
    return Results.BadRequest(new { error = "Invalid content type" });
}
```

## Contract Testing

**Python test:**
```python
import pytest
from app.models.models import DocumentMetadata
from datetime import datetime

def test_document_metadata_serialization():
    metadata = DocumentMetadata(
        id="test-123",
        title="Test Document",
        page_count=5,
        uploaded_at=datetime.now()
    )
    
    # Serialize to JSON
    json_str = metadata.model_dump_json()
    
    # Deserialize back
    restored = DocumentMetadata.model_validate_json(json_str)
    
    assert restored.id == metadata.id
    assert restored.title == metadata.title
```

**C# test:**
```csharp
[Fact]
public void DocumentMetadata_Serialization_RoundTrip()
{
    var metadata = new DocumentMetadata(
        Id: "test-123",
        Title: "Test Document",
        PageCount: 5,
        UploadedAt: DateTime.UtcNow
    );
    
    // Serialize
    var json = JsonSerializer.Serialize(metadata);
    
    // Deserialize
    var restored = JsonSerializer.Deserialize<DocumentMetadata>(json);
    
    Assert.Equal(metadata.Id, restored.Id);
    Assert.Equal(metadata.Title, restored.Title);
}
```

## Integration Testing Pattern

**Test cross-service serialization:**
```python
# test_contract_integration.py
import pytest
import httpx

@pytest.mark.asyncio
async def test_document_upload_contract():
    """Test that C# client can deserialize Python API response"""
    async with httpx.AsyncClient() as client:
        # Upload document to Python API
        response = await client.post(
            "http://localhost:8000/api/documents",
            files={"file": ("test.pdf", b"fake pdf content")}
        )
        
        assert response.status_code == 200
        data = response.json()
        
        # Verify contract fields present
        assert "id" in data
        assert "title" in data
        assert "uploaded_at" in data
```

## Common Pitfalls

### Case Sensitivity
**Problem:** Python uses `snake_case`, C# uses `PascalCase`

**Solution:** Configure JSON serialization

```csharp
// C# - match Python's snake_case
options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
```

```python
# Python - alias to PascalCase if needed
class DocumentMetadata(BaseModel):
    id: str = Field(alias="Id")
    title: str = Field(alias="Title")
```

### DateTime Timezone Issues
**Problem:** Mixed UTC and local times cause comparison failures

**Solution:** Always use UTC for APIs, convert at edges

### Optional vs Required
**Problem:** Python defaults to required, C# defaults to nullable

**Solution:** Be explicit

```python
required_field: str  # Required
optional_field: Optional[str] = None  # Optional
```

```csharp
string RequiredField { get; init; }  // Required
string? OptionalField { get; init; }  // Optional
```

## Breaking Change Checklist

When making breaking changes to contracts:
1. [ ] Update Python Pydantic models
2. [ ] Update C# records/DTOs
3. [ ] Update API documentation
4. [ ] Write migration guide if needed
5. [ ] Add contract tests for new structure
6. [ ] Test integration between services
7. [ ] Consider API versioning (e.g., `/api/v2/documents`)
8. [ ] Document breaking change in changelog

When coordinating contract changes, always validate serialization compatibility and test integration flows end-to-end.
```
