extern alias web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LocalAuthBootstrapper = web::AspireApp.Web.Services.LocalAuthBootstrapper;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;
using LocalAuthenticationSeedUser = web::AspireApp.Web.Services.LocalAuthenticationSeedUser;
using LocalAuthUser = web::AspireApp.Web.Data.LocalAuthUser;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public sealed class LocalAuthBootstrapperTests
{
    [Fact]
    public async Task InitializeAsync_SeedsMissingUsers_AndBackfillsRealDefaultTenants()
    {
        await using var context = CreateDbContext();
        context.LocalAuthUsers.Add(new LocalAuthUser
        {
            Username = "existing-user",
            NormalizedUsername = "EXISTING-USER",
            Email = "existing@aspire.test",
            NormalizedEmail = "EXISTING@ASPIRE.TEST",
            DisplayName = "Existing User",
            PasswordHash = "existing-hash",
            DefaultTenantId = "default",
            IsActive = true
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var bootstrapper = new LocalAuthBootstrapper(
            context,
            Options.Create(new LocalAuthenticationOptions
            {
                SeedUsers =
                [
                    new LocalAuthenticationSeedUser
                    {
                        Username = "existing-user",
                        Email = "existing@aspire.test",
                        DisplayName = "Changed Display Name",
                        DefaultTenantId = "tenant-a",
                        PasswordHash = "new-hash"
                    },
                    new LocalAuthenticationSeedUser
                    {
                        Username = "seeded-admin",
                        Email = "seeded-admin@aspire.test",
                        DisplayName = "Seeded Admin",
                        DefaultTenantId = "tenant-b",
                        PasswordHash = "seeded-hash"
                    }
                ]
            }),
            new TenantManagementService(context, NullLogger<TenantManagementService>.Instance));

        await bootstrapper.InitializeAsync(TestContext.Current.CancellationToken);

        var users = await context.LocalAuthUsers
            .OrderBy(user => user.Username)
            .ToListAsync(TestContext.Current.CancellationToken);
        var tenants = await context.Tenants
            .OrderBy(tenant => tenant.OwnerUserId)
            .ToListAsync(TestContext.Current.CancellationToken);
        var memberships = await context.TenantMemberships
            .OrderBy(membership => membership.UserId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, users.Count);
        Assert.Equal("existing-hash", users[0].PasswordHash);
        Assert.Equal("Existing User", users[0].DisplayName);
        Assert.Equal("seeded-hash", users[1].PasswordHash);

        Assert.Equal(2, tenants.Count);
        Assert.Equal(2, memberships.Count);

        foreach (var user in users)
        {
            Assert.False(string.IsNullOrWhiteSpace(user.DefaultTenantId));
            Assert.NotEqual("default", user.DefaultTenantId);
            Assert.NotEqual("tenant-a", user.DefaultTenantId);
            Assert.NotEqual("tenant-b", user.DefaultTenantId);

            var expectedUserId = $"local-{user.Id}";
            var tenant = Assert.Single(tenants, existing => existing.Id == user.DefaultTenantId);
            var membership = Assert.Single(memberships, existing => existing.UserId == expectedUserId);

            Assert.Equal(expectedUserId, tenant.OwnerUserId);
            Assert.True(tenant.IsProtected);
            Assert.True(membership.IsDefault);
            Assert.Equal(user.DefaultTenantId, membership.TenantId);
        }
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }
}
