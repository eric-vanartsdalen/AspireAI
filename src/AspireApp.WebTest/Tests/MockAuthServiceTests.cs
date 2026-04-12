extern alias web;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using AuthenticationContext = web::AspireApp.Web.Services.AuthenticationContext;
using AppAuthenticationStateProvider = web::AspireApp.Web.Services.AppAuthenticationStateProvider;
using MockAuthService = web::AspireApp.Web.Services.MockAuthService;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;
using TenantContextService = web::AspireApp.Web.Services.TenantContextService;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public class MockAuthServiceTests
{
    [Fact]
    public async Task SignInAsync_SetsAuthenticatedUser_And_SeedsTenant()
    {
        var authenticationContext = new AuthenticationContext();
        await using var dbContext = CreateDbContext();
        var tenantManagementService = new TenantManagementService(dbContext, NullLogger<TenantManagementService>.Instance);
        var tenantContext = new TenantContextService(tenantManagementService, authenticationContext);
        var authenticationStateProvider = new AppAuthenticationStateProvider(authenticationContext, tenantContext, new HttpContextAccessor());
        var navigationManager = new TestNavigationManager();
        var service = new MockAuthService(authenticationStateProvider, tenantContext, navigationManager);

        await service.SignInAsync("demo", "demo-taylor-jones", "/chat", TestContext.Current.CancellationToken);

        Assert.True(authenticationContext.IsAuthenticated);
        Assert.Equal("Taylor Jones", authenticationContext.CurrentUser?.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(tenantContext.CurrentTenantId));
        Assert.EndsWith("/auth/mock/signin?providerId=demo&userId=demo-taylor-jones&returnUrl=%2Fchat", navigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOutAsync_ClearsUser_And_ResetsTenant()
    {
        var authenticationContext = new AuthenticationContext();
        await using var dbContext = CreateDbContext();
        var tenantManagementService = new TenantManagementService(dbContext, NullLogger<TenantManagementService>.Instance);
        var tenantContext = new TenantContextService(tenantManagementService, authenticationContext);
        var authenticationStateProvider = new AppAuthenticationStateProvider(authenticationContext, tenantContext, new HttpContextAccessor());
        var navigationManager = new TestNavigationManager();
        var service = new MockAuthService(authenticationStateProvider, tenantContext, navigationManager);

        await service.SignInAsync("demo", "demo-taylor-jones", "/chat", TestContext.Current.CancellationToken);
        await service.SignOutAsync("/", TestContext.Current.CancellationToken);

        Assert.EndsWith("/auth/mock/signout?returnUrl=%2F", navigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_RejectsUnknownUser()
    {
        var authenticationContext = new AuthenticationContext();
        await using var dbContext = CreateDbContext();
        var tenantManagementService = new TenantManagementService(dbContext, NullLogger<TenantManagementService>.Instance);
        var tenantContext = new TenantContextService(tenantManagementService, authenticationContext);
        var authenticationStateProvider = new AppAuthenticationStateProvider(authenticationContext, tenantContext, new HttpContextAccessor());
        var navigationManager = new TestNavigationManager();
        var service = new MockAuthService(authenticationStateProvider, tenantContext, navigationManager);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SignInAsync("demo", "not-a-user", "/chat", TestContext.Current.CancellationToken));
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
