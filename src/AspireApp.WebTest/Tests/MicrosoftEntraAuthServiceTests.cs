extern alias web;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using AuthProviderOption = web::AspireApp.Web.Services.AuthProviderOption;
using MicrosoftEntraAuthService = web::AspireApp.Web.Services.MicrosoftEntraAuthService;
using MicrosoftEntraAuthenticationOptions = web::AspireApp.Web.Services.MicrosoftEntraAuthenticationOptions;

namespace AspireApp.WebTest.Tests;

public class MicrosoftEntraAuthServiceTests
{
    [Fact]
    public void GetProviders_HidesMicrosoftProvider_WhenConfigurationIsMissing()
    {
        var service = CreateService(new MicrosoftEntraAuthenticationOptions());

        var providers = service.GetProviders();

        Assert.Empty(providers);
    }

    [Fact]
    public void GetProviders_ReturnsLiveMicrosoftProvider_WhenConfigurationIsPresent()
    {
        var service = CreateService(new MicrosoftEntraAuthenticationOptions
        {
            ClientId = "client-id",
            ClientSecret = "secret"
        });

        var providers = service.GetProviders();

        var provider = Assert.Single(providers);
        Assert.Equal(MicrosoftEntraAuthService.ProviderId, provider.Id);
        Assert.False(provider.RequiresUserSelection);
    }

    [Fact]
    public async Task SignInAsync_NavigatesToMicrosoftChallengeEndpoint()
    {
        var navigationManager = new TestNavigationManager();
        var service = CreateService(
            new MicrosoftEntraAuthenticationOptions
            {
                ClientId = "client-id",
                ClientSecret = "secret"
            },
            navigationManager);

        await service.SignInAsync(MicrosoftEntraAuthService.ProviderId, redirectUri: "/chat", cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith("/auth/microsoft/signin?returnUrl=%2Fchat", navigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_Throws_WhenMicrosoftConfigurationIsMissing()
    {
        var service = CreateService(new MicrosoftEntraAuthenticationOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SignInAsync(MicrosoftEntraAuthService.ProviderId, redirectUri: "/chat", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("ClientId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetProviders_ReturnsLiveMicrosoftProvider_WhenTenantIdIsBlank()
    {
        var service = CreateService(new MicrosoftEntraAuthenticationOptions
        {
            TenantId = string.Empty,
            ClientId = "client-id",
            ClientSecret = "secret"
        });

        var providers = service.GetProviders();

        var provider = Assert.Single(providers);
        Assert.Equal(MicrosoftEntraAuthService.ProviderId, provider.Id);
    }

    private static MicrosoftEntraAuthService CreateService(
        MicrosoftEntraAuthenticationOptions options,
        TestNavigationManager? navigationManager = null)
    {
        return new MicrosoftEntraAuthService(
            Options.Create(options),
            navigationManager ?? new TestNavigationManager());
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
