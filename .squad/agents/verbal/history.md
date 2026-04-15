# Verbal — Project History

## Project Context

**Project:** AspireAI — AI-powered document processing and RAG platform pivoting to domain-agnostic agentic assistant (BRAIN)
**Owner:** Eric Van Artsdalen
**Stack:** C# (.NET 10), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
**Current State:** Phase 3 (Document Upload & Ingestion) partially complete; pivoting from chat-oriented RAG to modular agentic architecture

## Learnings

- Joined the team to provide strategic product review for the BRAIN pivot
- Strategic status reviews in AspireAI need a three-way comparison: planning docs (`roadmap\Roadmap.md`, `roadmap\Plan.md`, `roadmap\Tasks.md`), live implementation (`src\AspireApp.ApiService`, `src\AspireApp.PythonServices\app\brain`, `src\AspireApp.Web`), and validation evidence (branch/build/test state).
- The active BRAIN contract surface is split across `src\AspireApp.ApiService\Contracts\BrainContractModels.cs` and `src\AspireApp.PythonServices\app\contracts\models.py`; the repo-root `contracts\` folder currently exists only as a placeholder.
- Web chat is already routed through the BRAIN gateway via `src\AspireApp.Web\Services\BrainChatClient.cs` and `Components\Pages\Chat.razor.cs`, even though Semantic Kernel still remains in supporting Web paths like warmup and title generation.
- `roadmap\Tasks.md` is closer to implementation reality than `roadmap\Plan.md` or `roadmap\Roadmap.md`, but it still mixes checked milestones with stale unchecked subtasks, so maintainers should verify against code before treating a phase label as authoritative.
- Validation snapshot for roadmap audits: `dotnet build -nologo` succeeds and focused BRAIN Web tests pass; local Python BRAIN tests can be blocked by missing environment dependencies like `neo4j`.
