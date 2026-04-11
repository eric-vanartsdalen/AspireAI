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

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = HydrateFromHttpContext() ?? _authenticationContext.CurrentUser;
        var principal = _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? _httpContextAccessor.HttpContext.User
            : CreatePrincipal(user);

        if (_tenantContextService is not null)
        {
            await _tenantContextService.InitializeForUserAsync(
                user,
                _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        }

        return new AuthenticationState(principal);
    }

    public void SetCurrentUser(AuthenticatedUser? user)
    {
        _authenticationContext.SetCurrentUser(user);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private AuthenticatedUser? HydrateFromHttpContext()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = AuthenticatedUserClaims.BuildUser(principal);
        if (user is null)
        {
            _authenticationContext.SetCurrentUser(null);
            return null;
        }

        if (!string.Equals(_authenticationContext.CurrentUser?.UserId, user.UserId, StringComparison.OrdinalIgnoreCase))
        {
            _authenticationContext.SetCurrentUser(user);
        }

        return user;
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
