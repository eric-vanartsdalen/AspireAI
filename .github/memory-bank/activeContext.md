# Active Context

Last reviewed: 2025-11-02

## 2025-11-02

### Current Work Focus
- Streamlining Copilot instructions and guidance for better developer experience
- Ensuring consistent async task execution to avoid main thread blocking
- Updating legacy directives to align with modern .NET and Aspire practices

### Recent Changes
- Consolidated `taming-copilot.instructions.md` and `memory-bank.instructions.md` into `memory-recall.instructions.md`
- Updated `tasksync.instructions.md` for asynchronous execution
- Noted DDD as legacy in favor of Clean Architecture
- Replaced Unicode characters in `copilot-instructions.md`
- Refreshed prompt library with metadata and new AI-focused prompts
- Added memory bank Last reviewed markers and README guardrails
- Seeded `.github/tasks/` with reusable templates and archive checklist

### Next Steps
- Finalize WS3 Playwright harness integration once the shared suite ships
- Apply the new WS3 completion checklist across all prompts and capture validation notes
- Roll out WS4 task and memory guardrails to contributors (README, templates, archive process)
- Validate end-to-end Aspire workflows before documenting WS5 verification guidance

### Active Decisions
- **DDD vs Clean Architecture**: Moving away from strict DDD mandates toward more flexible Clean Architecture for better alignment with Aspire and modern .NET
- **Task Synchronization**: Prioritizing asynchronous execution to prevent thread blocking while maintaining atomic consistency
- **Instruction Consolidation**: Merging overlapping guidance files to reduce maintenance burden and improve discoverability
- **Prompt Standardization**: Adding consistent metadata to all prompts for better usability and reference linking

### Current Challenges
- Ensuring memory bank files are maintained without becoming stale
- Balancing comprehensive guidance with concise, actionable instructions
- Maintaining alignment between prompts, instructions, and actual codebase patterns