# System Patterns

Last reviewed: 2025-11-02

## 2025-11-02

### System Architecture
AspireAI follows a microservices architecture orchestrated by .NET Aspire AppHost:

- **AppHost**: Central orchestrator defining service relationships and health checks
- **Blazor Web UI**: Frontend service handling user interactions
- **Python FastAPI**: Backend service for document processing and AI workflows
- **Ollama**: AI model serving container
- **Neo4j**: Graph database for knowledge representation

### Key Technical Decisions
- **Orchestration**: .NET Aspire over Docker Compose for .NET-native service management
- **Communication**: REST APIs between services with health check endpoints
- **Data Sharing**: Bind-mounted volumes (`database/`, `data/`) for cross-service persistence
- **AI Integration**: Local Ollama serving for privacy and offline capability
- **Frontend**: Blazor Server for real-time updates and .NET ecosystem consistency

### Design Patterns Used
- **Repository Pattern**: Data access abstraction in .NET services
- **Mediator Pattern**: Request/response handling in API layers
- **Observer Pattern**: Event-driven updates in UI components
- **Factory Pattern**: Service instantiation in AppHost
- **Async/Await Pattern**: Non-blocking I/O operations throughout

### Component Relationships
```
AppHost
├── Blazor Web UI (Port: auto-assigned)
├── Python FastAPI (Port: 8000)
├── Ollama (Port: auto-assigned)
└── Neo4j (Ports: 7474, 7687)
```

- **Data Flow**: UI → API → Python processing → Neo4j storage → AI responses
- **Health Checks**: AppHost monitors all services via HTTP endpoints
- **Volume Mounting**: Shared `data/` for uploads, `database/` for persistence

### Architectural Principles
- **Separation of Concerns**: Clear boundaries between UI, API, processing, and storage
- **Dependency Injection**: Service lifetime management via .NET DI container
- **Configuration Management**: Environment-specific settings via `appsettings.json`
- **Error Handling**: Centralized exception handling with user-friendly messages
- **Scalability**: Async operations and containerization for horizontal scaling