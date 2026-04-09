using Microsoft.AspNetCore.Components;

namespace AspireApp.Web.Services;

/// <summary>
/// Mock interactive auth service with hardcoded provider-backed users.
/// </summary>
public sealed class MockAuthService(
    AppAuthenticationStateProvider authenticationStateProvider,
    TenantContextService tenantContextService,
    TenantManagementService tenantManagementService,
    NavigationManager navigationManager) : IAuthService
{
    public const string ServiceKey = "mock";

    private readonly AppAuthenticationStateProvider _authenticationStateProvider = authenticationStateProvider;
    private readonly TenantContextService _tenantContextService = tenantContextService;
    private readonly TenantManagementService _tenantManagementService = tenantManagementService;
    private readonly NavigationManager _navigationManager = navigationManager;

    public IReadOnlyList<AuthProviderOption> GetProviders() => MockAuthCatalog.GetProviders();

    public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId) => MockAuthCatalog.GetUsers(providerId);

    public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("The selected authentication provider requires a user selection.");
        }

        var selectedUser = MockAuthCatalog.FindUser(providerId, userId);

        if (selectedUser is null)
        {
            throw new InvalidOperationException("The selected mock user could not be found.");
        }

        return SignInWithTenantAsync(selectedUser, redirectUri, cancellationToken);
    }

    public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _authenticationStateProvider.SetCurrentUser(null);
        _tenantContextService.Reset();
        _navigationManager.NavigateTo(BuildSignOutUri(redirectUri), forceLoad: true);
        return Task.CompletedTask;
    }

    private async Task SignInWithTenantAsync(
        AuthenticatedUser selectedUser,
        string? redirectUri,
        CancellationToken cancellationToken)
    {
        var tenantSnapshot = await _tenantManagementService.EnsureTenantAccessAsync(
            new TenantUserDescriptor(selectedUser.UserId, selectedUser.DisplayName, selectedUser.Email),
            cancellationToken);

        var authenticatedUser = selectedUser with { DefaultTenantId = tenantSnapshot.DefaultTenantId };
        _authenticationStateProvider.SetCurrentUser(authenticatedUser);
        await _tenantContextService.InitializeForUserAsync(authenticatedUser, cancellationToken);
        _navigationManager.NavigateTo(
            BuildSignInUri(authenticatedUser.ProviderId, authenticatedUser.UserId, redirectUri),
            forceLoad: true);
    }

    private static string BuildSignInUri(string providerId, string userId, string? redirectUri)
    {
        var returnUrl = NormalizeLocalPath(redirectUri);
        return $"/auth/mock/signin?providerId={Uri.EscapeDataString(providerId)}&userId={Uri.EscapeDataString(userId)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    private static string BuildSignOutUri(string? redirectUri)
    {
        var returnUrl = NormalizeLocalPath(redirectUri);
        return $"/auth/mock/signout?returnUrl={Uri.EscapeDataString(returnUrl)}";
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
