---
description: 'C# implementation guidelines for AspireAI'
applyTo: '**/*.cs'
---

# AspireAI C# Practices

## Scope
- Applies to C# source files inside the AspireAI solution (API, Blazor, ServiceDefaults, helpers).
- Complements the architecture guidance; follow user instructions or project conventions if they conflict.

## Style & Readability
- Match the repository `.editorconfig` formatting; use `dotnet format` if unsure.
- Favor file-scoped namespaces and top-level using directives already present in the project.
- Keep methods small and purposeful; extract helpers when logic branches would obscure intent.
- Document public APIs with XML comments when they are consumed outside the declaring project; avoid comment noise for obvious implementation details.

## Language Features
- Target C# 13 features available with .NET 9, but only when they improve clarity (e.g., primary constructors, pattern matching).
- Use nullable reference types; treat warnings as prompts to improve intent rather than sprinkling `!` suppression operators.
- Prefer `async`/`await` with `ValueTask` or `Task` for I/O. Avoid synchronous over async patterns like `.Result` or `.Wait()`.
- Leverage `nameof` and `required` members for configuration objects to improve diagnostics.

## Error Handling & Diagnostics
- Throw domain-specific exceptions or return `ProblemDetails` from API endpoints instead of generic `Exception` messages.
- Log contextual information through the injected `ILogger<T>`; include correlation or document IDs when available.
- When catching exceptions, rethrow with `throw;` unless wrapping in a richer domain/message context.

## Dependency Injection & Configuration
- Register services via extension methods on `IServiceCollection` in the owning project to keep `Program.cs` tidy.
- Inject abstractions (`IClock`, `INeo4jClient`, etc.) rather than concrete classes to simplify testing.
- Use options patterns (`IOptions<T>`, `IOptionsMonitor<T>`) for structured configuration. Validate options at startup where failure is unacceptable at runtime.

## Data & External Resources
- For REST calls, prefer typed clients registered with `AddHttpClient`. Configure resilience (timeouts, retry, circuit breaker) through Polly where necessary.
- When interacting with Neo4j or other databases, isolate query logic in dedicated services or repositories; keep models immutable where feasible.
- Treat shared DTOs as contracts. Update the corresponding Python or Blazor counterparts when shapes change.

## Testing Guidance
- Follow existing test naming patterns in the project under test; do not introduce new schemes unless agreed upon.
- Use `Fake`, `Mock`, or minimal test stubs to validate behavior without spinning up full Aspire orchestration.
- Cover success, failure, and edge paths for new logic (null/empty input, cancellation, downstream failures).

## Decision Log
- Awaiting maintainer input on preferred mocking frameworks (currently mixed usage).
- No official preference between FluentAssertions and xUnit assertions—match the local project style.
