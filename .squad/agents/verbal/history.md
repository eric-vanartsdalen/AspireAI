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

### 2026-04-21 — MVP Documentation Review & Ordering Confirmation

**Role:** Communication specialist reviewing MVP declaration and post-MVP fix ordering.

**Context:** Bob updated documentation across README, roadmap, and session plans to declare functional MVP achievement (gateway-routed chat Regular mode works end-to-end). Two post-MVP fixes identified by Eric needed confirmation of priority ordering.

**Decision:** Approved Bob's MVP pattern and confirmed final ordering places two user-raised fixes at the top (high impact, clear technical scope):
1. Conversation Context Not Passed on Follow-Ups (HIGH PRIORITY)
2. Gateway Evidence Not Persisted with Messages (HIGH PRIORITY)

**Key Validation Points:**
- MVP declaration is explicit: "Functional MVP ✅" with clear criteria
- Known limitations documented side-by-side with achievements (builds confidence)
- Post-MVP fixes ordered by user impact, not technical convenience
- Technical scope specified (files affected, contracts involved)
- Ownership assigned (Jeff + Jarvis for context; Jeff + Buster for evidence)

**Why This Matters for Communication:**
- Clear milestone markers prevent stakeholder confusion between "in progress" and "shippable"
- Honest limitations (side-by-side with features) signal domain expertise, not incomplete work
- Ordered priorities with technical scope become actionable team directives
- Documentation serves as single source of truth for roadmap questions

**Pattern Ready for Reuse:** This MVP + ordered next-steps pattern should be applied at each major phase closure (Phase 1 complete, Phase 2 complete, etc.). Maintains stakeholder alignment and team focus.

### 2026-04-16 — MVP Prioritization Review & Confirmation

**Role:** Product advisor reviewing post-MVP fixes and prioritization rationale.

**Context:** Bob updated README, roadmap/Tasks.md, and roadmap/Plan.md to declare AspireAI as a functional MVP. Two post-MVP user-identified weaknesses needed explicit ordering and prioritization confirmation.

**Issues Reviewed:**
1. **Conversation context memory** — Users can't build multi-turn reasoning when session context isn't passed on follow-ups
2. **Evidence persistence** — Citations disappear after session ends (backend results not persisted)

**Decision Confirmed:**
- ✅ Both issues are **real UX weaknesses**, not speculative
- ✅ Both are **high-ROI improvements** (user-facing, achievable in Phase 3c within team capacity)
- ✅ Prioritization is **data-driven** (raised by Eric post-MVP usage) not internal convenience
- ✅ MVP is **feature-complete** with no blocking architectural gates

**Key Validation Points:**
- MVP claim is honest: Regular mode chat works end-to-end (upload → knowledge graph → retrieval-augmented response)
- Known limitations documented alongside features
- Post-MVP fixes ordered by user impact (not technical dependency)
- No architectural gates block Phase 3c work; purely capacity-gated on P3b completion

**Coordination Handoff:**
- Bob owns documentation updates ✅
- Coordinator SQL-tracks memory + evidence tasks (blocked on P3b)
- Jeff + Jarvis will lead investigation phase after P3b (2026-04-30 target start)
- Buster will validate evidence persistence implementation

**Pattern Refinement:**
MVP + post-MVP ordering pattern confirmed stable. This approach (explicit milestone marker + ordered next steps with technical scope + ownership) scales across team and phases.
