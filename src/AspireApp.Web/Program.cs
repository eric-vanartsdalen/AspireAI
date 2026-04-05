using AspireApp.Web;
using AspireApp.Web.Components;
using AspireApp.Web.Components.Pages;
using AspireApp.Web.Components.Shared;
using AspireApp.Web.Services;
using AspireApp.Web.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MVC services for API controllers
builder.Services.AddControllers();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

// Add HttpClient factory for general use
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// ADDING CONFIGURATIONS FOR STORAGE OF FILES
var connectionString = builder.Configuration.GetConnectionString("appdb")
    ?? throw new InvalidOperationException("Connection string 'appdb' is required for the operational upload store.");

builder.Services.AddDbContext<UploadDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register the FileStorageService with data directory (simplified - no bridge service needed)
var dataDirectory = ResolveContentRootPath(
    builder.Configuration.GetValue<string>("FileUpload:DataDirectory"),
    builder.Environment.ContentRootPath,
    "data");

builder.Services.AddScoped<FileStorageService>(sp =>
    new FileStorageService(
        sp.GetRequiredService<UploadDbContext>(),
        sp.GetRequiredService<ILogger<FileStorageService>>(),
        dataDirectory));

// Add this right after the AddHttpClient section
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// Add this line to make environment variables accessible
builder.Services.AddSingleton(provider => new EnvironmentProvider(builder.Environment));

// Register AI and Chat services
builder.Services.AddSingleton<ChatRefreshService>();
builder.Services.AddSingleton<AiInfoStateService>();

// Register Speech service
builder.Services.AddScoped<SpeechService>();

// Register Tenant Context service (scoped to Blazor circuit/session)
builder.Services.AddScoped<TenantContextService>();

// Register authentication services (scoped to Blazor circuit/session)
builder.Services.AddAspireAppAuthentication(builder.Configuration);

// Register Ollama warmup background service
builder.Services.AddHostedService<AspireApp.Web.Services.OllamaWarmupService>();

// Initialize configurations early
HomeConfigurations.PullConfigure();

var app = builder.Build();

// Initialize database and directories
await InitializeDatabaseAsync(app.Services, dataDirectory);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) &&
        (context.Request.Path.Equals("/chat", StringComparison.OrdinalIgnoreCase) ||
         context.Request.Path.Equals("/upload", StringComparison.OrdinalIgnoreCase) ||
         context.Request.Path.Equals("/weather", StringComparison.OrdinalIgnoreCase)) &&
        context.User.Identity?.IsAuthenticated != true)
    {
        var returnUrl = Uri.EscapeDataString($"{context.Request.Path}{context.Request.QueryString}");
        context.Response.Redirect($"/signin?returnUrl={returnUrl}");
        return;
    }

    await next();
});

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map API controllers
app.MapControllers();

var microsoftAuthenticationOptions = builder.Configuration
    .GetSection(MicrosoftEntraAuthenticationOptions.SectionName)
    .Get<MicrosoftEntraAuthenticationOptions>() ?? new MicrosoftEntraAuthenticationOptions();

var effectiveAuthService = AspireApp.Web.Services.AuthenticationOptions.ResolveEffectiveService(
    builder.Configuration.GetSection("Authentication").GetValue<string>("Service"),
    microsoftAuthenticationOptions.IsConfigured);

// Mock auth endpoints are only safe when the active service mode explicitly includes them.
// Without this gate, /auth/mock/signin would let anyone mint a session cookie and bypass
// real Microsoft Entra authentication when the effective mode is live Microsoft.
var mockEndpointsEnabled = !string.Equals(
    effectiveAuthService,
    AspireApp.Web.Services.AuthenticationOptions.MicrosoftService,
    StringComparison.OrdinalIgnoreCase);

if (mockEndpointsEnabled)
{
    app.MapPost("/auth/mock/session", async (MockAuthSessionRequest request, HttpContext httpContext) =>
    {
        var selectedUser = MockAuthCatalog.FindUser(request.ProviderId, request.UserId);
        if (selectedUser is null)
        {
            return Results.BadRequest(new { error = "Unknown mock user." });
        }

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CreatePrincipal(selectedUser),
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = false
            });

        return Results.Ok();
    });

    app.MapGet("/auth/mock/signin", async (string providerId, string userId, string? returnUrl, HttpContext httpContext) =>
    {
        var selectedUser = MockAuthCatalog.FindUser(providerId, userId);
        if (selectedUser is null)
        {
            return Results.BadRequest(new { error = "Unknown mock user." });
        }

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CreatePrincipal(selectedUser),
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = false
            });

        return Results.LocalRedirect(NormalizeLocalPath(returnUrl));
    });

    app.MapDelete("/auth/mock/session", async (HttpContext httpContext) =>
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok();
    });

    app.MapGet("/auth/mock/signout", async (string? returnUrl, HttpContext httpContext) =>
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.LocalRedirect(NormalizeLocalPath(returnUrl));
    });
}

// Only expose the OIDC challenge endpoint when the Microsoft scheme is actually registered.
// Without this guard, an unregistered scheme would produce a 500 with internal details.
var microsoftOidcRegistered = microsoftAuthenticationOptions.IsConfigured;

if (microsoftOidcRegistered)
{
    app.MapGet("/auth/microsoft/signin", (string? returnUrl) =>
    {
        var redirectUri = NormalizeLocalPath(returnUrl);
        return Results.Challenge(
            new AuthenticationProperties
            {
                RedirectUri = redirectUri
            },
            authenticationSchemes: [MicrosoftEntraAuthService.AuthenticationScheme]);
    });
}

app.MapGet("/auth/signout", async (string? returnUrl, HttpContext httpContext) =>
{
    var redirectUri = NormalizeLocalPath(returnUrl);
    var providerId = httpContext.User.FindFirstValue(ClaimTypes.AuthenticationMethod);

    // Always clear the local session cookie first.
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    // Federated sign-out: redirect through the Entra end_session endpoint only when
    // the OIDC scheme is registered. If the user signed in via Microsoft but the config
    // was removed, fall through to a local redirect instead of throwing.
    if (string.Equals(providerId, MicrosoftEntraAuthService.ProviderId, StringComparison.OrdinalIgnoreCase) &&
        microsoftOidcRegistered)
    {
        return Results.SignOut(
            new AuthenticationProperties
            {
                RedirectUri = redirectUri
            },
            authenticationSchemes: [MicrosoftEntraAuthService.AuthenticationScheme]);
    }

    return Results.LocalRedirect(redirectUri);
});

app.MapDefaultEndpoints();
// Add this after the existing endpoint mappings

app.MapGet("/health", () => Results.Ok("Healthy"));

await app.RunAsync();

// Simplified database initialization method
static async Task InitializeDatabaseAsync(IServiceProvider services, string dataDirectory)
{
    try
    {
        // Create data directory if it doesn't exist
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
            Console.WriteLine($"Created data directory: {dataDirectory}");
        }

        // Initialize database with EF Core
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
        
        // Ensure database schema is created
        await context.Database.EnsureCreatedAsync();
        Console.WriteLine("? Database schema initialized successfully");

        // Test database connection
        var canConnect = await context.Database.CanConnectAsync();
        if (canConnect)
        {
            Console.WriteLine("✓ Database connection test successful");
            
            // Show current database stats
            var fileCount = await context.Datasources.CountAsync();
            var pageCount = await context.DatasourcePages.CountAsync();
            
            Console.WriteLine($"Database initialized with:");
            Console.WriteLine($"  - {fileCount} datasources in datasources table");
            Console.WriteLine($"  - {pageCount} datasource pages");
        }
        else
        {
            Console.WriteLine("⚠ Warning: Database connection test failed");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing database: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        throw; // Re-throw to prevent application startup if database initialization fails
    }
}

static string ResolveContentRootPath(string? configuredPath, string contentRootPath, string defaultRelativePath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath)
        ? defaultRelativePath
        : configuredPath;

    return Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(Path.Combine(contentRootPath, path));
}

static string NormalizeLocalPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return "/";
    }

    return path.StartsWith("/", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal)
        ? path
        : "/";
}

static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user)
{
    var identity = new ClaimsIdentity(authenticationType: CookieAuthenticationDefaults.AuthenticationScheme);
    AuthenticatedUserClaims.AddClaims(identity, user);

    return new ClaimsPrincipal(identity);
}

internal sealed record MockAuthSessionRequest(string ProviderId, string UserId);

