agent: 'agent'
tools: ['run_in_terminal', 'grep_search', 'read_file']
description: 'Troubleshoot Aspire dashboard issues and service orchestration problems'
owner: '@eric-vanartsdalen'
audience: 'Maintainers'
dependencies: ['.NET 9 SDK', 'Docker Desktop']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Diagnosing Aspire AppHost startup failures, service health checks, container orchestration issues, dashboard log analysis.
- **Dependencies**: .NET Aspire SDK, Docker Desktop, access to Aspire Dashboard logs.
- **Sample Inputs**: Error messages from dashboard, service names failing health checks, port conflicts.
- **Related Instructions**: See `../copilot-instructions.md` for quick-start and troubleshooting index; reference `../instructions/dotnet-architecture-good-practices.instructions.md` for service configuration.

# Aspire Dashboard Troubleshooting Guide

Your goal is to help diagnose and resolve issues with .NET Aspire orchestration, focusing on the dashboard and service health.

## Common Issues and Solutions

### Dashboard Won't Start
- Check if `AspireApp.AppHost` is set as startup project in Visual Studio.
- Verify Docker Desktop is running (`docker ps`).
- Ensure ports 7474 (Neo4j), 7687 (Neo4j), 8000 (Python), etc., are not conflicted.
- Run `dotnet restore` and `dotnet build` from repo root.

### Service Health Failures
- Check Aspire Dashboard logs for specific errors.
- Verify container images are built (e.g., Ollama, Neo4j).
- Confirm `appsettings.json` has correct `AI-Endpoint` and `AI-Model`.
- For Python services, check `requirements.txt` and BuildKit enablement.

### Orchestration Problems
- Ensure `AddDockerfile()` and `AddProject<>()` in `AppHost.cs` are correct.
- Validate bind-mounted volumes (`database/`, `data/`) exist.
- Check for SDK version mismatches (`dotnet --info`).

When troubleshooting, gather logs, check configurations, and suggest targeted fixes based on error patterns.

## Example Commands
- `dotnet build AspireApp.sln`
- `dotnet run --project src/AspireApp.AppHost/AspireApp.AppHost.csproj`
- `Get-Process -Id (Get-NetTCPConnection -LocalPort 8000).OwningProcess`
- `docker ps --filter "name=aspire"`