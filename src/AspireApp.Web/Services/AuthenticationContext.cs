using Microsoft.AspNetCore.Http;

namespace AspireApp.Web.Services;

/// <summary>
/// Scoped authentication context for the current Blazor circuit.
/// </summary>
public sealed class AuthenticationContext(IHttpContextAccessor? httpContextAccessor = null)
{
    private readonly IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;
    private AuthenticatedUser? _currentUser;
    private bool _hydrationAttempted;

    public AuthenticatedUser? CurrentUser
    {
        get
        {
            EnsureHydratedFromHttpContext();
            return _currentUser;
        }
    }

    public bool IsAuthenticated => CurrentUser is not null;

    public event Action? OnChange;

    public void SetCurrentUser(AuthenticatedUser? user)
    {
        _hydrationAttempted = true;

        if (_currentUser == user)
        {
            return;
        }

        _currentUser = user;
        OnChange?.Invoke();
    }

    private void EnsureHydratedFromHttpContext()
    {
        if (_hydrationAttempted || _currentUser is not null)
        {
            return;
        }

        _hydrationAttempted = true;

        var principal = _httpContextAccessor?.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        _currentUser = AuthenticatedUserClaims.BuildUser(principal);
    }
}
