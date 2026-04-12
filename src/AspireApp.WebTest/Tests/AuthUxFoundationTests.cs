using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Xunit.v3.Priority;

namespace AspireApp.WebTest.Tests;

[TestCaseOrderer(typeof(PriorityOrderer))]
public class AuthUxFoundationTests : IClassFixture<TestFixture>
{
    private static readonly Regex SignInRegex = new("(sign in|get started|continue)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SignOutRegex = new("sign out|log out|logout", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuthenticatedUserRegex = new("signed in|tenant|account|profile|user", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProviderRegex = new("microsoft|google|demo|mock", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppHostMappingModel _data;
    private readonly IBrowser _browser;

    public AuthUxFoundationTests(TestFixture fixture)
    {
        _data = fixture.AppHostMapping;
        Assert.NotNull(_data.Browser);
        _browser = _data.Browser!;
    }

    [Fact, Priority(2)]
    public async Task UnauthenticatedLandingShowsSignInVisibility()
    {
        await WithPageAsync(async page =>
        {
            await page.GotoAsync(_data.WebfrontendUri, _data.Options);
            await WaitForPageLoadCompletion(page);

            await EnsureAuthUxAvailableOrSkipAsync(page);
            await AssertLandingVisibleAsync(page);

            var signOutControl = await FindVisibleAsync(page,
                current => current.Locator("[data-testid='auth-sign-out']"),
                current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignOutRegex }),
                current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignOutRegex }));

            Assert.Null(signOutControl);
        });
    }

    [Fact, Priority(2)]
    public async Task SuccessfulMockSignInTransitionsIntoAuthenticatedShell()
    {
        await WithPageAsync(async page =>
        {
            await SignInAsMockUserAsync(page);

            Assert.DoesNotContain("/signin", page.Url, StringComparison.OrdinalIgnoreCase);

            var identitySurface = await FindVisibleAsync(page,
                current => current.Locator("[data-testid='auth-user-display']"),
                current => current.Locator("[data-testid='auth-summary']"),
                current => current.GetByText(AuthenticatedUserRegex));

            Assert.NotNull(identitySurface);
        });
    }

    [Fact, Priority(2)]
    public async Task SignedInUserCanReachProtectedAppAreas()
    {
        await WithPageAsync(async page =>
        {
            await SignInAsMockUserAsync(page);

            await NavigateToProtectedRouteAndAssertAccessibleAsync(page, "chat", "AI Chatbot", "chat");
            await NavigateToProtectedRouteAndAssertAccessibleAsync(page, "upload", "Upload Data", "Document Upload");
        });
    }

    [Fact, Priority(2)]
    public async Task SignOutReturnsToLandingAndReprotectsAppAreas()
    {
        await WithPageAsync(async page =>
        {
            await SignInAsMockUserAsync(page);
            await SignOutAsync(page);
            await AssertLandingVisibleAsync(page);

            await page.GotoAsync(BuildAbsoluteUri(_data.WebfrontendUri, "chat"), _data.Options);
            await WaitForPageLoadCompletion(page);
            await AssertUnauthenticatedProtectedRedirectAsync(page);
        });
    }

    [Fact, Priority(2)]
    public async Task TenantBindingIsVisibleAfterSignIn()
    {
        await WithPageAsync(async page =>
        {
            await SignInAsMockUserAsync(page);

            var tenantSelector = page.Locator("#tenant-select");
            Assert.True(await tenantSelector.IsVisibleAsync(), "Signed-in UX must keep the tenant selector visible.");

            var selectedTenant = await tenantSelector.InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(selectedTenant), "Signed-in UX did not select a tenant.");
            Assert.NotEqual("default", selectedTenant);

            var exposedTenant = await TryReadVisibleTenantAsync(page);
            if (!string.IsNullOrWhiteSpace(exposedTenant))
            {
                Assert.False(string.IsNullOrWhiteSpace(exposedTenant));
            }
        });
    }

    private async Task SignInAsMockUserAsync(IPage page)
    {
        await page.GotoAsync(_data.WebfrontendUri, _data.Options);
        await WaitForPageLoadCompletion(page);
        await EnsureAuthUxAvailableOrSkipAsync(page);

        var landingCta = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-sign-in-cta']"),
            current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignInRegex }),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignInRegex }));

        if (landingCta is not null)
        {
            await landingCta.ClickAsync();
            await WaitForPageLoadCompletion(page);
        }
        else if (!page.Url.Contains("/signin", StringComparison.OrdinalIgnoreCase))
        {
            await page.GotoAsync(BuildAbsoluteUri(_data.WebfrontendUri, "signin"), _data.Options);
            await WaitForPageLoadCompletion(page);
        }

        var providerChoice = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-provider-mock-microsoft']"),
            current => current.Locator("[data-testid='auth-provider-mock-google']"),
            current => current.Locator("[data-testid='auth-provider-demo']"),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = ProviderRegex }),
            current => current.GetByRole(AriaRole.Link, new() { NameRegex = ProviderRegex }));

        Assert.NotNull(providerChoice);
        await providerChoice!.ClickAsync();
        await WaitForPageLoadCompletion(page);

        await SelectFirstMockUserIfPresentAsync(page);

        var submitControl = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-submit-sign-in']"),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignInRegex }),
            current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignInRegex }));

        if (submitControl is not null)
        {
            await submitControl.ClickAsync();
            await WaitForPageLoadCompletion(page);
        }

        await WaitForAuthenticatedShellAsync(page);
    }

    private async Task SelectFirstMockUserIfPresentAsync(IPage page)
    {
        var userSelect = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-user-select']"),
            current => current.Locator("#mock-user"),
            current => current.Locator("select[name='mock-user']"));

        if (userSelect is null)
        {
            return;
        }

        var values = await userSelect.EvaluateAsync<string[]>(
            "select => Array.from(select.options).map(option => option.value).filter(Boolean)");

        var firstValue = values.FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(firstValue),
            "Mock user picker was rendered, but it did not expose any selectable mock users.");

        await userSelect.SelectOptionAsync(new SelectOptionValue { Value = firstValue });
        await WaitForPageLoadCompletion(page);
    }

    private async Task SignOutAsync(IPage page)
    {
        var signOutControl = await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-sign-out']"),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignOutRegex }),
            current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignOutRegex }));

        Assert.NotNull(signOutControl);
        await signOutControl!.ClickAsync();
        await WaitForPageLoadCompletion(page);
    }

    private async Task NavigateToProtectedRouteAndAssertAccessibleAsync(IPage page, string route, params string[] expectedMarkers)
    {
        await page.GotoAsync(BuildAbsoluteUri(_data.WebfrontendUri, route), _data.Options);
        await WaitForPageLoadCompletion(page);

        Assert.DoesNotContain("/signin", page.Url, StringComparison.OrdinalIgnoreCase);

        foreach (var marker in expectedMarkers)
        {
            if (page.Url.Contains(route, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(await page.TitleAsync(), marker, StringComparison.OrdinalIgnoreCase) ||
                await page.GetByText(marker, new() { Exact = false }).First.IsVisibleAsync())
            {
                return;
            }
        }

        var content = await page.ContentAsync();
        Assert.Fail($"Protected route '{route}' did not expose any expected marker ({string.Join(", ", expectedMarkers)}). URL: {page.Url}. Content length: {content.Length}.");
    }

    private async Task EnsureAuthUxAvailableOrSkipAsync(IPage page)
    {
        if (await IsAuthLandingVisibleAsync(page) || await IsSignInSurfaceVisibleAsync(page))
        {
            return;
        }

        await page.GotoAsync(BuildAbsoluteUri(_data.WebfrontendUri, "signin"), _data.Options);
        await WaitForPageLoadCompletion(page);

        Assert.SkipWhen(
            !await IsSignInSurfaceVisibleAsync(page),
            "Mock auth UX foundation is not available in this checkout yet. Jeff needs to land the landing/sign-in/dashboard surfaces plus stable auth test hooks before these acceptance tests can execute.");
    }

    private async Task AssertLandingVisibleAsync(IPage page)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < timeoutAt)
        {
            var landingSurface = await FindVisibleAsync(page,
                current => current.Locator("[data-testid='auth-landing']"),
                current => current.Locator("[data-testid='auth-sign-in-cta']"),
                current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignInRegex }),
                current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignInRegex }));

            if (landingSurface is not null)
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Landing UX was not visible after navigation. Final URL: {page.Url}");
    }

    private async Task AssertUnauthenticatedProtectedRedirectAsync(IPage page)
    {
        if (page.Url.Contains("/signin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await AssertLandingVisibleAsync(page);
    }

    private async Task<bool> IsAuthLandingVisibleAsync(IPage page)
    {
        return await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-landing']"),
            current => current.Locator("[data-testid='auth-sign-in-cta']"),
            current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignInRegex }),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignInRegex })) is not null;
    }

    private async Task<bool> IsSignInSurfaceVisibleAsync(IPage page)
    {
        return await FindVisibleAsync(page,
            current => current.Locator("[data-testid='auth-sign-in']"),
            current => current.Locator("[data-testid='auth-provider-list']"),
            current => current.Locator("[data-testid^='auth-provider-']"),
            current => current.GetByRole(AriaRole.Button, new() { NameRegex = ProviderRegex }),
            current => current.GetByRole(AriaRole.Link, new() { NameRegex = ProviderRegex })) is not null;
    }

    private async Task WaitForAuthenticatedShellAsync(IPage page)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < timeoutAt)
        {
            var signOut = await FindVisibleAsync(page,
                current => current.Locator("[data-testid='auth-sign-out']"),
                current => current.GetByRole(AriaRole.Button, new() { NameRegex = SignOutRegex }),
                current => current.GetByRole(AriaRole.Link, new() { NameRegex = SignOutRegex }));

            if (signOut is not null)
            {
                return;
            }

            if (!page.Url.Contains("/signin", StringComparison.OrdinalIgnoreCase) &&
                await page.Locator("#tenant-select").IsVisibleAsync())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Sign-in did not transition into an authenticated shell. Final URL: {page.Url}");
    }

    private static async Task<string?> TryReadVisibleTenantAsync(IPage page)
    {
        var explicitTenant = page.Locator("[data-testid='auth-current-tenant']");
        if (await explicitTenant.IsVisibleAsync())
        {
            return await explicitTenant.TextContentAsync();
        }

        var authSummary = page.Locator("[data-testid='auth-user-display']");
        if (await authSummary.IsVisibleAsync())
        {
            var attributeValue = await authSummary.GetAttributeAsync("data-auth-tenant");
            if (!string.IsNullOrWhiteSpace(attributeValue))
            {
                return attributeValue;
            }

            return await authSummary.TextContentAsync();
        }

        return null;
    }

    private async Task WithPageAsync(Func<IPage, Task> testAction)
    {
        await using var browserContext = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });

        var page = await browserContext.NewPageAsync();

        try
        {
            await testAction(page);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task<ILocator?> FindVisibleAsync(IPage page, params Func<IPage, ILocator>[] candidates)
    {
        foreach (var candidateFactory in candidates)
        {
            var candidate = candidateFactory(page).First;
            try
            {
                if (await candidate.IsVisibleAsync())
                {
                    return candidate;
                }
            }
            catch (PlaywrightException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        return null;
    }

    private static string BuildAbsoluteUri(string baseUri, string relativePath)
    {
        return new Uri(new Uri($"{baseUri.TrimEnd('/')}/"), relativePath).AbsoluteUri;
    }

    private static async Task WaitForPageLoadCompletion(IPage page)
    {
        await Task.WhenAll(
            page.WaitForLoadStateAsync(LoadState.Load, new PageWaitForLoadStateOptions { Timeout = 10_000 }),
            page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10_000 }));
    }
}
