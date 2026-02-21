# SKILL: Aspire Architecture Review

## When to Use
When performing a comprehensive architecture review of an Aspire-orchestrated solution with cross-service dependencies (C# + Python + containers).

## Pattern

### 1. Map the Solution Topology
- Read `.sln` to identify all projects and solution folders
- Read `global.json` for SDK constraints
- Read every `.csproj` for target framework, package refs, and project refs
- Map the dependency graph: which projects reference which

### 2. Analyze Orchestration (AppHost.cs)
Key things to check:
- **Service registration:** Are all services properly registered with health checks?
- **Dependency ordering:** Do `WaitFor()` chains match actual startup dependencies?
- **Volume mounts:** Are bind mounts vs named volumes used appropriately?
- **Environment variables:** Are secrets parameterized? Any hardcoded credentials?
- **Port management:** Fixed vs dynamic ports, any conflicts?
- **Container images:** Pinned tags vs `latest`?

### 3. Cross-Service Contract Audit
For each service boundary:
- Compare C# DTOs with Python Pydantic models (field names, types, defaults)
- Compare method signatures between routers and services (do calls match implementations?)
- Check status/enum value alignment (casing, allowed values)
- Verify shared database schema matches both ORM configurations

### 4. Roadmap vs Reality Assessment
- Read each milestone gate and check if the code supports it
- Classify items as: ✅ Done, 🔧 Fixable (< 1 day), ⏳ Requires Work, 🚫 Aspirational
- Identify the critical path to the next deliverable

### 5. Output Structure
Always deliver:
1. Architecture overview (topology diagram)
2. Strengths (what's working well)
3. Gaps and risks (categorized by severity: CRITICAL / HIGH / MEDIUM / LOW)
4. Roadmap assessment table
5. Top 5 strategic recommendations (actionable, time-estimated)

## Anti-Patterns to Catch
- Routers calling methods that don't exist on the service class
- Status/enum value mismatches between services
- Legacy entities coexisting with canonical ones
- Containers wired in AppHost but not consumed by any code
- Shared database files accessed by multiple services without WAL mode
- Documentation referencing stale SDK versions or removed features
