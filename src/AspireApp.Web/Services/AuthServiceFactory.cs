using Microsoft.Extensions.Options;

namespace AspireApp.Web.Services;

/// <summary>
/// Resolves the active auth implementation from configuration without exposing service-locator logic to the UI.
/// </summary>
public sealed class AuthServiceFactory(
    IEnumerable<AuthServiceRegistration> registrations,
    IServiceProvider services,
    IOptions<AuthenticationOptions> options)
{
    private readonly IServiceProvider _services = services;
    private readonly AuthenticationOptions _options = options.Value;
    private readonly Dictionary<string, Type> _registrations = registrations
        .GroupBy(registration => registration.ServiceKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single().ImplementationType
                : throw new InvalidOperationException($"Authentication service '{group.Key}' is registered more than once."),
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RegisteredServices => _registrations.Keys.ToArray();

    public IAuthService Create()
    {
        var serviceKey = string.IsNullOrWhiteSpace(_options.Service)
            ? AuthenticationOptions.DefaultService
            : _options.Service.Trim();

        if (!_registrations.TryGetValue(serviceKey, out var implementationType))
        {
            throw new InvalidOperationException(
                $"Authentication service '{serviceKey}' is not registered. Registered services: {string.Join(", ", RegisteredServices.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}.");
        }

        return (IAuthService)_services.GetRequiredService(implementationType);
    }
}
