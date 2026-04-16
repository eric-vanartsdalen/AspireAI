extern alias web;

using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticationContext = web::AspireApp.Web.Services.AuthenticationContext;
using AutomaticProcessingDispatchResult = web::AspireApp.Web.Services.AutomaticProcessingDispatchResult;
using FileStorageService = web::AspireApp.Web.Shared.FileStorageService;
using IDocumentProcessingCoordinator = web::AspireApp.Web.Services.IDocumentProcessingCoordinator;
using LocalAuthService = web::AspireApp.Web.Services.LocalAuthService;
using Tenant = web::AspireApp.Web.Data.Tenant;
using TenantContextService = web::AspireApp.Web.Services.TenantContextService;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using TenantMembership = web::AspireApp.Web.Data.TenantMembership;
using UploadData = web::AspireApp.Web.Components.Pages.UploadData;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;
using XunitTestContext = Xunit.TestContext;

namespace AspireApp.WebTest.Tests;

public sealed class UploadDataTests : IDisposable
{
    private readonly BunitContext _testContext = new();

    [Fact]
    public async Task UploadFiles_PersistsSelectedFile_ForCurrentTenant()
    {
        await using var context = CreateDbContext();
        var tenantId = "tenant-allowed";
        var currentUser = SeedTenantMembership(context, tenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            var processingCoordinator = new FakeDocumentProcessingCoordinator();
            var authenticationContext = new AuthenticationContext();
            authenticationContext.SetCurrentUser(currentUser);

            var tenantManagement = new TenantManagementService(
                context,
                NullLogger<TenantManagementService>.Instance);
            var tenantContext = new TenantContextService(tenantManagement, authenticationContext);
            await tenantContext.InitializeForUserAsync(currentUser, XunitTestContext.Current.CancellationToken);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileUpload:MaxFileSize"] = "10485760"
                })
                .Build();

            var fileStorageService = new FileStorageService(
                context,
                NullLogger<FileStorageService>.Instance,
                dataDirectory,
                processingCoordinator);

            _testContext.Services.AddSingleton<IConfiguration>(configuration);
            _testContext.Services.AddSingleton(authenticationContext);
            _testContext.Services.AddSingleton(fileStorageService);
            _testContext.Services.AddSingleton(tenantContext);
            _testContext.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<UploadData>>(NullLogger<UploadData>.Instance);

            var cut = _testContext.Render<UploadData>();

            SetSelectedBrowserFile(cut.Instance, new TestBrowserFile("notes.txt", "text/plain", [1, 2, 3, 4]));
            await InvokeUploadAsync(cut);

            var storedFile = await context.Datasources.SingleAsync(XunitTestContext.Current.CancellationToken);
            Assert.Equal(tenantId, storedFile.TenantId);
            Assert.Equal("notes.txt", storedFile.OriginalFileName);
            Assert.Equal("upload", storedFile.SourceType);
            Assert.Equal("uploaded", storedFile.Status);
            Assert.True(File.Exists(Path.Combine(dataDirectory, storedFile.FileName)));
            Assert.Equal([storedFile.Id], processingCoordinator.QueuedDocumentIds);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Theory]
    [InlineData("url", "https://contoso.example/docs")]
    [InlineData("youtube_video", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("youtube_channel", "https://www.youtube.com/@happy-gilmore/videos")]
    public async Task UploadData_RendersWebSemantics_ForWebBackedSourceTypes(string sourceType, string sourceUrl)
    {
        await using var context = CreateDbContext();
        var tenantId = "tenant-allowed";
        var currentUser = SeedTenantMembership(context, tenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            context.Datasources.Add(new web::AspireApp.Web.Data.FileMetadata
            {
                FileName = "web-source",
                OriginalFileName = "web-source",
                FilePath = string.Empty,
                FileHash = "HASH-WEB",
                SourceType = sourceType,
                SourceUrl = sourceUrl,
                Status = "uploaded",
                TenantId = tenantId
            });
            await context.SaveChangesAsync(XunitTestContext.Current.CancellationToken);

            var cut = await RenderUploadDataAsync(context, currentUser, dataDirectory);

            cut.WaitForAssertion(() =>
            {
                var row = cut.Find("tbody tr");
                var badge = row.QuerySelector(".source-type-badge");
                var icon = row.QuerySelector("td.status-cell i");
                var urlCell = row.QuerySelector("td.url-cell");

                Assert.NotNull(badge);
                Assert.Contains("source-type-url", badge!.ClassName);
                Assert.Equal("WEB", badge.TextContent.Trim());

                Assert.NotNull(icon);
                Assert.Contains("bi-globe", icon!.ClassName);

                Assert.NotNull(urlCell);
                Assert.Contains(sourceUrl, urlCell!.TextContent);
            });
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    public void Dispose()
    {
        _testContext.Dispose();
    }

    private static async Task InvokeUploadAsync(IRenderedComponent<UploadData> cut)
    {
        var method = typeof(UploadData).GetMethod("UploadFiles", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate UploadData.UploadFiles for regression coverage.");

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        cut.Render();
    }

    private async Task<IRenderedComponent<UploadData>> RenderUploadDataAsync(
        UploadDbContext context,
        AuthenticatedUser currentUser,
        string dataDirectory,
        IDocumentProcessingCoordinator? processingCoordinator = null)
    {
        processingCoordinator ??= new FakeDocumentProcessingCoordinator();

        var authenticationContext = new AuthenticationContext();
        authenticationContext.SetCurrentUser(currentUser);

        var tenantManagement = new TenantManagementService(
            context,
            NullLogger<TenantManagementService>.Instance);
        var tenantContext = new TenantContextService(tenantManagement, authenticationContext);
        await tenantContext.InitializeForUserAsync(currentUser, XunitTestContext.Current.CancellationToken);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileUpload:MaxFileSize"] = "10485760"
            })
            .Build();

        var fileStorageService = new FileStorageService(
            context,
            NullLogger<FileStorageService>.Instance,
            dataDirectory,
            processingCoordinator);

        _testContext.Services.AddSingleton<IConfiguration>(configuration);
        _testContext.Services.AddSingleton(authenticationContext);
        _testContext.Services.AddSingleton(fileStorageService);
        _testContext.Services.AddSingleton(tenantContext);
        _testContext.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<UploadData>>(NullLogger<UploadData>.Instance);

        return _testContext.Render<UploadData>();
    }

    private static void SetSelectedBrowserFile(UploadData component, IBrowserFile browserFile)
    {
        var field = typeof(UploadData).GetField("_selectedBrowserFile", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate UploadData._selectedBrowserFile for regression coverage.");

        field.SetValue(component, browserFile);
    }

    private static AuthenticatedUser SeedTenantMembership(UploadDbContext context, string tenantId)
    {
        const string userId = "local-42";

        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Allowed workspace",
            OwnerUserId = userId,
            IsProtected = true
        });
        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = userId,
            IsDefault = true
        });
        context.SaveChanges();

        return new AuthenticatedUser(
            userId,
            "Local User",
            "local.user@aspire.test",
            LocalAuthService.ProviderId,
            LocalAuthService.ProviderDisplayName,
            tenantId);
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private static string CreateDataDirectory()
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestResults",
            "UploadDataTests",
            Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class TestBrowserFile(string name, string contentType, byte[] content) : IBrowserFile
    {
        private readonly byte[] _content = content;

        public string Name { get; } = name;

        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;

        public long Size => _content.LongLength;

        public string ContentType { get; } = contentType;

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Size > maxAllowedSize)
            {
                throw new IOException($"File size {Size} exceeds the configured limit {maxAllowedSize}.");
            }

            return new MemoryStream(_content, writable: false);
        }
    }

    private sealed class FakeDocumentProcessingCoordinator : IDocumentProcessingCoordinator
    {
        public List<int> QueuedDocumentIds { get; } = [];

        public Task<AutomaticProcessingDispatchResult> TryStartProcessingAsync(int documentId, CancellationToken cancellationToken = default)
        {
            QueuedDocumentIds.Add(documentId);
            return Task.FromResult(new AutomaticProcessingDispatchResult(true, true, "queued"));
        }

        public Task CleanupDocumentAsync(int documentId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
