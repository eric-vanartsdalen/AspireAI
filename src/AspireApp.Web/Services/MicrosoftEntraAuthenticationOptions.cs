namespace AspireApp.Web.Services;

/// <summary>
/// Local configuration for the Microsoft Entra ID interactive sign-in flow.
/// </summary>
public sealed class MicrosoftEntraAuthenticationOptions
{
    public const string SectionName = $"{AuthenticationOptions.SectionName}:Microsoft";

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/signin-oidc-microsoft";

    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc-microsoft";

    public string DefaultAppTenantId { get; set; } = "default";

    public Dictionary<string, string> UserTenantSeeds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> DomainTenantSeeds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
