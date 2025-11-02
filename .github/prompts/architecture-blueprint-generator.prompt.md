description: 'Comprehensive project architecture blueprint generator that analyzes codebases to create detailed architectural documentation. Automatically detects technology stacks and architectural patterns, generates visual diagrams, documents implementation patterns, and provides extensible blueprints for maintaining architectural consistency and guiding new development.'
agent: 'agent'
tools: ['read_file', 'grep_search', 'apply_patch', 'run_in_terminal']
owner: '@eric-vanartsdalen'
audience: 'Maintainers'
dependencies: ['.NET 9 SDK', 'Docker Desktop']
last_reviewed: '2025-11-02'
# Prompt: Architecture Blueprint Generator

## Metadata
- **Use Cases**: Produce an architectural overview for AspireAI or similar solutions, capture service boundaries, and highlight extension points for new contributors.
- **Prerequisites**: Repo cloned, `dotnet restore` completed, familiarity with Aspire AppHost orchestration.
- **Sample Inputs**: `PROJECT_TYPE=.NET`, `ARCHITECTURE_PATTERN=Clean Architecture`, `DETAIL_LEVEL=Detailed`.
- **Related Instructions**: `../instructions/dotnet-architecture-good-practices.instructions.md`, `../instructions/blazor.instructions.md`, `../instructions/python.instructions.md`.

## Configuration Flags
```
${PROJECT_TYPE="Auto-detect|.NET|Python|Blazor|Mixed"}
${ARCHITECTURE_PATTERN="Auto-detect|Clean|Microservices|Layered"}
${DIAGRAM_TYPE="C4|Flow|Component|None"}
${DETAIL_LEVEL="High-level|Detailed|Implementation"}
${INCLUDE_PATTERNS=true|false}
${INCLUDE_DECISIONS=true|false}
${FOCUS_ON_EXTENSIBILITY=true|false}
```

## Instructions
1. **Scan & Summarize**
   - Detect primary technologies, bounded contexts, and orchestration entry points (`AppHost.cs`).
   - Note shared infrastructure (Ollama, Neo4j, data volumes) and how services communicate.

2. **Document Layers**
   - Describe how UI, API, background workers, and persistence interact.
   - Highlight dependency direction and DI registration patterns.

3. **Cross-Cutting Concerns**
   - Capture auth, error handling, logging, configuration, and resilience strategies.
   - Mention validation and testing approaches relevant to Aspire.

4. **Service Details** (repeat per component)
   - Purpose and responsibility.
   - Key abstractions or modules.
   - Inbound/outbound communications (HTTP, message queues, graph queries).
   - Extension guidance if `${FOCUS_ON_EXTENSIBILITY}` is `true`.

5. **Optional Sections**
   - If `${INCLUDE_PATTERNS}` is `true`, catalogue notable implementation patterns (repository, CQRS, etc.).
   - If `${INCLUDE_DECISIONS}` is `true`, add brief ADR-style notes with context, decision, consequences.
   - When `${DIAGRAM_TYPE}` ≠ `None`, describe how to produce diagrams (e.g., C4 level 1/2 lists) rather than embedding binaries.

6. **Blueprint Output**
   - Title the deliverable `Project_Architecture_Blueprint.md`.
   - Provide a contents outline, followed by sections gathered above.
   - End with "Next Steps" summarizing validation tasks (build, run Aspire, manual testing).

## Validation Checklist
- Confirm assumptions against code snippets or configuration files.
- Call out any areas requiring maintainer confirmation.
- Keep the generated document under 400 lines and update `last_reviewed` when changes are made.
