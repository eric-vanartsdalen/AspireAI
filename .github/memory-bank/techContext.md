# Tech Context

Last reviewed: 2025-11-02

## 2025-11-02

### Technologies Used
- **Frontend**: Blazor Server (.NET 9/10), C# 12+, HTML/CSS/JavaScript
- **Backend**: Python FastAPI 3.12, Docker containerization
- **Database**: Neo4j graph database (containerized)
- **AI**: Ollama for local LLM serving
- **Orchestration**: .NET Aspire AppHost
- **Build Tools**: .NET CLI, Docker, PowerShell/bash scripts

### Development Setup
- **IDE**: Visual Studio 2022+ or VS Code with C# extensions
- **SDK Requirements**: .NET 9 or 10 SDK
- **Container Runtime**: Docker Desktop or Docker Engine
- **Python Environment**: Python 3.12+ for local FastAPI development
- **Git Workflow**: Feature branches with PR reviews

### Technical Constraints
- **Local Development Focus**: No cloud deployment configurations included
- **Container Dependencies**: Requires Docker for Python, Ollama, and Neo4j services
- **Network Ports**: Specific ports required (8000 for Python, 7474/7687 for Neo4j)
- **Resource Requirements**: Sufficient RAM for concurrent containers
- **Platform Limitations**: Windows/Mac/Linux support via Docker compatibility

### Dependencies
- **NuGet Packages**: Aspire hosting, Blazor components, EF Core (future)
- **Python Packages**: FastAPI, Uvicorn, document processing libraries
- **Docker Images**: Official Python, Neo4j, Ollama images
- **External Services**: None (all containerized locally)

### Development Workflow
1. Clone repository
2. `dotnet restore` from root
3. Optional: `pip install -r src/AspireApp.PythonServices/requirements.txt`
4. Set `AspireApp.AppHost` as startup project
5. F5 or `dotnet run --project src/AspireApp.AppHost`
6. Access dashboard and web UI at provided URLs

### Testing Strategy
- **Unit Tests**: xUnit for .NET components
- **Integration Tests**: Aspire orchestration validation
- **UI Tests**: Playwright for Blazor interactions (planned)
- **API Tests**: pytest for Python endpoints (planned)

### Performance Considerations
- **Async Operations**: All I/O operations use async/await
- **Container Resources**: Appropriate memory allocation for AI models
- **Database Optimization**: Neo4j indexing and query optimization
- **Caching**: In-memory caching for frequently accessed data