using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Schema;
using System;
using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

// Check if Docker is running  (See if we can somehow move the function to Extensions later)
CheckDockerIsRunning();

// Read Configs - setup details to pass through.
// (Not sure of the best way to accomplish this)
// CONFIGURATIONS  
var environmentName = builder.Environment.EnvironmentName;
var baseProjectDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
Console.WriteLine($"Base project directory: {baseProjectDirectory}");
var blazorProjectPath = Path.Combine(baseProjectDirectory, "AspireApp.Web");
var baseSettingsPath = Path.Combine(blazorProjectPath, "appsettings.json");
var envSettingsPath = Path.Combine(blazorProjectPath, $"appsettings.{environmentName}.json");
var configuration = new ConfigurationBuilder()
	.SetBasePath(Directory.GetCurrentDirectory())
	.AddJsonFile(baseSettingsPath, optional: false)
	.AddJsonFile(envSettingsPath, optional: true)
	.Build();
// Fetch and set values for ollama endpoint & model
var aiModel = configuration.GetValue<string>("AI_Model") ?? "phi3:latest";
var aiEndpoint = configuration.GetValue<string>("AI_Endpoint") ?? "http://localhost:11434";

// API Service
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
	.WithHttpHealthCheck("/health");

// TODO add AI Ollama & AI Model from configs
// SETUP OLLAMA & MODEL CONTAINERS
var ollama = builder.AddOllama("ollama")
	.WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "0.9.5" })
	.WithDataVolume()
	.WithContainerRuntimeArgs("--gpus", "all");
var appmodel = ollama.AddModel("chat", aiModel);

// BLAZOR app (TODO add references and Chat function later)
// See Blazor Home page... and the variables I pull...
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
	.WithExternalHttpEndpoints()
	.WithHttpHealthCheck("/health")
	.WithReference(apiService)
	.WithReference(ollama)
	.WithReference(appmodel)
	.WithEnvironment("AI_Endpoint", aiEndpoint)
	.WithEnvironment("AI_Model", aiModel)
	.WaitFor(ollama)
	.WaitFor(appmodel)
	.WaitFor(apiService);

await builder.Build().RunAsync();

// Support function for later moving to Extensions
static void CheckDockerIsRunning()
{
	var dockerProcesses = Process.GetProcessesByName("com.docker.backend");
	if (dockerProcesses.Length <= 0)
	{
		Console.WriteLine("Docker does not appear to be running!");
		throw new InvalidOperationException("Docker is not running. Please start Docker and try again.");
	}
	Console.WriteLine("Docker is running!");
}