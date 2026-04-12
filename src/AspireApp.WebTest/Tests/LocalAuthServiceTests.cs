extern alias web;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using LocalAuthService = web::AspireApp.Web.Services.LocalAuthService;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;

namespace AspireApp.WebTest.Tests;

public class LocalAuthServiceTests
{
    [Fact]
    public void GetProviders_ReturnsManagedCredentialProvider_WhenEnabled()
    {
        var service = CreateService(new LocalAuthenticationOptions { Enabled = true });

        var provider = Assert.Single(service.GetProviders());

        Assert.Equal(LocalAuthService.ProviderId, provider.Id);
        Assert.Equal(LocalAuthService.ProviderDisplayName, provider.DisplayName);
        Assert.False(provider.RequiresUserSelection);
        Assert.True(provider.RequiresCredentials);
        Assert.Equal("/auth/local/signin", provider.SignInPath);
    }

    [Fact]
    public void GetProviders_HidesLocalProvider_WhenDisabled()
    {
        var service = CreateService(new LocalAuthenticationOptions { Enabled = false });

        Assert.Empty(service.GetProviders());
    }

    [Fact]
    public async Task SignInAsync_NavigatesToLocalCredentialsSurface_WithProviderSelection()
    {
        var navigationManager = new TestNavigationManager();
        var service = CreateService(new LocalAuthenticationOptions { Enabled = true }, navigationManager);

        await service.SignInAsync(LocalAuthService.ProviderId, redirectUri: "/chat", cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith("/signin?provider=local&returnUrl=%2Fchat", navigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_Throws_ForUnknownProvider()
    {
        var service = CreateService(new LocalAuthenticationOptions { Enabled = true });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SignInAsync("other-provider", redirectUri: "/chat", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Unsupported local authentication provider", exception.Message, StringComparison.Ordinal);
    }

    private static LocalAuthService CreateService(
        LocalAuthenticationOptions options,
        TestNavigationManager? navigationManager = null) =>
        new(Options.Create(options), navigationManager ?? new TestNavigationManager());

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
