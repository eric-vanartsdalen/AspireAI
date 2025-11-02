---
description: 'Legacy .NET Framework guidance (not active by default)'
applyTo: 'legacy-dotnet-framework/**'
---

# Legacy .NET Framework Notes

## Scope
- AspireAI does not contain .NET Framework projects. Keep this file for historical reference only.
- No rules in this file apply automatically because `applyTo` is empty. If a legacy project is added, update the glob and revisit the content.

## What To Do If A Legacy Project Appears
- Confirm with maintainers before adopting .NET Framework; the default strategy is to stay on .NET 9 SDK projects.
- Should legacy code surface, build with `msbuild` rather than `dotnet build`, and add new files explicitly to the `.csproj`.
- Restrict language features to the version supported by the project and document any deviations in the project README.

## Decision Log
- 2025-11-02: Guidance parked; no active .NET Framework usage in the solution.
