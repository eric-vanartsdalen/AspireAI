namespace AspireApp.Web.Services;

/// <summary>
/// Configuration for selecting the active authentication implementation.
/// </summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";
    public const string AutoService = "auto";
    public const string DefaultService = AutoService;
    public const string LocalService = LocalAuthService.ServiceKey;
    public const string MockService = MockAuthService.ServiceKey;
    public const string MicrosoftService = MicrosoftEntraAuthService.ServiceKey;
    public const string CombinedService = CompositeAuthService.ServiceKey;

    public string Service { get; set; } = DefaultService;

    public static string ResolveEffectiveService(string? configuredService, bool microsoftConfigured, bool localConfigured = false)
    {
        var requestedService = string.IsNullOrWhiteSpace(configuredService)
            ? DefaultService
            : configuredService.Trim();

        return string.Equals(requestedService, AutoService, StringComparison.OrdinalIgnoreCase)
            ? microsoftConfigured || localConfigured
                ? CombinedService
                : MockService
            : requestedService;
    }

    public static bool ServiceIncludes(string effectiveService, string serviceKey)
    {
        return string.Equals(effectiveService, CombinedService, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(effectiveService, serviceKey, StringComparison.OrdinalIgnoreCase);
    }
}
