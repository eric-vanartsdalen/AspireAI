# Tasks Directory

This directory contains task tracking files for the AspireAI project. Tasks are used to manage feature development, bug fixes, and research work.

## Structure
- `_index.md`: Master index of all tasks with current status
- `TASK###-description.md`: Individual task files with detailed progress tracking

## Task Lifecycle
1. **Creation**: Add new task to `_index.md` and create individual file
2. **Progress**: Update status, subtasks, and progress log regularly
3. **Completion**: Mark as completed and archive old tasks

## Task Categories
- **Feature**: New functionality implementation
- **Bug**: Issue fixes and patches
- **Research**: Investigation and prototyping
- **Maintenance**: Code cleanup and technical debt

## Usage
- Use `show tasks` command in Copilot for filtered views
- Update task status after significant progress
- Include task references in commit messages
- Archive completed tasks older than 3 months

## Templates
- `feature-template.md`
- `bug-template.md`
- `research-template.md`

Use the templates as a starting point, then link the resulting task in `_index.md`.

## Guardrails
- Review open tasks monthly and move completed items to an archive folder (create `archive/` if needed).
- Ensure each task file includes `Added`, `Updated`, and `Status` fields at the top.
- Reference related PRs or commits in the progress log for traceability.