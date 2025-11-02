---
description: 'Blazor component and styling guidance'
applyTo: '**/*.razor, **/*.razor.cs, **/*.razor.css'
---

# AspireAI Blazor Guidance

## Scope
- Applies to Razor components, code-behind files, and component-scoped styles in `src/AspireApp.Web`.
- Follow these practices unless the user or an existing component establishes a different convention.

## Component Structure
- Keep markup expressive and push heavy logic into code-behind or injected services.
- Use `partial` classes when the component requires more than a few lines of C# logic.
- Respect lifecycle methods (`OnInitializedAsync`, `OnParametersSetAsync`) and avoid redundant state loading.

## Styling & Layout
- Prefer component-scoped `.razor.css` files for styling to minimize global CSS leaks.
- Use the existing design tokens or shared classes before adding bespoke styles.
- Keep CSS selectors shallow to maintain predictable cascade behavior.

## Data Flow & State
- Bind with `@bind` helpers and `EventCallback`/`EventCallback<T>` for parent-child communication.
- Use `CancellationToken` support on long-running operations triggered by the UI.
- Cache lightweight UI state within the component; promote shared state to scoped services only when multiple components need it.

## API Integration
- Inject typed or named `HttpClient` instances via DI; never instantiate new clients manually.
- Handle failures gracefully: show inline feedback and log via an injected `ILogger`.
- Consider using `Task` returning commands (e.g., `Func<Task>`) for button handlers to keep error handling reusable.

## Rendering Discipline
- Avoid forcing rerenders with `StateHasChanged()` unless absolutely necessary—prefer changing bound data.
- Use `ShouldRender` sparingly for expensive visualizations; document why it is overridden.

## Testing & Diagnostics
- Where practical, wrap complex logic in testable services. For UI-level verification, follow existing component test patterns (currently limited—coordinate with maintainers before adding large suites).
- Reproduce layout issues with Playwright snapshots when visual regressions are suspected.
