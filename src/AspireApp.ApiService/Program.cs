var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var brain = app.MapGroup("/brain")
    .WithTags("Brain");

brain.MapPost("/chat", () => BrainFeatureNotReady("chat", phase: 3))
    .WithName("BrainChat")
    .ProducesProblem(StatusCodes.Status501NotImplemented);

brain.MapPost("/ingest", () => BrainFeatureNotReady("ingest", phase: 2))
    .WithName("BrainIngest")
    .ProducesProblem(StatusCodes.Status501NotImplemented);

brain.MapPost("/query", () => BrainFeatureNotReady("query", phase: 2))
    .WithName("BrainQuery")
    .ProducesProblem(StatusCodes.Status501NotImplemented);

brain.MapGet("/health", () => TypedResults.Ok(new BrainHealthResponse(
        Status: "ok",
        Service: "brain-gateway",
        Phase: "0")))
    .WithName("BrainHealth")
    .Produces<BrainHealthResponse>(StatusCodes.Status200OK);

app.MapDefaultEndpoints();

await app.RunAsync();

static IResult BrainFeatureNotReady(string capability, int phase) =>
    Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: $"BRAIN {capability} is not implemented yet",
        detail: $"POST /brain/{capability} is scaffolded for Phase {phase} and currently returns 501 by design.");

internal sealed record BrainHealthResponse(string Status, string Service, string Phase);
