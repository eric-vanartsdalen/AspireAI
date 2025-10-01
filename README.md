# AspireAI

AspireAI is a collaborative learning project designed to help developers explore and experiment with:

Disclaimer: This is a hacked together example, so there may be bad practices and places to improve. Use at your own risk!

- **Blazor**: Building interactive web UIs using C# and .NET.
- **AI Integrations**: Experimenting with AI features and services in .NET applications.
- **Shared Configurations**: Managing and sharing configuration across different parts of an application.
- **Aspire Platform**: Using Aspire to scaffold and manage modern .NET projects.

## Prerequisites

Before getting started, ensure you have the following installed:

- **.NET 9 SDK**: [Download here](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
Check from commandline
```
dotnet --version
```
- **Aspire Tooling**: [Setup instructions](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling?tabs=windows&pivots=vscode) Install tools from commandline

Once prerequisites are installed, you can perform an initial build:
```
dotnet build
```

## Goals

- Provide hands-on examples for Blazor components and patterns.
- Demonstrate how to connect and use AI services.
- Show how to centralize and share configuration settings.
- Serve as a collaborative playground for learning and sharing knowledge.

## Getting Started

1. **Clone the repository**
2. **Restore dependencies** (once projects are added)
3. **Run the project**
4. **Explore the code and features**

## Troubleshooting

### Startup Project Issues

**Problem**: The application may fail to start properly or show 404 errors when accessing URLs.

**Common Cause**: The startup project may be incorrectly set to `AspireApp.ApiService` instead of `AspireApp.AppHost`.

**Solution**: Since this is an Aspire application, the startup project should always be `AspireApp.AppHost`, which orchestrates all services including the API, web frontend, and supporting services (Ollama, Neo4j, Python services).

**To fix this in Visual Studio:**
1. In Solution Explorer, right-click on the `AspireApp.AppHost` project
2. Select "Set as Startup Project"
3. The `AspireApp.AppHost` project should now appear in **bold** in Solution Explorer
4. Run the application (F5) - this will launch the Aspire dashboard and coordinate all services

**Expected behavior when working correctly:**
- The Aspire dashboard should launch showing all services
- The web application should be accessible through the dashboard
- All supporting services (API, Neo4j, Ollama) should start automatically

## Contributing

Contributions are welcome! Feel free to fork and add new Blazor components, AI integrations, or configuration examples as the project grows.

## License

MIT

