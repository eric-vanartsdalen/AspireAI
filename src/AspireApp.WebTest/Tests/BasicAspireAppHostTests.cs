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

		// Check basic Dashboard items
		Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
		Assert.Equivalent("AspireApp resources", title);

		// Capture all the Configured AppHost Resources and where appropropriate, their expected configuration endpoint links.
		await page.Locator("table tbody tr td[col-index='1']").First.WaitForAsync(new LocatorWaitForOptions()
		{
			Timeout = 5000
		});
		List<ILocator> rowNames = (await page.Locator("table tbody tr td[col-index='1']").AllAsync()).ToList();
		List<ILocator> rowResources = (await page.Locator("table tbody tr td[col-index='4']").AllAsync()).ToList();
		List<ILocator> rowLinks = (await page.Locator("table tbody tr td[col-index='5']").AllAsync()).ToList();
		string ollamaLink = string.Empty;
		string webFrontendLink = string.Empty;
		for ( int i = 0; i<rowNames.Count; i++)
		{
			var name = await rowNames[i].TextContentAsync();
			var resource = await rowResources[i].TextContentAsync();
			var link = await rowLinks[i].TextContentAsync();
			var message = $"{name.Trim()} | {resource.Trim()} | {link.Trim()}";
			Console.WriteLine(message);
			if(name.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
			{
				ollamaLink = link.Trim();
			}
			if(name.Contains("WebFrontend", StringComparison.OrdinalIgnoreCase))
			{
				webFrontendLink = link.Trim();
			}
		}
		// Find service for Ollama and Web Frontend and check their links are correct.
		Assert.False(string.IsNullOrWhiteSpace(ollamaLink), "Ollama link should not be empty");
		Assert.False(string.IsNullOrWhiteSpace(webFrontendLink), "Web Frontend link should not be empty");
		// issue seems to be here...
		// My hunch is that when the AspireApp.AppHost is launched from the fixture
		// it may not be correctly picking up the configured endpoints for Ollama and Web Frontend,
		// which results in the dashboard showing incorrect links for these resources.
		// The test then fails when it tries to validate those links against the expected URIs from the fixture data.
		Assert.True(ollamaLink.Contains(_data.OllamaUri, StringComparison.OrdinalIgnoreCase),
			$"Ollama link ({ollamaLink}) should contain {_data.OllamaUri}");
		Assert.True(webFrontendLink.Contains(_data.WebfrontendUri, StringComparison.OrdinalIgnoreCase), 
			$"Web Frontend link ({webFrontendLink}) should contain {_data.WebfrontendUri}");

		Console.WriteLine("Aspire Dashboard Resources");
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
