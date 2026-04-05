extern alias web;

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using AuthProviderOption = web::AspireApp.Web.Services.AuthProviderOption;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using IAuthService = web::AspireApp.Web.Services.IAuthService;
using MicrosoftEntraAuthService = web::AspireApp.Web.Services.MicrosoftEntraAuthService;
using SignInPanel = web::AspireApp.Web.Components.Shared.SignInPanel;

namespace AspireApp.WebTest.Tests;

public sealed class SignInPanelTests : IDisposable
{
    private static readonly AuthenticatedUser[] MockMicrosoftUsers =
    [
        new("ms-avery-collins", "Avery Collins", "avery.collins@contoso.com", "microsoft", "Microsoft", "tenant-a"),
        new("ms-maya-patel", "Maya Patel", "maya.patel@fabrikam.com", "microsoft", "Microsoft", "default")
    ];

    private readonly BunitContext _testContext = new();

    [Fact]
    public void Render_DoesNotShowDemoAccountPicker_WhenLiveMicrosoftIsTheDefaultProvider()
    {
        var authService = CreateAuthService();
        _testContext.Services.AddSingleton<IAuthService>(authService);

        var cut = _testContext.Render<SignInPanel>(parameters => parameters
            .Add(component => component.RedirectUri, "/chat"));

        cut.Find("[data-testid='auth-provider-microsoft-entra']");
        Assert.DoesNotContain("Choose a demo account for Microsoft", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='auth-user-select']"));
    }

    [Fact]
    public async Task SelectingLiveMicrosoftAfterOpeningMockMicrosoftPicker_ClearsTheTrapAndUsesHostedFlow()
    {
        var authService = CreateAuthService();
        _testContext.Services.AddSingleton<IAuthService>(authService);

        var cut = _testContext.Render<SignInPanel>(parameters => parameters
            .Add(component => component.RedirectUri, "/chat"));

        InvokeSelectProvider(cut, "microsoft");

        Assert.Contains("Choose a demo account for Microsoft", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("avery.collins@contoso.com", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("maya.patel@fabrikam.com", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='auth-user-select']"));

        InvokeSelectProvider(cut, MicrosoftEntraAuthService.ProviderId);

        Assert.DoesNotContain("Choose a demo account for Microsoft", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='auth-user-select']"));

        await InvokeSignInAsync(cut);

        Assert.Equal(MicrosoftEntraAuthService.ProviderId, authService.LastProviderId);
        Assert.Null(authService.LastUserId);
        Assert.Equal("/chat", authService.LastRedirectUri);
    }

    public void Dispose()
    {
        _testContext.Dispose();
    }

    private static void InvokeSelectProvider(IRenderedComponent<SignInPanel> cut, string providerId)
    {
        var method = typeof(SignInPanel).GetMethod("SelectProvider", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate SignInPanel.SelectProvider for regression coverage.");

        method.Invoke(cut.Instance, [providerId]);
        cut.Render();
    }

    private static async Task InvokeSignInAsync(IRenderedComponent<SignInPanel> cut)
    {
        var method = typeof(SignInPanel).GetMethod("SignInAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate SignInPanel.SignInAsync for regression coverage.");

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        cut.Render();
    }

    private static RecordingAuthService CreateAuthService()
    {
        return new RecordingAuthService(
            [
                new AuthProviderOption(
                    MicrosoftEntraAuthService.ProviderId,
                    "Microsoft",
                    "Use the hosted Microsoft sign-in page for your work or school account, then return to AspireAI already signed in.",
                    "provider-microsoft",
                    false),
                new AuthProviderOption(
                    "microsoft",
                    "Microsoft",
                    "Mock Entra-style sign-in for work accounts.",
                    "provider-microsoft",
                    true)
            ],
            new Dictionary<string, IReadOnlyList<AuthenticatedUser>>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft"] = MockMicrosoftUsers
            });
    }

    private sealed class RecordingAuthService(
        IReadOnlyList<AuthProviderOption> providers,
        IReadOnlyDictionary<string, IReadOnlyList<AuthenticatedUser>> usersByProvider) : IAuthService
    {
        private readonly IReadOnlyList<AuthProviderOption> _providers = providers;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<AuthenticatedUser>> _usersByProvider = usersByProvider;

        public string? LastProviderId { get; private set; }

        public string? LastUserId { get; private set; }

        public string? LastRedirectUri { get; private set; }

        public IReadOnlyList<AuthProviderOption> GetProviders() => _providers;

        public IReadOnlyList<AuthenticatedUser> GetUsers(string providerId)
        {
            return _usersByProvider.TryGetValue(providerId, out var users)
                ? users
                : [];
        }

        public Task SignInAsync(string providerId, string? userId = null, string? redirectUri = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastProviderId = providerId;
            LastUserId = userId;
            LastRedirectUri = redirectUri;

            return Task.CompletedTask;
        }

        public Task SignOutAsync(string? redirectUri = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRedirectUri = redirectUri;
            return Task.CompletedTask;
        }
    }
}
