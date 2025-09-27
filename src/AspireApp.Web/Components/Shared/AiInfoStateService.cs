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
                Console.WriteLine($"Chatbot.OnInitializedAsync: Overriding AI_Endpoint with Aspire Ollama URI: {aspireOllamaUri}");
                Environment.SetEnvironmentVariable("AI_Endpoint", aspireOllamaUri);
                _configuration["AI_Endpoint"] = aspireOllamaUri;
            }
            // set through configuration
            var aiUri = Environment.GetEnvironmentVariable("AI_Endpoint")
                ?? _configuration["AI_Endpoint"]
                ?? string.Empty;
            var aiModel = Environment.GetEnvironmentVariable("AI_Model")
                ?? _configuration["AI_Model"]
                ?? string.Empty;

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
                        if (responseBody.Trim().Equals("Ollama is running", StringComparison.InvariantCultureIgnoreCase))
                        {
                            endpointAvailable = true;
                        }
                    }
                }
                catch { }
            }
            SetState(endpointAvailable, aiUri, aiModel);
        }
    }
}
