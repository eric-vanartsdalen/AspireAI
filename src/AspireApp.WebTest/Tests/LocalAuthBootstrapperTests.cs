extern alias web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LocalAuthBootstrapper = web::AspireApp.Web.Services.LocalAuthBootstrapper;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;
using LocalAuthenticationSeedUser = web::AspireApp.Web.Services.LocalAuthenticationSeedUser;
using LocalAuthUser = web::AspireApp.Web.Data.LocalAuthUser;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public sealed class LocalAuthBootstrapperTests
{
    [Fact]
    public async Task InitializeAsync_SeedsMissingUsers_WithoutOverwritingExistingRows()
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
            }));

        await bootstrapper.InitializeAsync(TestContext.Current.CancellationToken);

        var users = await context.LocalAuthUsers
            .OrderBy(user => user.Username)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, users.Count);
        Assert.Equal("existing-hash", users[0].PasswordHash);
        Assert.Equal("Existing User", users[0].DisplayName);
        Assert.Equal("seeded-hash", users[1].PasswordHash);
        Assert.Equal("tenant-b", users[1].DefaultTenantId);
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }
}
