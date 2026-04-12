using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace AspireApp.Web.Services;

/// <summary>
/// Managed local auth provider that renders a username/password form on the sign-in page.
/// </summary>
public sealed class LocalAuthService(
    IOptions<LocalAuthenticationOptions> options,
    NavigationManager navigationManager) : IAuthService
{
    public const string ServiceKey = "local";
    public const string ProviderId = "local";
    public const string ProviderDisplayName = "Local account";
    public const string InvalidCredentialErrorCode = "invalid-credentials";

    private static readonly AuthProviderOption Provider = new(
        ProviderId,
        ProviderDisplayName,
        "Use a managed AspireAI username or email with your password. Development can allow first-use username registration.",
        "provider-local",
        RequiresUserSelection: false,
        RequiresCredentials: true,
        SignInPath: "/auth/local/signin");

    private readonly LocalAuthenticationOptions _options = options.Value;
    private readonly NavigationManager _navigationManager = navigationManager;

    public IReadOnlyList<AuthProviderOption> GetProviders() => _options.Enabled ? [Provider] : [];

    public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

    public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(providerId, ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported local authentication provider '{providerId}'.");
        }

        _navigationManager.NavigateTo(BuildSignInUri(redirectUri));
        return Task.CompletedTask;
    }

    public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _navigationManager.NavigateTo(BuildSignOutUri(redirectUri), forceLoad: true);
        return Task.CompletedTask;
    }

    private static string BuildSignInUri(string? redirectUri)
    {
        var returnUrl = NormalizeLocalPath(redirectUri);
        return $"/signin?provider={Uri.EscapeDataString(ProviderId)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

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
