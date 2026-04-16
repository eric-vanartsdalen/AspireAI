# Project Context

- **Owner:** {user name}
- **Project:** {project description}
- **Stack:** {languages, frameworks, tools}
- **Created:** {timestamp}

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-04-17T23:55:30Z — Roadmap Cleanup Pattern: Verification Before Edit
- When roadmap wording requires update, have primary author (Bob) draft changes, then have QA reviewer (Buster) verify honesty against test evidence before approval.
- After approval, primary author performs surgical cleanup (remove duplicates/stale entries) in a second pass.
- QA reviewer rechecks final state for internal consistency before merge.
- Pattern prevents overclaiming and catches redundancy before commit.

### 2026-04-17T23:55:30Z — Phase 3 Critical Path: Agent Framework Selection is BLOCKING GATE
- Agent framework choice (LangGraph vs. CrewAI vs. Autogen) unblocks all Phase 3 gates (P3-A through P3-G).
- Decision deadline: 2026-04-24. 2-day prototype per candidate framework (Jarvis lead, Bob oversight).
- Sequencing: P3-A (/brain/chat) → P3-B (reasoning) → P3-C (proactive monitor) + P3-D (Blazor routing).
- All teams can work independently once framework chosen; no architectural rework needed.

### 2026-04-17T23:55:30Z — Contradiction Detection: Phase 2 → Phase 3 Defer Pattern
- Non-blocking Phase 2 items (contradiction detection) deferred to Phase 3 when better home exists (Critic Agent).
- Reduces Phase 2 execution risk; clarifies Phase 3 agent responsibilities.
- Pattern: Identify non-blocking items early, defer with clear reasoning, record in roadmap.

