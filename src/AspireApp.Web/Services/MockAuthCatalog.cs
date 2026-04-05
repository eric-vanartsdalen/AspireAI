namespace AspireApp.Web.Services;

public static class MockAuthCatalog
{
    private static readonly IReadOnlyList<AuthProviderOption> Providers =
    [
        new("microsoft", "Microsoft", "Mock Entra-style sign-in for work accounts.", "provider-microsoft", true),
        new("google", "Google", "Mock workspace sign-in for personal and shared docs.", "provider-google", true),
        new("demo", "Demo workspace", "Local demo identities for quick UX validation.", "provider-demo", true)
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AuthenticatedUser>> UsersByProvider =
        new Dictionary<string, IReadOnlyList<AuthenticatedUser>>(StringComparer.OrdinalIgnoreCase)
        {
            ["microsoft"] =
            [
                new("ms-avery-collins", "Avery Collins", "avery.collins@contoso.com", "microsoft", "Microsoft", "tenant-a"),
                new("ms-maya-patel", "Maya Patel", "maya.patel@fabrikam.com", "microsoft", "Microsoft", "default")
            ],
            ["google"] =
            [
                new("google-gabriel-torres", "Gabriel Torres", "gabriel.torres@gmail.com", "google", "Google", "tenant-b"),
                new("google-priya-shah", "Priya Shah", "priya.shah@gmail.com", "google", "Google", "default")
            ],
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
