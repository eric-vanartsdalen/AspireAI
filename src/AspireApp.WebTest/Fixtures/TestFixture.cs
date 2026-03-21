using Aspire.Hosting;
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
		var services = _app.Services;

		// ✅ Start Playwright
		_playwright = await Playwright.CreateAsync();

		_browser = await _playwright.Chromium.LaunchAsync(new()
		{
			Headless = false
		});

		_context = await _browser.NewContextAsync(new()
		{
			IgnoreHTTPSErrors = true
		});

		AppHostMapping.Page = await _context.NewPageAsync();
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