namespace AspireApp.Web.Services;

internal static class LocalAuthValueNormalizer
{
    public static string Clean(string value) => value.Trim();

    public static string Normalize(string value) => Clean(value).ToUpperInvariant();
}
