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
using FileStorageService = web::AspireApp.Web.Shared.FileStorageService;
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
                dataDirectory);

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
}
