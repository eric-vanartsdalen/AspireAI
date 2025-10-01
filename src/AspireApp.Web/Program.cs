using AspireApp.Web;
using AspireApp.Web.Components;
using AspireApp.Web.Components.Shared;
using AspireApp.Web.Components.Pages;
using AspireApp.Web.Data;
using Microsoft.EntityFrameworkCore;

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

// ADDING CONFIGURATIONS FOR STORAGE OF FILES
// Configure SQLite database (and location)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=../database/data-resources.db";
builder.Services.AddDbContext<UploadDbContext>(options =>
    options.UseSqlite(connectionString));

// Register the DocumentBridgeService first (required by FileStorageService)
builder.Services.AddScoped<DocumentBridgeService>();

// Register the FileStorageService with data directory and bridge service
var fileUploadDataDir = builder.Configuration.GetValue<string>("FileUpload:DataDirectory");
var dataDirectory = !string.IsNullOrEmpty(fileUploadDataDir)
    ? Path.IsPathRooted(fileUploadDataDir)
        ? fileUploadDataDir
        : Path.Combine(builder.Environment.ContentRootPath, fileUploadDataDir ?? string.Empty)
    : Path.Combine(builder.Environment.ContentRootPath, "data");

builder.Services.AddScoped<FileStorageService>(sp =>
    new FileStorageService(
        sp.GetRequiredService<UploadDbContext>(),
        sp.GetRequiredService<ILogger<FileStorageService>>(),
        dataDirectory,
        sp.GetRequiredService<DocumentBridgeService>()));

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

// Initialize database and directories with enhanced bridge support
await InitializeDatabaseAsync(app.Services, connectionString, dataDirectory);

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

// Map API controllers
app.MapControllers();

app.MapDefaultEndpoints();
// Add this after the existing endpoint mappings

app.MapGet("/health", () => Results.Ok("Healthy"));

await app.RunAsync();

// Enhanced database and directory initialization method
static async Task InitializeDatabaseAsync(IServiceProvider services, string connectionString, string dataDirectory)
{
    try
    {
        // Create database directory if it doesn't exist
        var dbPath = connectionString.Replace("Data Source=", "").Trim();
        
        // Make database path absolute if it's relative
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.GetFullPath(dbPath);
        }
        
        var dbDirectory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
            Console.WriteLine($"Created database directory: {dbDirectory}");
        }

        // Create data directory if it doesn't exist
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
            Console.WriteLine($"Created data directory: {dataDirectory}");
        }

        // Initialize database with enhanced schema support
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<UploadDbContext>();
        var bridgeService = scope.ServiceProvider.GetRequiredService<DocumentBridgeService>();
        
        // Ensure database schema is created (includes both original and new tables)
        var schemaInitialized = await bridgeService.EnsureDatabaseSchemaAsync();
        if (schemaInitialized)
        {
            Console.WriteLine($"Database schema initialized successfully at: {dbPath}");
        }
        else
        {
            Console.WriteLine($"Warning: Database schema initialization had issues");
        }

        // Test database connection
        var canConnect = await context.Database.CanConnectAsync();
        if (canConnect)
        {
            Console.WriteLine("Database connection test successful");
            
            // Test queries on both table systems
            var fileCount = await context.Files.CountAsync();
            var documentCount = await context.Documents.CountAsync();
            var processedDocCount = await context.ProcessedDocuments.CountAsync();
            var pageCount = await context.DocumentPages.CountAsync();
            
            Console.WriteLine($"Database initialized with:");
            Console.WriteLine($"  - {fileCount} files in Files table");
            Console.WriteLine($"  - {documentCount} documents in Documents table");
            Console.WriteLine($"  - {processedDocCount} processed documents");
            Console.WriteLine($"  - {pageCount} document pages");

            // Perform initial sync if needed
            if (fileCount > documentCount)
            {
                var syncedCount = await bridgeService.SyncFileMetadataToDocumentsAsync();
                Console.WriteLine($"Synced {syncedCount} files to Documents table");
            }

            // Perform health check
            var healthCheck = await bridgeService.PerformHealthCheckAsync();
            if (healthCheck.OverallHealthy)
            {
                Console.WriteLine("? Document bridge system is healthy");
            }
            else
            {
                Console.WriteLine($"?? Document bridge system health issues: {healthCheck.ErrorMessage}");
            }
        }
        else
        {
            Console.WriteLine("Warning: Database connection test failed");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error initializing database: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        throw; // Re-throw to prevent application startup if database initialization fails
    }
}
