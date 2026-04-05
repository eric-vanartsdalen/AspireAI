namespace AspireApp.Web.Services;

/// <summary>
/// Configuration for selecting the active authentication implementation.
/// </summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";
    public const string DefaultService = "mock";

    public string Service { get; set; } = DefaultService;
}
