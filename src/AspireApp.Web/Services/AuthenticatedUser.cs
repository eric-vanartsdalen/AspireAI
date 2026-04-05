namespace AspireApp.Web.Services;

/// <summary>
/// Represents the authenticated user visible to the Blazor shell.
/// </summary>
public sealed record AuthenticatedUser(
    string UserId,
    string DisplayName,
    string Email,
    string ProviderId,
    string ProviderDisplayName,
    string DefaultTenantId)
{
    public string Initials =>
        string.Concat(
            DisplayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));
}
