using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using AspireApp.WebTest.DataModels;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Playwright;
using System.Text;

namespace AspireApp.WebTest.Fixtures;

public class TestFixture : IAsyncLifetime
{
	private const string AspireDashboardResourceName = "aspire-dashboard";
	private const string DashboardBrowserTokenEnvironmentVariable = "DASHBOARD__FRONTEND__BROWSERTOKEN";
	private const string DashboardPublicUrlEnvironmentVariable = "DASHBOARD__FRONTEND__PUBLICURL";

	private DistributedApplication? _app;

	public AppHostMappingModel AppHostMapping { get; private set; } = new();

	private IPlaywright? _playwright;
	private IBrowser? _browser;
	private IBrowserContext? _context;

	public async Task InitializeAsync()
	{
		// ✅ Start Aspire AppHost
		var appHost = await DistributedApplicationTestingBuilder
			.CreateAsync<Projects.AspireApp_AppHost>(
				[],
				static (applicationOptions, _) =>
				{
					applicationOptions.DisableDashboard = false;
					applicationOptions.AllowUnsecuredTransport = true;
				});

		_app = await appHost.BuildAsync();
		await _app.StartAsync();

		// ✅ Resolve Aspire Dashboard endpoint including token
		var dashboardState = await _app.ResourceNotifications.WaitForResourceHealthyAsync(AspireDashboardResourceName);
		var _aspireDashboardUri = GetEnvironmentVariable(
			dashboardState.Snapshot.EnvironmentVariables, 
			DashboardPublicUrlEnvironmentVariable);
		var _aspireDashboardBrowserToken = GetRequiredEnvironmentVariable(
			dashboardState.Snapshot.EnvironmentVariables,
			DashboardBrowserTokenEnvironmentVariable);

		_aspireDashboardUri = _aspireDashboardUri.EndsWith("/", StringComparison.Ordinal)
			? _aspireDashboardUri
			: $"{_aspireDashboardUri}/";

		var builder = new StringBuilder().Append(_aspireDashboardUri + "login?t=" + _aspireDashboardBrowserToken);
		AppHostMapping.AspireDashboardLoginUri = builder.ToString();

		AppHostMapping.WebfrontendUri = _app.GetEndpoint("webfrontend", "http").AbsoluteUri;
		AppHostMapping.OllamaUri = _app.GetEndpoint("ollama", "http").AbsoluteUri;

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

		AppHostMapping.BrowserContext = _context;
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

	private static string GetRequiredEnvironmentVariable(IEnumerable<EnvironmentVariableSnapshot> environmentVariables, string name)
	{
		var value = GetEnvironmentVariable(environmentVariables, name);

		return !string.IsNullOrWhiteSpace(value)
			? value
			: throw new InvalidOperationException($"Could not find required Aspire dashboard environment variable '{name}'.");
	}

	private static string? GetEnvironmentVariable(IEnumerable<EnvironmentVariableSnapshot> environmentVariables, string name)
	{
		foreach (var environmentVariable in environmentVariables)
		{
			if (string.Equals(environmentVariable.Name, name, StringComparison.Ordinal))
			{
				return environmentVariable.Value;
			}
		}

		return null;
	}
}
