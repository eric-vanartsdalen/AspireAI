using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using AspireApp.WebTest.DataModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Text;

namespace AspireApp.WebTest.Fixtures;

public class TestFixture : IAsyncLifetime
{
	private const string AspireDashboardResourceName = "aspire-dashboard";
	private const string PythonServiceResourceName = "python-service";
	private const string WebFrontendResourceName = "webfrontend";
	private const string DashboardBrowserTokenEnvironmentVariable = "DASHBOARD__FRONTEND__BROWSERTOKEN";
	private const string DashboardPublicUrlEnvironmentVariable = "DASHBOARD__FRONTEND__PUBLICURL";
	private const string SharedDataPathConfigurationEnvironmentVariable = "SharedPaths__Data";
	private const string SharedDatabasePathConfigurationEnvironmentVariable = "SharedPaths__Database";
	private const string SharedDatabaseFileNameConfigurationEnvironmentVariable = "SharedPaths__DatabaseFileName";
	private const string PythonRunAsRootEnvironmentVariable = "PYTHON_RUN_AS_ROOT";

	private DistributedApplication? _app;
	private readonly Dictionary<string, string?> _originalEnvironmentVariables = new(StringComparer.Ordinal);

	public AppHostMappingModel AppHostMapping { get; private set; } = new();

	private IPlaywright? _playwright;
	private IBrowser? _browser;
	private IBrowserContext? _context;
	private string? _testRunRoot;
	private string? _testDataPath;
	private string? _testDatabasePath;
	private string? _testDatabaseFileName;

	public async Task InitializeAsync()
	{
		var appHostContentRoot = GetAppHostContentRoot();
		var repositoryRoot = GetRepositoryRoot(appHostContentRoot);
		ConfigureIsolatedStorage(repositoryRoot);
		if (string.IsNullOrWhiteSpace(_testDataPath) || string.IsNullOrWhiteSpace(_testDatabasePath) || string.IsNullOrWhiteSpace(_testDatabaseFileName))
		{
			throw new InvalidOperationException("Test fixture storage paths were not initialized.");
		}
		// determine mode in use
		var debugMode = false;
		var configFile = "appsettings.json";
#if DEBUG
		Debug.WriteLine("Debug build");
		configFile = "appsettings.Development.json";
		debugMode = true;
#else
			Console.WriteLine("Release build");
#endif
		var configuration = new ConfigurationBuilder()
			.AddJsonFile(Path.Combine(appHostContentRoot, configFile), optional: false)
			.Build();
		Assert.NotNull(configuration);

		// ✅ Start Aspire AppHost
		var appHostArgs = new[]
		{
			$"--SharedPaths:Data={_testDataPath}",
			$"--SharedPaths:Database={_testDatabasePath}",
			$"--SharedPaths:DatabaseFileName={_testDatabaseFileName}",
			"--PYTHON_RUN_AS_ROOT=true"
		};

		var appHost = await DistributedApplicationTestingBuilder
			.CreateAsync<Projects.AspireApp_AppHost>(
				appHostArgs,
				(applicationOptions, hostOptions) =>
				{
					applicationOptions.DisableDashboard = false;
					applicationOptions.AllowUnsecuredTransport = true;

					// Match a normal AppHost run so the dashboard sees the same config files.
					hostOptions.ContentRootPath = appHostContentRoot;
					hostOptions.EnvironmentName = debugMode ? Environments.Development : Environments.Production;
				});
		_app = await appHost.BuildAsync();
		await _app.StartAsync();

		// Wait for the primary app surfaces used by the UI tests before
		// capturing their dynamic endpoints from Aspire.
		var webState = await _app.ResourceNotifications.WaitForResourceHealthyAsync(WebFrontendResourceName);
		var pythonState = await _app.ResourceNotifications.WaitForResourceHealthyAsync(PythonServiceResourceName);
		ValidateDatabaseBindings(webState.Snapshot.EnvironmentVariables, pythonState.Snapshot.EnvironmentVariables);

		// ✅ Resolve Aspire Dashboard endpoint including token
		var dashboardState = await _app.ResourceNotifications.WaitForResourceHealthyAsync(AspireDashboardResourceName);
		var _aspireDashboardUri = GetRequiredEnvironmentVariable(
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
        // Resolve and Map endpoints presented in the Aspire Dashboard itself
		AppHostMapping.WebfrontendUri = _app.GetEndpoint("webfrontend", "https").AbsoluteUri.TrimEnd('/');
		AppHostMapping.OllamaUri = _app.GetEndpoint("ollama", "http").AbsoluteUri.TrimEnd('/');
        AppHostMapping.GraphDBUri = _app.GetEndpoint("graph-db", "http").AbsoluteUri.TrimEnd('/');
        AppHostMapping.LightRagUri = _app.GetEndpoint("lightrag", "http").AbsoluteUri.TrimEnd('/');
        AppHostMapping.PythonServiceUri = _app.GetEndpoint("python-service", "http").AbsoluteUri.TrimEnd('/');
		AppHostMapping.ApiServiceUri = _app.GetEndpoint("apiservice", "https").AbsoluteUri.TrimEnd('/');
        AppHostMapping.SharedDataPath = _testDataPath;
        AppHostMapping.SharedDatabasePath = _testDatabasePath;

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
        // Map main browser context for tests to use.
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
		RestoreEnvironmentVariables();
		CleanupIsolatedStorage();
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

	private static string GetAppHostContentRoot()
	{
		var appHostContentRoot = Path.GetFullPath(
			Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AspireApp.AppHost"));
		// determine mode in use
		var debugMode = false;
#if DEBUG
		Debug.WriteLine("Debug build");
		debugMode = true;
#else
			Console.WriteLine("Release build");
#endif
		string targetConfigFilename = debugMode
			? "appsettings.Development.json"
			: "appsettings.json";
		if (!File.Exists(Path.Combine(appHostContentRoot, targetConfigFilename)))
		{
			throw new DirectoryNotFoundException(
				$"Could not locate the AspireApp.AppHost content root at '{appHostContentRoot}'.");
		}
		return appHostContentRoot;
	}

	private static string GetRepositoryRoot(string appHostContentRoot)
	{
		return Path.GetFullPath(Path.Combine(appHostContentRoot, "..", ".."));
	}

	private void ConfigureIsolatedStorage(string repositoryRoot)
	{
		var testRunId = Guid.NewGuid().ToString("N");
		_testRunRoot = Path.Combine(
			repositoryRoot,
			"TestResults",
			"AspireApp.WebTest",
			testRunId);
		_testDataPath = Path.Combine(_testRunRoot, "data");
		_testDatabasePath = Path.Combine(repositoryRoot, "database");
		_testDatabaseFileName = $"data-resources-test-{testRunId}.db";

		Directory.CreateDirectory(_testDataPath);
		Directory.CreateDirectory(_testDatabasePath);

		SetEnvironmentVariable(SharedDataPathConfigurationEnvironmentVariable, _testDataPath);
		SetEnvironmentVariable(SharedDatabasePathConfigurationEnvironmentVariable, _testDatabasePath);
		SetEnvironmentVariable(SharedDatabaseFileNameConfigurationEnvironmentVariable, _testDatabaseFileName);
		SetEnvironmentVariable(PythonRunAsRootEnvironmentVariable, "true");
	}

	private void SetEnvironmentVariable(string name, string value)
	{
		if (!_originalEnvironmentVariables.ContainsKey(name))
		{
			_originalEnvironmentVariables[name] = Environment.GetEnvironmentVariable(name);
		}

		Environment.SetEnvironmentVariable(name, value);
	}

	private void RestoreEnvironmentVariables()
	{
		foreach (var environmentVariable in _originalEnvironmentVariables)
		{
			Environment.SetEnvironmentVariable(environmentVariable.Key, environmentVariable.Value);
		}

		_originalEnvironmentVariables.Clear();
	}

	private void CleanupIsolatedStorage()
	{
		if (string.IsNullOrWhiteSpace(_testRunRoot) || !Directory.Exists(_testRunRoot))
		{
			return;
		}

		try
		{
			Directory.Delete(_testRunRoot, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}

		if (!string.IsNullOrWhiteSpace(_testDatabasePath) && !string.IsNullOrWhiteSpace(_testDatabaseFileName))
		{
			var databaseFile = Path.Combine(_testDatabasePath, _testDatabaseFileName);
			if (File.Exists(databaseFile))
			{
				try
				{
					File.Delete(databaseFile);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
	}

	private void ValidateDatabaseBindings(IEnumerable<EnvironmentVariableSnapshot> webVariables, IEnumerable<EnvironmentVariableSnapshot> pythonVariables)
	{
		if (string.IsNullOrWhiteSpace(_testDatabaseFileName))
		{
			return;
		}

		var webConnectionString = GetEnvironmentVariable(webVariables, "ConnectionStrings__DefaultConnection");
		if (!string.IsNullOrWhiteSpace(webConnectionString) &&
			!webConnectionString.Contains(_testDatabaseFileName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"Web frontend connection string did not include expected test database '{_testDatabaseFileName}'. Value: {webConnectionString}");
		}

		var pythonDbPath = GetEnvironmentVariable(pythonVariables, "ASPIRE_DB_PATH");
		if (!string.IsNullOrWhiteSpace(pythonDbPath) &&
			!pythonDbPath.EndsWith(_testDatabaseFileName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"Python service database path did not include expected test database '{_testDatabaseFileName}'. Value: {pythonDbPath}");
		}
	}
}
