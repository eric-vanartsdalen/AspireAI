extern alias web;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuthProviderOption = web::AspireApp.Web.Services.AuthProviderOption;
using AuthServiceFactory = web::AspireApp.Web.Services.AuthServiceFactory;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticationOptions = web::AspireApp.Web.Services.AuthenticationOptions;
using IAuthService = web::AspireApp.Web.Services.IAuthService;
using AuthenticationServiceCollectionExtensions = web::AspireApp.Web.Services.AuthenticationServiceCollectionExtensions;

namespace AspireApp.WebTest.Tests;

public class AuthServiceFactoryTests
{
    [Fact]
    public void Create_UsesConfiguredServiceKey()
    {
        using var serviceProvider = BuildServices("secondary");
        using var scope = serviceProvider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<AuthServiceFactory>();

        var service = factory.Create();

        Assert.IsType<SecondaryFakeAuthService>(service);
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

    private static ServiceProvider BuildServices(string configuredService)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AuthenticationOptions.SectionName}:Service"] = configuredService
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<AuthenticationOptions>(configuration.GetSection(AuthenticationOptions.SectionName));
        services.AddScoped<AuthServiceFactory>();
        AuthenticationServiceCollectionExtensions.AddAuthServiceRegistration<PrimaryFakeAuthService>(services, "primary");
        AuthenticationServiceCollectionExtensions.AddAuthServiceRegistration<SecondaryFakeAuthService>(services, "secondary");

        return services.BuildServiceProvider();
    }

    private sealed class PrimaryFakeAuthService : IAuthService
    {
        public IReadOnlyList<AuthProviderOption> GetProviders() => [];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SecondaryFakeAuthService : IAuthService
    {
        public IReadOnlyList<AuthProviderOption> GetProviders() => [];

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
