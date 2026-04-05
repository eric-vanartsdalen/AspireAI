namespace AspireApp.Web.Services;

public static class MockAuthSessionCookie
{
    public const string CookieName = "aspireapp-mock-auth";

    public static string Serialize(string providerId, string userId)
    {
        return $"{Uri.EscapeDataString(providerId)}|{Uri.EscapeDataString(userId)}";
    }

    public static bool TryDeserialize(string? cookieValue, out string providerId, out string userId)
    {
        providerId = string.Empty;
        userId = string.Empty;

        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return false;
        }

        var parts = cookieValue.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        providerId = Uri.UnescapeDataString(parts[0]);
        userId = Uri.UnescapeDataString(parts[1]);

        return !string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(userId);
    }
}
