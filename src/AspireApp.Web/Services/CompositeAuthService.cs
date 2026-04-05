namespace AspireApp.Web.Services;

/// <summary>
/// Exposes multiple auth providers through the existing UI seam while delegating execution to provider-specific services.
/// </summary>
public sealed class CompositeAuthService(
    MockAuthService mockAuthService,
    MicrosoftEntraAuthService microsoftEntraAuthService) : IAuthService
{
    public const string ServiceKey = "combined";

    private readonly MockAuthService _mockAuthService = mockAuthService;
    private readonly MicrosoftEntraAuthService _microsoftEntraAuthService = microsoftEntraAuthService;

    public IReadOnlyList<AuthProviderOption> GetProviders() =>
    [
        .. _microsoftEntraAuthService.GetProviders(),
        .. _mockAuthService.GetProviders()
    ];

    public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId)
    {
        return string.Equals(providerId, MicrosoftEntraAuthService.ProviderId, StringComparison.OrdinalIgnoreCase)
            ? []
            : _mockAuthService.GetUsers(providerId);
    }

    public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        return string.Equals(providerId, MicrosoftEntraAuthService.ProviderId, StringComparison.OrdinalIgnoreCase)
            ? _microsoftEntraAuthService.SignInAsync(providerId, userId, redirectUri, cancellationToken)
            : _mockAuthService.SignInAsync(providerId, userId, redirectUri, cancellationToken);
    }

    public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        return _microsoftEntraAuthService.SignOutAsync(redirectUri, cancellationToken);
    }
}
