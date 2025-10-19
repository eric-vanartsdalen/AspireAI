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

        [Inject]
        public required SpeechService SpeechService { get; set; }

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

        // Speech-related properties
        private SpeechSupport? SpeechSupport { get; set; }
        private bool IsListening { get; set; } = false;
        private bool IsSpeaking { get; set; } = false;
        private string SpeechTranscript { get; set; } = string.Empty;
        private string InterimTranscript { get; set; } = string.Empty;
        private string SpeechStatusText { get; set; } = string.Empty;
        private string SpeechStatusMessage { get; set; } = string.Empty;
        private string? CurrentlySpeakingMessage { get; set; } = null; // Track which message is being spoken

        [Inject]
        public AiInfoStateService AiInfoState { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            Console.WriteLine("=== Chat OnInitializedAsync START ===");
            
            // Debug: Check what configuration values we have
            var configEndpoint = configuration["AI-Endpoint"];
            var configModel = configuration["AI-Model"];
            var envEndpoint = Environment.GetEnvironmentVariable("AI-Endpoint");
            var envModel = Environment.GetEnvironmentVariable("AI-Model");
            
            Console.WriteLine($"Chat: Config AI-Endpoint = '{configEndpoint}'");
            Console.WriteLine($"Chat: Config AI-Model = '{configModel}'");
            Console.WriteLine($"Chat: Env AI-Endpoint = '{envEndpoint}'");
            Console.WriteLine($"Chat: Env AI-Model = '{envModel}'");
            Console.WriteLine($"Chat: HomeConfigurations.ActiveModelURL = '{HomeConfigurations.ActiveModelURL}'");
            Console.WriteLine($"Chat: HomeConfigurations.ActiveModel = '{HomeConfigurations.ActiveModel}'");
            
            // Use the centralized AiInfoStateService initialization
            await AiInfoState.InitializeAsync();
            
            Console.WriteLine($"Chat: AiInfoState.EndPointAvailable = {AiInfoState.EndPointAvailable}");
            Console.WriteLine($"Chat: AiInfoState.CurrentAiUri = '{AiInfoState.CurrentAiUri}'");
            Console.WriteLine($"Chat: AiInfoState.CurrentAiModel = '{AiInfoState.CurrentAiModel}'");

            // Initialize speech service
            await InitializeSpeechService();

            // Initialize kernel early to avoid delays during first AI call
            await InitializeKernelAsync();

            await CheckOllamaService();
            IsNotFirstTimeLoading = true;
            
            Console.WriteLine("=== Chat OnInitializedAsync END ===");
            StateHasChanged(); // This call is for the Chatbot component itself.
        }

        private async Task InitializeSpeechService()
        {
            try
            {
                // Initialize speech service and get support information
                SpeechSupport = await SpeechService.InitializeAsync();
                
                if (!SpeechSupport.SpeechRecognition && !SpeechSupport.TextToSpeech)
                {
                    SpeechStatusMessage = "Speech features are not supported in this browser. Please use Chrome, Edge, or Safari for voice functionality.";
                }
                else if (!SpeechSupport.SpeechRecognition)
                {
                    SpeechStatusMessage = "Speech recognition is not supported in this browser. Text-to-speech is available.";
                }
                else if (!SpeechSupport.TextToSpeech)
                {
                    SpeechStatusMessage = "Text-to-speech is not supported in this browser. Speech recognition is available.";
                }

                // Subscribe to speech events
                SpeechService.SpeechRecognitionResult += OnSpeechRecognitionResult;
                SpeechService.SpeechRecognitionError += OnSpeechRecognitionError;
                SpeechService.SpeechRecognitionEnd += OnSpeechRecognitionEnd;
                SpeechService.TextToSpeechStart += OnTextToSpeechStart;
                SpeechService.TextToSpeechEnd += OnTextToSpeechEnd;
                SpeechService.TextToSpeechError += OnTextToSpeechError;

                Console.WriteLine($"Speech service initialized - Recognition: {SpeechSupport.SpeechRecognition}, TTS: {SpeechSupport.TextToSpeech}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing speech service: {ex.Message}");
                SpeechStatusMessage = "Error initializing speech features. Please refresh the page to try again.";
            }
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
                // Stop listening if currently active before submitting query
                if (IsListening)
                {
                    await StopListening();
                }

                Status = Question;
                Question = string.Empty;
                
                // Clear speech transcript when sending message
                SpeechTranscript = string.Empty;
                InterimTranscript = string.Empty;
                
                IsAIResponsing = true;
                AIResponse = string.Empty;
                StateHasChanged();

                _chatHistory.AddUserMessage(Status);
                StateHasChanged();

                // Scroll to bottom after adding user message with a small delay
                try
                {
                    await Task.Delay(50); // Small delay to ensure DOM update
                    await JSRuntime.InvokeVoidAsync("scrollChatToBottom");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scrolling: {ex.Message}");
                }

                await CallBackgroundAI();
            }
        }

        // Speech event handlers
        private void OnSpeechRecognitionResult(string finalTranscript, string interimTranscript)
        {
            InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(finalTranscript))
                {
                    // Add final transcript to the question input
                    if (string.IsNullOrWhiteSpace(Question))
                    {
                        Question = finalTranscript.Trim();
                    }
                    else
                    {
                        Question += " " + finalTranscript.Trim();
                    }
                    
                    SpeechTranscript = string.Empty;
                }
                
                InterimTranscript = interimTranscript;
                SpeechTranscript = finalTranscript;
                StateHasChanged();
            });
        }

        private void OnSpeechRecognitionError(string error)
        {
            InvokeAsync(() =>
            {
                IsListening = false;
                SpeechStatusText = $"Speech recognition stopped";
                StateHasChanged();
            });
        }

        private void OnSpeechRecognitionEnd()
        {
            InvokeAsync(() =>
            {
                IsListening = false;
                SpeechStatusText = "Speech recognition stopped";
                SpeechTranscript = string.Empty;
                InterimTranscript = string.Empty;
                StateHasChanged();
            });
        }

        private void OnTextToSpeechStart()
        {
            InvokeAsync(() =>
            {
                IsSpeaking = true;
                StateHasChanged();
            });
        }

        private void OnTextToSpeechEnd()
        {
            InvokeAsync(() =>
            {
                IsSpeaking = false;
                CurrentlySpeakingMessage = null;
                SpeechStatusText = $"Text-to-speech stopped";
                StateHasChanged();
            });
        }

        private void OnTextToSpeechError(string error)
        {
            InvokeAsync(() =>
            {
                IsSpeaking = false;
                CurrentlySpeakingMessage = null;
                SpeechStatusText = $"Text-to-speech stopped";
                StateHasChanged();
            });
        }

        // Speech control methods
        private async Task ToggleMicrophone()
        {
            if (IsListening)
            {
                await StopListening();
            }
            else
            {
                await StartListening();
            }
        }

        private async Task StartListening()
        {
            try
            {
                var success = await SpeechService.StartListeningAsync();
                if (success)
                {
                    // Stop any ongoing TTS when starting speech recognition
                    if (IsSpeaking)
                    {
                        await StopSpeaking();
                    }
                    
                    IsListening = true;
                    SpeechStatusText = "Listening... Speak now";
                }
                else
                {
                    SpeechStatusText = "Failed to start speech recognition";
                }
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting speech recognition: {ex.Message}");
                SpeechStatusText = "Error starting speech recognition";
                StateHasChanged();
            }
        }

        private async Task StopListening()
        {
            try
            {
                await SpeechService.StopListeningAsync();
                IsListening = false;
                SpeechStatusText = "Speech recognition stopped";
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping speech recognition: {ex.Message}");
            }
        }

        private async Task ToggleTextToSpeech()
        {
            if (IsSpeaking)
            {
                await StopSpeaking();
            }
            else if (!string.IsNullOrEmpty(AIResponse))
            {
                await SpeakAIResponse();
            }
        }

        private async Task SpeakAIResponse()
        {
            if (!string.IsNullOrEmpty(AIResponse))
            {
                CurrentlySpeakingMessage = AIResponse;
                await SpeakMessage(AIResponse);
            }
        }

        private async Task SpeakMessage(string message)
        {
            try
            {
                // Stop any ongoing speech or listening
                if (IsSpeaking)
                {
                    await StopSpeaking();
                    return; // If we're already speaking this message, just stop
                }
                
                if (IsListening)
                {
                    await StopListening();
                }

                // Set the currently speaking message
                CurrentlySpeakingMessage = message;
                
                // Convert markdown to plain text for better speech
                var plainText = ConvertMarkdownToPlainText(message);
                await SpeechService.SpeakAsync(plainText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error speaking message: {ex.Message}");
                SpeechStatusText = "Error speaking message";
                CurrentlySpeakingMessage = null;
                StateHasChanged();
            }
        }

        private async Task StopSpeaking()
        {
            try
            {
                await SpeechService.StopSpeakingAsync();
                IsSpeaking = false;
                CurrentlySpeakingMessage = null;
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping speech: {ex.Message}");
            }
        }

        // Helper method to check if a specific message is currently being spoken
        private bool IsMessageBeingSpoken(string message)
        {
            return IsSpeaking && CurrentlySpeakingMessage == message;
        }

        // Helper properties for HTML attributes to avoid complex Razor expressions
        private bool IsTtsMessageButtonDisabled(string message)
        {
            return IsSpeaking && !IsMessageBeingSpoken(message);
        }

        private bool IsTtsButtonDisabled()
        {
            return string.IsNullOrEmpty(AIResponse);
        }

        private string ConvertMarkdownToPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return string.Empty;

            try
            {
                // Convert markdown to HTML first
                var html = Markdown.ToHtml(markdown);
                
                // Simple HTML tag removal for speech
                var plainText = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
                
                // Decode HTML entities
                plainText = System.Net.WebUtility.HtmlDecode(plainText);
                
                return plainText.Trim();
            }
            catch
            {
                // Fallback to original text if conversion fails
                return markdown;
            }
        }

        // Other existing methods...

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

            // Auto-scroll to bottom after each render when AI is responding
            if (IsAIResponsing)
            {
                try
                {
                    await Task.Delay(10); // Small delay to ensure DOM update
                    await JSRuntime.InvokeVoidAsync("scrollChatToBottom");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scrolling during render: {ex.Message}");
                }
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
                const int updateIntervalMs = 100; // Update UI every 100ms max

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
                        
                        // Auto-scroll during AI response
                        try
                        {
                            // Small delay to ensure the DOM updates before scrolling
                            await Task.Delay(10);
                            await JSRuntime.InvokeVoidAsync("scrollChatToBottom");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error scrolling during stream: {ex.Message}");
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

            // Final scroll to bottom after AI response is complete
            try
            {
                await Task.Delay(50); // Delay to ensure DOM update
                await JSRuntime.InvokeVoidAsync("scrollChatToBottom");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scrolling after AI response: {ex.Message}");
            }

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
                // Unsubscribe from speech events
                if (SpeechService != null)
                {
                    SpeechService.SpeechRecognitionResult -= OnSpeechRecognitionResult;
                    SpeechService.SpeechRecognitionError -= OnSpeechRecognitionError;
                    SpeechService.SpeechRecognitionEnd -= OnSpeechRecognitionEnd;
                    SpeechService.TextToSpeechStart -= OnTextToSpeechStart;
                    SpeechService.TextToSpeechEnd -= OnTextToSpeechEnd;
                    SpeechService.TextToSpeechError -= OnTextToSpeechError;
                    
                    await SpeechService.DisposeAsync();
                }

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
