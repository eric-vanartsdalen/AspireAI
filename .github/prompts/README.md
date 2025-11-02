# Prompt Metadata Guide

## Purpose
- Provide a shared schema for every file under `.github/prompts/` so Copilot sessions can surface the right helper quickly.
- Keep prompts under 200 lines while conveying the minimum context required to act safely inside the AspireAI solution.

## Required Front Matter Fields
| Field | Description | Example |
|-------|-------------|---------|
| `description` | One-sentence summary of the prompt's purpose. | `'Troubleshoot Aspire dashboard issues and service orchestration problems'` |
| `agent` | Execution style (`agent`, `copilot`, etc.). | `agent: 'agent'` |
| `tools` | Array of tool identifiers available to the prompt. Omit if automation does not require tools. | `['run_in_terminal', 'read_file']` |
| `owner` | Single point of contact (GitHub handle or team). | `'@eric-vanartsdalen'` |
| `audience` | Who should invoke the prompt (e.g., "Maintainers", "Contributors", "Automation"). | `'Maintainers'` |
| `dependencies` | Short list of external requirements (SDKs, services). | `['Docker Desktop', '.NET 9 SDK']` |
| `last_reviewed` | ISO date when the prompt was last validated. | `'2025-11-02'` |

Place the metadata block at the top of the file using simple `key: value` lines. Skip the opening/closing `---` fences for now—current prompt tooling rejects custom fields when the block is treated as strict YAML.

## Recommended Metadata Blocks
Inside the prompt body, include a `## Metadata` section listing:
- **Use Cases**
- **Prerequisites** (expanded view of `dependencies`)
- **Sample Inputs**
- **Related Instructions** (link to files in `.github/instructions/`)

## Content Guidelines
- Lead with the goal statement so the intent is obvious.
- Use headings (`##`) to separate scenarios, recipes, or checklists.
- Keep examples grounded in the AspireAI repo (paths, commands, services).
- Call out validation steps or safeguards when executing commands.

## Example Template
```markdown
description: 'Investigate Aspire dashboard health check failures'
agent: 'agent'
tools: ['run_in_terminal', 'read_file']
owner: '@eric-vanartsdalen'
audience: 'Maintainers'
dependencies: ['.NET 9 SDK', 'Docker Desktop']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Diagnosing unhealthy Aspire services, reviewing dashboard logs.
- **Prerequisites**: Access to repo, Docker running, Aspire AppHost restored.
- **Sample Inputs**: Service names, port numbers, log snippets.
- **Related Instructions**: `../instructions/dotnet-architecture-good-practices.instructions.md`

# Aspire Dashboard Health Triage
(Body content here)
```

## Available Prompts

### Architecture & Design
- **architecture-blueprint-generator.prompt.md** - Generate comprehensive architectural documentation with service boundaries and extension points
- **ai-evaluation-scripts.prompt.md** - Create evaluation scripts for AI model performance

### .NET Development
- **csharp-async.prompt.md** - Best practices for C# async/await patterns
- **csharp-docs.prompt.md** - XML documentation standards for C# code
- **ef-core.prompt.md** - Entity Framework Core patterns

### Aspire & Orchestration
- **aspire-dashboard-troubleshooting.prompt.md** - Debug Aspire dashboard and service orchestration issues *(Updated 2025-11-02)*
- **dependency-update-workflow.prompt.md** - Coordinate updates across NuGet, pip, and Docker *(New 2025-11-02)*

### Python Services
- **python-ingestion-debugging.prompt.md** - Debug FastAPI document processing and Graph-RAG *(Updated 2025-11-02)*
- **cross-service-contract-sync.prompt.md** - Synchronize C#↔Python data contracts *(New 2025-11-02)*

### Neo4j & Graph Database
- **neo4j-cypher-prototyping.prompt.md** - Write and optimize Cypher queries *(Updated 2025-11-02)*

### Testing & Automation
- **playwright-explore-website.prompt.md** - Explore web applications with Playwright
- **playwright-automation-fill-in-form.prompt.md** - Automate form filling workflows
- **playwright-generate-test.prompt.md** - Generate Playwright test scripts

### SQL & Database
- **sql-code-review.prompt.md** - Review SQL code for performance and best practices
- **sql-optimization.prompt.md** - Optimize SQL queries and stored procedures

## Recent Updates (2025-11-02)
- Updated existing prompts to reference new instruction files: `aspire-orchestration.instructions.md`, `neo4j-integration.instructions.md`, `python.instructions.md`, `cross-service-contracts.instructions.md`, `testing.instructions.md`, `dependency-management.instructions.md`
- Created **dependency-update-workflow.prompt.md** for coordinating package updates across the stack
- Created **cross-service-contract-sync.prompt.md** for maintaining C#↔Python API contracts

## Maintenance Checklist
- Update `last_reviewed` when materially changing the prompt.
- Ensure `owner` reflects the person or team responsible for keeping the prompt accurate.
- When retiring a prompt, note the replacement in this README.
