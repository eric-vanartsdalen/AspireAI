namespace AspireApp.Web.Services;

/// <summary>
/// Abstraction for interactive auth flows so the UI can swap mock auth for real providers later.
/// </summary>
public interface IAuthService
{
    IReadOnlyList<AuthProviderOption> GetProviders();

    IReadOnlyList<AuthenticatedUser> GetUsers(string providerId);

    Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default);

    Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default);
}
