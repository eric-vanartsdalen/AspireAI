using AspireApp.Web.Components.Shared;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Diagnostics;

namespace AspireApp.Web.Components.Pages
{
    partial class Chat : ComponentBase, IAsyncDisposable
    {
        [Inject]
        public required IConfiguration configuration { get; set; }

        [Inject]
        public required IJSRuntime JSRuntime { get; set; }

        [Inject]
        public required ChatRefreshService RefreshService { get; set; }

        [Inject]
        public required IHttpClientFactory HttpClientFactory { get; set; }

        private ElementReference questionInput;
        private CancellationTokenSource? _cancellationTokenSource;
        private DotNetObjectReference<Chat>? _dotNetRef;
        
        // Cache the kernel instance to avoid recreation overhead
        private Kernel? _kernel;
        private readonly object _kernelLock = new object();

        private Microsoft.SemanticKernel.ChatCompletion.ChatHistory _chatHistory { get; set; } = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
        private string Status { get; set; } = string.Empty;
        private string Question { get; set; } = string.Empty;
        private string AIResponse { get; set; } = string.Empty;
        private string ElapsedTimeMessage { get; set; } = string.Empty;
        private Boolean IsAIResponsing { get; set; } = false;
        private Boolean IsNotFirstTimeLoading { get; set; } = false;
        private string OllamaServiceMessage { get; set; } = string.Empty;

        [Inject]
        public AiInfoStateService AiInfoState { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            // Use the centralized AiInfoStateService initialization
            await AiInfoState.InitializeAsync();

            // Initialize kernel early to avoid delays during first AI call
            await InitializeKernelAsync();

            await CheckOllamaService();
            IsNotFirstTimeLoading = true;
            StateHasChanged(); // This call is for the Chatbot component itself.
        }

        private async Task InitializeKernelAsync()
        {
            await Task.Run(() =>
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
                    }
                }
            });
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
                }
                return _kernel;
            }
        }

        private async Task QueryAIChat()
        {
            if (!Question.Trim().Equals(string.Empty) && !IsAIResponsing)
            {
                Status = Question;
                Question = string.Empty;
                IsAIResponsing = true;
                AIResponse = string.Empty;
                StateHasChanged();

                _chatHistory.AddUserMessage(Status);
                StateHasChanged();

                try
                {
                    await JSRuntime.InvokeVoidAsync("scrollToBottom");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scrolling: {ex.Message}");
                }

                await CallBackgroundAI();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                _dotNetRef?.Dispose(); // Cleanup any existing reference
                _dotNetRef = DotNetObjectReference.Create(this);

                bool functionReady = false;
                int retries = 20; // Try for up to 2 seconds (20 * 100ms)
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        // Check if the JS function is defined on the window object
                        functionReady = await JSRuntime.InvokeAsync<bool>("eval", "typeof window.initializeKeyboardShortcuts === 'function'");
                        if (functionReady)
                        {
                            Console.WriteLine($"initializeKeyboardShortcuts function found after {i + 1} attempt(s).");
                            break; // Function is ready
                        }
                    }
                    catch (JSException ex)
                    {
                        // Log if eval itself fails, might indicate JS environment issues
                        Console.WriteLine($"JS eval check for initializeKeyboardShortcuts failed (attempt {i + 1}/{retries}): {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        // Catch other potential exceptions during the check
                        Console.WriteLine($"Generic error during JS eval check (attempt {i + 1}/{retries}): {ex.Message}");
                    }
                    await Task.Delay(100); // Wait before retrying
                }

                if (functionReady)
                {
                    try
                    {
                        await JSRuntime.InvokeVoidAsync("initializeKeyboardShortcuts", _dotNetRef);
                        Console.WriteLine("Successfully initialized keyboard shortcuts after polling.");
                    }
                    catch (Exception ex)
                    {
                        // This catch is for errors during the actual call to initializeKeyboardShortcuts
                        Console.WriteLine($"Error calling initializeKeyboardShortcuts after it was found: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    // Log an error if the function is still not found after all retries
                    Console.WriteLine($"Error initializing keyboard shortcuts: 'initializeKeyboardShortcuts' function not found after {retries * 100}ms timeout.");
                }
            }
            if (firstRender || !IsAIResponsing)
            {
                await FocusQuestionInput();
            }
        }

        private async Task FocusQuestionInput()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("focusElement", questionInput);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error focusing input: {ex.Message}");
            }
        }

        private async Task CallBackgroundAI()
        {
            // Use the cached kernel instance instead of creating new one each time
            var kernel = GetOrCreateKernel();
            var stopwatch = Stopwatch.StartNew();

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
                var promptSettings = new PromptExecutionSettings();

                var stream = chatCompletionService.GetStreamingChatMessageContentsAsync(
                    _chatHistory,
                    promptSettings,
                    kernel,
                    _cancellationTokenSource.Token
                );

                // Initialize empty response
                AIResponse = string.Empty;

                // Buffer updates to reduce UI thrashing
                var updateBuffer = new System.Text.StringBuilder();
                var lastUpdateTime = DateTime.UtcNow;
                const int updateIntervalMs = 50; // Update UI every 50ms max

                await foreach (var message in stream)
                {
                    updateBuffer.Append(message.Content);
                    
                    // Only update UI if enough time has passed to reduce thrashing
                    var now = DateTime.UtcNow;
                    if ((now - lastUpdateTime).TotalMilliseconds >= updateIntervalMs)
                    {
                        AIResponse = updateBuffer.ToString();
                        StateHasChanged();
                        lastUpdateTime = now;
                        
                        try
                        {
                            await JSRuntime.InvokeVoidAsync("scrollToBottom");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error scrolling: {ex.Message}");
                        }
                    }
                }
                
                // Final update to ensure all content is displayed
                AIResponse = updateBuffer.ToString();
            }
            catch (OperationCanceledException)
            {
                // Append halt message immediately on cancellation
                const string haltTag = "[AI response was manually halted prematurely.]";
                if (!AIResponse.Contains(haltTag, StringComparison.Ordinal))
                {
                    AIResponse += "\n" + haltTag;
                }
            }
            catch (Exception e)
            {
                AIResponse = "Call Exception occurred! " + e.Message;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
            
            // Add the complete AI response to chat history only once after streaming is done
            if (!string.IsNullOrEmpty(AIResponse))
            {
                _chatHistory.AddAssistantMessage(AIResponse);
            }
            
            stopwatch.Stop();
            IsAIResponsing = false;
            ElapsedTimeMessage = $"Response time: {stopwatch.Elapsed.TotalMilliseconds} milliseconds";
            StateHasChanged();
            await FocusQuestionInput();
        }

        private void StopAIResponse()
        {
            if (IsAIResponsing)
            {
                _cancellationTokenSource?.Cancel();
            }
        }

        private async Task CheckOllamaService()
        {
            Console.WriteLine("Inside CheckOllamaService");
            if (AiInfoState.CurrentAiUri.Trim() == string.Empty)
            {
                OllamaServiceMessage = "Ollama configuration URI is not set.";
                return;
            }
            try
            {
                // Use HttpClient factory instead of creating new instance
                using var client = HttpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5); // Set reasonable timeout
                
                var response = await client.GetAsync(AiInfoState.CurrentAiUri);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (content.Trim().Equals("Ollama is running"))
                    {
                        Console.WriteLine("Ollama is running");
                        OllamaServiceMessage = string.Empty;
                    }
                    else
                    {
                        OllamaServiceMessage = $"Not Ollama Service endpoint. \n{AiInfoState.CurrentAiUri}  returned:\n {content}";
                    }
                }
                else
                {
                    OllamaServiceMessage = "Ollama service is not available.";
                }
            }
            catch (Exception e)
            {
                OllamaServiceMessage = "Ollama service is not available. " + e.Message;
            }
        }

        private MarkupString ConvertToMarkup(string textmessage)
        {
            try
            {
                return new MarkupString(Markdown.ToHtml(textmessage));
            }
            catch
            {
                return new MarkupString(textmessage);
            }
        }

        [JSInvokable]
        public void HandleCtrlC()
        {
            Console.WriteLine("HandleCtrlC called");
            if (IsAIResponsing)
            {
                Console.WriteLine("AI is responding, stopping response");
                StopAIResponse();
            }
            else
            {
                Console.WriteLine("HandleCtrlC called but AI is not responding");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // Cancel any ongoing AI operations
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                
                // Note: Kernel doesn't implement IDisposable, so we don't need to dispose it
                _kernel = null;
                
                if (_dotNetRef != null)
                {
                    await JSRuntime.InvokeVoidAsync("dispose");
                    _dotNetRef.Dispose();
                }
            }
            catch (JSDisconnectedException)
            {
                // Ignore disconnect exceptions
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during disposal: {ex.Message}");
            }
        }
    }
}
