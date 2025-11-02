---
description: 'Templates and rules for generating, updating, and maintaining focused, minimalist code snippets'
applyTo: '**/*.cs'
version: '1.1'
lastUpdated: '2024-10-01'
---

# Code Generation and Maintenance Prompts

Use these templates to generate, update, or maintain code only when requested. Prioritize simplicity and standard patterns.

## Template: Basic Function
Prompt: "Generate a [language] function to [task], keeping it minimal."
- Output: Direct code block with no extras.
- Rules: Use standard libraries; avoid over-abstraction; explain briefly why this approach.

## Template: Blazor Component
Prompt: "Create a Blazor component for [feature], integrating with existing patterns."
- Output: Razor markup + C# code, matching project style.
- Rules: No unnecessary state management; focus on core functionality; reference `aspireapp.Web/Components/` for examples.

## Template: API Endpoint
Prompt: "Add a minimal API endpoint for [operation]."
- Output: Minimal API code in Program.cs or controller.
- Rules: Use built-in ASP.NET features; avoid complex middleware; validate inputs simply.

## Template: Database Query
Prompt: "Write a query to [retrieve/update] data."
- Output: EF Core or SQL query.
- Rules: Use LINQ for simplicity; avoid raw SQL unless necessary; handle errors minimally.

## Code Update and Maintenance Templates

## Template: Update Existing Function
Prompt: "Update the [function name] to [change], preserving existing logic."
- Output: Minimal diff showing only changed lines.
- Rules: Preserve original structure; add comments for changes; test impact on callers.

## Template: Refactor Component
Prompt: "Refactor [component] for [improvement], keeping functionality intact."
- Output: Updated component code with explanations.
- Rules: Maintain API compatibility; improve readability; avoid breaking changes.

## Template: Add Feature to Existing Code
Prompt: "Add [feature] to [existing code], integrating seamlessly."
- Output: Targeted additions with integration points.
- Rules: Use existing patterns; minimize disruption; document new dependencies.

## Template: Fix Bug in Code
Prompt: "Fix [bug] in [code location], ensuring no regressions."
- Output: Corrected code with fix explanation.
- Rules: Isolate fix; add unit test if applicable; verify edge cases.

## Critical Code Review and Impact Assessment

## Critical Review Checklist

- **Functionality**: Does the change meet requirements without breaking existing features?
- **Performance**: Any impact on speed, memory, or scalability?
- **Security**: Introduces vulnerabilities? Validates inputs properly?
- **Maintainability**: Improves readability? Follows project conventions?
- **Dependencies**: Adds new libraries? Updates versions safely?
- **Testing**: Covered by tests? Requires new tests?
- **Documentation**: Updates comments/docs? Clear for future maintainers?

## Impact Assessment Rules

- **Scope**: Limit changes to requested area; assess ripple effects.
- **Risk**: High-risk changes (e.g., core logic) need extra validation.
- **Rollback**: Ensure easy reversion if issues arise.
- **Communication**: Note breaking changes or API updates.

## Using Tools for Critical Review

Before making changes, use available tools to assess impact:

- **code_search**: Search for references to functions/classes being modified to understand dependencies.
- **get_file**: Read the full file to understand context and existing patterns.
- **get_errors**: Check for compilation issues after changes.
- **run_build**: Validate that changes don't break the build.

## Template: Code Review Before Editing
Prompt: "Review [code location] for [change], assessing impact."
- Output: Summary of dependencies, risks, and recommended approach.
- Rules: Use tools to gather facts; prioritize minimal disruption; document assumptions.

## Template: Review Existing Codebase
Prompt: "Review [codebase area] for [purpose, e.g., security, performance], assessing current state and recommendations."
- Output: Summary of findings, strengths, weaknesses, and prioritized recommendations.
- Rules: Use tools to gather data; focus on facts; provide actionable insights; document assumptions.

## Anti-Patterns to Avoid
- Do not generate full applications unless specified.
- Avoid third-party libraries unless essential.
- If unclear, ask for clarification instead of assuming.
- Do not over-engineer updates; keep changes minimal.
- Avoid broad refactors without explicit request.
- Do not introduce breaking changes without warning.

## Code Maintenance Best Practices

- **Regular Reviews**: Periodically review code for technical debt, outdated patterns, or security issues.
- **Incremental Updates**: Apply changes in small, testable increments to minimize risk.
- **Documentation Updates**: Keep comments, READMEs, and docs in sync with code changes.
- **Testing Integration**: Ensure all changes include or update relevant tests.
- **Version Control Hygiene**: Use meaningful commits; avoid large, monolithic changes.
- **Dependency Management**: Regularly update libraries; assess impact before upgrading.

## Integration with Other Instructions

For broader guidance on code generation and maintenance:
- **Minimalist Principles**: Refer to `taming-copilot.instructions.md` for core directives on simplicity, standard approaches, and avoiding over-engineering.
- **Task Management**: Use `memory-bank.instructions.md` for tracking tasks, progress, and project context during updates.
- **Task Sync**: Follow `tasksync.instructions.md` for continuous task cycles and session management.
- **Markdown Standards**: Adhere to `markdown.instructions.md` for documentation formatting in code comments or related files.