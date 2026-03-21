using AspireApp.WebTest.DataModels;
using AspireApp.WebTest.Fixtures;
using Microsoft.Playwright;

namespace AspireApp.WebTest.Tests;

public class BasicAspireAppHostTests : IClassFixture<TestFixture>
{
	private readonly AppHostMappingModel _data;
	protected IBrowserContext _browserContext;

	public BasicAspireAppHostTests(TestFixture fixture)
	{
		_data = fixture.AppHostMapping;
		Assert.NotNull(_data.BrowserContext);
		_browserContext = _data.BrowserContext!;
	}

	[Fact]
	public async Task AspireDashboardLoads()
	{
		Assert.False(string.IsNullOrWhiteSpace(_data.AspireDashboardLoginUri));

		IPage page = await _browserContext.NewPageAsync();

		// Navigate to the tokenized login URI. The dashboard validates the
		// browser token, sets an auth cookie, and redirects to the root page.
		await page.GotoAsync(_data.AspireDashboardLoginUri, _data.Options);

		// Wait for the post-authentication redirect to leave /login.
		// If GotoAsync already followed a server-side 302 this resolves immediately;
		// otherwise it waits for the Blazor-driven redirect after token validation.
		await page.WaitForURLAsync(
			url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
			new PageWaitForURLOptions { Timeout = 120_000 });

		// Blazor Server sets the page title asynchronously via <PageTitle>
		// after the SignalR circuit initializes. Give it generous time.
		await page.WaitForFunctionAsync(
			"() => document.title && document.title.length > 0",
			null,
			new PageWaitForFunctionOptions { Timeout = 60_000 });

		var title = await page.TitleAsync();

		Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
		Assert.Equivalent("AspireApp resources", title);
	}

	[Fact]
	public async Task WebLoads()
	{
		Console.WriteLine($"Navigating to Web Frontend at: {_data.WebfrontendUri}");
		IPage page = await _browserContext.NewPageAsync();
		await page.GotoAsync(_data.WebfrontendUri, _data.Options);
		// Check page title equal "Home"
		var title = await page.TitleAsync();
		Assert.Equivalent("Home", title);
	}

	[Fact]
	public async Task OllamaLoads()
	{
		Console.WriteLine($"Navigating to Ollama Frontend at: {_data.OllamaUri}");
		IPage page = await _browserContext.NewPageAsync();
		await page.GotoAsync(_data.OllamaUri, _data.Options);
		// Check the page contains text "Ollama is running"
		var content = await page.ContentAsync();
		Assert.Contains("Ollama is running", content, StringComparison.OrdinalIgnoreCase);
	}
}
