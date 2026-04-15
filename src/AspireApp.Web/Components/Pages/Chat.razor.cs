using AspireApp.Web.Components.Shared;
using AspireApp.Web.Services;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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
        public required IHttpClientFactory HttpClientFactory { get; set; }

        [Inject]
        public required SpeechService SpeechService { get; set; }

        [Inject]
        public required AuthenticationStateProvider AuthenticationStateProvider { get; set; }

        [Inject]
        public required IChatConversationService ChatConversationService { get; set; }

        [Inject]
        public required TenantContextService TenantContext { get; set; }

        [Inject]
        public AiInfoStateService AiInfoState { get; set; } = default!;

        [Inject]
        public required IBrainChatClient BrainChatClient { get; set; }

        private ElementReference questionInput;
        private ElementReference conversationTitleInput;
        private CancellationTokenSource? _cancellationTokenSource;
        private DotNetObjectReference<Chat>? _dotNetRef;
        private const int AiFirstTokenTimeoutSeconds = 45;
        private const int AiResponseTimeoutSeconds = 150;
        private const string HaltedResponseTag = "[AI response was manually halted prematurely.]";
        private const string TimedOutResponseTag = "[AI response timed out before completion.]";

        private Kernel? _kernel;
        private readonly object _kernelLock = new();

        private ChatHistory _chatHistory { get; set; } = new();
        private IReadOnlyList<ChatConversationSummary> Conversations { get; set; } = [];
        private Guid? ActiveConversationId { get; set; }
        private string ActiveConversationTitle { get; set; } = ChatConversationTitleHelper.BuildFallbackTitle(string.Empty);
        private string ConversationTitleDraft { get; set; } = string.Empty;
        private bool IsEditingConversationTitle { get; set; }
        private bool ShouldFocusConversationTitleInput { get; set; }
        private bool ShouldFocusQuestionInput { get; set; } = true;
        private string ConversationStatusMessage { get; set; } = string.Empty;
        private bool ConversationStatusIsError { get; set; }
        private AuthenticatedUser? CurrentUser { get; set; }
        private string Status { get; set; } = string.Empty;
        private string Question { get; set; } = string.Empty;
        private string AIResponse { get; set; } = string.Empty;
        private string ElapsedTimeMessage { get; set; } = string.Empty;
        private bool IsAIResponsing { get; set; }
        private bool IsNotFirstTimeLoading { get; set; }
        private string OllamaServiceMessage { get; set; } = string.Empty;

        // Speech-related properties
        private SpeechSupport? SpeechSupport { get; set; }
        private bool IsListening { get; set; }
        private bool IsSpeaking { get; set; }
        private string SpeechTranscript { get; set; } = string.Empty;
        private string InterimTranscript { get; set; } = string.Empty;
        private string SpeechStatusText { get; set; } = string.Empty;
        private string SpeechStatusMessage { get; set; } = string.Empty;
        private string? CurrentlySpeakingMessage { get; set; }
        private bool IsInteractiveReady { get; set; }

        // Mode selector
        private string SelectedChatMode { get; set; } = ChatConversationModes.Regular;

        // Citation tracking: maps assistant message index to evidence from the gateway response
        private readonly Dictionary<int, BrainChatResponse> _messageEvidence = new();

        private string ConversationStatusCssClass => ConversationStatusIsError ? "alert alert-danger" : "alert alert-info";

        private bool HasActiveConversation => ActiveConversationId.HasValue;

        private string ConversationHeading => HasActiveConversation
            ? ActiveConversationTitle
            : ChatConversationTitleHelper.BuildFallbackTitle(string.Empty);

        private string InputPlaceholder => AiInfoState.EndPointAvailable
            ? "Enter your question or use voice input"
            : "AI service unavailable. Saved conversations are still available.";

        protected override async Task OnInitializedAsync()
        {
            Console.WriteLine("=== Chat OnInitializedAsync START ===");

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

            CurrentUser = await ResolveCurrentUserAsync();
            if (CurrentUser is not null)
            {
                await TenantContext.EnsureInitializedAsync();
                await LoadConversationSummariesAsync();
            }

            await AiInfoState.InitializeAsync();

            Console.WriteLine($"Chat: AiInfoState.EndPointAvailable = {AiInfoState.EndPointAvailable}");
            Console.WriteLine($"Chat: AiInfoState.CurrentAiUri = '{AiInfoState.CurrentAiUri}'");
            Console.WriteLine($"Chat: AiInfoState.CurrentAiModel = '{AiInfoState.CurrentAiModel}'");

            await InitializeSpeechService();
            await InitializeKernelAsync();
            await CheckOllamaService();

            IsNotFirstTimeLoading = true;

            Console.WriteLine("=== Chat OnInitializedAsync END ===");
            StateHasChanged();
        }

        private async Task<AuthenticatedUser?> ResolveCurrentUserAsync()
        {
            if (AuthenticationContext.CurrentUser is not null)
            {
                CurrentUser = AuthenticationContext.CurrentUser;
                return CurrentUser;
            }

            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = AuthenticatedUserClaims.BuildUser(authState.User);
            if (user is null)
            {
                AuthenticationContext.SetCurrentUser(null);
                CurrentUser = null;
                return null;
            }

            AuthenticationContext.SetCurrentUser(user);
            CurrentUser = user;
            return CurrentUser;
        }

        private async Task LoadConversationSummariesAsync()
        {
            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                Conversations = [];
                ResetConversationDraft(clearStatus: false);
                return;
            }

            Conversations = await ChatConversationService.ListConversationsAsync(user.UserId);

            if (!ActiveConversationId.HasValue)
            {
                return;
            }

            var activeConversation = Conversations.FirstOrDefault(conversation => conversation.ConversationId == ActiveConversationId.Value);
            if (activeConversation is null)
            {
                ResetConversationDraft(clearStatus: false);
                return;
            }

            ApplyConversationSummary(activeConversation);
        }

        private Task StartNewConversationAsync()
        {
            if (IsAIResponsing)
            {
                return Task.CompletedTask;
            }

            ResetConversationDraft();
            RequestQuestionInputFocus();
            StateHasChanged();
            return Task.CompletedTask;
        }

        private async Task SelectConversationAsync(Guid conversationId)
        {
            if (IsAIResponsing)
            {
                return;
            }

            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                SetConversationStatus("Your sign-in session expired. Please sign in again.", isError: true);
                return;
            }

            var conversation = await ChatConversationService.GetConversationAsync(conversationId, user.UserId);
            if (conversation is null)
            {
                SetConversationStatus("That conversation could not be loaded.", isError: true);
                await LoadConversationSummariesAsync();
                return;
            }

            ApplyConversationDetail(conversation);
            ClearConversationStatus();
            RequestQuestionInputFocus();
            await ScrollChatToBottomAsync();
        }

        private void BeginConversationTitleEdit()
        {
            if (!HasActiveConversation || IsAIResponsing)
            {
                return;
            }

            ConversationTitleDraft = ActiveConversationTitle;
            IsEditingConversationTitle = true;
            ShouldFocusQuestionInput = false;
            ShouldFocusConversationTitleInput = true;
        }

        private void CancelConversationTitleEdit()
        {
            IsEditingConversationTitle = false;
            ShouldFocusConversationTitleInput = false;
            ConversationTitleDraft = ActiveConversationTitle;
            RequestQuestionInputFocus();
        }

        private async Task SaveConversationTitleAsync()
        {
            if (!HasActiveConversation || IsAIResponsing)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ConversationTitleDraft))
            {
                SetConversationStatus("Enter a title before saving.", isError: true);
                ShouldFocusConversationTitleInput = true;
                return;
            }

            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                SetConversationStatus("Your sign-in session expired. Please sign in again.", isError: true);
                ShouldFocusConversationTitleInput = true;
                return;
            }

            var renamedConversation = await ChatConversationService.RenameConversationAsync(
                ActiveConversationId!.Value,
                user.UserId,
                ConversationTitleDraft);

            if (renamedConversation is null)
            {
                SetConversationStatus("That conversation could not be renamed.", isError: true);
                ShouldFocusConversationTitleInput = true;
                return;
            }

            ApplyConversationSummary(renamedConversation);
            IsEditingConversationTitle = false;
            ShouldFocusConversationTitleInput = false;
            RequestQuestionInputFocus();
            await LoadConversationSummariesAsync();
            ClearConversationStatus();
        }

        private async Task DeleteConversationAsync(Guid conversationId)
        {
            if (IsAIResponsing)
            {
                return;
            }

            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                SetConversationStatus("Your sign-in session expired. Please sign in again.", isError: true);
                return;
            }

            var deleted = await ChatConversationService.DeleteConversationAsync(conversationId, user.UserId);
            if (!deleted)
            {
                SetConversationStatus("That conversation could not be deleted.", isError: true);
                return;
            }

            if (ActiveConversationId == conversationId)
            {
                ResetConversationDraft(clearStatus: false);
            }

            await LoadConversationSummariesAsync();
            SetConversationStatus("Conversation deleted.", isError: false);
        }

        private void ApplyConversationSummary(ChatConversationSummary conversation)
        {
            ActiveConversationId = conversation.ConversationId;
            ActiveConversationTitle = conversation.Title;
            SelectedChatMode = ChatConversationModes.Normalize(conversation.ChatMode);

            if (!IsEditingConversationTitle)
            {
                ConversationTitleDraft = conversation.Title;
            }
        }

        private void ApplyConversationDetail(ChatConversationDetail conversation)
        {
            ActiveConversationId = conversation.ConversationId;
            ActiveConversationTitle = conversation.Title;
            ConversationTitleDraft = conversation.Title;
            IsEditingConversationTitle = false;
            SelectedChatMode = ChatConversationModes.Normalize(conversation.ChatMode);
            Question = string.Empty;
            AIResponse = string.Empty;
            ElapsedTimeMessage = string.Empty;
            _chatHistory = conversation.Messages.ToChatHistory();
            _messageEvidence.Clear();
        }

        private void ResetConversationDraft(bool clearStatus = true)
        {
            ActiveConversationId = null;
            ActiveConversationTitle = ChatConversationTitleHelper.BuildFallbackTitle(string.Empty);
            ConversationTitleDraft = string.Empty;
            IsEditingConversationTitle = false;
            ShouldFocusConversationTitleInput = false;
            ShouldFocusQuestionInput = false;
            SelectedChatMode = ChatConversationModes.Regular;
            Question = string.Empty;
            AIResponse = string.Empty;
            ElapsedTimeMessage = string.Empty;
            _chatHistory = new ChatHistory();
            _messageEvidence.Clear();

            if (clearStatus)
            {
                ClearConversationStatus();
            }
        }

        private void SetConversationStatus(string message, bool isError)
        {
            ConversationStatusMessage = message;
            ConversationStatusIsError = isError;
        }

        private async Task OnChatModeChangedAsync(string newMode)
        {
            SelectedChatMode = ChatConversationModes.Normalize(newMode);

            if (!ActiveConversationId.HasValue)
            {
                return;
            }

            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                return;
            }

            await ChatConversationService.UpdateChatModeAsync(
                ActiveConversationId.Value,
                user.UserId,
                SelectedChatMode);
        }

        private string FormatConfidenceBadge(double confidence)
        {
            return confidence switch
            {
                >= 0.8 => "high",
                >= 0.5 => "medium",
                _ => "low"
            };
        }

        private void ClearConversationStatus()
        {
            ConversationStatusMessage = string.Empty;
            ConversationStatusIsError = false;
        }

        private async Task<bool> PersistUserMessageAsync(string message)
        {
            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                SetConversationStatus("Your sign-in session expired. Please sign in again.", isError: true);
                return false;
            }

            ChatConversationSummary? conversationSummary;
            if (!ActiveConversationId.HasValue)
            {
                conversationSummary = await ChatConversationService.StartConversationAsync(
                    user.UserId,
                    TenantContext.CurrentTenantId,
                    message);
            }
            else
            {
                conversationSummary = await ChatConversationService.AddMessageAsync(
                    ActiveConversationId.Value,
                    user.UserId,
                    ChatConversationRoles.User,
                    message);
            }

            if (conversationSummary is null)
            {
                SetConversationStatus("We couldn't save your message. Try starting a new conversation.", isError: true);
                return false;
            }

            ApplyConversationSummary(conversationSummary);
            await LoadConversationSummariesAsync();
            return true;
        }

        private async Task PersistAssistantMessageAsync(string message)
        {
            if (!ActiveConversationId.HasValue)
            {
                return;
            }

            var user = await ResolveCurrentUserAsync();
            if (user is null)
            {
                SetConversationStatus("The response was shown, but your session expired before it could be saved.", isError: true);
                return;
            }

            var conversationSummary = await ChatConversationService.AddMessageAsync(
                ActiveConversationId.Value,
                user.UserId,
                ChatConversationRoles.Assistant,
                message);

            if (conversationSummary is null)
            {
                SetConversationStatus("The response was shown, but it could not be saved to your history.", isError: true);
                return;
            }

            ApplyConversationSummary(conversationSummary);
            await LoadConversationSummariesAsync();
        }

        private async Task InitializeSpeechService()
        {
            try
            {
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
                        HomeConfigurations.ForceReconfigure();
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
                    HomeConfigurations.ForceReconfigure();
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
            if (IsAIResponsing)
            {
                return;
            }

            var currentQuestion = await ReadQuestionInputAsync();
            if (string.IsNullOrWhiteSpace(currentQuestion))
            {
                return;
            }

            if (!AiInfoState.EndPointAvailable)
            {
                SetConversationStatus("The AI service is currently unavailable. You can still browse saved conversations.", isError: true);
                return;
            }

            ClearConversationStatus();

            if (IsListening)
            {
                await StopListening();
            }

            Status = currentQuestion;
            Question = string.Empty;
            SpeechTranscript = string.Empty;
            InterimTranscript = string.Empty;

            var persisted = await PersistUserMessageAsync(Status);
            if (!persisted)
            {
                Question = Status;
                Status = string.Empty;
                StateHasChanged();
                return;
            }

            IsAIResponsing = true;
            AIResponse = string.Empty;
            _chatHistory.AddUserMessage(Status);
            StateHasChanged();

            await ScrollChatToBottomAsync();
            await CallBackgroundAI();
        }

        private async Task<string> ReadQuestionInputAsync()
        {
            var fallback = Question.Trim();

            try
            {
                var currentValue = await JSRuntime.InvokeAsync<string>("getElementValue", questionInput);
                return string.IsNullOrWhiteSpace(currentValue)
                    ? fallback
                    : currentValue.Trim();
            }
            catch (JSException)
            {
                return fallback;
            }
        }

        private void OnSpeechRecognitionResult(string finalTranscript, string interimTranscript)
        {
            InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(finalTranscript))
                {
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
                SpeechStatusText = "Speech recognition stopped";
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
                SpeechStatusText = "Text-to-speech stopped";
                StateHasChanged();
            });
        }

        private void OnTextToSpeechError(string error)
        {
            InvokeAsync(() =>
            {
                IsSpeaking = false;
                CurrentlySpeakingMessage = null;
                SpeechStatusText = "Text-to-speech stopped";
                StateHasChanged();
            });
        }

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
                if (IsSpeaking)
                {
                    await StopSpeaking();
                    return;
                }

                if (IsListening)
                {
                    await StopListening();
                }

                CurrentlySpeakingMessage = message;

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

        private bool IsMessageBeingSpoken(string message)
        {
            return IsSpeaking && CurrentlySpeakingMessage == message;
        }

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
            {
                return string.Empty;
            }

            try
            {
                var html = Markdown.ToHtml(markdown);
                var plainText = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
                plainText = System.Net.WebUtility.HtmlDecode(plainText);
                return plainText.Trim();
            }
            catch
            {
                return markdown;
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                _dotNetRef?.Dispose();
                _dotNetRef = DotNetObjectReference.Create(this);

                var functionReady = false;
                const int retries = 20;
                for (var i = 0; i < retries; i++)
                {
                    try
                    {
                        functionReady = await JSRuntime.InvokeAsync<bool>("eval", "typeof window.initializeKeyboardShortcuts === 'function'");
                        if (functionReady)
                        {
                            Console.WriteLine($"initializeKeyboardShortcuts function found after {i + 1} attempt(s).");
                            break;
                        }
                    }
                    catch (JSException ex)
                    {
                        Console.WriteLine($"JS eval check for initializeKeyboardShortcuts failed (attempt {i + 1}/{retries}): {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Generic error during JS eval check (attempt {i + 1}/{retries}): {ex.Message}");
                    }

                    await Task.Delay(100);
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
                        Console.WriteLine($"Error calling initializeKeyboardShortcuts after it was found: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }
                else
                {
                    Console.WriteLine($"Error initializing keyboard shortcuts: 'initializeKeyboardShortcuts' function not found after {retries * 100}ms timeout.");
                }

                IsInteractiveReady = true;
                StateHasChanged();
            }

            if (ShouldFocusConversationTitleInput && IsEditingConversationTitle)
            {
                ShouldFocusConversationTitleInput = false;
                await FocusConversationTitleInput();
            }
            else if (ShouldFocusQuestionInput && !IsEditingConversationTitle)
            {
                ShouldFocusQuestionInput = false;
                await FocusQuestionInput();
            }

            if (IsAIResponsing)
            {
                await ScrollChatToBottomAsync(delayMs: 10);
            }
        }

        private void RequestQuestionInputFocus()
        {
            if (IsEditingConversationTitle)
            {
                return;
            }

            ShouldFocusConversationTitleInput = false;
            ShouldFocusQuestionInput = true;
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

        private async Task FocusConversationTitleInput()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("focusElement", conversationTitleInput);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error focusing conversation title input: {ex.Message}");
            }
        }

        private async Task ScrollChatToBottomAsync(int delayMs = 50)
        {
            try
            {
                await Task.Delay(delayMs);
                await JSRuntime.InvokeVoidAsync("scrollChatToBottom");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scrolling chat: {ex.Message}");
            }
        }

        private async Task CallBackgroundAI()
        {
            var stopwatch = Stopwatch.StartNew();

            var manualStopTokenSource = new CancellationTokenSource();
            using var responseTimeoutTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                manualStopTokenSource.Token,
                responseTimeoutTokenSource.Token);

            _cancellationTokenSource = manualStopTokenSource;

            try
            {
                var chatResponse = await BrainChatClient.ChatAsync(
                    query: Status,
                    mode: SelectedChatMode,
                    tenantId: TenantContext.CurrentTenantId,
                    conversationId: ActiveConversationId?.ToString(),
                    cancellationToken: linkedTokenSource.Token);

                AIResponse = chatResponse.Answer;

                // Track evidence for the next assistant message index
                var nextAssistantIndex = _chatHistory.Count(m =>
                    m.Role == AuthorRole.Assistant);
                if (chatResponse.Evidence.Count > 0 || chatResponse.Confidence > 0)
                {
                    _messageEvidence[nextAssistantIndex] = chatResponse;
                }
            }
            catch (OperationCanceledException) when (manualStopTokenSource.IsCancellationRequested)
            {
                if (!AIResponse.Contains(HaltedResponseTag, StringComparison.Ordinal))
                {
                    AIResponse += "\n" + HaltedResponseTag;
                }
            }
            catch (OperationCanceledException) when (responseTimeoutTokenSource.IsCancellationRequested)
            {
                SetConversationStatus(
                    "The AI service took too long to respond. Your prompt is still saved, so you can retry in a moment.",
                    isError: true);
            }
            catch (BrainChatException ex)
            {
                Console.WriteLine($"BRAIN chat error: {ex.Message}");
                SetConversationStatus(
                    "Knowledge retrieval encountered a problem. Please try again.",
                    isError: true);
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

            if (!string.IsNullOrEmpty(AIResponse))
            {
                _chatHistory.AddAssistantMessage(AIResponse);
                await PersistAssistantMessageAsync(AIResponse);
            }

            stopwatch.Stop();
            IsAIResponsing = false;
            ElapsedTimeMessage = $"Response time: {stopwatch.Elapsed.TotalMilliseconds} milliseconds";
            StateHasChanged();

            await ScrollChatToBottomAsync();
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
                using var client = HttpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);

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

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _kernel = null;

                if (_dotNetRef != null)
                {
                    await JSRuntime.InvokeVoidAsync("dispose");
                    _dotNetRef.Dispose();
                }
            }
            catch (JSDisconnectedException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during disposal: {ex.Message}");
            }
        }
    }
}
