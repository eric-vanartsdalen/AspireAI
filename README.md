# AspireAI

A modular, Blazor-based chat assistant platform that ingests documents, builds knowledge graphs, and delivers retrieval-augmented answers with source citations � all orchestrated through .NET Aspire.

> **Disclaimer:** This is an experimental learning project. Expect rough edges and areas to improve. Use at your own risk!

## What It Does

1. **Chat** � Conversational UI powered by local LLMs (Ollama) via Semantic Kernel, with speech-to-text and text-to-speech support.
2. **Ingest** � Upload documentation - a Python/Docling pipeline extracts page-level content and persists output.
3. **Retrieve** � LightRAG + Neo4j turn extracted content into a queryable knowledge graph, surfacing cited answers in chat.

## Technology Stack

| Layer | Technology |
|-------|------------|
| Orchestration | .NET Aspire |
| Web UI | Blazor (.NET 10) |
| AI Runtime | Ollama (local containerized LLMs) accessed via Semantic Kernel |
| Document Processing | Python FastAPI + Docling |
| Knowledge Graph | LightRAG (containerized) with Neo4j (containerized) |
| Relational Storage | SQLite (EF Core) |
| Containerization | Docker |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) � verify with `dotnet --version`
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) � required for Neo4j, Ollama, and Python containers
- [Aspire Tooling](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling) � workload install via `dotnet workload install aspire`
- Python 3.11+ *(optional, for local Python development outside containers)*

## Getting Started

```bash
# 1. Clone and restore
git clone https://github.com/eric-vanartsdalen/AspireAI.git
cd AspireAI
dotnet restore

# 2. Build
dotnet build

# 3. Run (launches Aspire dashboard + all services)
dotnet run --project src/AspireApp.AppHost
```

The Aspire dashboard opens automatically. All services � Web UI, Python processing, Neo4j, Ollama � start and register health checks there.

> **Startup project must be `AspireApp.AppHost`.** If the app 404s or services are missing, right-click `AspireApp.AppHost` in Solution Explorer ? *Set as Startup Project*.

## Project Layout

| Path | Purpose |
|------|---------|
| `src/AspireApp.AppHost/` | Aspire orchestration � wires all services, containers, and config |
| `src/AspireApp.Web/` | Blazor UI � chat, file upload, speech I/O |
| `src/AspireApp.ApiService/` | Minimal API (placeholder for future gateway) |
| `src/AspireApp.PythonServices/` | FastAPI � document processing, RAG retrieval, Neo4j integration |
| `src/AspireApp.Neo4JService/` | Neo4j Docker build context and config |
| `src/AspireApp.ServiceDefaults/` | Shared .NET service configuration and health checks |
| `data/` | Bind-mounted volume for uploaded documents |
| `database/` | SQLite and Neo4j storage volumes |

## Documentation

| Document | Contents |
|----------|----------|
| [Architecture](roadmap/Architecture.md) | System design, component map, data schemas, design principles |
| [Plan](plan.md) | Epics and phased roadmap from foundation through advanced features |
| [Tasks](roadmap/Tasks.md) | Active work breakdown � stabilization track, checklists, gate criteria |

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Wrong startup project / 404 errors | Set `AspireApp.AppHost` as startup project |
| Containers not starting | Start Docker Desktop; re-run AppHost |
| Ollama offline | Check container health in dashboard; verify `AI-Endpoint` / `AI-Model` in appsettings |
| Neo4j / Python errors | Check dashboard logs; ensure ports 7474, 7687, 8000 are free |
| SDK mismatch | `dotnet --info` must show .NET 10.0; install matching SDK from `global.json` |

## Local Microsoft Sign-In

The web app auto-detects live Microsoft auth when the default `auto` service mode is active. If Microsoft client settings (TenantId, ClientId, ClientSecret) are all present, the landing and sign-in pages show a real Microsoft Entra ID button alongside the demo providers. If those settings are absent, only the demo providers appear.

### Azure App Registration

Before adding secrets, create an app registration in the [Azure portal](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade):

1. **New registration** — give it a name (e.g. `AspireAI Local`), set *Supported account types* to your preference.
2. **Redirect URI** — add a *Web* platform URI: `https://localhost:{port}/signin-oidc-microsoft`. Use the HTTPS port from your `webfrontend` launch profile (or the Aspire dashboard URL when running under Aspire).
3. **Client secret** — under *Certificates & secrets*, create a new client secret and copy the value immediately.

### Adding Secrets

Add the registration values to `src\AspireApp.Web` user-secrets:

```powershell
dotnet user-secrets set "Authentication:Microsoft:TenantId" "<entra-tenant-id-or-domain>" --project src\AspireApp.Web
dotnet user-secrets set "Authentication:Microsoft:ClientId" "<app-registration-client-id>" --project src\AspireApp.Web
dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "<client-secret-value>" --project src\AspireApp.Web
```

Optional tenant-mapping seeds:

```powershell
dotnet user-secrets set "Authentication:Microsoft:DefaultAppTenantId" "default" --project src\AspireApp.Web
dotnet user-secrets set "Authentication:Microsoft:UserTenantSeeds:your.name@yourdomain.com" "tenant-a" --project src\AspireApp.Web
```

### Service Modes

| Mode | Behavior |
|------|----------|
| `auto` (default) | Shows real Microsoft when configured, demo providers otherwise. Resolves to `combined` or `mock` at runtime. |
| `combined` | Always shows both real Microsoft and demo providers (Microsoft hidden if not configured). |
| `mock` | Demo providers only — real Microsoft button never appears, even if configured. Mock auth endpoints are blocked when set to `microsoft`. |
| `microsoft` | Real Microsoft only. Mock endpoints are disabled at the HTTP level to prevent session-cookie bypass. |

To force demo-only mode even when Microsoft settings exist, set `Authentication:Service` to `mock`.

## Contributing

Contributions welcome! Fork the repo, create a feature branch, and open a PR. See the [Plan](plan.md) for current priorities.

## License

MIT

