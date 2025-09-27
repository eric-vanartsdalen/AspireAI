using AspireApp.Web;
using AspireApp.Web.Components;
using AspireApp.Web.Components.Shared;
using AspireApp.Web.Components.Pages;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

// Add HttpClient factory for general use
builder.Services.AddHttpClient();

// Add this right after the AddHttpClient section
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// Add this line to make environment variables accessible
builder.Services.AddSingleton(provider => new EnvironmentProvider(builder.Environment));

// Register AI and Chat services
builder.Services.AddSingleton<ChatRefreshService>();
builder.Services.AddSingleton<AiInfoStateService>();

// Register Speech service
builder.Services.AddScoped<SpeechService>();

// Initialize configurations early
HomeConfigurations.PullConfigure();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();
// Add this after the existing endpoint mappings

app.MapGet("/health", () => Results.Ok("Healthy"));

await app.RunAsync();
