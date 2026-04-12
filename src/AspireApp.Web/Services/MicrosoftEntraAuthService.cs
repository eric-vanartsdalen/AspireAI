using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace AspireApp.Web.Services;

/// <summary>
/// Interactive Microsoft Entra ID auth flow that delegates challenge/sign-out to ASP.NET Core auth middleware.
/// </summary>
public sealed class MicrosoftEntraAuthService(
    IOptions<MicrosoftEntraAuthenticationOptions> options,
    NavigationManager navigationManager) : IAuthService
{
    public const string ServiceKey = "microsoft";
    public const string AuthenticationScheme = "MicrosoftEntraOidc";
    public const string ProviderId = "microsoft-entra";

    private static readonly AuthProviderOption Provider = new(
        ProviderId,
        "Microsoft",
        "Use the hosted Microsoft sign-in page for your work, school, or personal Microsoft account, then return to AspireAI already signed in.",
        "provider-microsoft",
        RequiresUserSelection: false,
        SignInPath: "/auth/microsoft/signin");

    private readonly MicrosoftEntraAuthenticationOptions _options = options.Value;
    private readonly NavigationManager _navigationManager = navigationManager;

    public IReadOnlyList<AuthProviderOption> GetProviders() => _options.IsConfigured ? [Provider] : [];

    public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => [];

    public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(providerId, ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported Microsoft Entra ID provider '{providerId}'.");
        }

        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Microsoft Entra ID is not configured. Set Authentication:Microsoft:ClientId and ClientSecret before using the live provider. TenantId is optional; blank uses the Microsoft common endpoint.");
        }

        _navigationManager.NavigateTo(BuildSignInUri(redirectUri), forceLoad: true);
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
        return $"/auth/microsoft/signin?returnUrl={Uri.EscapeDataString(returnUrl)}";
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
