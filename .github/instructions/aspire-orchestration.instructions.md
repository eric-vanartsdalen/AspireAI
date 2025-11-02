---
description: '.NET Aspire orchestration patterns for AppHost configuration'
applyTo: '**/AppHost.cs,**/Program.cs,**/*.AppHost/**'
---

# Aspire Orchestration Guidance

## Scope
- Use this document when working with `src/AspireApp.AppHost/AppHost.cs` or any Aspire orchestration code.
- Covers service registration, resource wiring, health checks, environment configuration, and dependency ordering.
- Complements `dotnet-architecture-good-practices.instructions.md` and `copilot-instructions.md`.

## Core Principles
- **Declarative composition**: Services, containers, and dependencies are declared in `AppHost.cs` using fluent API.
- **Dynamic port assignment**: Let Aspire assign ports except for well-known services (Neo4j 7474/7687).
- **Resource references**: Services reference each other using `WithReference()` or `GetEndpoint()` for cross-service communication.
- **Health monitoring**: All services should expose health check endpoints; Aspire dashboard monitors them automatically.
- **Dependency ordering**: Use `WaitFor()` to ensure services start in correct order.

## Service Registration Patterns

### .NET Projects
```csharp
// Register a .NET project with health check
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

// Register with external endpoints (for ingress)
var webFrontend = builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");
```

**Best Practices**:
- Use descriptive resource names (lowercase, hyphenated: `apiservice`, `webfrontend`)
- Always include health checks for monitoring
- Use `WithExternalHttpEndpoints()` only for publicly accessible services

### Container Services (Dockerfile)
```csharp
// Register a Dockerfile-based service
var pythonServices = builder
    .AddDockerfile("python-service", "../../src/AspireApp.PythonServices/", "Dockerfile")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
    .WithHttpHealthCheck("/health");
```

**Best Practices**:
- Path is relative to AppHost project directory
- Use `WithHttpEndpoint()` to expose ports with descriptive names
- Support configurable Dockerfiles via configuration (e.g., `Dockerfile.lightweight`)
- Always tag endpoints with meaningful names (`http`, `bolt`, `grpc`)

### External Container Images
```csharp
// Add container from registry
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "latest" })
    .WithDataVolume()
    .WithContainerRuntimeArgs("--gpus", "all");
```

**Best Practices**:
- Use official Aspire extensions when available (`AddOllama()`, `AddRedis()`)
- Specify explicit tags instead of `latest` for production stability
- Use `WithDataVolume()` for data persistence across container restarts

## Configuration & Parameters

### Configuration Values
```csharp
// Read from appsettings.json
var modelName = builder.Configuration["AI-Model"] ?? "phi4-mini:latest";
var useLightweightBuild = builder.Configuration["USE_LIGHTWEIGHT_PYTHON"] ?? "false";
```

### Aspire Parameters
```csharp
// Parameters with defaults from configuration
var aiModel = builder.AddParameterFromConfiguration("AI-Model", "AI-Model");
var aiEndpoint = builder.AddParameterFromConfiguration("AI-Endpoint", "AI-Endpoint");

// Secret parameters (prompt at runtime if not in config)
var neo4jUser = builder.AddParameter("neo4j-user", "neo4j");
var neo4jPass = builder.AddParameter("neo4j-pass", secret: true);
```

**Best Practices**:
- Use `AddParameter()` for values that change per environment
- Mark sensitive values with `secret: true` to prevent logging
- Retrieve async parameter values with `await param.Resource.GetValueAsync(CancellationToken.None)`
- Never hardcode credentials; always use parameters or configuration

### Passing Configuration to Services
```csharp
// Pass parameters as environment variables
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WithEnvironment("AI-Endpoint", aiEndpoint.Resource)
    .WithEnvironment("AI-Model", aiModel.Resource);

// Pass computed values
.WithEnvironment("NEO4J_AUTH", $"{neo4jUserValue}/{neo4jPassValue}");
```

## Volume Management

### Named Volumes (Persistent)
```csharp
// Docker-managed volumes for persistence
.WithVolume("neo4j-data", "/data")
.WithVolume("python-pip-cache", "/root/.cache/pip")
```

**Use Cases**: Database data, caches, logs that must persist across container restarts

### Bind Mounts (Host Access)
```csharp
// Mount host directory into container
.WithBindMount("../../data", "/app/data")
.WithBindMount("../../database/neo4j/backup", "/backup")
```

**Use Cases**: Sharing data between host and container, debugging, local development

**Best Practices**:
- Use volumes for persistence; bind mounts for local access
- Paths are relative to AppHost project directory
- Document mount purposes in comments
- Create host directories in advance to avoid permission issues

## Service Dependencies

### Resource References
```csharp
// Reference another service (injects connection info)
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WithReference(apiService)
    .WithReference(ollama);
```

**Effect**: Automatically injects service discovery variables into the dependent service

### Endpoint References
```csharp
// Get specific endpoint URL
.WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
.WithEnvironment("PYTHON_SERVICE_URL", pythonServices.GetEndpoint("http"))
```

**Use Cases**: When you need explicit endpoint URLs rather than automatic service discovery

### Dependency Ordering
```csharp
// Ensure services start in correct order
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WaitFor(ollama)
    .WaitFor(apiService)
    .WaitFor(neo4jDb)
    .WaitFor(pythonServices);
```

**Best Practices**:
- Chain `WaitFor()` calls for all critical dependencies
- Database services should be waited for by application services
- AI models should be waited for by services that query them
- Don't create circular dependencies

## Health Checks

### HTTP Health Checks
```csharp
// Simple health check on root
.WithHttpHealthCheck("/")

// Health check on specific endpoint
.WithHttpHealthCheck("/health")
```

**Implementation Requirements**:
- Service must expose the specified endpoint
- Endpoint should return 200 OK when healthy
- Keep health checks lightweight (no expensive operations)
- Include dependency checks (database connections, etc.)

### Health Check Implementation Example (C#)
```csharp
app.MapHealthChecks("/health");
```

### Health Check Implementation Example (Python)
```python
@app.get("/health")
async def health():
    return {"status": "healthy"}
```

## Container Optimization

### BuildKit & Caching
```csharp
.WithEnvironment("DOCKER_BUILDKIT", "1")  // Enable BuildKit
.WithVolume("python-pip-cache", "/root/.cache/pip")  // Cache dependencies
.WithEnvironment("PIP_CACHE_DIR", "/root/.cache/pip")
```

**Best Practices**:
- Always enable BuildKit for faster builds
- Cache package managers (pip, npm, NuGet) with volumes
- Use multi-stage Dockerfiles to minimize image size
- Support lightweight build variants via configuration

### Container Runtime Arguments
```csharp
.WithContainerRuntimeArgs("--gpus", "all")  // Enable GPU access
.WithContainerRuntimeArgs("--memory", "4g")  // Limit memory
```

**Use Cases**: GPU access for AI models, resource limits, special capabilities

## Timeout Configuration

### Global Timeouts (appsettings.json)
```json
{
  "Aspire": {
    "Dcp": {
      "ContainerStartTimeout": "00:10:00",
      "ImagePullTimeout": "00:15:00"
    }
  }
}
```

**Best Practices**:
- Increase timeouts for large images (Ollama, Neo4j)
- Set reasonable defaults for CI/CD environments
- Document timeout rationale in comments

## Common Patterns

### Database Service Pattern
```csharp
var db = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/", "Dockerfile")
    .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")
    .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt")
    .WithVolume("neo4j-data", "/data")
    .WithBindMount("../../database/neo4j/backup", "/backup")
    .WithEnvironment("NEO4J_AUTH", $"{user}/{pass}")
    .WithHttpHealthCheck("/");
```

### AI Model Service Pattern
```csharp
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "latest" })
    .WithDataVolume()
    .WithContainerRuntimeArgs("--gpus", "all");
    
var model = ollama.AddModel("chat", modelName);
```

### Python Worker Pattern
```csharp
var worker = builder.AddDockerfile("python-service", "../../src/AspireApp.PythonServices/", "Dockerfile")
    .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
    .WithBindMount("../../data", "/app/data")
    .WithVolume("aspire-database", "/app/database")
    .WithVolume("python-pip-cache", "/root/.cache/pip")
    .WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
    .WithEnvironment("PIP_CACHE_DIR", "/root/.cache/pip")
    .WithEnvironment("DOCKER_BUILDKIT", "1")
    .WithHttpHealthCheck("/health")
    .WaitFor(neo4jDb);
```

## Troubleshooting

### Service Won't Start
- Check Aspire dashboard logs for specific errors
- Verify Docker Desktop is running
- Ensure required ports are not in use
- Check health check endpoint is accessible
- Verify `WaitFor()` dependencies are satisfied

### Configuration Not Applied
- Confirm parameter names match between AppHost and appsettings.json
- Check environment variables are passed with `.WithEnvironment()`
- Verify async parameter retrieval with `GetValueAsync()`
- Ensure configuration is not cached incorrectly

### Volume/Mount Issues
- Verify host paths exist and are accessible
- Check path is relative to AppHost project directory
- Ensure Docker has permission to access host directories
- Use absolute paths if relative paths fail

### Port Conflicts
- Let Aspire assign ports dynamically unless required
- Check for services using well-known ports (7474, 7687, 8000)
- Use `netstat` or `Get-NetTCPConnection` to identify conflicts
- Update fixed ports in `WithHttpEndpoint()` or `WithEndpoint()`

## Maintenance & Evolution

### Adding a New Service

**Steps**:
1. Create the service project or Dockerfile
2. Register in AppHost.cs with appropriate method (`AddProject<>()`, `AddDockerfile()`, etc.)
3. Add health check endpoint (`WithHttpHealthCheck()`)
4. Configure environment variables and volumes
5. Establish dependencies with `WaitFor()`
6. Reference from dependent services with `WithReference()`
7. Test startup order and health checks in dashboard

**Example - Adding a Redis Cache**:
```csharp
var redis = builder.AddRedis("cache")
    .WithDataVolume()
    .WithEndpoint(port: 6379, targetPort: 6379, name: "tcp");

// Update dependent services
builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    .WithReference(redis)  // Add this
    .WithHttpHealthCheck("/health");
```

### Updating Service Configuration

**When to Update AppHost.cs**:
- Adding new environment variables
- Changing volume mounts or paths
- Adding/removing service dependencies
- Updating health check endpoints
- Modifying container runtime arguments

**Example - Adding Environment Variable**:
```csharp
// Before
var pythonServices = builder.AddDockerfile("python-service", "../../src/AspireApp.PythonServices/", "Dockerfile")
    .WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"));

// After - adding new feature flag
var pythonServices = builder.AddDockerfile("python-service", "../../src/AspireApp.PythonServices/", "Dockerfile")
    .WithEnvironment("NEO4J_URI", neo4jDb.GetEndpoint("bolt"))
    .WithEnvironment("ENABLE_VECTOR_SEARCH", builder.Configuration["EnableVectorSearch"] ?? "false");
```

**Best Practices**:
- Add configuration values to `appsettings.json` first
- Use `AddParameter()` or `AddParameterFromConfiguration()` for values that vary per environment
- Test configuration changes locally before committing
- Document new environment variables in service README

### Changing Service Dependencies

**Adding a Dependency**:
```csharp
// Service now depends on Redis
builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    .WithReference(redis)        // Add reference
    .WithHttpHealthCheck("/health")
    .WaitFor(redis);             // Add wait dependency
```

**Removing a Dependency**:
```csharp
// Service no longer uses Ollama directly
builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    // .WithReference(ollama)    // Remove this line
    .WithHttpHealthCheck("/health");
    // .WaitFor(ollama);         // Remove this line
```

**Changing Dependency Order**:
```csharp
// Ensure correct startup sequence
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WaitFor(neo4jDb)           // Database first
    .WaitFor(pythonServices)    // Then Python (depends on Neo4j)
    .WaitFor(ollama)            // Then AI model
    .WaitFor(apiService);       // Then API (depends on all)
```

### Updating Container Images

**Updating Base Image Tags**:
```csharp
// Before
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "latest" });

// After - pinning to specific version
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "0.1.47" });
```

**Switching Dockerfile Variants**:
```csharp
// Support multiple build configurations
var pythonDockerfile = builder.Configuration["USE_LIGHTWEIGHT_PYTHON"] == "true" 
    ? "Dockerfile.lightweight" 
    : "Dockerfile";

var pythonServices = builder.AddDockerfile("python-service", 
    "../../src/AspireApp.PythonServices/", 
    pythonDockerfile);
```

**Best Practices**:
- Pin production images to specific tags, not `latest`
- Test image updates in development before production
- Document breaking changes in service README
- Consider backward compatibility when updating

### Modifying Volume Mounts

**Adding a New Volume**:
```csharp
// Before
var neo4jDb = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/", "Dockerfile")
    .WithVolume("neo4j-data", "/data")
    .WithVolume("neo4j-logs", "/logs");

// After - adding plugin cache
var neo4jDb = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/", "Dockerfile")
    .WithVolume("neo4j-data", "/data")
    .WithVolume("neo4j-logs", "/logs")
    .WithVolume("neo4j-plugins", "/plugins");  // New volume
```

**Changing Volume Type (Volume → Bind Mount)**:
```csharp
// Before - Docker volume (not accessible from host)
.WithVolume("aspire-database", "/app/database")

// After - Bind mount (accessible from host for debugging)
.WithBindMount("../../database", "/app/database")
```

**Migration Considerations**:
- Existing data in volumes must be migrated manually
- Use `docker volume inspect` to locate volume data
- Copy data before switching types
- Document migration steps for team members

### Updating Health Checks

**Changing Health Check Endpoint**:
```csharp
// Before
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    .WithHttpHealthCheck("/");

// After - more specific health endpoint
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");
```

**Requirement**: Update the service to expose the new endpoint before deploying

**Adding Health Check to Existing Service**:
```csharp
// Before - no health check (not recommended)
var worker = builder.AddDockerfile("worker", "../../src/Worker/", "Dockerfile");

// After - adding health monitoring
var worker = builder.AddDockerfile("worker", "../../src/Worker/", "Dockerfile")
    .WithHttpHealthCheck("/health");
```

### Port Management Changes

**Changing Fixed Ports**:
```csharp
// Before - port conflict discovered
.WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")

// After - moving to different port
.WithHttpEndpoint(port: 8001, targetPort: 8000, name: "http")
```

**Moving to Dynamic Port Assignment**:
```csharp
// Before - hardcoded port
.WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")

// After - let Aspire assign (recommended)
.WithHttpEndpoint(targetPort: 8000, name: "http")
```

**Impact**: Update any hardcoded URLs in other services to use `GetEndpoint()` instead

### Removing a Service

**Steps**:
1. Remove all `WithReference()` calls pointing to the service
2. Remove all `WaitFor()` calls for the service
3. Remove any `GetEndpoint()` calls referencing the service
4. Remove the service registration itself
5. Clean up environment variables that reference the service
6. Remove associated volumes if no longer needed
7. Test that dependent services handle absence gracefully

**Example - Removing Legacy Service**:
```csharp
// Remove these lines
// var legacyDb = builder.AddDockerfile("legacy-db", "../../src/LegacyDb/", "Dockerfile");

// Update dependent services - remove references
builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
    // .WithReference(legacyDb)    // Remove
    // .WaitFor(legacyDb)          // Remove
    .WithHttpHealthCheck("/health");
```

### Configuration Parameter Updates

**Adding New Parameters**:
```csharp
// 1. Add to appsettings.json
// "VectorSearchEnabled": "true"

// 2. Register parameter in AppHost.cs
var vectorSearchEnabled = builder.AddParameterFromConfiguration("VectorSearchEnabled", "VectorSearchEnabled");

// 3. Pass to services
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
    .WithEnvironment("VectorSearchEnabled", vectorSearchEnabled.Resource);
```

**Renaming Parameters** (requires coordination):
```csharp
// Step 1: Add new parameter, keep old one temporarily
var newModelParam = builder.AddParameterFromConfiguration("AI-ModelName", "AI-ModelName");
var oldModelParam = builder.AddParameterFromConfiguration("AI-Model", "AI-Model");  // Deprecated

// Step 2: Update services to use new parameter
.WithEnvironment("AI-ModelName", newModelParam.Resource)
// .WithEnvironment("AI-Model", oldModelParam.Resource)  // Keep for backward compat

// Step 3: After all services updated, remove old parameter
```

### Handling Breaking Changes

**Version-Gated Changes**:
```csharp
var useNewPattern = builder.Configuration["UseNewArchitecture"] == "true";

if (useNewPattern) {
    // New pattern
    var neo4j = builder.AddContainer("graph-db", "neo4j", "5.0")
        .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt");
} else {
    // Legacy pattern
    var neo4j = builder.AddDockerfile("graph-db", "../../src/AspireApp.Neo4jService/", "Dockerfile")
        .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt");
}
```

**Gradual Migration Strategy**:
1. Add feature flag to configuration
2. Support both old and new patterns simultaneously
3. Test new pattern thoroughly in development
4. Flip feature flag in staging
5. Remove old pattern after validation

### Testing Changes

**Before Committing**:
- Run `dotnet restore` and `dotnet build` from repo root
- Start Aspire with `dotnet run --project src/AspireApp.AppHost`
- Verify all services show green in dashboard
- Check health endpoints return 200 OK
- Test affected features end-to-end
- Review dashboard logs for warnings/errors

**Validation Checklist**:
- [ ] All services start successfully
- [ ] Health checks pass
- [ ] Dependent services can communicate
- [ ] Configuration values propagate correctly
- [ ] Volumes/mounts are accessible
- [ ] No port conflicts
- [ ] Feature functionality works as expected

## Decision Log
- **2025-11-02**: Initial creation based on current AppHost.cs patterns
- **2025-11-02**: Documented volume vs bind mount strategies
- **2025-11-02**: Added health check implementation examples
- **2025-11-02**: Included timeout configuration guidance
- **2025-11-02**: Added comprehensive maintenance and evolution patterns

## Related Instructions
- `dotnet-architecture-good-practices.instructions.md` - Architecture patterns
- `csharp.instructions.md` - C# coding standards
- `python.instructions.md` - Python service patterns
- Prompts: `aspire-dashboard-troubleshooting.prompt.md`

Update this file when new Aspire patterns emerge or orchestration requirements change.
