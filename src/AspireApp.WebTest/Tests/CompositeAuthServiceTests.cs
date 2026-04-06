extern alias web;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using AppAuthenticationStateProvider = web::AspireApp.Web.Services.AppAuthenticationStateProvider;
using AuthProviderOption = web::AspireApp.Web.Services.AuthProviderOption;
using AuthServiceRegistration = web::AspireApp.Web.Services.AuthServiceRegistration;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticationContext = web::AspireApp.Web.Services.AuthenticationContext;
using AuthenticationOptions = web::AspireApp.Web.Services.AuthenticationOptions;
using CompositeAuthService = web::AspireApp.Web.Services.CompositeAuthService;
using IAuthService = web::AspireApp.Web.Services.IAuthService;
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
        using var harness = CreateHarness();

        var providers = harness.Service.GetProviders();

        var microsoftProvider = Assert.Single(providers, provider => provider.Id == MicrosoftEntraAuthService.ProviderId);
        Assert.False(microsoftProvider.RequiresUserSelection);
        Assert.False(microsoftProvider.RequiresCredentials);
        Assert.Contains(providers, provider => provider.Id == "demo");
    }

    [Fact]
    public void GetProviders_ExposesLocalAlongsideMicrosoftAndDemoProviders_WhenRegistered()
    {
        var localAuthService = new LocalFakeAuthService();
        using var harness = CreateHarness(localAuthService);

        var providers = harness.Service.GetProviders();

        var localProvider = Assert.Single(providers, provider => provider.Id == AuthenticationOptions.LocalService);
        Assert.False(localProvider.RequiresUserSelection);
        Assert.True(localProvider.RequiresCredentials);
        Assert.Equal("/auth/local/signin", localProvider.SignInPath);
        Assert.Contains(providers, provider => provider.Id == MicrosoftEntraAuthService.ProviderId);
        Assert.Contains(providers, provider => provider.Id == "demo");
    }

    [Fact]
    public void GetUsers_DoesNotExposeUserPicker_ForCredentialOrHostedProviders()
    {
        using var harness = CreateHarness(new LocalFakeAuthService());

        var localUsers = harness.Service.GetUsers(AuthenticationOptions.LocalService);
        var microsoftUsers = harness.Service.GetUsers(MicrosoftEntraAuthService.ProviderId);
        var demoUsers = harness.Service.GetUsers("demo");

        Assert.Empty(localUsers);
        Assert.Empty(microsoftUsers);
        Assert.NotEmpty(demoUsers);
    }

    [Fact]
    public async Task SignInAsync_RoutesLiveMicrosoftProviderToHostedChallenge()
    {
        using var harness = CreateHarness();

        await harness.Service.SignInAsync(
            MicrosoftEntraAuthService.ProviderId,
            redirectUri: "/chat",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith("/auth/microsoft/signin?returnUrl=%2Fchat", harness.NavigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_PreservesDemoFlow_ForMockProviders()
    {
        using var harness = CreateHarness();

        await harness.Service.SignInAsync("demo", "demo-taylor-jones", "/chat", TestContext.Current.CancellationToken);

        Assert.True(harness.AuthenticationContext.IsAuthenticated);
        Assert.Equal("demo", harness.AuthenticationContext.CurrentUser?.ProviderId);
        Assert.Equal("demo", harness.TenantContext.CurrentTenantId);
        Assert.EndsWith("/auth/mock/signin?providerId=demo&userId=demo-taylor-jones&returnUrl=%2Fchat", harness.NavigationManager.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_RoutesLocalProviderToCredentialService_WhenRegistered()
    {
        var localAuthService = new LocalFakeAuthService();
        using var harness = CreateHarness(localAuthService);

        await harness.Service.SignInAsync(
            AuthenticationOptions.LocalService,
            redirectUri: "/chat",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AuthenticationOptions.LocalService, localAuthService.LastProviderId);
        Assert.Null(localAuthService.LastUserId);
        Assert.Equal("/chat", localAuthService.LastRedirectUri);
    }

    [Fact]
    public async Task SignOutAsync_UsesSharedSignOutEndpoint()
    {
        using var harness = CreateHarness();

        await harness.Service.SignOutAsync("/chat", TestContext.Current.CancellationToken);

        Assert.EndsWith("/auth/signout?returnUrl=%2Fchat", harness.NavigationManager.Uri, StringComparison.Ordinal);
    }

    private static CompositeHarness CreateHarness(LocalFakeAuthService? localAuthService = null)
    {
        var services = new ServiceCollection();
        var navigationManager = new TestNavigationManager();

        services.AddSingleton<NavigationManager>(navigationManager);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<AuthenticationContext>();
        services.AddScoped<TenantContextService>();
        services.AddScoped<AppAuthenticationStateProvider>();
        services.AddOptions();
        services.Configure<MicrosoftEntraAuthenticationOptions>(options =>
        {
            options.TenantId = "contoso.onmicrosoft.com";
            options.ClientId = "client-id";
            options.ClientSecret = "secret";
        });

        services.AddScoped<MockAuthService>();
        services.AddScoped<MicrosoftEntraAuthService>();
        services.AddScoped<CompositeAuthService>();
        services.AddSingleton(AuthServiceRegistration.Create<MockAuthService>(AuthenticationOptions.MockService));
        services.AddSingleton(AuthServiceRegistration.Create<MicrosoftEntraAuthService>(AuthenticationOptions.MicrosoftService));

        if (localAuthService is not null)
        {
            services.AddSingleton(localAuthService);
            services.AddSingleton(AuthServiceRegistration.Create<LocalFakeAuthService>(AuthenticationOptions.LocalService));
        }

        services.AddSingleton(AuthServiceRegistration.Create<CompositeAuthService>(AuthenticationOptions.CombinedService));

        var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateScope();

        return new CompositeHarness(
            serviceProvider,
            scope,
            scope.ServiceProvider.GetRequiredService<CompositeAuthService>(),
            navigationManager,
            scope.ServiceProvider.GetRequiredService<AuthenticationContext>(),
            scope.ServiceProvider.GetRequiredService<TenantContextService>());
    }

    private sealed class CompositeHarness(
        ServiceProvider serviceProvider,
        IServiceScope scope,
        CompositeAuthService service,
        TestNavigationManager navigationManager,
        AuthenticationContext authenticationContext,
        TenantContextService tenantContext) : IDisposable
    {
        public CompositeAuthService Service { get; } = service;

        public TestNavigationManager NavigationManager { get; } = navigationManager;

        public AuthenticationContext AuthenticationContext { get; } = authenticationContext;

        public TenantContextService TenantContext { get; } = tenantContext;

        public void Dispose()
        {
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }

    private sealed class LocalFakeAuthService : IAuthService
    {
        private static readonly AuthProviderOption LocalProvider = new(
            AuthenticationOptions.LocalService,
            "Local account",
            "Managed credentials posted to the server for validation.",
            "provider-local",
            false,
            true,
            "/auth/local/signin");

        public string? LastProviderId { get; private set; }

        public string? LastUserId { get; private set; }

        public string? LastRedirectUri { get; private set; }

        public IReadOnlyList<AuthProviderOption> GetProviders() => [LocalProvider];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastProviderId = providerId;
            LastUserId = userId;
            LastRedirectUri = redirectUri;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastRedirectUri = redirectUri;
            return Task.CompletedTask;
        }
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
