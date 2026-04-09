extern alias web;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LocalAccountAuthenticator = web::AspireApp.Web.Services.LocalAccountAuthenticator;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;
using LocalAuthUser = web::AspireApp.Web.Data.LocalAuthUser;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public sealed class LocalAccountAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_ReturnsUser_WhenUsernameMatchesIgnoringCase()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var localUser = CreateUser(passwordHasher, "local-admin", "local-admin@aspire.test");
        context.LocalAuthUsers.Add(localUser);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("LOCAL-ADMIN", "CorrectHorseBatteryStaple!23", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Local Admin", result.DisplayName);
        Assert.Equal("local", result.ProviderId);

        Assert.False(string.IsNullOrWhiteSpace(result.DefaultTenantId));
        var tenant = await context.Tenants.SingleAsync(TestContext.Current.CancellationToken);
        var membership = await context.TenantMemberships.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(result.DefaultTenantId, tenant.Id);
        Assert.Equal($"local-{localUser.Id}", tenant.OwnerUserId);
        Assert.True(tenant.IsProtected);
        Assert.True(membership.IsDefault);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsUser_WhenEmailMatches()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var localUser = CreateUser(passwordHasher, "local-admin", "local-admin@aspire.test");
        context.LocalAuthUsers.Add(localUser);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("LOCAL-ADMIN@ASPIRE.TEST", "CorrectHorseBatteryStaple!23", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("local-admin@aspire.test", result.Email);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenPasswordDoesNotMatch()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var localUser = CreateUser(passwordHasher, "local-admin", "local-admin@aspire.test");
        context.LocalAuthUsers.Add(localUser);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("local-admin", "wrong-password-value", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_ForUnknownUsername_WhenSelfRegistrationDisabled()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(
            context,
            passwordHasher,
            new LocalAuthenticationOptions
            {
                Enabled = true,
                AllowSelfRegistration = false
            });

        var result = await authenticator.AuthenticateAsync("new-user", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, await context.LocalAuthUsers.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsUser_WhenPasswordMatchesMinimumLength()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var localUser = CreateUser(passwordHasher, "local-admin", "local-admin@aspire.test", "Passcode10");
        context.LocalAuthUsers.Add(localUser);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("local-admin", "Passcode10", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenPasswordIsShorterThanMinimum()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var localUser = CreateUser(passwordHasher, "local-admin", "local-admin@aspire.test", "Passcode9");
        context.LocalAuthUsers.Add(localUser);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("local-admin", "Passcode9", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    private static LocalAccountAuthenticator CreateAuthenticator(
        UploadDbContext context,
        PasswordHasher<LocalAuthUser> passwordHasher,
        LocalAuthenticationOptions? options = null)
    {
        var tenantManagementService = new TenantManagementService(
            context,
            NullLogger<TenantManagementService>.Instance);

        return new LocalAccountAuthenticator(
            context,
            passwordHasher,
            Options.Create(options ?? new LocalAuthenticationOptions { Enabled = true }),
            tenantManagementService);
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private static LocalAuthUser CreateUser(
        PasswordHasher<LocalAuthUser> passwordHasher,
        string username,
        string email,
        string password = "CorrectHorseBatteryStaple!23")
    {
        var user = new LocalAuthUser
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Local Admin",
            DefaultTenantId = "tenant-a",
            IsActive = true
        };

        user.PasswordHash = passwordHasher.HashPassword(user, password);
        return user;
    }
}
