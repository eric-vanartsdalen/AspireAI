using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using AspireApp.WebTest.DataModels;
using Microsoft.Playwright;

namespace AspireApp.WebTest.Fixtures;

public class TestFixture : IAsyncLifetime
{
	private DistributedApplication? _app;

	public AppHostMappingModel AppHostMapping { get; private set; } = new();

	private IPlaywright? _playwright;
	private IBrowser? _browser;
	private IBrowserContext? _context;

	public async Task InitializeAsync()
	{
		// ✅ Start Aspire AppHost
		var appHost = await DistributedApplicationTestingBuilder
			.CreateAsync<Projects.AspireApp_AppHost>();

		_app = await appHost.BuildAsync();
		await _app.StartAsync();

		// ✅ Resolve endpoint (adjust name if needed)
		AppHostMapping.WebfrontendUri = _app.GetEndpoint("webfrontend", "http").AbsoluteUri;
		AppHostMapping.OllamaUri = _app.GetEndpoint("ollama", "http").AbsoluteUri;

		// TODO: Find if possible, the actual Aspire Dashboard URI.
		// When Aspire Dashboard starts, the console log shows where the dashbaord is like example below.
		// Is there a way or place to get the URL and the login token programmatically instead of having to pull and copy it from the console log?
		// info: Aspire.Hosting.DistributedApplication[0]
		// Login to the dashboard at https://localhost:17171/login?t=9c251823a996fa456b9c4fd2612eb5e6
		//
		var services = _app.Services;

		// ✅ Start Playwright
		_playwright = await Playwright.CreateAsync();

		_browser = await _playwright.Chromium.LaunchAsync(new()
		{
			Headless = false
		});

		AppHostMapping.BrowserContext = await _browser.NewContextAsync(new()
		{
			IgnoreHTTPSErrors = true
		});
	}


	public async Task DisposeAsync()
	{
		if (_context != null)
			await _context.DisposeAsync();

		if (_browser != null)
			await _browser.DisposeAsync();

		_playwright?.Dispose();

		if (_app != null)
			await _app.DisposeAsync();
	}

	async ValueTask IAsyncLifetime.InitializeAsync()
	{
		await InitializeAsync();
	}

	async ValueTask IAsyncDisposable.DisposeAsync()
	{
		await DisposeAsync();
	}
}