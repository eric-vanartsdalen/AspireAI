---
description: 'Markdown documentation guidance for AspireAI'
applyTo: '**/*.md'
---

# AspireAI Markdown Guidance

## Scope
- Applies to repository documentation, notes, and planning files. Blog-style front matter is optional and used only when the target hosting system requires it.

## Essentials
- Start files with a single `#` heading unless the surrounding system (e.g., GitHub README) already injects one.
- Keep paragraphs short and use bullet lists for sequences or checklists.
- Use fenced code blocks with language hints (```csharp, ```powershell) when showing commands or code.
- Prefer relative links to files within the repo; verify external URLs before committing.
- Provide alt text for images and clarify diagrams or screenshots in nearby text.

## Formatting Tips
- Wrap lines naturally; hard wrapping is optional but keep lines below 200 characters for readability.
- Tables should remain simple—limit column count and keep cell content concise.
- Use callouts (e.g., `> Note`) sparingly to highlight warnings or prerequisites.
- Document decisions with dated bullet entries when recording design discussions.

## Quality Checklist
- Does the document explain why the change or process matters?
- Are steps actionable with the correct command syntax for Windows PowerShell when needed?
- Have you removed stale references to files or folders that no longer exist?
- If the file will be rendered outside GitHub, confirm the destination supports the Markdown features you used.
