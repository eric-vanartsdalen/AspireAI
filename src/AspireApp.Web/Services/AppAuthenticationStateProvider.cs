using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace AspireApp.Web.Services;

/// <summary>
/// Bridges the scoped authentication context into Blazor's authorization primitives.
/// </summary>
public sealed class AppAuthenticationStateProvider(
    AuthenticationContext authenticationContext,
    TenantContextService? tenantContextService = null,
    IHttpContextAccessor? httpContextAccessor = null) : AuthenticationStateProvider
{
    private const string AuthenticationType = "MockExternalProvider";
    private readonly AuthenticationContext _authenticationContext = authenticationContext;
    private readonly TenantContextService? _tenantContextService = tenantContextService;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        HydrateFromHttpContext();
        var principal = _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? _httpContextAccessor.HttpContext.User
            : CreatePrincipal(_authenticationContext.CurrentUser);
        return Task.FromResult(new AuthenticationState(principal));
    }

    public void SetCurrentUser(AuthenticatedUser? user)
    {
        _authenticationContext.SetCurrentUser(user);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private void HydrateFromHttpContext()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var user = BuildUserFromClaims(principal);
        if (user is null)
        {
            _authenticationContext.SetCurrentUser(null);
            return;
        }

        if (!string.Equals(_authenticationContext.CurrentUser?.UserId, user.UserId, StringComparison.OrdinalIgnoreCase))
        {
            _authenticationContext.SetCurrentUser(user);
        }

        _tenantContextService?.InitializeForUser(user.DefaultTenantId);
    }

    private static AuthenticatedUser? BuildUserFromClaims(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var displayName = principal.FindFirstValue(ClaimTypes.Name);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var providerId = principal.FindFirstValue(ClaimTypes.AuthenticationMethod);
        var providerDisplayName = principal.FindFirstValue("provider_display_name");
        var tenantId = principal.FindFirstValue("tenant_id");

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(providerId) ||
            string.IsNullOrWhiteSpace(providerDisplayName) ||
            string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        return new AuthenticatedUser(userId, displayName, email, providerId, providerDisplayName, tenantId);
    }

    private static ClaimsPrincipal CreatePrincipal(AuthenticatedUser? user)
    {
        if (user is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.AuthenticationMethod, user.ProviderId),
            new Claim("provider_display_name", user.ProviderDisplayName),
            new Claim("tenant_id", user.DefaultTenantId)
        ], AuthenticationType);

        return new ClaimsPrincipal(identity);
    }
}
