---
applyTo: '**/requirements.txt,**/*.csproj,**/Dockerfile*,**/global.json'
description: 'Dependency management for NuGet packages, Python dependencies, Docker base images, and SDK versions'
---

# Dependency Management Instructions

## Scope
Apply these patterns when updating dependencies, coordinating breaking changes, or managing security updates across .NET, Python, and Docker components.

## Core Philosophy
- **Coordinate breaking changes**: Update cross-service contracts first, then consuming services
- **Test incrementally**: Update one service's dependencies at a time, validate, then proceed
- **Pin critical versions**: Lock Neo4j, Ollama, and base image versions; allow minor updates for libraries
- **Cache-aware updates**: Changes to `requirements.txt` or `.csproj` files invalidate Docker cache layers

---

## .NET Package Management

### Maintenance: Updating NuGet Packages

**Update a single package:**
```powershell
dotnet add src/AspireApp.Web/AspireApp.Web.csproj package Microsoft.SemanticKernel --version 1.67.0
```

**Update all packages in a project:**
```powershell
cd src/AspireApp.Web
dotnet list package --outdated
dotnet add package <PackageName> --version <NewVersion>
```

**Update Aspire SDK (requires coordination):**
1. Update `Aspire.AppHost.Sdk` version in `AppHost.csproj`
2. Update `Aspire.Hosting.AppHost` package reference
3. Update `Microsoft.Extensions.ServiceDiscovery` in `ServiceDefaults.csproj`
4. Test orchestration: `dotnet run --project src/AspireApp.AppHost`

**Example from codebase:**
```xml
<!-- AppHost.csproj -->
<Sdk Name="Aspire.AppHost.Sdk" Version="9.3.1" />
<PackageReference Include="Aspire.Hosting.AppHost" Version="9.5.1" />
```

### Breaking Changes: Semantic Kernel Update Example

**When Microsoft.SemanticKernel changes API surface:**
1. Check migration guide: `https://github.com/microsoft/semantic-kernel/releases`
2. Update `AspireApp.Web.csproj`:
   ```xml
   <PackageReference Include="Microsoft.SemanticKernel" Version="1.67.0" />
   <PackageReference Include="Microsoft.SemanticKernel.Connectors.Ollama" Version="1.67.0-alpha" />
   ```
3. Update consuming code in `Components/Pages/Chat.razor.cs` or services
4. Test chat flows with Ollama before committing

### SDK Version Management

**Update .NET SDK (global.json):**
```json
{
  "sdk": {
    "version": "10.0.0",
    "rollForward": "latestMinor",
    "allowPrerelease": true
  }
}
```

**Policy:**
- Major SDK updates (9.x → 10.x): Test all projects, update `TargetFramework` in `.csproj` files
- Minor updates (10.0 → 10.1): Usually safe with `rollForward: latestMinor`
- Prerelease: Only enabled during active .NET preview periods (pre-GA)

**Validation steps:**
1. `dotnet --info` (verify SDK installed)
2. `dotnet restore` (check compatibility)
3. `dotnet build` (ensure no breaking API changes)
4. `dotnet run --project src/AspireApp.AppHost` (full orchestration test)

---

## Python Dependency Management

### Maintenance: Updating requirements.txt

**Current structure (requirements.txt):**
```txt
# Core framework (update together)
fastapi
uvicorn
pydantic

# Document processing (CUDA-sensitive)
docling-core
docling-ibm-models
pypdf2
python-docx

# Database
neo4j
```

**Update a single package:**
1. Check compatibility: `pip list --outdated` (in local venv or container)
2. Update `requirements.txt`:
   ```txt
   fastapi==0.115.0  # Pin if breaking change, use unpinned for minors
   ```
3. Rebuild container: `docker build -t aspireapp-python -f src/AspireApp.PythonServices/Dockerfile .`
4. Test health endpoint: `curl http://localhost:8000/health`

**Update Neo4j driver (breaking change potential):**
```txt
neo4j==5.16.0  # Check driver compatibility matrix with Neo4j 5.15.0 container
```
- Neo4j driver 5.x is compatible with Neo4j server 5.x
- Test connection patterns in `app/services/neo4j_service.py` after update
- Verify query patterns still work (async sessions, transactions)

### Docker Layer Optimization

**Dockerfile dependency install order (maintains cache):**
```dockerfile
# requirements.txt changes invalidate this layer and all below
COPY requirements.txt .

# Install small/stable packages first (cached longer)
RUN pip install fastapi uvicorn pydantic

# Install large/volatile packages last (minimize cache invalidation)
RUN pip install docling docling-ibm-models
```

**When adding new dependencies:**
1. Add to `requirements.txt` in logical group (core/processing/database)
2. Consider Dockerfile install order if package is large (>100MB)
3. Update `Dockerfile.lightweight` if package is optional for basic testing

---

## Docker Base Image Management

### Maintenance: Updating Base Images

**Python base image (Dockerfile):**
```dockerfile
FROM python:3.12.7-slim AS base
```

**Update policy:**
- Patch updates (3.12.7 → 3.12.8): Update immediately, low risk
- Minor updates (3.12 → 3.13): Test thoroughly, check breaking changes in Python release notes
- Major updates (3.x → 4.x): Plan migration, expect breaking changes

**Neo4j base image (Neo4jService/Dockerfile):**
```dockerfile
FROM neo4j:5.15.0 AS base
```

**Update policy:**
- Pin exact version (not `5.15` or `latest`) for production predictability
- Minor updates (5.15.0 → 5.15.1): Usually safe, test backup/restore workflows
- Major updates (5.x → 6.x): Check plugin compatibility (APOC, GDS), test Cypher queries

**Plugin version coordination:**
```dockerfile
# APOC must match Neo4j version (5.15.0 → apoc-5.15.0-core.jar)
RUN wget -O /plugins/apoc-5.15.0-core.jar \
    https://github.com/neo4j-contrib/neo4j-apoc-procedures/releases/download/5.15.0/apoc-5.15.0-core.jar
```

### Ollama Container Updates

**Managed by Aspire (AppHost.cs):**
```csharp
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "latest" })
```

**Update policy:**
- Using `latest` tag (acceptable for development, risky for production)
- To pin: Change `Tag = "latest"` to specific version (e.g., `Tag = "0.3.0"`)
- Model compatibility: Test `phi4-mini:latest` pulls correctly after Ollama update

---

## Cross-Service Coordination

### Breaking Change Workflow

**Scenario: Neo4j driver breaking change affects Python service API:**

1. **Update Python dependencies:**
   ```txt
   neo4j==5.17.0  # Has breaking change to session API
   ```

2. **Update Neo4j service code:**
   ```python
   # app/services/neo4j_service.py
   async with self.driver.session(database="neo4j") as session:
       # Update to new API pattern if required
   ```

3. **Test Python service independently:**
   ```powershell
   cd src/AspireApp.PythonServices
   docker build -t test-python .
   docker run -p 8000:8000 test-python
   curl http://localhost:8000/health
   ```

4. **Update contract models if API changes:**
   - Update `app/models/models.py` (Python Pydantic)
   - Update corresponding C# records in `AspireApp.Web/Data/` or `ApiService/`
   - See `cross-service-contracts.instructions.md` for field sync patterns

5. **Test integrated orchestration:**
   ```powershell
   dotnet run --project src/AspireApp.AppHost
   ```
   - Check Aspire dashboard for all services healthy
   - Test document upload → Neo4j storage → retrieval flow

### Security Update Coordination

**When CVE requires immediate update:**

1. **Identify affected components:**
   ```powershell
   # Check .NET packages
   dotnet list package --vulnerable --include-transitive
   
   # Check Python (in venv or container)
   pip list --outdated
   safety check  # If available
   ```

2. **Update priority order:**
   - **Critical**: Base images (Python, Neo4j), direct dependencies with known exploits
   - **High**: Aspire SDK, FastAPI, authentication libraries
   - **Medium**: Logging, telemetry, development tools

3. **Parallel testing strategy:**
   - Update `ServiceDefaults` project first (affects all .NET services)
   - Test API and Web projects independently before orchestration
   - Update Python service last (easiest to rollback via Docker)

4. **Rollback plan:**
   - Git tag before dependency updates: `git tag pre-security-update-20251102`
   - Keep previous Docker images tagged: `docker tag aspireapp-python:latest aspireapp-python:backup`

---

## Dependency Verification

### Pre-Commit Checks

**Before committing dependency changes:**
```powershell
# .NET restore and build
dotnet restore
dotnet build

# Python requirements validation (if local venv exists)
pip install -r src/AspireApp.PythonServices/requirements.txt --dry-run

# Docker build test (validates Dockerfile syntax and layer caching)
docker build -t test-python -f src/AspireApp.PythonServices/Dockerfile .
docker build -t test-neo4j -f src/AspireApp.Neo4JService/Dockerfile .

# Full orchestration test
dotnet run --project src/AspireApp.AppHost
```

**Health check validation:**
- Aspire dashboard: All services show green health status
- Neo4j browser: `http://localhost:7474` accessible
- Python service: `http://localhost:8000/health` returns 200
- Web UI: Document upload and chat features operational

### CI/CD Integration Points

**When updating dependencies in CI:**
- Restore caching strategies may need adjustment (NuGet cache, pip cache, Docker layers)
- Update GitHub Actions if SDK version changes (`actions/setup-dotnet@v4`)
- Bump timeout values if large packages added (docling takes ~5min to install)

---

## Common Scenarios

### Adding a New Python Package

**Steps:**
1. Add to `requirements.txt` in appropriate group:
   ```txt
   # Utilities
   python-json-logger
   httpx  # ← New addition
   ```

2. Update Dockerfile install order if large package:
   ```dockerfile
   # Install core dependencies first
   RUN pip install fastapi uvicorn httpx  # ← Added here
   ```

3. Rebuild and test:
   ```powershell
   docker build -t aspireapp-python -f src/AspireApp.PythonServices/Dockerfile .
   docker run -p 8000:8000 aspireapp-python
   ```

### Adding a New .NET Package to Web Project

**Steps:**
1. Add package reference:
   ```powershell
   dotnet add src/AspireApp.Web/AspireApp.Web.csproj package Blazored.LocalStorage --version 4.5.0
   ```

2. Register service in `Program.cs` if required:
   ```csharp
   builder.Services.AddBlazoredLocalStorage();
   ```

3. Test build and run:
   ```powershell
   dotnet build src/AspireApp.Web
   dotnet run --project src/AspireApp.AppHost
   ```

### Updating Neo4j Major Version (5.x → 6.x)

**Breaking change workflow:**
1. Check Neo4j 6.x release notes for Cypher/driver changes
2. Update `Neo4JService/Dockerfile`:
   ```dockerfile
   FROM neo4j:6.0.0 AS base
   ```

3. Update plugin versions:
   ```dockerfile
   RUN wget -O /plugins/apoc-6.0.0-core.jar \
       https://github.com/neo4j-contrib/neo4j-apoc-procedures/releases/download/6.0.0/apoc-6.0.0-core.jar
   ```

4. Update Python neo4j driver in `requirements.txt`:
   ```txt
   neo4j==5.18.0  # Check compatibility matrix for Neo4j 6.x
   ```

5. Test queries in `app/services/neo4j_service.py`:
   - Connection patterns
   - Transaction API changes
   - Cypher syntax deprecations

6. Backup data before orchestration test:
   ```powershell
   docker exec neo4j-container neo4j-admin database dump neo4j
   ```

---

## Troubleshooting

### Dependency Resolution Failures

**"PackageReference version conflict":**
- Check transitive dependencies: `dotnet list package --include-transitive`
- Pin conflicting package in `Directory.Packages.props` or `.csproj`

**"pip install timeout":**
- Increase timeout in Dockerfile: `ENV PIP_DEFAULT_TIMEOUT=2000`
- Split large installs: Install docling packages separately

**"Docker layer cache miss after minor change":**
- Ensure `requirements.txt` copied before install commands
- Order `COPY` commands from least-to-most frequently changed

### Version Compatibility Issues

**Neo4j driver incompatible with server:**
- Check matrix: `https://neo4j.com/docs/python-manual/current/install/`
- Neo4j Python driver 5.x supports Neo4j 5.x and 4.4

**Aspire SDK version mismatch:**
- `Aspire.AppHost.Sdk` version must match `Aspire.Hosting.AppHost` package
- ServiceDefaults package versions should align with Aspire SDK

---

## Decision Log

- 2025-11-02: Created maintenance-first dependency management guide with examples from AspireApp codebase (AppHost.csproj, requirements.txt, Neo4j/Python Dockerfiles).
