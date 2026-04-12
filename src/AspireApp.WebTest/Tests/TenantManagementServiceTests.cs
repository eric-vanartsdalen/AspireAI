extern alias web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using LocalAuthUser = web::AspireApp.Web.Data.LocalAuthUser;
using Tenant = web::AspireApp.Web.Data.Tenant;
using TenantMembership = web::AspireApp.Web.Data.TenantMembership;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using TenantUserDescriptor = web::AspireApp.Web.Services.TenantUserDescriptor;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public sealed class TenantManagementServiceTests
{
    [Fact]
    public async Task CreateTenantAsync_CreatesNonProtectedOwnedTenant()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);

        var result = await service.CreateTenantAsync(
            "local-1",
            "Project Atlas",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Project Atlas", result.Name);
        Assert.True(result.IsOwner);
        Assert.False(result.IsProtected);
        Assert.False(result.IsDefault);
        Assert.True(await context.TenantMemberships.AnyAsync(
            membership => membership.TenantId == result.TenantId && membership.UserId == "local-1",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenameTenantAsync_ReturnsTrue_ForOwnedTenant()
    {
        await using var context = CreateDbContext();
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.RenameTenantAsync(
            "local-1",
            "tenant-alpha",
            "Project Atlas",
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(
            "Project Atlas",
            await context.Tenants
                .Where(tenant => tenant.Id == "tenant-alpha")
                .Select(tenant => tenant.Name)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteTenantAsync_ReturnsTrue_ForNonProtectedOwnedTenant()
    {
        await using var context = CreateDbContext();
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.DeleteTenantAsync(
            "local-1",
            "tenant-alpha",
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.False(await context.Tenants.AnyAsync(
            tenant => tenant.Id == "tenant-alpha",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMemberByUsernameAsync_ReturnsTrue_WhenEligibleUserExists()
    {
        await using var context = CreateDbContext();
        SeedLocalUser(context, 1, "owner");
        SeedLocalUser(context, 2, "collaborator");
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.AddMemberByUsernameAsync(
            "local-1",
            "tenant-alpha",
            "COLLABORATOR",
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.True(await context.TenantMemberships.AnyAsync(
            membership => membership.TenantId == "tenant-alpha" && membership.UserId == "local-2",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMemberByUsernameAsync_ReturnsFalse_WhenUsernameIsUnknown()
    {
        await using var context = CreateDbContext();
        SeedLocalUser(context, 1, "owner");
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.AddMemberByUsernameAsync(
            "local-1",
            "tenant-alpha",
            "missing-user",
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal(1, await context.TenantMemberships.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMemberByUsernameAsync_ReturnsFalse_WhenUserIsAlreadyMember()
    {
        await using var context = CreateDbContext();
        SeedLocalUser(context, 1, "owner");
        SeedLocalUser(context, 2, "collaborator");
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = "tenant-alpha",
            UserId = "local-2",
            IsDefault = false
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.AddMemberByUsernameAsync(
            "local-1",
            "tenant-alpha",
            "collaborator",
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal(2, await context.TenantMemberships.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMemberByUsernameAsync_ReturnsFalse_WhenOwnerAttemptsToAddSelf()
    {
        await using var context = CreateDbContext();
        SeedLocalUser(context, 1, "owner");
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.AddMemberByUsernameAsync(
            "local-1",
            "tenant-alpha",
            "owner",
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal(1, await context.TenantMemberships.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteTenantAsync_ReturnsFalse_ForProtectedTenant()
    {
        await using var context = CreateDbContext();
        SeedOwnedTenant(context, "tenant-default", "local-1", isProtected: true);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.DeleteTenantAsync(
            "local-1",
            "tenant-default",
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.True(await context.Tenants.AnyAsync(
            tenant => tenant.Id == "tenant-default",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnsureTenantAccessAsync_CreatesDefaultTenant_WhenUserHasNoMemberships()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);
        var user = new TenantUserDescriptor("local-1", "Test User", "test@aspire.test");

        var snapshot = await service.EnsureTenantAccessAsync(user, TestContext.Current.CancellationToken);

        var tenant = Assert.Single(snapshot.Tenants);
        Assert.True(tenant.IsDefault);
        Assert.True(tenant.IsProtected);
        Assert.True(tenant.IsOwner);
        Assert.Equal(snapshot.DefaultTenantId, tenant.TenantId);
        Assert.StartsWith("tenant-", tenant.TenantId);
    }

    [Fact]
    public async Task EnsureTenantAccessAsync_BackfillsDefaultTenant_WhenNoMembershipIsDefault()
    {
        await using var context = CreateDbContext();
        context.Tenants.Add(new Tenant
        {
            Id = "tenant-existing",
            Name = "Existing",
            OwnerUserId = "local-1",
            IsProtected = false
        });
        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = "tenant-existing",
            UserId = "local-1",
            IsDefault = false
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);
        var user = new TenantUserDescriptor("local-1", "Test User", "test@aspire.test");

        var snapshot = await service.EnsureTenantAccessAsync(user, TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Tenants.Count);
        var defaultTenant = Assert.Single(snapshot.Tenants, tenant => tenant.IsDefault);
        Assert.True(defaultTenant.IsProtected);
        Assert.Equal(snapshot.DefaultTenantId, defaultTenant.TenantId);
        Assert.NotEqual("tenant-existing", defaultTenant.TenantId);
    }

    [Fact]
    public async Task EnsureTenantAccessAsync_ResolvesMultipleDefaults_KeepingProtectedFirst()
    {
        await using var context = CreateDbContext();
        var now = DateTime.UtcNow;

        context.Tenants.Add(new Tenant
        {
            Id = "tenant-a",
            Name = "A",
            OwnerUserId = "local-1",
            IsProtected = true,
            CreatedAt = now
        });
        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = "tenant-a",
            UserId = "local-1",
            IsDefault = true,
            CreatedAt = now
        });
        context.Tenants.Add(new Tenant
        {
            Id = "tenant-b",
            Name = "B",
            OwnerUserId = "local-1",
            IsProtected = false,
            CreatedAt = now.AddMinutes(1)
        });
        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = "tenant-b",
            UserId = "local-1",
            IsDefault = true,
            CreatedAt = now.AddMinutes(1)
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);
        var user = new TenantUserDescriptor("local-1", "Test User", "test@aspire.test");

        var snapshot = await service.EnsureTenantAccessAsync(user, TestContext.Current.CancellationToken);

        var defaultTenant = Assert.Single(snapshot.Tenants, tenant => tenant.IsDefault);
        Assert.Equal("tenant-a", defaultTenant.TenantId);
        Assert.True(defaultTenant.IsProtected);
    }

    [Fact]
    public async Task EnsureTenantAccessAsync_ProtectsUnprotectedDefault()
    {
        await using var context = CreateDbContext();
        context.Tenants.Add(new Tenant
        {
            Id = "tenant-a",
            Name = "A",
            OwnerUserId = "local-1",
            IsProtected = false
        });
        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = "tenant-a",
            UserId = "local-1",
            IsDefault = true
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);
        var user = new TenantUserDescriptor("local-1", "Test User", "test@aspire.test");

        var snapshot = await service.EnsureTenantAccessAsync(user, TestContext.Current.CancellationToken);

        var defaultTenant = Assert.Single(snapshot.Tenants, tenant => tenant.IsDefault);
        Assert.True(defaultTenant.IsProtected);
        var persisted = await context.Tenants.SingleAsync(
            tenant => tenant.Id == "tenant-a",
            TestContext.Current.CancellationToken);
        Assert.True(persisted.IsProtected);
    }

    [Fact]
    public async Task EnsureTenantAccessAsync_ThrowsOnEmptyUserId()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);
        var user = new TenantUserDescriptor("", "Test User", "test@aspire.test");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EnsureTenantAccessAsync(user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMemberByUsernameAsync_ReturnsFalse_WhenSaveThrowsGenericException()
    {
        await using var context = CreateGenericSaveFailureDbContext();
        SeedLocalUser(context, 1, "owner");
        SeedLocalUser(context, 2, "collaborator");
        SeedOwnedTenant(context, "tenant-alpha", "local-1");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var result = await service.AddMemberByUsernameAsync(
            "local-1",
            "tenant-alpha",
            "collaborator",
            TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    private static TenantManagementService CreateService(UploadDbContext context) =>
        new(context, NullLogger<TenantManagementService>.Instance);

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private static void SeedLocalUser(UploadDbContext context, int id, string username)
    {
        context.LocalAuthUsers.Add(new LocalAuthUser
        {
            Id = id,
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            Email = $"{username}@aspire.test",
            NormalizedEmail = $"{username}@aspire.test".ToUpperInvariant(),
            DisplayName = username,
            PasswordHash = "hash",
            DefaultTenantId = string.Empty,
            IsActive = true
        });
    }

    private static void SeedOwnedTenant(UploadDbContext context, string tenantId, string ownerUserId, bool isProtected = false)
    {
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = tenantId,
            OwnerUserId = ownerUserId,
            IsProtected = isProtected
        });

        context.TenantMemberships.Add(new TenantMembership
        {
            TenantId = tenantId,
            UserId = ownerUserId,
            IsDefault = isProtected
        });
    }

    private static UploadDbContext CreateGenericSaveFailureDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new GenericSaveFailureDbContext(options);
    }

    private sealed class GenericSaveFailureDbContext(
        DbContextOptions<UploadDbContext> options) : UploadDbContext(options)
    {
        private int _saveCallCount;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCallCount++;
            if (_saveCallCount > 1)
            {
                throw new InvalidOperationException("Simulated generic save failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
