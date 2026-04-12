extern alias web;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuthProviderOption = web::AspireApp.Web.Services.AuthProviderOption;
using AuthServiceFactory = web::AspireApp.Web.Services.AuthServiceFactory;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticationOptions = web::AspireApp.Web.Services.AuthenticationOptions;
using AuthenticationServiceCollectionExtensions = web::AspireApp.Web.Services.AuthenticationServiceCollectionExtensions;
using IAuthService = web::AspireApp.Web.Services.IAuthService;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;

namespace AspireApp.WebTest.Tests;

public class AuthServiceFactoryTests
{
    [Fact]
    public void Create_UsesConfiguredServiceKey()
    {
        using var serviceProvider = BuildServices(AuthenticationOptions.CombinedService);
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var service = factory.Create();

        Assert.IsType<CombinedFakeAuthService>(service);
    }

    [Fact]
    public void Create_UsesCombinedService_WhenAutoAndMicrosoftIsConfigured()
    {
        using var serviceProvider = BuildServices(AuthenticationOptions.AutoService, microsoftConfigured: true, localConfigured: false);
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var service = factory.Create();

        Assert.IsType<CombinedFakeAuthService>(service);
    }

    [Fact]
    public void Create_UsesCombinedService_WhenAutoAndLocalIsConfigured()
    {
        using var serviceProvider = BuildServices(AuthenticationOptions.AutoService, localConfigured: true);
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var service = factory.Create();

        Assert.IsType<CombinedFakeAuthService>(service);
    }

    [Fact]
    public void Create_UsesMockService_WhenAutoAndNoLiveProvidersAreConfigured()
    {
        using var serviceProvider = BuildServices(AuthenticationOptions.AutoService, localConfigured: false);
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var service = factory.Create();

        Assert.IsType<MockFakeAuthService>(service);
    }

    [Fact]
    public void Create_UsesLocalService_WhenConfigured()
    {
        using var serviceProvider = BuildServices(AuthenticationOptions.LocalService, localConfigured: true);
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var service = factory.Create();

        Assert.IsType<LocalFakeAuthService>(service);
    }

    [Fact]
    public void Create_ThrowsForUnknownConfiguredService()
    {
        using var serviceProvider = BuildServices("missing");
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildServices(string configuredService, bool microsoftConfigured = false, bool localConfigured = true)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{AuthenticationOptions.SectionName}:Service"] = configuredService,
            [$"{LocalAuthenticationOptions.SectionName}:Enabled"] = localConfigured.ToString()
        };

        if (microsoftConfigured)
        {
            settings[$"{AuthenticationOptions.SectionName}:Microsoft:ClientId"] = "client-id";
            settings[$"{AuthenticationOptions.SectionName}:Microsoft:ClientSecret"] = "secret";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<AuthenticationOptions>(configuration.GetSection(AuthenticationOptions.SectionName));
        services.Configure<LocalAuthenticationOptions>(configuration.GetSection(LocalAuthenticationOptions.SectionName));
        services.Configure<web::AspireApp.Web.Services.MicrosoftEntraAuthenticationOptions>(
            configuration.GetSection(web::AspireApp.Web.Services.MicrosoftEntraAuthenticationOptions.SectionName));
        services.AddScoped<AuthServiceFactory>();
        AuthenticationServiceCollectionExtensions.AddAuthServiceRegistration<LocalFakeAuthService>(services, AuthenticationOptions.LocalService);
        AuthenticationServiceCollectionExtensions.AddAuthServiceRegistration<MockFakeAuthService>(services, AuthenticationOptions.MockService);
        AuthenticationServiceCollectionExtensions.AddAuthServiceRegistration<MicrosoftFakeAuthService>(services, AuthenticationOptions.MicrosoftService);
        AuthenticationServiceCollectionExtensions.AddAuthServiceRegistration<CombinedFakeAuthService>(services, AuthenticationOptions.CombinedService);

        return services.BuildServiceProvider();
    }

    private sealed class MockFakeAuthService : IAuthService
    {
        public IReadOnlyList<AuthProviderOption> GetProviders() => [];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MicrosoftFakeAuthService : IAuthService
    {
        public IReadOnlyList<AuthProviderOption> GetProviders() => [];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LocalFakeAuthService : IAuthService
    {
        public IReadOnlyList<AuthProviderOption> GetProviders() => [];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CombinedFakeAuthService : IAuthService
    {
        public IReadOnlyList<AuthProviderOption> GetProviders() => [];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
