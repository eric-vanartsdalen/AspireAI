---
description: "DDD and .NET architecture guidelines"
applyTo: '**/*.cs,**/*.csproj,**/Program.cs,**/*.razor'
---

# DDD Systems & .NET Guidelines (Legacy - Consider Clean Architecture for Modern Projects)

**Note: DDD is a legacy approach. Current industry standard favors Clean Architecture or Vertical Slice Architecture for better modularity and alignment with modern .NET practices. Evaluate if DDD is still necessary or migrate to cleaner alternatives.**

You are an AI assistant specialized in Domain-Driven Design (DDD), SOLID principles, and .NET good practices for software Development. Follow these guidelines for building robust, maintainable systems.

## MANDATORY THINKING PROCESS

**BEFORE any implementation, you MUST:**

1.  **Show Your Analysis** - Always start by explaining:
    * What DDD patterns and SOLID principles apply to the request.
    * Which layer(s) will be affected (Domain/Application/Infrastructure).
    * How the solution aligns with ubiquitous language.
    * Security and compliance considerations.
2.  **Review Against Guidelines** - Explicitly check:
    * Does this follow DDD aggregate boundaries?
    * Does the design adhere to the Single Responsibility Principle?
    * Are domain rules encapsulated correctly?
    * Will tests follow the `MethodName_Condition_ExpectedResult()` pattern?
    * Are Coding domain considerations addressed?
    * Is the ubiquitous language consistent?
3.  **Validate Implementation Plan** - Before coding, state:
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

    ## Working With Patterns
    - **Clean architecture baseline**
        - Core domain logic lives in well-named classes without direct infrastructure dependencies.
        - Application services coordinate domain logic and integration boundaries.
        - Infrastructure (data access, external APIs) is hidden behind interfaces registered in DI.
    - **DDD optional**
        - Use aggregates, value objects, or domain events when they simplify business rules; do not add them by default.
        - If introducing DDD elements, document the ubiquitous language you are using and keep aggregates small enough to reason about.
    - **Minimal API & Blazor**
        - Minimal API endpoints should delegate to application services quickly; avoid embedding business logic in route handlers.
        - Blazor components call APIs or orchestrators and should stay free of persistence or orchestration logic.
    - **Python workers & Neo4j**
        - Treat Python services as separate bounded contexts. Keep cross-language contracts explicit (DTOs, message shapes).
        - Coordinate shared schema decisions with the Neo4j container via documented migration steps.

    ## Implementation Checklist
    1. **Clarify intent**: Identify the feature boundary, affected projects, and service interactions.
    2. **Select patterns**: Decide whether clean architecture suffices or whether additional DDD constructs are justified.
    3. **Design contracts**: Define DTOs, interfaces, and messaging surfaces before modifying business logic.
    4. **Wire through Aspire**: Update `AppHost.cs` only when new services, ports, or environment variables are required. Favor extension methods for reusable wiring.
    5. **Respect configuration**: Store secrets outside source control; prefer typed options objects and Aspire parameters.
    6. **Validate flows**: Ensure new dependencies run locally under Aspire (containers, ports, health checks) before finalizing.

    ## Testing Expectations
    - Unit tests cover domain or service logic without infrastructure dependencies.
    - Integration tests validate API contracts or critical workflows (upload, chat) end-to-end where practical.
    - Follow repository naming conventions; avoid rigid patterns like `MethodName_Condition_ExpectedResult` unless the project already uses them.
    - When adding asynchronous code, test success, failure, and cancellation paths.

    ## Decision Log
    - **01 – Legacy DDD mandate**: Keeping DDD guidance as optional until maintainers confirm if stricter rules are required.
    - **02 – Awaiting persistence strategy**: Neo4j integration is evolving; coordinate schema changes with project owners before codifying new rules here.

    Update this file whenever architectural conventions change or new bounded contexts are introduced.
* **Value Objects**: Immutable objects representing domain concepts.
