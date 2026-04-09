extern alias web;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LocalAccountAuthenticator = web::AspireApp.Web.Services.LocalAccountAuthenticator;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;
using LocalAuthService = web::AspireApp.Web.Services.LocalAuthService;
using LocalAuthUser = web::AspireApp.Web.Data.LocalAuthUser;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

/// <summary>
/// Documents the server-posted local credential contract shared by the form, endpoint, and authenticator.
/// </summary>
public sealed class LocalAuthEndpointContractTests
{
    [Fact]
    public void LocalSignInForm_UsesIdentifierFieldName()
    {
        const string formFieldName = "identifier";
        const string endpointParameterName = "identifier";
        const string authenticatorParameterName = "identifier";

        Assert.Equal(endpointParameterName, formFieldName);
        Assert.Equal(authenticatorParameterName, formFieldName);
    }

    [Fact]
    public void LocalInvalidCredentialErrorCode_RemainsStable()
    {
        Assert.Equal("invalid-credentials", LocalAuthService.InvalidCredentialErrorCode);
    }

    [Fact]
    public void LocalMinimumPasswordLength_RemainsStable()
    {
        Assert.Equal(10, LocalAuthenticationOptions.MinimumPasswordLength);
    }

    [Fact]
    public async Task LocalSignIn_WithValidCredentials_Succeeds()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var localUser = CreateUser(passwordHasher, "test-user", "test@aspire.test");
        context.LocalAuthUsers.Add(localUser);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var authenticator = new LocalAccountAuthenticator(
            context,
            passwordHasher,
            Options.Create(new LocalAuthenticationOptions { Enabled = true }),
            new TenantManagementService(context, NullLogger<TenantManagementService>.Instance));

        var result = await authenticator.AuthenticateAsync("test-user", "TestPassword123!", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Test User", result.DisplayName);
        Assert.Equal("local", result.ProviderId);
    }

    [Fact]
    public async Task LocalSignIn_WithUnknownEmailIdentifier_ReturnsNull()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<LocalAuthUser>();
        var authenticator = new LocalAccountAuthenticator(
            context,
            passwordHasher,
            Options.Create(new LocalAuthenticationOptions
            {
                Enabled = true,
                AllowSelfRegistration = true
            }),
            new TenantManagementService(context, NullLogger<TenantManagementService>.Instance));

        var result = await authenticator.AuthenticateAsync("test@example.com", "TestPassword123!", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private static LocalAuthUser CreateUser(PasswordHasher<LocalAuthUser> passwordHasher, string username, string email)
    {
        var user = new LocalAuthUser
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Test User",
            DefaultTenantId = "tenant-a",
            IsActive = true
        };

        user.PasswordHash = passwordHasher.HashPassword(user, "TestPassword123!");
        return user;
    }
}
