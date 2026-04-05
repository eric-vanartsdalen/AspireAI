namespace AspireApp.Web.Services;

/// <summary>
/// Scoped authentication context for the current Blazor circuit.
/// </summary>
public sealed class AuthenticationContext
{
    private AuthenticatedUser? _currentUser;

    public AuthenticatedUser? CurrentUser => _currentUser;

    public bool IsAuthenticated => _currentUser is not null;

    public event Action? OnChange;

    public void SetCurrentUser(AuthenticatedUser? user)
    {
        if (_currentUser == user)
        {
            return;
        }

        _currentUser = user;
        OnChange?.Invoke();
    }
}
