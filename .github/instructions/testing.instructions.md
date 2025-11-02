---
description: 'Testing strategies and patterns for AspireAI services'
applyTo: '**/*Test*.cs,**/*test*.py,**/tests/**,**/*.test.ts'
---

# Testing Guidance for AspireAI

## Scope
- Testing strategies across .NET (Blazor, API), Python (FastAPI), and Neo4j integration
- Aspire-aware testing (services orchestrated together)
- Maintenance-first: testing existing features, then patterns for new features

## Core Principles
- **Test at appropriate boundaries**: Unit tests for logic, integration for service interactions, E2E for user workflows
- **Aspire-aware testing**: Integration tests should work within the Aspire orchestration context
- **Mock external dependencies**: Neo4j, Ollama, external APIs when testing logic in isolation
- **Test data management**: Use fixtures and cleanup to prevent test pollution
- **Fast feedback**: Unit tests run in milliseconds, integration in seconds, E2E sparingly

## Test Pyramid for AspireAI

```
        E2E (Playwright)
       /    Blazor UI flows
      /
     /   Integration Tests
    /   API↔Python↔Neo4j
   /
  /  Unit Tests
 /   Logic, models, services
```

## Maintenance Patterns

### Adding Tests to Existing Code

**Python Service - Adding Unit Test**:
```python
# tests/unit/test_database_service.py
import pytest
from unittest.mock import Mock, patch, MagicMock
from app.services.database_service import DatabaseService

@pytest.fixture
def mock_connection_pool():
    """Mock database connection pool"""
    pool = Mock()
    conn = MagicMock()
    cursor = MagicMock()
    cursor.fetchall.return_value = []
    conn.cursor.return_value = cursor
    pool.get_connection.return_value.__enter__ = Mock(return_value=conn)
    pool.get_connection.return_value.__exit__ = Mock(return_value=False)
    return pool

def test_get_unprocessed_files(mock_connection_pool):
    """Test retrieving unprocessed files"""
    # Arrange
    with patch('app.services.database_service.ConnectionPool', return_value=mock_connection_pool):
        db_service = DatabaseService()
        
        # Mock data
        mock_connection_pool.get_connection.return_value.__enter__.return_value.cursor.return_value.fetchall.return_value = [
            (1, "test.pdf", "test.pdf", "/path", "hash", 1024, "application/pdf", "2025-11-02", "uploaded", None, None, None, None, None, None, "upload", None)
        ]
    
    # Act
    files = db_service.get_unprocessed_files()
    
    # Assert
    assert len(files) == 1
    assert files[0]["file_name"] == "test.pdf"
    assert files[0]["status"] == "uploaded"
```

**C# Service - Adding Unit Test**:
```csharp
// Tests/Services/WeatherApiClientTests.cs
using Xunit;
using Moq;
using System.Net.Http;
using System.Threading.Tasks;

public class WeatherApiClientTests
{
    [Fact]
    public async Task GetWeatherAsync_ReturnsForecasts()
    {
        // Arrange
        var mockHttpClient = new HttpClient(new MockHttpMessageHandler());
        var client = new WeatherApiClient(mockHttpClient);
        
        // Act
        var forecasts = await client.GetWeatherAsync(5);
        
        // Assert
        Assert.NotNull(forecasts);
        Assert.Equal(5, forecasts.Length);
    }
}
```

### Adding Integration Tests

**Python - FastAPI Endpoint Test**:
```python
# tests/integration/test_documents_api.py
import pytest
from fastapi.testclient import TestClient
from app.fastapi import app

@pytest.fixture
def client():
    """Create test client"""
    return TestClient(app)

@pytest.fixture
def test_database():
    """Set up test database"""
    # Use in-memory or test database
    os.environ["DATABASE_PATH"] = ":memory:"
    yield
    # Cleanup after test

def test_list_documents_endpoint(client, test_database):
    """Test GET /documents endpoint"""
    # Act
    response = client.get("/documents/")
    
    # Assert
    assert response.status_code == 200
    data = response.json()
    assert isinstance(data, list)

def test_get_document_not_found(client, test_database):
    """Test GET /documents/{id} with non-existent ID"""
    # Act
    response = client.get("/documents/99999")
    
    # Assert
    assert response.status_code == 404
```

**C# - Minimal API Integration Test**:
```csharp
// Tests/Integration/ApiServiceTests.cs
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class ApiServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public ApiServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
    
    [Fact]
    public async Task WeatherForecast_ReturnsSuccessStatusCode()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Act
        var response = await client.GetAsync("/weatherforecast");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("temperatureC", content);
    }
}
```

### Testing Aspire-Orchestrated Services

**Python - Test with Real Neo4j**:
```python
# tests/integration/test_neo4j_integration.py
import pytest
import os
from app.services.neo4j_service import Neo4jService
from app.models.models import Document
from datetime import datetime

@pytest.fixture(scope="module")
def neo4j_service():
    """Use real Neo4j from Aspire for integration tests"""
    # Ensure Aspire is running first
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    service = Neo4jService(uri=uri)
    
    # Verify connection
    if not service.health_check():
        pytest.skip("Neo4j not available - ensure Aspire is running")
    
    yield service
    service.close()

@pytest.fixture(autouse=True)
def cleanup_test_data(neo4j_service):
    """Clean up test data before and after each test"""
    yield
    # Cleanup after test
    with neo4j_service.get_driver().session() as session:
        session.run("MATCH (d:Document {id: 99999}) DETACH DELETE d")

def test_create_and_retrieve_document_node(neo4j_service):
    """Test document node creation and retrieval"""
    # Arrange
    doc = Document(
        id=99999,
        filename="test_integration.pdf",
        original_filename="test.pdf",
        file_path="/test/path.pdf",
        upload_date=datetime.now(),
        processed=False
    )
    
    # Act
    node_id = neo4j_service.create_document_node(doc)
    
    # Assert
    assert node_id is not None
    
    # Verify node exists
    with neo4j_service.get_driver().session() as session:
        result = session.run(
            "MATCH (d:Document {id: $id}) RETURN d.filename as filename",
            {"id": 99999}
        )
        record = result.single()
        assert record is not None
        assert record["filename"] == "test_integration.pdf"
```

### Testing Cross-Service Interactions

**C# Client Calling Python Service**:
```csharp
// Tests/Integration/CrossServiceTests.cs
using Xunit;
using System.Net.Http;
using System.Net.Http.Json;

public class CrossServiceTests
{
    [Fact]
    public async Task PythonService_ReturnsDocuments()
    {
        // Arrange - assumes Aspire is running
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
        
        // Act
        var response = await httpClient.GetAsync("/documents/");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var documents = await response.Content.ReadFromJsonAsync<List<Document>>();
        Assert.NotNull(documents);
    }
    
    [Fact]
    public async Task PythonService_ContractMatches()
    {
        // Test that Python response deserializes to C# model
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8000") };
        
        var response = await httpClient.GetAsync("/documents/health/database");
        response.EnsureSuccessStatusCode();
        
        var health = await response.Content.ReadFromJsonAsync<DatabaseHealth>();
        Assert.NotNull(health);
        Assert.NotNull(health.Status);
    }
}
```

### Updating Tests for Breaking Changes

**Python - Updating Model Test**:
```python
# Before - test expects string datetime
def test_document_serialization_old():
    doc = Document(
        id=1,
        filename="test.pdf",
        upload_date="2025-11-02T14:30:00"  # String
    )
    assert isinstance(doc.upload_date, str)

# After - test expects datetime object
def test_document_serialization_new():
    from datetime import datetime
    doc = Document(
        id=1,
        filename="test.pdf",
        upload_date=datetime(2025, 11, 2, 14, 30, 0)  # datetime
    )
    assert isinstance(doc.upload_date, datetime)
    # Verify serialization format
    json_str = doc.model_dump_json()
    assert "2025-11-02" in json_str
```

## Creation Patterns

### Python Unit Tests

**Service Logic Test**:
```python
# tests/unit/test_neo4j_service.py
import pytest
from unittest.mock import Mock, MagicMock
from app.services.neo4j_service import Neo4jService

@pytest.fixture
def mock_driver():
    """Mock Neo4j driver"""
    driver = Mock()
    session = MagicMock()
    driver.session.return_value.__enter__ = Mock(return_value=session)
    driver.session.return_value.__exit__ = Mock(return_value=False)
    return driver

def test_create_document_node_calls_correct_query(mock_driver):
    """Verify correct Cypher query is executed"""
    # Arrange
    service = Neo4jService()
    service._driver = mock_driver
    
    doc = Document(id=1, filename="test.pdf", upload_date=datetime.now())
    
    # Act
    service.create_document_node(doc)
    
    # Assert
    session = mock_driver.session.return_value.__enter__.return_value
    session.run.assert_called_once()
    call_args = session.run.call_args
    assert "CREATE (d:Document" in call_args[0][0]
```

**Pydantic Model Validation Test**:
```python
# tests/unit/test_models.py
import pytest
from pydantic import ValidationError
from app.models.models import DocumentUploadRequest

def test_document_upload_request_valid():
    """Test valid document upload request"""
    request = DocumentUploadRequest(
        filename="test.pdf",
        content_type="application/pdf",
        size_bytes=1024
    )
    assert request.filename == "test.pdf"

def test_document_upload_request_invalid_size():
    """Test validation for invalid file size"""
    with pytest.raises(ValidationError) as exc_info:
        DocumentUploadRequest(
            filename="test.pdf",
            content_type="application/pdf",
            size_bytes=-1  # Invalid
        )
    assert "size_bytes" in str(exc_info.value)
```

### C# Unit Tests

**Service Logic Test**:
```csharp
// Tests/Services/DocumentProcessorTests.cs
using Xunit;
using Moq;

public class DocumentProcessorTests
{
    [Fact]
    public void ProcessDocument_ValidPdf_ReturnsSuccess()
    {
        // Arrange
        var mockNeo4jClient = new Mock<INeo4jClient>();
        var processor = new DocumentProcessor(mockNeo4jClient.Object);
        var document = new Document { Id = 1, Filename = "test.pdf" };
        
        // Act
        var result = processor.Process(document);
        
        // Assert
        Assert.True(result.Success);
        mockNeo4jClient.Verify(x => x.CreateNode(It.IsAny<Document>()), Times.Once);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ProcessDocument_InvalidFilename_ThrowsException(string filename)
    {
        // Arrange
        var processor = new DocumentProcessor(Mock.Of<INeo4jClient>());
        var document = new Document { Id = 1, Filename = filename };
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => processor.Process(document));
    }
}
```

### Async Testing

**Python Async Test**:
```python
# tests/unit/test_async_operations.py
import pytest
import asyncio
from app.services.embedding_service import EmbeddingService

@pytest.mark.asyncio
async def test_embed_batch_concurrent():
    """Test batch embedding processes concurrently"""
    service = EmbeddingService()
    texts = ["text1", "text2", "text3"]
    
    # Act
    start = asyncio.get_event_loop().time()
    embeddings = await service.embed_batch_async(texts)
    duration = asyncio.get_event_loop().time() - start
    
    # Assert
    assert len(embeddings) == 3
    # Should be faster than sequential (each takes ~0.1s)
    assert duration < 0.25  # Concurrent should be < 0.3s
```

**C# Async Test**:
```csharp
// Tests/Services/AsyncServiceTests.cs
using Xunit;
using System.Threading.Tasks;

public class AsyncServiceTests
{
    [Fact]
    public async Task GetDataAsync_CompletesSuccessfully()
    {
        // Arrange
        var service = new DataService();
        
        // Act
        var result = await service.GetDataAsync();
        
        // Assert
        Assert.NotNull(result);
    }
    
    [Fact]
    public async Task GetDataAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new DataService();
        
        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetDataAsync(cts.Token));
    }
}
```

## Test Data Management

### Python Fixtures
```python
# tests/conftest.py (shared fixtures)
import pytest
from app.services.database_service import DatabaseService

@pytest.fixture(scope="function")
def clean_database():
    """Provide clean database for each test"""
    db = DatabaseService(db_path=":memory:")  # In-memory DB
    yield db
    # Cleanup automatically handled by in-memory

@pytest.fixture
def sample_document():
    """Provide sample document data"""
    from datetime import datetime
    return {
        "id": 1,
        "filename": "test.pdf",
        "upload_date": datetime.now(),
        "processed": False
    }

@pytest.fixture
def sample_documents():
    """Provide multiple sample documents"""
    from datetime import datetime
    return [
        {"id": i, "filename": f"test{i}.pdf", "upload_date": datetime.now()}
        for i in range(1, 6)
    ]
```

### C# Test Fixtures
```csharp
// Tests/Fixtures/DatabaseFixture.cs
using Xunit;

public class DatabaseFixture : IDisposable
{
    public DatabaseService Database { get; private set; }
    
    public DatabaseFixture()
    {
        // Setup in-memory or test database
        Database = new DatabaseService(":memory:");
        Database.Initialize();
    }
    
    public void Dispose()
    {
        Database?.Dispose();
    }
}

// Use in tests
public class DocumentTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    
    public DocumentTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public void TestWithDatabase()
    {
        var doc = _fixture.Database.GetDocument(1);
        Assert.NotNull(doc);
    }
}
```

## E2E Testing (Playwright - Future)

### Blazor UI Test Pattern
```csharp
// Tests/E2E/UploadWorkflowTests.cs (when Playwright is added)
using Microsoft.Playwright;
using Xunit;

public class UploadWorkflowTests : IAsyncLifetime
{
    private IPlaywright _playwright;
    private IBrowser _browser;
    
    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }
    
    [Fact]
    public async Task UploadDocument_CompletesSuccessfully()
    {
        // Arrange
        var page = await _browser.NewPageAsync();
        await page.GotoAsync("http://localhost:5000/upload");
        
        // Act
        await page.SetInputFilesAsync("#fileInput", "test-data/sample.pdf");
        await page.ClickAsync("#uploadButton");
        
        // Assert
        await page.WaitForSelectorAsync(".success-message");
        var successText = await page.TextContentAsync(".success-message");
        Assert.Contains("uploaded successfully", successText);
    }
    
    public async Task DisposeAsync()
    {
        await _browser?.DisposeAsync();
        _playwright?.Dispose();
    }
}
```

## Running Tests

### Python Tests
```bash
# Install test dependencies
pip install pytest pytest-asyncio pytest-cov

# Run all tests
pytest

# Run with coverage
pytest --cov=app --cov-report=html

# Run specific test file
pytest tests/unit/test_database_service.py

# Run specific test
pytest tests/unit/test_database_service.py::test_get_unprocessed_files

# Run integration tests only (requires Aspire running)
pytest tests/integration/

# Run with verbose output
pytest -v
```

### C# Tests
```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test project
dotnet test tests/AspireApp.Tests/

# Run with coverage (requires coverlet)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run specific test
dotnet test --filter "FullyQualifiedName~WeatherApiClientTests"
```

## Test Organization

### Python Structure
```
src/AspireApp.PythonServices/
├── app/
│   ├── services/
│   ├── models/
│   └── routers/
└── tests/
    ├── conftest.py           # Shared fixtures
    ├── unit/
    │   ├── test_database_service.py
    │   ├── test_neo4j_service.py
    │   └── test_models.py
    ├── integration/
    │   ├── test_documents_api.py
    │   └── test_neo4j_integration.py
    └── e2e/
        └── test_full_workflow.py
```

### C# Structure
```
tests/
├── AspireApp.UnitTests/
│   ├── Services/
│   │   └── WeatherApiClientTests.cs
│   └── Models/
│       └── DocumentTests.cs
├── AspireApp.IntegrationTests/
│   ├── ApiServiceTests.cs
│   └── CrossServiceTests.cs
└── AspireApp.E2ETests/
    └── UploadWorkflowTests.cs
```

## Mocking Strategies

### Mocking Neo4j
```python
# Python - Mock Neo4j driver
from unittest.mock import Mock, MagicMock

@pytest.fixture
def mock_neo4j_driver():
    driver = Mock()
    session = MagicMock()
    result = MagicMock()
    result.single.return_value = {"node_id": "test-node-id"}
    session.run.return_value = result
    driver.session.return_value.__enter__ = Mock(return_value=session)
    driver.session.return_value.__exit__ = Mock(return_value=False)
    return driver
```

```csharp
// C# - Mock Neo4j client
var mockNeo4jClient = new Mock<INeo4jClient>();
mockNeo4jClient
    .Setup(x => x.CreateNodeAsync(It.IsAny<Document>()))
    .ReturnsAsync("test-node-id");
```

### Mocking HTTP Clients
```python
# Python - Mock httpx/requests
import httpx
from unittest.mock import AsyncMock

@pytest.fixture
def mock_http_client(monkeypatch):
    async def mock_get(*args, **kwargs):
        return httpx.Response(200, json={"status": "ok"})
    
    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)
```

```csharp
// C# - Mock HttpClient with HttpMessageHandler
public class MockHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        return Task.FromResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"status\":\"ok\"}")
        });
    }
}

var mockHandler = new MockHttpMessageHandler();
var httpClient = new HttpClient(mockHandler);
```

## CI/CD Integration

### GitHub Actions Example
```yaml
name: Tests

on: [push, pull_request]

jobs:
  test-python:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-python@v4
        with:
          python-version: '3.11'
      - run: pip install -r requirements.txt pytest pytest-cov
      - run: pytest --cov=app tests/unit/
      
  test-csharp:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'
      - run: dotnet restore
      - run: dotnet test --no-restore --verbosity normal
```

## Decision Log
- **2025-11-02**: Initial creation with maintenance-first focus
- **2025-11-02**: Documented Aspire-aware testing patterns
- **2025-11-02**: Established Python pytest and C# xUnit patterns
- **Future**: Add Playwright E2E testing when UI test suite is established

## Related Instructions
- `python.instructions.md` - Python testing tools (pytest)
- `csharp.instructions.md` - C# testing frameworks
- `neo4j-integration.instructions.md` - Neo4j integration testing
- `cross-service-contracts.instructions.md` - Contract testing patterns

Update this file when new testing patterns emerge or testing infrastructure changes.
