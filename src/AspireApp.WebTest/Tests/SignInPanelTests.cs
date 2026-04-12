extern alias web;

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using AuthProviderOption = web::AspireApp.Web.Services.AuthProviderOption;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticationOptions = web::AspireApp.Web.Services.AuthenticationOptions;
using IAuthService = web::AspireApp.Web.Services.IAuthService;
using LocalAuthenticationOptions = web::AspireApp.Web.Services.LocalAuthenticationOptions;
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
        RegisterServices(authService);

        var cut = RenderComponent();

        cut.Find("[data-testid='auth-provider-microsoft-entra']");
        Assert.DoesNotContain("Choose a demo account for Microsoft", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='auth-user-select']"));
    }

    [Fact]
    public async Task SelectingLiveMicrosoftAfterOpeningMockMicrosoftPicker_ClearsTheTrapAndUsesHostedFlow()
    {
        var authService = CreateAuthService();
        RegisterServices(authService);

        var cut = RenderComponent();

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

    [Fact]
    public void Render_ShowsServerPostedCredentialForm_ForLocalProvider()
    {
        var authService = CreateAuthService();
        RegisterServices(authService, new TestNavigationManager("https://localhost/signin?provider=local"));

        var cut = RenderComponent();

        cut.Find("[data-testid='auth-provider-local']");

        var form = cut.Find("[data-testid='auth-credentials-form']");
        Assert.Equal("post", form.GetAttribute("method"));

        var action = form.GetAttribute("action");
        Assert.NotNull(action);
        Assert.EndsWith("/auth/local/signin", action, StringComparison.Ordinal);

        var usernameInput = cut.Find("[data-testid='auth-identifier']");
        var passwordInput = cut.Find("[data-testid='auth-password']");

        Assert.Equal(string.Empty, usernameInput.GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, passwordInput.GetAttribute("value") ?? string.Empty);
        Assert.NotNull(usernameInput.GetAttribute("required"));
        Assert.NotNull(passwordInput.GetAttribute("required"));
        Assert.Equal(LocalAuthenticationOptions.MinimumPasswordLength.ToString(), passwordInput.GetAttribute("minlength"));
        Assert.Contains("first-time usernames can register on sign-in", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"Local passwords need to be {LocalAuthenticationOptions.MinimumPasswordLength} characters or more.",
            cut.Markup,
            StringComparison.Ordinal);
        cut.Find("[data-testid='auth-submit-sign-in']");
        Assert.Empty(cut.FindAll("[data-testid='auth-user-select']"));
    }

    [Fact]
    public void SelectingLocalProviderAfterOpeningMockPicker_ShowsCredentialForm_AndClearsDemoSelection()
    {
        var authService = CreateAuthService();
        RegisterServices(authService);

        var cut = RenderComponent();

        InvokeSelectProvider(cut, "microsoft");
        Assert.Single(cut.FindAll("[data-testid='auth-user-select']"));

        InvokeSelectProvider(cut, AuthenticationOptions.LocalService);

        Assert.Empty(cut.FindAll("[data-testid='auth-user-select']"));
        cut.Find("[data-testid='auth-credentials-form']");
        Assert.Single(cut.FindAll("[data-testid='auth-submit-sign-in']"));
    }

    [Fact]
    public void Render_ShowsGenericCredentialError_WhenQueryContainsInvalidCredentials()
    {
        var authService = CreateAuthService();
        RegisterServices(authService, new TestNavigationManager("https://localhost/signin?provider=local&error=invalid-credentials"));

        var cut = RenderComponent();

        Assert.Contains("We couldn't sign you in with those credentials.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown mock user", cut.Markup, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _testContext.Dispose();
    }

    private IRenderedComponent<SignInPanel> RenderComponent() =>
        _testContext.Render<SignInPanel>(parameters => parameters
            .Add(component => component.RedirectUri, "/chat"));

    private void RegisterServices(RecordingAuthService authService, NavigationManager? navigationManager = null)
    {
        _testContext.Services.AddSingleton<IAuthService>(authService);
        _testContext.Services.AddSingleton<NavigationManager>(navigationManager ?? new TestNavigationManager());
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

    private static RecordingAuthService CreateAuthService(bool localFirst = false)
    {
        var providers = new List<AuthProviderOption>();
        if (localFirst)
        {
            providers.Add(CreateLocalProvider());
        }

        providers.Add(new AuthProviderOption(
            MicrosoftEntraAuthService.ProviderId,
            "Microsoft",
            "Use the hosted Microsoft sign-in page for your work or school account, then return to AspireAI already signed in.",
            "provider-microsoft",
            false,
            false,
            "/auth/microsoft/signin"));

        if (!localFirst)
        {
            providers.Add(CreateLocalProvider());
        }

        providers.Add(new AuthProviderOption(
            "microsoft",
            "Microsoft",
            "Mock Entra-style sign-in for work accounts.",
            "provider-microsoft",
            true));

        return new RecordingAuthService(
            providers,
            new Dictionary<string, IReadOnlyList<AuthenticatedUser>>(StringComparer.OrdinalIgnoreCase)
            {
                ["microsoft"] = MockMicrosoftUsers
            });
    }

    private static AuthProviderOption CreateLocalProvider() =>
        new(
            AuthenticationOptions.LocalService,
            "Local account",
            "Use a managed username and password that AspireAI validates on the server.",
            "provider-local",
            false,
            true,
            "/auth/local/signin");

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

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string initialUri = "https://localhost/signin")
        {
            Initialize("https://localhost/", initialUri);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
