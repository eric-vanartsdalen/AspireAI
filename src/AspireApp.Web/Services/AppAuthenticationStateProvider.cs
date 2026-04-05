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

        var user = AuthenticatedUserClaims.BuildUser(principal);
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
    private static ClaimsPrincipal CreatePrincipal(AuthenticatedUser? user)
    {
        if (user is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var identity = new ClaimsIdentity(authenticationType: AuthenticationType);
        AuthenticatedUserClaims.AddClaims(identity, user);

        return new ClaimsPrincipal(identity);
    }
}
