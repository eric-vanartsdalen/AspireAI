```prompt
agent: 'agent'
tools: ['run_in_terminal', 'read_file', 'grep_search', 'replace_string_in_file']
description: 'Coordinate dependency updates across NuGet, pip, and Docker with breaking change management'
owner: '@eric-vanartsdalen'
audience: 'Maintainers'
dependencies: ['.NET 10 SDK', 'Python 3.12', 'Docker Desktop']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Updating NuGet packages, Python dependencies, Docker base images; coordinating breaking changes; security updates.
- **Dependencies**: .NET SDK, Python, Docker, access to package managers.
- **Sample Inputs**: Package names to update, target versions, breaking change migration guides.
- **Related Instructions**: See `../instructions/dependency-management.instructions.md` for comprehensive update patterns; reference `../instructions/cross-service-contracts.instructions.md` for coordinating cross-service changes; see `../instructions/testing.instructions.md` for validation strategies.

# Dependency Update Workflow

Your goal is to safely coordinate dependency updates across .NET, Python, and Docker components with proper testing and rollback plans.

## Pre-Update Assessment

### Identify Affected Components
1. **Check for outdated packages:**
   ```powershell
   # .NET packages
   dotnet list package --outdated --include-transitive
   
   # Python packages (in container or venv)
   pip list --outdated
   ```

2. **Review release notes:**
   - Check for breaking changes in package changelogs
   - Identify migration guides or API changes
   - Note minimum version requirements

3. **Assess impact scope:**
   - Single service update (low risk)
   - Cross-service contract change (medium risk)
   - SDK or base image change (high risk)

## Update Workflow by Type

### .NET Package Updates

**Single package (maintenance):**
1. Update `.csproj` file with new version
2. Run `dotnet restore` and `dotnet build`
3. Test affected service independently
4. Run Aspire orchestration test

**Breaking change (e.g., Semantic Kernel API change):**
1. Read migration guide for API surface changes
2. Update package reference in `.csproj`
3. Update consuming code (services, components)
4. Run targeted tests before full orchestration
5. Update related documentation

**Aspire SDK update:**
1. Update `Aspire.AppHost.Sdk` version in `AppHost.csproj`
2. Update `Aspire.Hosting.AppHost` package version
3. Update `ServiceDefaults` project Aspire packages
4. Test orchestration startup and health checks
5. Validate dashboard functionality

### Python Dependency Updates

**Single package:**
1. Update version in `requirements.txt` (pin if breaking change)
2. Rebuild Docker image: `docker build -t test-python -f src/AspireApp.PythonServices/Dockerfile .`
3. Test health endpoint: `curl http://localhost:8000/health`
4. Validate affected features

**Neo4j driver update (potential breaking change):**
1. Check driver compatibility matrix with Neo4j server version
2. Update `requirements.txt`: `neo4j==5.x.x`
3. Review `app/services/neo4j_service.py` for API changes
4. Test connection patterns (sessions, transactions, async)
5. Run integration tests with Neo4j container

**Large package (docling, CUDA dependencies):**
1. Update version in `requirements.txt`
2. Consider Dockerfile install order (install separately for cache)
3. Increase timeout if needed: `ENV PIP_DEFAULT_TIMEOUT=2000`
4. Test document processing workflows

### Docker Base Image Updates

**Python base image:**
1. Update `FROM python:3.x.x-slim` in `Dockerfile`
2. For minor updates (3.12.7 → 3.12.8): Low risk, rebuild and test
3. For major updates (3.12 → 3.13): Review Python release notes, test thoroughly
4. Rebuild all layers: `docker build --no-cache`

**Neo4j base image:**
1. Update `FROM neo4j:5.x.x` in `Neo4jService/Dockerfile`
2. Coordinate plugin versions (APOC, GDS must match Neo4j version)
3. Update plugin download URLs in Dockerfile
4. Test backup/restore workflows
5. Validate Cypher queries still work

## Cross-Service Coordination

**When updates require contract changes:**
1. Update Python Pydantic models first
2. Update corresponding C# records/DTOs
3. Test contract serialization (see `cross-service-contracts.instructions.md`)
4. Update API endpoints if signatures changed
5. Run integration tests across services

**Example breaking change flow:**
```
1. Neo4j driver 5.17.0 changes session API
2. Update requirements.txt: neo4j==5.17.0
3. Update neo4j_service.py with new session patterns
4. Test Python service health
5. If API response changes, update Python models
6. Update C# DTOs to match
7. Test document upload → Neo4j → retrieval flow
8. Run full Aspire orchestration test
```

## Security Updates

**Immediate CVE response:**
1. Identify vulnerable component: `dotnet list package --vulnerable`
2. Check severity and exploit availability
3. Update priority: Critical → High → Medium
4. Apply update using appropriate workflow above
5. Test critical paths only (fast validation)
6. Deploy and monitor

**Proactive security updates:**
1. Schedule monthly dependency review
2. Update all outdated packages in test branch
3. Run full test suite
4. Document any behavioral changes
5. Merge after validation

## Validation Strategy

**Per-service testing:**
```powershell
# Test API service
dotnet test src/AspireApp.ApiService.Tests  # If tests exist
dotnet run --project src/AspireApp.ApiService

# Test Web UI
dotnet run --project src/AspireApp.Web

# Test Python service
docker run -p 8000:8000 aspireapp-python
curl http://localhost:8000/health
```

**Orchestration testing:**
```powershell
dotnet run --project src/AspireApp.AppHost
# Check Aspire dashboard - all services green
# Test document upload feature
# Test chat with Ollama
# Verify Neo4j data persistence
```

**Regression checks:**
- Document upload and processing works
- Neo4j queries return expected results
- Chat functionality with Ollama operational
- Health checks all passing

## Rollback Plan

**Before updates:**
1. Create git tag: `git tag pre-dep-update-$(Get-Date -Format yyyyMMdd)`
2. Tag Docker images: `docker tag aspireapp-python:latest aspireapp-python:backup`
3. Note current versions in changelog

**If rollback needed:**
1. Revert git changes: `git reset --hard <tag>`
2. Restore Docker images: `docker tag aspireapp-python:backup aspireapp-python:latest`
3. Rebuild if necessary: `dotnet build && docker-compose up`

## Post-Update Tasks

**Documentation:**
1. Update `dependency-management.instructions.md` if new patterns emerged
2. Note breaking changes in commit message
3. Update README if setup steps changed

**Monitoring:**
1. Watch Aspire dashboard for degraded health
2. Check logs for new warnings/errors
3. Monitor performance metrics (if available)

## Common Issues

**"Package version conflict":**
- Check transitive dependencies: `dotnet list package --include-transitive`
- Pin conflicting version explicitly in `.csproj`

**"Docker cache invalidation":**
- Ensure `requirements.txt` copied before install
- Order Dockerfile commands from least-to-most frequently changed

**"Neo4j plugin incompatible":**
- APOC/GDS versions must match Neo4j major.minor version
- Update plugin download URLs when updating Neo4j base image

## Example Scenarios

**Update Microsoft.SemanticKernel (breaking change):**
```powershell
# 1. Check migration guide
curl https://github.com/microsoft/semantic-kernel/releases/latest

# 2. Update Web project
dotnet add src/AspireApp.Web/AspireApp.Web.csproj package Microsoft.SemanticKernel --version 1.67.0
dotnet add src/AspireApp.Web/AspireApp.Web.csproj package Microsoft.SemanticKernel.Connectors.Ollama --version 1.67.0-alpha

# 3. Update consuming code (Chat.razor.cs, services)
# 4. Test chat flows
dotnet run --project src/AspireApp.AppHost
```

**Security update for Python dependency:**
```powershell
# 1. Identify vulnerability
safety check --file src/AspireApp.PythonServices/requirements.txt

# 2. Update requirements.txt
# fastapi==0.115.1  # CVE-XXXX-XXXXX fixed

# 3. Rebuild and test
docker build -t aspireapp-python -f src/AspireApp.PythonServices/Dockerfile .
docker run -p 8000:8000 aspireapp-python
curl http://localhost:8000/health

# 4. Full orchestration test
dotnet run --project src/AspireApp.AppHost
```

When coordinating updates, always prioritize safety over speed, test incrementally, and maintain rollback capability.
```
