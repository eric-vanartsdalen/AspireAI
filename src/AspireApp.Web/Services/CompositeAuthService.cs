using Microsoft.AspNetCore.Components;

namespace AspireApp.Web.Services;

/// <summary>
/// Exposes multiple auth providers through the existing UI seam while delegating execution to provider-specific services.
/// </summary>
public sealed class CompositeAuthService(
    IEnumerable<AuthServiceRegistration> registrations,
    IServiceProvider services,
    NavigationManager navigationManager) : IAuthService
{
    public const string ServiceKey = "combined";

    private readonly IServiceProvider _services = services;
    private readonly NavigationManager _navigationManager = navigationManager;
    private readonly IReadOnlyList<AuthServiceRegistration> _registrations = registrations
        .Where(registration => !string.Equals(registration.ServiceKey, ServiceKey, StringComparison.OrdinalIgnoreCase))
        .GroupBy(registration => registration.ServiceKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Count() == 1
            ? group.Single()
            : throw new InvalidOperationException($"Authentication service '{group.Key}' is registered more than once."))
        .OrderBy(registration => GetSortOrder(registration.ServiceKey))
        .ThenBy(registration => registration.ServiceKey, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<AuthProviderOption> GetProviders()
    {
        var providers = new List<AuthProviderOption>();
        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in _registrations)
        {
            foreach (var provider in ResolveService(registration).GetProviders())
            {
                if (!providerIds.Add(provider.Id))
                {
                    throw new InvalidOperationException($"Authentication provider '{provider.Id}' is registered more than once.");
                }

                providers.Add(provider);
            }
        }

        return providers;
    }

    public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) =>
        ResolveProviderService(providerId).GetUsers(providerId);

    public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default) =>
        ResolveProviderService(providerId).SignInAsync(providerId, userId, redirectUri, cancellationToken);

    public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _navigationManager.NavigateTo(BuildSignOutUri(redirectUri), forceLoad: true);
        return Task.CompletedTask;
    }

    private IAuthService ResolveProviderService(string providerId)
    {
        foreach (var registration in _registrations)
        {
            var service = ResolveService(registration);
            if (service.GetProviders().Any(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)))
            {
                return service;
            }
        }

        throw new InvalidOperationException($"Authentication provider '{providerId}' is not registered.");
    }

    private IAuthService ResolveService(AuthServiceRegistration registration) =>
        (IAuthService)_services.GetRequiredService(registration.ImplementationType);

    private static int GetSortOrder(string serviceKey) => serviceKey switch
    {
        AuthenticationOptions.LocalService => 0,
        AuthenticationOptions.MicrosoftService => 1,
        AuthenticationOptions.MockService => 2,
        _ => 10
    };

    private static string BuildSignOutUri(string? redirectUri)
    {
        var returnUrl = NormalizeLocalPath(redirectUri);
        return $"/auth/signout?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    private static string NormalizeLocalPath(string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return "/";
        }

        return redirectUri.StartsWith("/", StringComparison.Ordinal) && !redirectUri.StartsWith("//", StringComparison.Ordinal)
            ? redirectUri
            : "/";
    }
}
