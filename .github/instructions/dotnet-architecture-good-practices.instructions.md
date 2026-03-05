---
description: 'Architecture guidance for AspireAI .NET projects'
applyTo: '**/*.cs,**/*.csproj,**/Program.cs,**/*.razor'
---

# AspireAI Architecture Guidance

## Scope
- Use this document when a request touches solution structure, layering, or cross-cutting design choices in the AspireAI solution.
- The codebase targets .NET 9 with Aspire AppHost orchestration. Prioritize clean architecture patterns; DDD concepts are optional tools, not mandatory gates.
- Treat these guidelines as a companion to `copilot-instructions.md`. If user direction conflicts, follow the user.

## Architectural Priorities
- **Separation of concerns**: Keep UI (Blazor), API (Minimal API), background workers (Python), and orchestration responsibilities isolated.
- **Dependency direction**: Web/UI → API → domain/services → infrastructure. Avoid reversing dependencies without a clear adapter or abstraction.
- **Aspire first**: Service wiring belongs in `src/AspireApp.AppHost/AppHost.cs`. Feature-specific wiring should be expressed via typed options, DI registrations, or extension methods in the owning project.
- **Async by default**: Prefer asynchronous APIs for I/O to keep services responsive under orchestration.
- **Configuration clarity**: Settings that affect multiple services belong in shared configuration objects or Aspire parameters; keep project-specific options scoped locally.

## Clean Architecture Baseline
- **Core domain logic** lives in well-named classes without direct infrastructure dependencies.
- **Application services** coordinate domain logic and integration boundaries.
- **Infrastructure** (data access, external APIs) is hidden behind interfaces registered in DI.
- **SOLID principles** apply: favor single responsibility, open/closed design, interface segregation, and dependency inversion.
- Keep business rules testable without spinning up containers or external dependencies.

## Aspire Orchestration Patterns
- **Service registration**: All services (containers, projects) are defined in `AppHost.cs`.
- **Resource naming**: Use descriptive names that match the service purpose (`apiservice`, `webfrontend`, `neo4j`, `ollama`).
- **Port management**: Let Aspire assign ports dynamically; avoid hardcoding except for well-known services (Neo4j 7474/7687).
- **Environment variables**: Pass configuration to services via `WithEnvironment()` or reference other resources with `WithReference()`.
- **Health checks**: Implement health endpoints in each service; Aspire dashboard monitors them automatically.
- **Volume mounts**: Use `WithBindMount()` for persistent data (`database/`, `data/`) that must survive container restarts.
- **Container configuration**: Keep Dockerfiles in the service project; register with `AddDockerfile()` in AppHost.

## Working With Patterns

### Minimal API & Blazor
- **Minimal API endpoints** should delegate to application services quickly; avoid embedding business logic in route handlers.
- **Blazor components** call APIs or orchestrators and should stay free of persistence or orchestration logic.
- Use `IHttpClientFactory` or typed clients for service-to-service communication.
- Return `Results<T>` or `ProblemDetails` from API endpoints for consistent error responses.

### Python Workers & Neo4j
- Treat Python services as **separate bounded contexts**. Keep cross-language contracts explicit (DTOs, message shapes).
- Define shared models in both C# and Python; maintain parity manually or via code generation.
- Coordinate Neo4j schema decisions via documented migration steps or scripts.
- Use environment variables for connection strings; never hardcode credentials.

### Optional DDD Constructs
- Use **aggregates**, **value objects**, or **domain events** when they simplify business rules; do not add them by default.
- If introducing DDD elements, document the **ubiquitous language** you are using and keep aggregates small enough to reason about.
- DDD is helpful for complex domains with rich invariants; for CRUD or orchestration logic, simpler patterns suffice.

## Implementation Checklist
1. **Clarify intent**: Identify the feature boundary, affected projects, and service interactions.
2. **Select patterns**: Decide whether clean architecture suffices or whether additional DDD constructs are justified.
3. **Design contracts**: Define DTOs, interfaces, and messaging surfaces before modifying business logic.
4. **Wire through Aspire**: Update `AppHost.cs` only when new services, ports, or environment variables are required. Favor extension methods for reusable wiring.
5. **Respect configuration**: Store secrets outside source control; prefer typed options objects and Aspire parameters.
6. **Validate flows**: Ensure new dependencies run locally under Aspire (containers, ports, health checks) before finalizing.

## Dependency Injection Best Practices
- Register services via **extension methods** on `IServiceCollection` in the owning project to keep `Program.cs` tidy.
- Inject **abstractions** (`IWeatherApiClient`, `INeo4jService`) rather than concrete classes to simplify testing.
- Use **options patterns** (`IOptions<T>`, `IOptionsMonitor<T>`) for structured configuration. Validate options at startup where failure is unacceptable at runtime.
- Favor **scoped** lifetimes for services that maintain per-request state; use **singleton** for stateless helpers or caches.

## Configuration Management
- Store environment-specific settings in `appsettings.Development.json` and `appsettings.json`.
- Use Aspire parameters for values that differ across environments (endpoints, model names).
- Never commit secrets; use user secrets locally (`dotnet user-secrets set`) or environment variables in production.
- Validate configuration objects at startup with `ValidateOnStart()` or custom validators.

## Testing Expectations
- **Unit tests** cover domain or service logic without infrastructure dependencies.
- **Integration tests** validate API contracts or critical workflows (upload, chat) end-to-end where practical.
- Follow repository naming conventions; avoid rigid patterns like `MethodName_Condition_ExpectedResult` unless the project already uses them.
- When adding asynchronous code, test **success**, **failure**, and **cancellation** paths.
- Mock or fake external dependencies (Neo4j, Ollama, HTTP clients) for isolated testing.

## Error Handling & Logging
- Throw domain-specific exceptions or return `ProblemDetails` from API endpoints instead of generic `Exception` messages.
- Log contextual information through the injected `ILogger<T>`; include correlation or document IDs when available.
- When catching exceptions, rethrow with `throw;` unless wrapping in a richer domain/message context.
- Use structured logging with message templates: `logger.LogInformation("Processing document {DocumentId}", docId);`

## Cross-Service Communication
- Use **typed HttpClients** for service-to-service calls; register with `AddHttpClient<TClient>()`.
- Configure resilience (timeouts, retry, circuit breaker) through **Polly** policies where necessary.
- Pass correlation IDs across service boundaries for distributed tracing.
- Handle partial failures gracefully; avoid cascading errors that take down multiple services.

## Data & Schema Management
- **Neo4j schema**: Coordinate changes via migration scripts in `src/AspireApp.PythonServices/scripts/`.
- **Shared DTOs**: Keep C# models in `ApiService` or `ServiceDefaults`; mirror shapes in Python models.
- Treat database schemas as contracts; version changes and test migrations before deploying.
- Use **immutable models** where feasible to prevent accidental mutation.

## Decision Log
- **2025-11-02**: Removed legacy DDD mandates; DDD is now optional and context-driven.
- **2025-11-02**: Added Aspire orchestration patterns and cross-service communication guidance.
- **2025-11-02**: Clarified testing expectations and removed rigid naming conventions.
- **Awaiting**: Neo4j persistence strategy evolving; coordinate schema changes with project owners before codifying new rules here.

## Maintenance Notes
Update this file whenever architectural conventions change or new bounded contexts are introduced. Record decisions in the log above with dates and rationale.
