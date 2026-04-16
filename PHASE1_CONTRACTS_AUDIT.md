# Phase 1: Core Contracts — C# Audit Report

**Date:** 2025-11-02  
**Auditor:** Jeff (C# Dev)  
**Status:** Implemented on this branch; retained as the audit/reference document for the Phase 1 contract slice

---

## Executive Summary

> **Update:** The Phase 1 contract slice described below has now been delivered in the repo. See `src/AspireApp.ApiService/Contracts/` and `src/AspireApp.WebTest/Tests/BrainContractRoundTripTests.cs` for the implemented artifacts.

Phase 1 required defining mirror C# record types for the BRAIN contract models (`CanonicalDocument`, `ValidatedDocument`, `KnowledgeResult`, `ReasonResponse`) that are specified in Python. At audit time, the C# side only had Entity Framework models (`FileMetadata`, `DocumentPage`) for persistence and **no wire-format DTOs** for API serialization. The implemented solution followed this audit's recommendation: explicit `JsonPropertyName` attributes on all C# contract records for snake_case JSON parity and round-trip deserialization.

---

## Current C# Contract Surface

### Persistence Layer (EF Core Entities)
- **Location:** `src/AspireApp.Web/Data/DocumentEntities.cs` + `ChatConversationEntities.cs`
- **Models:**
  - `FileMetadata` (maps to `files` table)
  - `DocumentPage` (maps to `document_pages` table)
  - `ChatConversation`, `ChatConversationMessage` (chat persistence)
- **Serialization:** None currently—these are DB-mapped only via EF Core `[Column]` attributes (snake_case names match SQLite schema)

### API Gateway Layer (Minimal API Stubs)
- **Location:** `src/AspireApp.ApiService/Program.cs`
- **Current State:**
  - `/brain/health` → returns `BrainHealthResponse` (inline record)
  - `/brain/chat`, `/brain/ingest`, `/brain/query` → return 501 (stub phase)
- **Issue:** No input/output contracts yet; only health response is defined

### Test Fixtures & Models
- **Location:** `src/AspireApp.WebTest/` (DataModels, Factories, Fixtures)
- **Current State:** Minimal test infrastructure; basic AppHost smoke tests use Playwright
- **Opportunity:** Contract round-trip tests should live here

### Service Defaults
- **Location:** `src/AspireApp.ServiceDefaults/`
- **Current State:** Shared DI configuration, no contract models

---

## Phase 1 Deliverables — C# Placement Map

### Required New Directory
```
src/AspireApp.ApiService/
├── Contracts/                          ← NEW
│   ├── BrainContractModels.cs         ← Phase 1 contracts go here
│   ├── Common/
│   │   └── EnvelopeMixin.cs           ← tenant_id, correlation_id
│   └── Serialization.cs               ← JSON config helpers (if needed)
```

**Rationale:** ApiService is the Gateway; contracts belong there. Consumers (Web, tests) reference via `aspireApp.ApiService/Contracts/` namespace.

### C# Record Types to Create

| Phase 1 Python Model | C# Mirror Record | Location | JSON Field Names |
|---|---|---|---|
| `CanonicalDocument` | `CanonicalDocument` | `BrainContractModels.cs` | `tenant_id`, `document_id`, `source_type`, `source_confidence`, `pages`, `metadata` |
| `PageContent` (nested) | `PageContent` | `BrainContractModels.cs` | `page_number`, `content`, `section`, `metadata` |
| `ValidatedDocument` | `ValidatedDocument` | `BrainContractModels.cs` | Extends `CanonicalDocument` + `claims`, `contradictions`, `overall_confidence` |
| `Claim` (nested) | `Claim` | `BrainContractModels.cs` | `claim_id`, `text`, `confidence`, `evidence`, `source_ref` |
| `Evidence` (nested) | `Evidence` | `BrainContractModels.cs` | `content`, `confidence`, `source` |
| `KnowledgeResult` | `KnowledgeResult` | `BrainContractModels.cs` | `results` (list of `KnowledgeItem`), uses `KnowledgeItem` |
| `KnowledgeItem` (nested) | `KnowledgeItem` | `BrainContractModels.cs` | `content`, `confidence`, `source_refs`, `relevance_score` |
| `ReasonResponse` | `ReasonResponse` | `BrainContractModels.cs` | `answer`, `confidence`, `evidence`, `reasoning_steps`, `proactive_suggestions` |
| `ReasoningStep` (nested) | `ReasoningStep` | `BrainContractModels.cs` | `step`, `reasoning`, `tool`, `result` |
| Envelope mixin | `ICorrelatable` or `CorrelationEnvelope` | `Common/EnvelopeMixin.cs` | `tenant_id`, `correlation_id` |

---

## Snake_case JSON Field Mapping — Critical Details

### Problem
- Python Pydantic models use snake_case field names (e.g., `source_confidence`, `document_id`)
- C# record property names use PascalCase by convention (e.g., `SourceConfidence`, `DocumentId`)
- System.Text.Json serialization must map these correctly both ways

### Solution: JsonPropertyName Attributes
```csharp
// Example Pattern
public record CanonicalDocument(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("document_id")] int DocumentId,
    [property: JsonPropertyName("source_type")] string SourceType,
    [property: JsonPropertyName("source_confidence")] double SourceConfidence,
    [property: JsonPropertyName("pages")] List<PageContent> Pages,
    [property: JsonPropertyName("metadata")] Dictionary<string, object>? Metadata
);
```

### Round-Trip Validation Requirements
- Serialize C# object → JSON: verify snake_case keys appear
- Deserialize JSON → C# object: verify snake_case keys map to PascalCase properties
- Re-serialize C# object: verify same JSON shape
- Cross-language: Python JSON → C# deserialization → Python deserialization

---

## Test Surface Changes Required

### New Test File
```
src/AspireApp.WebTest/Tests/ContractRoundTripTests.cs
```

### Test Patterns (3 Categories)

#### 1. Serialization Shape Tests
```csharp
[Fact]
public void CanonicalDocument_SerializesToSnakeCaseJson()
{
    // Arrange
    var doc = new CanonicalDocument(
        TenantId: "test-tenant",
        DocumentId: 1,
        SourceType: "upload",
        SourceConfidence: 0.95,
        Pages: [new PageContent(1, "content", null)],
        Metadata: null
    );
    
    // Act
    var json = JsonSerializer.Serialize(doc, JsonOptions);
    var element = JsonDocument.Parse(json).RootElement;
    
    // Assert
    Assert.True(element.TryGetProperty("tenant_id", out _));     // snake_case
    Assert.True(element.TryGetProperty("document_id", out _));
    Assert.False(element.TryGetProperty("TenantId", out _));    // NOT PascalCase
}
```

#### 2. Deserialization Tests (Python JSON → C#)
```csharp
[Fact]
public void CanonicalDocument_DeserializesFromPythonJson()
{
    // Arrange - Python-emitted JSON
    var pythonJson = """
    {
        "tenant_id": "test-tenant",
        "document_id": 1,
        "source_type": "upload",
        "source_confidence": 0.95,
        "pages": [{"page_number": 1, "content": "text", "section": null, "metadata": null}],
        "metadata": null
    }
    """;
    
    // Act
    var doc = JsonSerializer.Deserialize<CanonicalDocument>(pythonJson, JsonOptions);
    
    // Assert
    Assert.NotNull(doc);
    Assert.Equal("test-tenant", doc.TenantId);
    Assert.Equal(0.95, doc.SourceConfidence);
    Assert.Single(doc.Pages);
}
```

#### 3. Full Round-Trip Tests (C# → JSON → C# → JSON)
```csharp
[Fact]
public void CanonicalDocument_RoundTripPreservesValues()
{
    // Arrange
    var original = new CanonicalDocument(
        TenantId: "round-trip",
        DocumentId: 42,
        SourceType: "upload",
        SourceConfidence: 0.88,
        Pages: [new PageContent(1, "test content", null)],
        Metadata: new Dictionary<string, object> { ["key"] = "value" }
    );
    
    // Act - serialize → deserialize → serialize
    var json1 = JsonSerializer.Serialize(original, JsonOptions);
    var deserialized = JsonSerializer.Deserialize<CanonicalDocument>(json1, JsonOptions);
    var json2 = JsonSerializer.Serialize(deserialized, JsonOptions);
    
    // Assert
    Assert.Equal(json1, json2);
    Assert.Equal(original.TenantId, deserialized!.TenantId);
    Assert.Equal(original.DocumentId, deserialized.DocumentId);
}
```

### Test Configuration (Fixture)
- Define static `JsonSerializerOptions JsonOptions` with camelCase naming policy (if needed) or rely on `JsonPropertyName` attributes
- **Recommendation:** Explicit `JsonPropertyName` is safer than global naming policies for cross-language contracts

---

## Parity Concerns & Edge Cases

### 1. DateTime Serialization (ISO 8601)
- **Python:** Pydantic uses `datetime` objects; serializes to ISO 8601 by default
- **C#:** System.Text.Json also uses ISO 8601
- **Risk:** None—automatic alignment
- **Test:** Include datetime fields in all models; verify format in JSON

### 2. Optional/Nullable Fields
- **Python:** `Optional[T]` + default None
- **C#:** `T?` + default null, or PascalCase properties with nullable annotations
- **Pattern:**
  ```csharp
  [property: JsonPropertyName("metadata")] 
  Dictionary<string, object>? Metadata = null
  ```
- **Test:** Verify null fields serialize to `null` in JSON, deserialize back to null

### 3. List Serialization (Pages, Evidence, Suggestions)
- **Python:** `List[T]` from `typing`
- **C#:** `List<T>` or `IEnumerable<T>`
- **Recommendation:** Use `List<T>` in records for mutability if needed; or `ImmutableList<T>` if immutable
- **Test:** Include multi-element collections; verify array indices

### 4. Nested Record Flattening (Not Expected in Phase 1)
- `PageContent`, `Claim`, `Evidence`, `ReasoningStep` are all embedded; no separate endpoints
- If Phase 2 exposes these as standalone resources, add flatten/unflatten logic

### 5. Enum Serialization (status, role, etc.)
- **Python:** `Enum` members serialize to string values
- **C#:** Use `[JsonConverter(typeof(JsonStringEnumConverter))]` on enums
- **Not in Phase 1 contracts**, but document for future phases

---

## .NET-Specific Implementation Checklist

- [ ] Create `src/AspireApp.ApiService/Contracts/BrainContractModels.cs`
- [ ] Define all Phase 1 record types with `JsonPropertyName` attributes
- [ ] Create `src/AspireApp.ApiService/Contracts/Common/EnvelopeMixin.cs` (or interface for `TenantId`, `CorrelationId`)
- [ ] Add `using System.Text.Json.Serialization;` to all contract files
- [ ] Define static `JsonSerializerOptions` in ApiService Program.cs (or ServiceDefaults) for consistency
- [ ] Create `src/AspireApp.WebTest/Tests/ContractRoundTripTests.cs` with 3 test categories
- [ ] Add reference from WebTest.csproj to ApiService.csproj (if not already present)
- [ ] Verify deserialization with Playwright/integration tests once Python side emits real contracts
- [ ] Document JSON field naming in `.github/instructions/cross-service-contracts.instructions.md` (update existing guidance)

---

## Known .NET-Specific Risks

1. **JsonPropertyName Maintenance**
   - If Python field names change, C# `JsonPropertyName` must stay in sync
   - No automatic generation; manual sync required
   - **Mitigation:** Contract round-trip tests catch misalignment immediately

2. **System.Text.Json Quirks**
   - Default behavior: camelCase naming policy can interfere with explicit `JsonPropertyName`
   - **Recommendation:** Avoid global naming policies; rely on explicit attributes
   - **Test:** Verify `"tenant_id"` (lowercase snake) not `"tenantId"` (camelCase)

3. **Enum String Serialization**
   - If enums used (e.g., `SourceType` as enum, not string), verify `[JsonConverter(typeof(JsonStringEnumConverter))]` applied
   - **For Phase 1:** Keep `SourceType`, status fields as strings to avoid this complexity

4. **Inheritance vs. Composition**
   - `ValidatedDocument` "extends" `CanonicalDocument` in Python
   - **C# Approach:** Use record inheritance or explicit composition
   - **Recommendation:** Inheritance for Phase 1 (simpler for nested field mapping)
   ```csharp
   public record ValidatedDocument(
       string TenantId,
       int DocumentId,
       // ... all CanonicalDocument fields ...
       [property: JsonPropertyName("claims")] List<Claim> Claims,
       [property: JsonPropertyName("contradictions")] List<Contradiction> Contradictions,
       [property: JsonPropertyName("overall_confidence")] double OverallConfidence
   ) : CanonicalDocument(TenantId, DocumentId, /* ... */);
   ```
   - **Test:** Verify deserialization works for both parent and child types

5. **ICorrelatable Interface vs. Envelope Mixin**
   - **Option A:** Add `tenant_id`, `correlation_id` to every record individually
   - **Option B:** Base interface `ICorrelatable` with default implementations
   - **Recommendation for Phase 1:** Option A (explicit fields) to avoid complexity; refactor in Phase 4 if needed

---

## Integration Points

### ApiService Program.cs
- Register contract models in JSON serialization options
- Example:
  ```csharp
  var options = new JsonSerializerOptions { PropertyNamingPolicy = null };
  // Rely on JsonPropertyName attributes
  app.Services.AddSingleton(options);
  ```

### Web Frontend (Phase 3)
- Blazor components will use typed HttpClient to call `/brain/*` endpoints
- Typed clients return `CanonicalDocument`, `KnowledgeResult`, `ReasonResponse`
- UI binds to C# record properties (PascalCase) automatically

### WebTest Project
- Contract round-trip tests use the same `JsonSerializerOptions`
- Playwright integration tests can validate end-to-end JSON shape

---

## Python Side Expectations (For Coordination)

- Python will emit JSON with **exact snake_case** field names (e.g., `source_confidence`, not `sourceConfidence`)
- Pydantic models in `app/contracts/models.py` are source of truth
- Phase 1 Python deliverable should include sample JSON payloads for each model
- **Sync point:** Python contract definition → C# record generation (can be manual if needed; no code gen required)

---

## Recommended Implementation Order

1. **Day 1 (Contracts):**
   - Create `Contracts/` directory in ApiService
   - Define all Phase 1 record types in `BrainContractModels.cs`
   - Add `JsonPropertyName` attributes for all snake_case fields
   - Compile & verify no syntax errors

2. **Day 2 (Tests):**
   - Create `ContractRoundTripTests.cs` in WebTest
   - Implement serialization shape tests (all models)
   - Implement deserialization tests with hand-written Python JSON samples
   - Run tests → all should pass

3. **Day 3 (Integration):**
   - Once Python side emits real `app/contracts/models.py`, run full round-trip with actual JSON
   - Update integration tests with live Python endpoints (POST `/brain/ingest`, etc.)
   - Validate cross-language serialization parity

4. **Day 4 (Gate P1-A):**
   - Milestone: All contracts defined, round-trip tests passing
   - Commit & prepare for Phase 2 implementation

---

## Files Ready to Implement

| File | Status | Owner |
|------|--------|-------|
| `src/AspireApp.ApiService/Contracts/BrainContractModels.cs` | Ready to code | Jeff |
| `src/AspireApp.ApiService/Contracts/Common/EnvelopeMixin.cs` | Ready to code | Jeff |
| `src/AspireApp.WebTest/Tests/ContractRoundTripTests.cs` | Ready to code | Jeff (test pattern) or Buster (test strat) |
| `src/AspireApp.ApiService/Program.cs` | Update JsonSerializerOptions | Jeff |
| `.github/instructions/cross-service-contracts.instructions.md` | Update with C# guidance | Jeff |

---

## Gate P1-A Acceptance Criteria

✓ All Phase 1 contract records defined in `src/AspireApp.ApiService/Contracts/`  
✓ All records use `JsonPropertyName` for snake_case field mapping  
✓ Round-trip serialization tests pass (C# → JSON → C# → JSON parity)  
✓ Deserialization tests pass with hand-written Python JSON samples  
✓ No compilation warnings in ApiService or WebTest projects  
✓ `.github/instructions/cross-service-contracts.instructions.md` includes C# enum, nullable, and inheritance patterns  

---

## Open Questions for Coordinator

1. **Envelope mixin design:** Should `tenant_id` and `correlation_id` be on every Phase 1 model, or added later? (Recommend: now, for proactive monitoring in Phase 2)
2. **ICorrelatable interface:** Base interface or explicit fields? (Recommend: explicit for Phase 1)
3. **Inheritance for ValidatedDocument:** Record inheritance or composition? (Recommend: inheritance for simplicity)
4. **Enum strategy:** Keep status/source_type as strings in Phase 1, or migrate to enums? (Recommend: strings for Phase 1; enums in Phase 4)

---

**Audit prepared by:** Jeff (C# Dev)  
**Date:** 2025-11-02  
**Confidence Level:** High — C# surface is clear; parity concerns are well-understood  
**Ready for:** Implementation batch (coordinate with Python side for model finalization)
