namespace AspireApp.Web.Services;

/// <summary>
/// Configuration for the managed local username/password provider.
/// </summary>
public sealed class LocalAuthenticationOptions
{
    public const string SectionName = $"{AuthenticationOptions.SectionName}:Local";
    public const int MinimumPasswordLength = 10;

    public bool Enabled { get; set; } = true;

    public bool AllowSelfRegistration { get; set; }

    public List<LocalAuthenticationSeedUser> SeedUsers { get; set; } = [];
}

/// <summary>
/// Optional local development seed user. Only precomputed password hashes are accepted.
/// </summary>
public sealed class LocalAuthenticationSeedUser
{
    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string DefaultTenantId { get; set; } = TenantContextService.DefaultTenantId;

    public string PasswordHash { get; set; } = string.Empty;
}
