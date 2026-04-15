extern alias web;

using System.Security.Claims;
using System.Reflection;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticatedUserClaims = web::AspireApp.Web.Services.AuthenticatedUserClaims;
using AutomaticProcessingDispatchResult = web::AspireApp.Web.Services.AutomaticProcessingDispatchResult;
using FileMetadata = web::AspireApp.Web.Data.FileMetadata;
using FileStorageService = web::AspireApp.Web.Shared.FileStorageService;
using FileUploadController = web::AspireApp.Web.Controllers.FileUploadController;
using IDocumentProcessingCoordinator = web::AspireApp.Web.Services.IDocumentProcessingCoordinator;
using LocalAuthService = web::AspireApp.Web.Services.LocalAuthService;
using Tenant = web::AspireApp.Web.Data.Tenant;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using TenantMembership = web::AspireApp.Web.Data.TenantMembership;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;
using UrlUploadRequest = web::AspireApp.Web.Controllers.UrlUploadRequest;

namespace AspireApp.WebTest.Tests;

public sealed class FileUploadControllerTests
{
    [Fact]
    public async Task GetUploadedFiles_ReturnsForbidden_WhenHeaderTenantIsNotMember()
    {
        await using var context = CreateDbContext();
        var allowedTenantId = "tenant-allowed";
        var blockedTenantId = "tenant-blocked";
        var currentUser = SeedTenantMembership(context, allowedTenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            var controller = CreateController(context, currentUser, dataDirectory);
            controller.Request.Headers["X-Tenant-Id"] = blockedTenantId;

            var result = await controller.GetUploadedFiles(TestContext.Current.CancellationToken);

            var forbidden = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task UploadFile_ReturnsForbidden_WhenHeaderTenantIsNotMember()
    {
        await using var context = CreateDbContext();
        var allowedTenantId = "tenant-allowed";
        var blockedTenantId = "tenant-blocked";
        var currentUser = SeedTenantMembership(context, allowedTenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            var controller = CreateController(context, currentUser, dataDirectory);
            controller.Request.Headers["X-Tenant-Id"] = blockedTenantId;

            await using var stream = new MemoryStream([1, 2, 3, 4]);
            IFormFile file = new FormFile(stream, 0, stream.Length, "file", "notes.txt");

            var result = await controller.UploadFile(file, TestContext.Current.CancellationToken);

            var forbidden = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task UploadUrl_ReturnsForbidden_WhenHeaderTenantIsNotMember()
    {
        await using var context = CreateDbContext();
        var allowedTenantId = "tenant-allowed";
        var blockedTenantId = "tenant-blocked";
        var currentUser = SeedTenantMembership(context, allowedTenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            var controller = CreateController(context, currentUser, dataDirectory);
            controller.Request.Headers["X-Tenant-Id"] = blockedTenantId;

            var result = await controller.UploadUrl(
                new UrlUploadRequest { Url = "https://blocked.example/path" },
                TestContext.Current.CancellationToken);

            var forbidden = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task GetUploadedFiles_UsesDefaultTenant_WhenHeaderMissing()
    {
        await using var context = CreateDbContext();
        var allowedTenantId = "tenant-allowed";
        var otherTenantId = "tenant-other";
        var currentUser = SeedTenantMembership(context, allowedTenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            context.Datasources.AddRange(
                new FileMetadata
                {
                    FileName = "allowed.txt",
                    OriginalFileName = "allowed.txt",
                    FilePath = string.Empty,
                    FileHash = "HASH-ALLOWED",
                    SourceType = "url",
                    SourceUrl = "https://allowed.example",
                    Status = "uploaded",
                    TenantId = allowedTenantId
                },
                new FileMetadata
                {
                    FileName = "other.txt",
                    OriginalFileName = "other.txt",
                    FilePath = string.Empty,
                    FileHash = "HASH-OTHER",
                    SourceType = "url",
                    SourceUrl = "https://other.example",
                    Status = "uploaded",
                    TenantId = otherTenantId
                });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var controller = CreateController(context, currentUser, dataDirectory);

            var result = await controller.GetUploadedFiles(TestContext.Current.CancellationToken);

            var ok = Assert.IsType<OkObjectResult>(result);
            var files = Assert.IsAssignableFrom<IEnumerable<FileMetadata>>(GetAnonymousProperty(ok.Value!, "files"));
            var fileList = files.ToList();

            Assert.Single(fileList);
            Assert.Equal(allowedTenantId, fileList[0].TenantId);
            Assert.Equal("allowed.txt", fileList[0].FileName);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task DeleteFileAsync_ReturnsFalse_WhenTenantDoesNotMatch()
    {
        await using var context = CreateDbContext();
        var dataDirectory = CreateDataDirectory();

        try
        {
            var storage = new FileStorageService(
                context,
                NullLogger<FileStorageService>.Instance,
                dataDirectory);

            context.Datasources.Add(new FileMetadata
            {
                FileName = "tenant-only-url",
                OriginalFileName = "tenant-only-url",
                FilePath = string.Empty,
                FileHash = "HASH-DELETE",
                SourceType = "url",
                SourceUrl = "https://delete.example",
                Status = "uploaded",
                TenantId = "tenant-allowed"
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var fileId = await context.Datasources
                .Select(file => file.Id)
                .SingleAsync(TestContext.Current.CancellationToken);

            var blockedDelete = await storage.DeleteFileAsync(fileId, "tenant-blocked", TestContext.Current.CancellationToken);
            var allowedDelete = await storage.DeleteFileAsync(fileId, "tenant-allowed", TestContext.Current.CancellationToken);

            Assert.False(blockedDelete);
            Assert.True(allowedDelete);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task UploadFile_QueuesAutomaticProcessing()
    {
        await using var context = CreateDbContext();
        var tenantId = "tenant-allowed";
        var currentUser = SeedTenantMembership(context, tenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            var processingCoordinator = new FakeDocumentProcessingCoordinator();
            var controller = CreateController(context, currentUser, dataDirectory, processingCoordinator);

            await using var stream = new MemoryStream([1, 2, 3, 4]);
            IFormFile file = new FormFile(stream, 0, stream.Length, "file", "notes.txt");

            var result = await controller.UploadFile(file, TestContext.Current.CancellationToken);

            var ok = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
            var storedFileId = await context.Datasources.Select(fileRecord => fileRecord.Id).SingleAsync(TestContext.Current.CancellationToken);
            await WaitForQueuedDocumentAsync(processingCoordinator, storedFileId);
            Assert.Equal([storedFileId], processingCoordinator.QueuedDocumentIds);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task DeleteFile_CleansProcessedArtifactsBeforeRemovingMetadata()
    {
        await using var context = CreateDbContext();
        var tenantId = "tenant-allowed";
        var currentUser = SeedTenantMembership(context, tenantId);
        var dataDirectory = CreateDataDirectory();

        try
        {
            var processingCoordinator = new FakeDocumentProcessingCoordinator();
            context.Datasources.Add(new FileMetadata
            {
                FileName = "processed.pdf",
                OriginalFileName = "processed.pdf",
                FilePath = dataDirectory,
                FileHash = "HASH-PROCESSED",
                SourceType = "upload",
                Status = "processed",
                TenantId = tenantId,
                DoclingDocumentPath = Path.Combine(dataDirectory, "processed", "documents", "1", "document.json")
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var fileId = await context.Datasources.Select(file => file.Id).SingleAsync(TestContext.Current.CancellationToken);

            var controller = CreateController(context, currentUser, dataDirectory, processingCoordinator);
            var result = await controller.DeleteFile(fileId, TestContext.Current.CancellationToken);

            var ok = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
            Assert.Equal([fileId], processingCoordinator.CleanedDocumentIds);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
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

    private static FileUploadController CreateController(
        UploadDbContext context,
        AuthenticatedUser currentUser,
        string dataDirectory,
        IDocumentProcessingCoordinator? processingCoordinator = null)
    {
        var storage = new FileStorageService(
            context,
            NullLogger<FileStorageService>.Instance,
            dataDirectory,
            processingCoordinator);
        var tenantManagement = new TenantManagementService(
            context,
            NullLogger<TenantManagementService>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileUpload:DataDirectory"] = dataDirectory
            })
            .Build();
        var controller = new FileUploadController(
            storage,
            tenantManagement,
            NullLogger<FileUploadController>.Instance,
            configuration,
            new NullHostApplicationLifetime());

        var identity = new ClaimsIdentity(authenticationType: "Test");
        AuthenticatedUserClaims.AddClaims(identity, currentUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private static object GetAnonymousProperty(object instance, string propertyName)
    {
        return instance.GetType()
                   .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                   ?.GetValue(instance)
               ?? throw new InvalidOperationException($"Property '{propertyName}' was not found.");
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
            "FileUploadControllerTests",
            Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task WaitForQueuedDocumentAsync(FakeDocumentProcessingCoordinator processingCoordinator, int documentId, int timeoutMs = 2_000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (processingCoordinator.QueuedDocumentIds.Contains(documentId))
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out waiting for automatic processing to queue document {documentId}.");
    }

    private sealed class FakeDocumentProcessingCoordinator : IDocumentProcessingCoordinator
    {
        public List<int> QueuedDocumentIds { get; } = [];

        public List<int> CleanedDocumentIds { get; } = [];

        public Task<AutomaticProcessingDispatchResult> TryStartProcessingAsync(int documentId, CancellationToken cancellationToken = default)
        {
            QueuedDocumentIds.Add(documentId);
            return Task.FromResult(new AutomaticProcessingDispatchResult(true, true, "queued"));
        }

        public Task CleanupDocumentAsync(int documentId, CancellationToken cancellationToken = default)
        {
            CleanedDocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class NullHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
