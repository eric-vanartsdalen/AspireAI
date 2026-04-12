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

public sealed class LocalAccountSelfProvisioningTests
{
    [Fact]
    public async Task AuthenticateAsync_CreatesUser_WhenUnknownUsernameHasValidPassword_AndSelfRegistrationEnabled()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("new-user", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        var savedUser = await context.LocalAuthUsers.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("new-user", savedUser.Username);
        Assert.NotEqual("LongEnough123!", savedUser.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(savedUser, savedUser.PasswordHash, "LongEnough123!"));
    }

    [Fact]
    public async Task AuthenticateAsync_AllowsImmediateSubsequentLogin_ForAutoCreatedUser()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        var createdUser = await authenticator.AuthenticateAsync("repeat-user", "LongEnough123!", TestContext.Current.CancellationToken);
        var secondLogin = await authenticator.AuthenticateAsync("repeat-user", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.NotNull(createdUser);
        Assert.NotNull(secondLogin);
        Assert.Equal(createdUser.UserId, secondLogin.UserId);
        Assert.Equal(1, await context.LocalAuthUsers.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Tenants.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.TenantMemberships.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_ReusesExistingUser_WhenUsernameCasingDiffers()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        var createdUser = await authenticator.AuthenticateAsync("Mixed.User-01", "LongEnough123!", TestContext.Current.CancellationToken);
        var secondLogin = await authenticator.AuthenticateAsync("mixed.user-01", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.NotNull(createdUser);
        Assert.NotNull(secondLogin);
        Assert.Equal(createdUser.UserId, secondLogin.UserId);
        Assert.Equal(1, await context.LocalAuthUsers.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_AssignsDerivedFields_ForAutoCreatedUser()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        await authenticator.AuthenticateAsync("  Mixed.User-01  ", "LongEnough123!", TestContext.Current.CancellationToken);

        var savedUser = await context.LocalAuthUsers.SingleAsync(TestContext.Current.CancellationToken);
        var tenant = await context.Tenants.SingleAsync(TestContext.Current.CancellationToken);
        var membership = await context.TenantMemberships.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Mixed.User-01", savedUser.Username);
        Assert.Equal("MIXED.USER-01", savedUser.NormalizedUsername);
        Assert.Equal("MIXED.USER-01@local.aspireai", savedUser.Email);
        Assert.Equal("MIXED.USER-01@LOCAL.ASPIREAI", savedUser.NormalizedEmail);
        Assert.Equal("Mixed.User-01", savedUser.DisplayName);
        Assert.Equal(tenant.Id, savedUser.DefaultTenantId);
        Assert.True(tenant.IsProtected);
        Assert.True(membership.IsDefault);
        Assert.True(savedUser.IsActive);
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenUnknownIdentifierIsEmailShaped()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("unknown@example.com", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, await context.LocalAuthUsers.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenUsernameContainsInvalidCharacters()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("bad user!", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, await context.LocalAuthUsers.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthenticateAsync_ReturnsNull_WhenDuplicateSaveRaceOccurs()
    {
        await using var context = CreateDuplicateRaceDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = CreateAuthenticator(context, passwordHasher);

        var result = await authenticator.AuthenticateAsync("race-user", "LongEnough123!", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(context.ChangeTracker.Entries<LocalAuthUser>());
        Assert.Equal(0, await context.LocalAuthUsers.CountAsync(TestContext.Current.CancellationToken));
    }

    private static LocalAccountAuthenticator CreateAuthenticator(
        UploadDbContext context,
        PasswordHasher<LocalAuthUser> passwordHasher)
    {
        var tenantManagementService = new TenantManagementService(
            context,
            NullLogger<TenantManagementService>.Instance);

        return new LocalAccountAuthenticator(
            context,
            passwordHasher,
            Options.Create(new LocalAuthenticationOptions
            {
                Enabled = true,
                AllowSelfRegistration = true
            }),
            tenantManagementService);
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private static UploadDbContext CreateDuplicateRaceDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new DuplicateRaceUploadDbContext(options);
    }

    private sealed class DuplicateRaceUploadDbContext(
        DbContextOptions<UploadDbContext> options) : UploadDbContext(options)
    {
        private bool _hasThrown;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new DbUpdateException("Simulated unique constraint violation.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
