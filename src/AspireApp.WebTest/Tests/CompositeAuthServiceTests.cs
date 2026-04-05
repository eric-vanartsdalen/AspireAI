extern alias web;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using AppAuthenticationStateProvider = web::AspireApp.Web.Services.AppAuthenticationStateProvider;
using AuthenticationContext = web::AspireApp.Web.Services.AuthenticationContext;
using CompositeAuthService = web::AspireApp.Web.Services.CompositeAuthService;
using MicrosoftEntraAuthService = web::AspireApp.Web.Services.MicrosoftEntraAuthService;
using MicrosoftEntraAuthenticationOptions = web::AspireApp.Web.Services.MicrosoftEntraAuthenticationOptions;
using MockAuthService = web::AspireApp.Web.Services.MockAuthService;
using TenantContextService = web::AspireApp.Web.Services.TenantContextService;

namespace AspireApp.WebTest.Tests;

public class CompositeAuthServiceTests
{
    [Fact]
    public void GetProviders_ExposesLiveMicrosoftAlongsideDemoProviders_WhenConfigured()
    {
        var service = CreateService();

        var providers = service.GetProviders();

        var microsoftProvider = Assert.Single(providers, provider => provider.Id == MicrosoftEntraAuthService.ProviderId);
        Assert.False(microsoftProvider.RequiresUserSelection);
        Assert.Contains(providers, provider => provider.Id == "demo");
    }

    [Fact]
    public void GetUsers_DoesNotExposeDemoUserPicker_ForLiveMicrosoftProvider()
    {
        var service = CreateService();

        var microsoftUsers = service.GetUsers(MicrosoftEntraAuthService.ProviderId);
        var demoUsers = service.GetUsers("demo");

        Assert.Empty(microsoftUsers);
        Assert.NotEmpty(demoUsers);
    }

    [Fact]
    public async Task SignInAsync_RoutesLiveMicrosoftProviderToHostedChallenge()
    {
        var navigationManager = new TestNavigationManager();
        var service = CreateService(navigationManager: navigationManager);

        await service.SignInAsync(MicrosoftEntraAuthService.ProviderId, redirectUri: "/chat", cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith("/auth/microsoft/signin?returnUrl=%2Fchat", navigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_PreservesDemoFlow_ForMockProviders()
    {
        var authenticationContext = new AuthenticationContext();
        var tenantContext = new TenantContextService();
        var navigationManager = new TestNavigationManager();
        var service = CreateService(authenticationContext, tenantContext, navigationManager);

        await service.SignInAsync("demo", "demo-taylor-jones", "/chat", TestContext.Current.CancellationToken);

        Assert.True(authenticationContext.IsAuthenticated);
        Assert.Equal("demo", authenticationContext.CurrentUser?.ProviderId);
        Assert.Equal("demo", tenantContext.CurrentTenantId);
        Assert.EndsWith("/auth/mock/signin?providerId=demo&userId=demo-taylor-jones&returnUrl=%2Fchat", navigationManager.Uri, StringComparison.Ordinal);
    }

    private static CompositeAuthService CreateService(
        AuthenticationContext? authenticationContext = null,
        TenantContextService? tenantContext = null,
        TestNavigationManager? navigationManager = null)
    {
        var authContext = authenticationContext ?? new AuthenticationContext();
        var tenantContextService = tenantContext ?? new TenantContextService();
        var navManager = navigationManager ?? new TestNavigationManager();
        var mockAuthService = new MockAuthService(
            new AppAuthenticationStateProvider(authContext, tenantContextService),
            tenantContextService,
            navManager);
        var microsoftAuthService = new MicrosoftEntraAuthService(
            Options.Create(new MicrosoftEntraAuthenticationOptions
            {
                TenantId = "contoso.onmicrosoft.com",
                ClientId = "client-id",
                ClientSecret = "secret"
            }),
            navManager);

        return new CompositeAuthService(mockAuthService, microsoftAuthService);
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
