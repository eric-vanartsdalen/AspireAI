agent: 'agent'
tools: ['read_file', 'grep_search', 'apply_patch', 'run_in_terminal']
description: 'Get best practices for C# async programming'
owner: '@eric-vanartsdalen'
audience: 'C# Maintainers'
dependencies: ['.NET 9 SDK']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Reviewing existing C# code for async issues, implementing new async methods, optimizing async performance.
- **Dependencies**: .NET framework with async/await support (C# 5+), Task Parallel Library.
- **Sample Inputs**: C# code snippets (e.g., methods with blocking calls, exception handling in async code).
- **Related Instructions**: See `../instructions/csharp.instructions.md` for AspireAI coding conventions; reference `../instructions/dotnet-architecture-good-practices.instructions.md` for async design guidance; see `../instructions/testing.instructions.md` for async test patterns.

# C# Async Programming Best Practices

Your goal is to help me follow best practices for asynchronous programming in C#.

## Naming Conventions

- Use the 'Async' suffix for all async methods
- Match method names with their synchronous counterparts when applicable (e.g., `GetDataAsync()` for `GetData()`)

## Return Types

- Return `Task<T>` when the method returns a value
- Return `Task` when the method doesn't return a value
- Consider `ValueTask<T>` for high-performance scenarios to reduce allocations
- Avoid returning `void` for async methods except for event handlers

## Exception Handling

- Use try/catch blocks around await expressions
- Avoid swallowing exceptions in async methods
- Use `ConfigureAwait(false)` when appropriate to prevent deadlocks in library code
- Propagate exceptions with `Task.FromException()` instead of throwing in async Task returning methods

## Performance

- Use `Task.WhenAll()` for parallel execution of multiple tasks
- Use `Task.WhenAny()` for implementing timeouts or taking the first completed task
- Avoid unnecessary async/await when simply passing through task results
- Consider cancellation tokens for long-running operations

## Common Pitfalls

- Never use `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()` in async code
- Avoid mixing blocking and async code
- Don't create async void methods (except for event handlers)
- Always await Task-returning methods

## Implementation Patterns

- Implement the async command pattern for long-running operations
- Use async streams (IAsyncEnumerable<T>) for processing sequences asynchronously
- Consider the task-based asynchronous pattern (TAP) for public APIs

When reviewing my C# code, identify these issues and suggest improvements that follow these best practices.
