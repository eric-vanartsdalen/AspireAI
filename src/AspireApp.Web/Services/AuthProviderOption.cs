namespace AspireApp.Web.Services;

/// <summary>
/// Describes a sign-in provider shown in the sign-in experience.
/// </summary>
public sealed record AuthProviderOption(
    string Id,
    string DisplayName,
    string Description,
    string AccentCssClass,
    bool RequiresUserSelection,
    bool RequiresCredentials = false,
    string? SignInPath = null);
