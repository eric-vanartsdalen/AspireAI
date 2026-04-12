namespace AspireApp.Web.Services;

public static class MockAuthCatalog
{
    private static readonly IReadOnlyList<AuthProviderOption> Providers =
    [
        new("demo", "Demo workspace", "Local demo identities for quick UX validation.", "provider-demo", true)
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AuthenticatedUser>> UsersByProvider =
        new Dictionary<string, IReadOnlyList<AuthenticatedUser>>(StringComparer.OrdinalIgnoreCase)
        {
            ["demo"] =
            [
                new("demo-taylor-jones", "Taylor Jones", "taylor@demo.local", "demo", "Demo workspace", "demo"),
                new("demo-robin-singh", "Robin Singh", "robin@demo.local", "demo", "Demo workspace", "tenant-a")
            ]
        };

    public static IReadOnlyList<AuthProviderOption> GetProviders() => Providers;

    public static IReadOnlyList<AuthenticatedUser> GetUsers(string providerId)
    {
        return UsersByProvider.TryGetValue(providerId, out var users)
            ? users
            : [];
    }

    public static AuthenticatedUser? FindUser(string providerId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return GetUsers(providerId)
            .FirstOrDefault(user => user.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
    }
}
