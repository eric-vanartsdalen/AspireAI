# Team Roster

> AspireAI — Aspire-hosted orchestration with Blazor, Minimal API, Python FastAPI, Neo4j, and Ollama for document processing and RAG workflows.

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. Does not generate domain artifacts. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Bob | Lead / Architect | `.squad/agents/bob/charter.md` | ✅ Active |
| Jeff | .NET Dev | `.squad/agents/jeff/charter.md` | ✅ Active |
| Jarvis | Python / Data Dev | `.squad/agents/jarvis/charter.md` | ✅ Active |
| Buster | QA / Tester | `.squad/agents/buster/charter.md` | ✅ Active |
| Scribe | Session Logger | `.squad/agents/scribe/charter.md` | 📋 Silent |
| Ralph | Work Monitor | — | 🔄 Monitor |

## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage (adding missing tests, fixing flaky tests)
- Lint/format fixes and code style cleanup
- Dependency updates and version bumps
- Small isolated features with clear specs
- Boilerplate/scaffolding generation
- Documentation fixes and README updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear specs and acceptance criteria
- Refactoring with existing test coverage
- API endpoint additions following established patterns
- Migration scripts with well-defined schemas

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Multi-system integration requiring coordination
- Ambiguous requirements needing clarification
- Security-critical changes (auth, encryption, access control)
- Performance-critical paths requiring benchmarking
- Changes requiring cross-team discussion

## Project Context

- **Owner:** Eric Van Artsdalen
- **Stack:** C# (.NET 9), Blazor, Minimal API, Python (FastAPI), Neo4j, Ollama, Docker, Aspire
- **Description:** AI-powered document processing and RAG platform with graph database knowledge storage, orchestrated via .NET Aspire
- **Created:** 2026-02-21T23:32:00Z
