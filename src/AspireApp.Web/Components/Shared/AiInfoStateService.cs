namespace AspireApp.Web.Components.Shared
{
    public class AiInfoStateService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private bool _initialized = false;

        public AiInfoStateService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        public bool EndPointAvailable { get; private set; } = false;
        public string CurrentAiUri { get; private set; } = string.Empty;
        public string CurrentAiModel { get; private set; } = string.Empty;

        public event Action? OnChange;

        public void SetState(bool endpointAvailable, string currentAiUri, string currentAiModel)
        {
            EndPointAvailable = endpointAvailable;
            CurrentAiUri = currentAiUri;
            CurrentAiModel = currentAiModel;
            OnChange?.Invoke();
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;
            // Override Environment URL if Aspire Ollama in use...
            var aspireOllamaUri = ServiceDiscoveryUtilities.GetServiceConnectionString("ollama").Replace("Endpoint=", "");
            if (!string.IsNullOrEmpty(aspireOllamaUri))
            {
                Console.WriteLine($"Chatbot.OnInitializedAsync: Overriding AI-Endpoint with Aspire Ollama URI: {aspireOllamaUri}");
                Environment.SetEnvironmentVariable("AI-Endpoint", aspireOllamaUri);
            }
            
            // Use the correct configuration keys (with hyphens) and try both environment variable formats
            var aiUri = Environment.GetEnvironmentVariable("AI-Endpoint")      // AppHost format
                ?? Environment.GetEnvironmentVariable("AI_Endpoint")           // Legacy format
                ?? _configuration["AI-Endpoint"]                               // Configuration format
                ?? _configuration["AI_Endpoint"]                               // Legacy configuration format
                ?? string.Empty;
                
            var aiModel = Environment.GetEnvironmentVariable("AI-Model")       // AppHost format
                ?? Environment.GetEnvironmentVariable("AI_Model")              // Legacy format
                ?? _configuration["AI-Model"]                                  // Configuration format
                ?? _configuration["AI_Model"]                                  // Legacy configuration format
                ?? string.Empty;

            Console.WriteLine($"AiInfoStateService: AI URI = '{aiUri}', AI Model = '{aiModel}'");

            bool endpointAvailable = false;

            if (!string.IsNullOrEmpty(aiUri))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(aiUri);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"AiInfoStateService: Response from {aiUri}: {responseBody}");
                        if (responseBody.Trim().Equals("Ollama is running", StringComparison.InvariantCultureIgnoreCase))
                        {
                            endpointAvailable = true;
                            Console.WriteLine("AiInfoStateService: Ollama endpoint is available");
                        }
                        else
                        {
                            Console.WriteLine($"AiInfoStateService: Unexpected response from Ollama endpoint: {responseBody}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"AiInfoStateService: HTTP error {response.StatusCode} from {aiUri}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"AiInfoStateService: Exception checking endpoint {aiUri}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("AiInfoStateService: No AI URI configured");
            }
            
            SetState(endpointAvailable, aiUri, aiModel);
        }
    }
}
