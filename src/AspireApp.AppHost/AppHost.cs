using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Schema;
using System;
using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

// Config with .NET Aspire
var aiModel = builder.AddParameterFromConfiguration("AI-Model", "AI-Model");
var aiEndpoint = builder.AddParameterFromConfiguration("AI-Endpoint", "AI-Endpoint");

// API Service
var apiService = builder.AddProject<Projects.AspireApp_ApiService>("apiservice")
	.WithHttpHealthCheck("/health");

// SETUP OLLAMA & MODEL CONTAINERS
var modelName = builder.Configuration["AI-Model"] ?? "phi4-mini:latest";
var ollama = builder.AddOllama("ollama")
    .WithAnnotation(new ContainerImageAnnotation { Image = "ollama/ollama", Tag = "latest" })
    .WithDataVolume()
    .WithContainerRuntimeArgs("--gpus", "all");
var appmodel = ollama.AddModel("chat", modelName);

// See Blazor Home page... and the variables I pull...
builder.AddProject<Projects.AspireApp_Web>("webfrontend")
	.WithExternalHttpEndpoints()
	.WithHttpHealthCheck("/health")
	.WithReference(apiService)
	.WithReference(ollama)
    .WithReference(appmodel)
	.WithEnvironment("AI-Endpoint", aiEndpoint)
	.WithEnvironment("AI-Model", aiModel)
	.WaitFor(ollama)
    .WaitFor(appmodel)
	.WaitFor(apiService);

await builder.Build().RunAsync();
