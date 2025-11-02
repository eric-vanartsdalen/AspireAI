using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AspireApp.Web.Components.Pages;

namespace AspireApp.Web.Services;

/// <summary>
/// Background service that warms up the Ollama model on application startup
/// to reduce first-request latency by keeping the model loaded in memory.
/// </summary>
public class OllamaWarmupService : IHostedService
{
	private readonly ILogger<OllamaWarmupService> _logger;
	private readonly IConfiguration _configuration;
	private Timer? _keepAliveTimer;
	private Kernel? _kernel;
	private readonly object _kernelLock = new object();

	// Warmup settings
	private const int WARMUP_DELAY_SECONDS = 10; // Wait 10 seconds after startup before warmup
	private const int KEEP_ALIVE_INTERVAL_MINUTES = 10; // Send keep-alive every 10 minutes
	private const string WARMUP_PROMPT = "Hi"; // Minimal prompt for warmup

	public OllamaWarmupService(
		ILogger<OllamaWarmupService> logger,
		IConfiguration configuration)
	{
		_logger = logger;
		_configuration = configuration;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("🔥 Ollama Warmup Service starting...");

		// Start warmup in background - don't block application startup
		_ = Task.Run(async () => await RunWarmupAsync(cancellationToken), cancellationToken);

		_logger.LogInformation("✅ Ollama Warmup Service started (warmup running in background)");
		return Task.CompletedTask;
	}

	private async Task RunWarmupAsync(CancellationToken cancellationToken)
	{
		try
		{
			// Pull configuration to ensure ActiveModelURL and ActiveModel are set
			HomeConfigurations.PullConfigure();

			// Wait for the application to fully initialize and Ollama to be ready
			_logger.LogInformation("⏳ Waiting {Delay} seconds for services to initialize...", WARMUP_DELAY_SECONDS);
			await Task.Delay(TimeSpan.FromSeconds(WARMUP_DELAY_SECONDS), cancellationToken);

			// Verify Ollama endpoint is available before attempting warmup
			if (!await IsOllamaAvailableAsync(cancellationToken))
			{
				_logger.LogWarning("⚠️ Ollama service not available yet. Skipping warmup - will rely on first user request.");
				return;
			}

			// Perform initial warmup
			await WarmupModelAsync(cancellationToken);

			// Set up periodic keep-alive to maintain model in memory
			_keepAliveTimer = new Timer(
				  callback: async _ => await KeepAliveAsync(),
			 state: null,
				   dueTime: TimeSpan.FromMinutes(KEEP_ALIVE_INTERVAL_MINUTES),
					   period: TimeSpan.FromMinutes(KEEP_ALIVE_INTERVAL_MINUTES)
				   );

			_logger.LogInformation("🎉 Ollama model is warmed up and ready!");
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("🛑 Warmup cancelled during application shutdown");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "❌ Error during Ollama warmup");
		}
	}

	private async Task<bool> IsOllamaAvailableAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
			var response = await httpClient.GetAsync(HomeConfigurations.ActiveModelURL, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				var isOllama = content.Contains("Ollama is running", StringComparison.OrdinalIgnoreCase);

				if (isOllama)
				{
					_logger.LogInformation("✅ Ollama service is available at {Endpoint}", HomeConfigurations.ActiveModelURL);
					return true;
				}
			}

			_logger.LogWarning("⚠️ Ollama endpoint responded but with unexpected content");
			return false;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(
				"⚠️ Could not reach Ollama service at {Endpoint}: {Error}",
				HomeConfigurations.ActiveModelURL,
				ex.Message
			);
			return false;
		}
	}

	private async Task WarmupModelAsync(CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("🔥 Warming up Ollama model: {Model} at {Endpoint}",
			   HomeConfigurations.ActiveModel,
			   HomeConfigurations.ActiveModelURL);

			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			// Create or get kernel
			var kernel = GetOrCreateKernel();

			// Get chat completion service
			var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

			// Create minimal chat history
			var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
			chatHistory.AddUserMessage(WARMUP_PROMPT);

			// Execute warmup request (non-streaming for simplicity)
			var response = await chatCompletionService.GetChatMessageContentAsync(
					chatHistory,
			cancellationToken: cancellationToken
			 );

			stopwatch.Stop();

			_logger.LogInformation(
				"✅ Model warmup completed in {ElapsedMs}ms. Model is now loaded and ready!",
				stopwatch.ElapsedMilliseconds
			);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(
				ex,
				"⚠️ Model warmup failed - this may cause slower first response. Model: {Model}",
				HomeConfigurations.ActiveModel
			);
		}
	}

	private async Task KeepAliveAsync()
	{
		try
		{
			_logger.LogDebug("🔄 Sending keep-alive to maintain model in memory");

			var kernel = GetOrCreateKernel();
			var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

			var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
			chatHistory.AddUserMessage("ping"); // Minimal keep-alive

			// Quick request to keep model loaded
			await chatCompletionService.GetChatMessageContentAsync(
				chatHistory,
				cancellationToken: CancellationToken.None
			);
			_logger.LogDebug("✅ Keep-alive successful");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "⚠️ Keep-alive request failed");
		}
	}

	private Kernel GetOrCreateKernel()
	{
		lock (_kernelLock)
		{
			if (_kernel == null)
			{
				IKernelBuilder builder = Kernel.CreateBuilder();
				builder.AddOllamaChatCompletion(
			 modelId: HomeConfigurations.ActiveModel,
				  endpoint: new Uri(HomeConfigurations.ActiveModelURL)
					);
				_kernel = builder.Build();

				_logger.LogDebug("Created new Semantic Kernel instance for warmup");
			}
			return _kernel;
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("🛑 Ollama Warmup Service stopping...");
		_keepAliveTimer?.Change(Timeout.Infinite, 0);
		_keepAliveTimer?.Dispose();
		_keepAliveTimer = null;
		return Task.CompletedTask;
	}
}
