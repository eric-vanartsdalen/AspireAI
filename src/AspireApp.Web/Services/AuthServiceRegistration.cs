namespace AspireApp.Web.Services;

/// <summary>
/// Describes an auth service implementation that can be selected from configuration.
/// </summary>
public sealed record AuthServiceRegistration(string ServiceKey, Type ImplementationType)
{
    public static AuthServiceRegistration Create<TService>(string serviceKey)
        where TService : class, IAuthService =>
        new(serviceKey, typeof(TService));
}
