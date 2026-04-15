extern alias web;

using IBrainChatClient = web::AspireApp.Web.Services.IBrainChatClient;
using BrainChatResponse = web::AspireApp.Web.Services.BrainChatResponse;
using BrainChatEvidence = web::AspireApp.Web.Services.BrainChatEvidence;
using BrainChatReasoningStep = web::AspireApp.Web.Services.BrainChatReasoningStep;
using BrainChatException = web::AspireApp.Web.Services.BrainChatException;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Chat = web::AspireApp.Web.Components.Pages.Chat;
using HomeConfigurations = web::AspireApp.Web.Components.Pages.HomeConfigurations;
using AiInfoStateService = web::AspireApp.Web.Components.Shared.AiInfoStateService;
using SpeechService = web::AspireApp.Web.Components.Shared.SpeechService;
using SpeechSupport = web::AspireApp.Web.Components.Shared.SpeechSupport;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;
using AuthenticationContext = web::AspireApp.Web.Services.AuthenticationContext;
using AuthenticatedUser = web::AspireApp.Web.Services.AuthenticatedUser;
using AuthenticatedUserClaims = web::AspireApp.Web.Services.AuthenticatedUserClaims;
using ChatConversationDetail = web::AspireApp.Web.Services.ChatConversationDetail;
using ChatConversationMessageRecord = web::AspireApp.Web.Services.ChatConversationMessageRecord;
using ChatConversationRoles = web::AspireApp.Web.Services.ChatConversationRoles;
using ChatConversationSummary = web::AspireApp.Web.Services.ChatConversationSummary;
using ChatConversationModes = web::AspireApp.Web.Services.ChatConversationModes;
using IChatConversationService = web::AspireApp.Web.Services.IChatConversationService;
using TenantContextService = web::AspireApp.Web.Services.TenantContextService;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;

namespace AspireApp.WebTest.Tests;

/// <summary>
/// Validates Critique-mode UI/product behavior:
/// - Critique toggle is enabled
/// - Selected mode propagates to BrainChatClient.ChatAsync
/// - Reasoning steps render correctly
/// - Regular mode continues working
/// </summary>
public sealed class ChatCritiqueModeTests
{
    [Fact]
    public async Task CritiqueToggle_IsEnabled_AfterProductLayerImplementation()
    {
        // Arrange: Set up minimal test context
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        // Act: Render the chat component
        var cut = testContext.Render<Chat>();

        // Assert: Critique radio button should be enabled (not disabled)
        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            Assert.NotNull(critiqueRadio);
            Assert.False(critiqueRadio.HasAttribute("disabled"),
                "Critique mode radio should be enabled after product layer implementation.");
        });
    }

    [Fact]
    public async Task SelectingCritiqueMode_ChangesSelectedModeProperty()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Wait for initial render
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='chat-mode-regular']")));

        // Act: Click the Critique radio button
        await cut.InvokeAsync(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);
        });

        // Assert: Critique radio should be checked
        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            Assert.True(critiqueRadio.GetAttribute("checked") is not null,
                "Critique radio should be checked after selection.");
        });
    }

    [Fact]
    public async Task SendingMessage_InCritiqueMode_PassesCritiqueModeToClient()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Switch to Critique mode
        await cut.InvokeAsync(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);
        });

        // Act: Send a message
        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Test critique query");
            
            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        // Assert: Client should have received mode="critique"
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(chatClient.LastRequest);
            Assert.Equal(ChatConversationModes.Critique, chatClient.LastRequest.Value.Mode);
            Assert.Equal("Test critique query", chatClient.LastRequest.Value.Query);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendingMessage_InRegularMode_PassesRegularModeToClient()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Ensure Regular mode is selected (default)
        cut.WaitForAssertion(() =>
        {
            var regularRadio = cut.Find("[data-testid='chat-mode-regular']");
            Assert.NotNull(regularRadio.GetAttribute("checked"));
        });

        // Act: Send a message
        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Test regular query");
            
            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        // Assert: Client should have received mode="regular"
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(chatClient.LastRequest);
            Assert.Equal(ChatConversationModes.Regular, chatClient.LastRequest.Value.Mode);
            Assert.Equal("Test regular query", chatClient.LastRequest.Value.Query);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CritiqueResponse_WithReasoningSteps_RendersReasoningPanel()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient
        {
            ResponseToReturn = new BrainChatResponse(
                Answer: "Based on thorough analysis, the answer is 42.",
                Confidence: 0.95,
                Evidence: new[]
                {
                    new BrainChatEvidence("Evidence snippet", 0.9, "doc-1")
                },
                ReasoningSteps: new[]
                {
                    new BrainChatReasoningStep(
                        Step: "analyze_claim",
                        Reasoning: "Cross-referenced multiple sources",
                        Tool: "knowledge_retriever",
                        Result: "Found 3 supporting documents"),
                    new BrainChatReasoningStep(
                        Step: "validate_facts",
                        Reasoning: "Verified against Neo4j graph",
                        Tool: "graph_validator",
                        Result: "No contradictions detected")
                },
                ProactiveSuggestions: Array.Empty<string>())
        };
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Act: Send a message in Critique mode
        await cut.InvokeAsync(async () =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);

            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("What is the answer?");
            
            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        // Assert: Reasoning panel should render with steps
        cut.WaitForAssertion(() =>
        {
            var reasoningPanel = cut.FindAll("[data-testid='chat-reasoning-panel']");
            Assert.Single(reasoningPanel);

            var reasoningSteps = cut.FindAll("[data-testid='chat-reasoning-step']");
            Assert.Equal(2, reasoningSteps.Count);

            // Verify first step content
            var firstStep = reasoningSteps[0];
            Assert.Contains("analyze_claim", firstStep.TextContent);
            Assert.Contains("Cross-referenced multiple sources", firstStep.TextContent);
            Assert.Contains("knowledge_retriever", firstStep.TextContent);

            // Verify second step content
            var secondStep = reasoningSteps[1];
            Assert.Contains("validate_facts", secondStep.TextContent);
            Assert.Contains("Verified against Neo4j graph", secondStep.TextContent);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegularResponse_WithoutReasoningSteps_DoesNotRenderReasoningPanel()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient
        {
            ResponseToReturn = new BrainChatResponse(
                Answer: "Quick answer from regular mode.",
                Confidence: 0.85,
                Evidence: new[]
                {
                    new BrainChatEvidence("Evidence snippet", 0.8, "doc-1")
                },
                ReasoningSteps: Array.Empty<BrainChatReasoningStep>(),
                ProactiveSuggestions: Array.Empty<string>())
        };
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Act: Send a message in Regular mode
        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Quick question");
            
            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        // Assert: No reasoning panel should be present
        cut.WaitForAssertion(() =>
        {
            var reasoningPanels = cut.FindAll("[data-testid='chat-reasoning-panel']");
            Assert.Empty(reasoningPanels);

            // But evidence panel should still be there
            var evidencePanels = cut.FindAll("[data-testid='chat-evidence-panel']");
            Assert.Single(evidencePanels);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CritiqueMode_RendersProgressDetails_WhenReasoningStepsIncludeToolResults()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient
        {
            ResponseToReturn = new BrainChatResponse(
                Answer: "Comprehensive answer with detailed progress.",
                Confidence: 0.92,
                Evidence: Array.Empty<BrainChatEvidence>(),
                ReasoningSteps: new[]
                {
                    new BrainChatReasoningStep(
                        Step: "query_knowledge",
                        Reasoning: "Searching document knowledge base",
                        Tool: "vector_search",
                        Result: "Retrieved 5 relevant chunks"),
                    new BrainChatReasoningStep(
                        Step: "synthesize_answer",
                        Reasoning: "Combining evidence into coherent response",
                        Tool: null,
                        Result: "Draft answer generated")
                },
                ProactiveSuggestions: Array.Empty<string>())
        };
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Act: Send message in Critique mode
        await cut.InvokeAsync(async () =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);

            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Tell me more");
            
            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        // Assert: Tool results should be visible in reasoning steps
        cut.WaitForAssertion(() =>
        {
            var steps = cut.FindAll("[data-testid='chat-reasoning-step']");
            Assert.Equal(2, steps.Count);

            var firstStep = steps[0];
            Assert.Contains("Retrieved 5 relevant chunks", firstStep.TextContent);
            Assert.Contains("vector_search", firstStep.TextContent);

            var secondStep = steps[1];
            Assert.Contains("Draft answer generated", secondStep.TextContent);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ModeHintText_ChangesBasedOnSelectedMode()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        // Assert: Initially shows Regular mode hint
        cut.WaitForAssertion(() =>
        {
            var modeHint = cut.Find(".chat-mode-hint");
            Assert.Contains("Fast, knowledge-enhanced responses", modeHint.TextContent);
        });

        // Act: Switch to Critique mode
        await cut.InvokeAsync(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);
        });

        // Assert: Hint text should change
        cut.WaitForAssertion(() =>
        {
            var modeHint = cut.Find(".chat-mode-hint");
            Assert.Contains("Thorough, agent-verified answers", modeHint.TextContent);
        });
    }

    [Fact]
    public async Task ExistingConversation_LoadsWithStoredChatMode()
    {
        // Arrange
        var conversationService = new StubChatConversationServiceWithCritiqueConversation();
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(conversationService, chatClient);

        var cut = testContext.Render<Chat>();

        // Act: Select the critique-mode conversation
        await cut.InvokeAsync(() =>
        {
            var conversationButton = cut.Find("[data-testid='chat-conversation-select']");
            conversationButton.Click();
        });

        // Assert: Critique mode should be selected
        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            Assert.NotNull(critiqueRadio.GetAttribute("checked"));
        });
    }

    [Fact]
    public async Task SelectingSavedConversation_UpdatesModeAcrossCritiqueAndRegularThreads()
    {
        var conversationService = new StubChatConversationServiceWithMixedModes();
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(conversationService, chatClient);

        var cut = testContext.Render<Chat>();

        static AngleSharp.Dom.IElement FindConversationButton(Bunit.IRenderedComponent<Chat> component, string title)
        {
            foreach (var button in component.FindAll("[data-testid='chat-conversation-select']"))
            {
                if (button.TextContent.Contains(title, StringComparison.Ordinal))
                {
                    return button;
                }
            }

            Assert.Fail($"Could not find a saved conversation button for '{title}'.");
            return null!;
        }

        await cut.InvokeAsync(() => FindConversationButton(cut, "Critique mode conversation").Click());

        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            var regularRadio = cut.Find("[data-testid='chat-mode-regular']");
            Assert.NotNull(critiqueRadio.GetAttribute("checked"));
            Assert.Null(regularRadio.GetAttribute("checked"));
        });

        await cut.InvokeAsync(() => FindConversationButton(cut, "Regular mode conversation").Click());

        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            var regularRadio = cut.Find("[data-testid='chat-mode-regular']");
            Assert.Null(critiqueRadio.GetAttribute("checked"));
            Assert.NotNull(regularRadio.GetAttribute("checked"));
        });
    }

    [Fact]
    public async Task CritiqueModeFailure_ShowsGatewayProblemDetail_InConversationStatus()
    {
        var chatClient = new RecordingBrainChatClient
        {
            ExceptionToThrow = new BrainChatException(
                "Critique mode unavailable: agent provider not configured (check OLLAMA_ENDPOINT).",
                StatusCodes.Status503ServiceUnavailable,
                "BRAIN chat failed")
        };
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        await cut.InvokeAsync(async () =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);

            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Please critique this");

            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Critique mode unavailable: agent provider not configured",
                cut.Markup,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "Knowledge retrieval encountered a problem. Please try again.",
                cut.Markup,
                StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromSeconds(5));
    }

    // Helper: Create test context with required services
    private static Bunit.BunitContext CreateTestContext(
        IChatConversationService? conversationService = null,
        IBrainChatClient? brainChatClient = null)
    {
        ConfigureEnvironmentVariables();

        var testContext = new Bunit.BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.JSInterop.Setup<bool>("eval").SetResult(true);
        testContext.JSInterop.SetupVoid("initializeKeyboardShortcuts");
        testContext.JSInterop.SetupVoid("focusElement");
        testContext.JSInterop.SetupVoid("scrollChatToBottom");
        testContext.JSInterop.Setup<SpeechSupport>("initializeSpeechManager").SetResult(new SpeechSupport
        {
            SpeechRecognition = false,
            TextToSpeech = false,
            Both = false
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var authenticationContext = new AuthenticationContext();
        authenticationContext.SetCurrentUser(CreateUser());

        var dbContext = CreateDbContext();
        var tenantManagementService = new TenantManagementService(
            dbContext,
            NullLogger<TenantManagementService>.Instance);
        var tenantContext = new TenantContextService(tenantManagementService, authenticationContext);
        var httpClientFactory = new StubHttpClientFactory();
        var chatConversationServiceToUse = conversationService ?? new StubChatConversationService();

        testContext.Services.AddSingleton<IConfiguration>(configuration);
        testContext.Services.AddSingleton<IJSRuntime>(testContext.JSInterop.JSRuntime);
        testContext.Services.AddSingleton(httpClientFactory);
        testContext.Services.AddSingleton<IHttpClientFactory>(httpClientFactory);
        testContext.Services.AddSingleton(new SpeechService(testContext.JSInterop.JSRuntime));
        testContext.Services.AddSingleton<AuthenticationStateProvider>(new StubAuthenticationStateProvider(CreateUser()));
        testContext.Services.AddSingleton(authenticationContext);
        testContext.Services.AddSingleton<IChatConversationService>(chatConversationServiceToUse);
        testContext.Services.AddSingleton(tenantContext);
        testContext.Services.AddSingleton(new AiInfoStateService(configuration, httpClientFactory));
        
        if (brainChatClient is not null)
        {
            testContext.Services.AddSingleton<IBrainChatClient>(brainChatClient);
        }

        return testContext;
    }

    private static void ConfigureEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__ollama", "http://localhost:11434");
        Environment.SetEnvironmentVariable("ConnectionStrings__chat", "phi4-mini:latest");
        Environment.SetEnvironmentVariable("AI-Endpoint", "http://localhost:11434");
        Environment.SetEnvironmentVariable("AI-Model", "phi4-mini:latest");
        HomeConfigurations.ForceReconfigure();
    }

    private static AuthenticatedUser CreateUser()
    {
        return new AuthenticatedUser(
            "demo-taylor-jones",
            "Taylor Jones",
            "taylor@example.com",
            "demo",
            "Demo provider",
            "tenant-alpha");
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    // Test Doubles

    private sealed class RecordingBrainChatClient : IBrainChatClient
    {
        public (string Query, string Mode, string? TenantId, string? ConversationId, int TopK)? LastRequest { get; private set; }
        public BrainChatResponse? ResponseToReturn { get; set; }
        public BrainChatException? ExceptionToThrow { get; set; }

        public Task<BrainChatResponse> ChatAsync(
            string query,
            string mode,
            string? tenantId,
            string? conversationId,
            int topK = 5,
            CancellationToken cancellationToken = default)
        {
            LastRequest = (query, mode, tenantId, conversationId, topK);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResponseToReturn ?? new BrainChatResponse(
                Answer: "Default stub response",
                Confidence: 0.9,
                Evidence: Array.Empty<BrainChatEvidence>(),
                ReasoningSteps: Array.Empty<BrainChatReasoningStep>(),
                ProactiveSuggestions: Array.Empty<string>()));
        }
    }

    private sealed class StubAuthenticationStateProvider(AuthenticatedUser user) : AuthenticationStateProvider
    {
        private readonly AuthenticatedUser _user = user;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(authenticationType: "Test");
            AuthenticatedUserClaims.AddClaims(identity, _user);
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler())
            {
                BaseAddress = new Uri("http://localhost")
            };
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Ollama is running")
            });
        }
    }

    private sealed class StubChatConversationService : IChatConversationService
    {
        private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly DateTime Timestamp = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);
        private ChatConversationSummary? _activeSummary;
        private int _messageCount;

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_activeSummary is not null)
            {
                return Task.FromResult<IReadOnlyList<ChatConversationSummary>>([_activeSummary]);
            }

            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    ConversationId,
                    "Existing conversation title",
                    "User preview",
                    "tenant-alpha",
                    ChatConversationModes.Regular,
                    1,
                    false,
                    Timestamp,
                    Timestamp)
            ]);
        }

        public Task<ChatConversationDetail?> GetConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_activeSummary is not null && conversationId == _activeSummary.ConversationId)
            {
                return Task.FromResult<ChatConversationDetail?>(
                    new ChatConversationDetail(
                        _activeSummary.ConversationId,
                        _activeSummary.Title,
                        _activeSummary.TenantId,
                        _activeSummary.ChatMode,
                        _activeSummary.HasUserEditedTitle,
                        _activeSummary.CreatedAt,
                        _activeSummary.UpdatedAt,
                        [
                            new ChatConversationMessageRecord(
                                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                                ChatConversationRoles.User,
                                _activeSummary.Preview,
                                1,
                                _activeSummary.CreatedAt)
                        ]));
            }

            return Task.FromResult<ChatConversationDetail?>(
                new ChatConversationDetail(
                    conversationId,
                    "Existing conversation title",
                    "tenant-alpha",
                    ChatConversationModes.Regular,
                    false,
                    Timestamp,
                    Timestamp,
                    [
                        new ChatConversationMessageRecord(
                            Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            ChatConversationRoles.User,
                            "Original prompt",
                            1,
                            Timestamp)
                    ]));
        }

        public Task<ChatConversationSummary> StartConversationAsync(
            string ownerUserId,
            string? tenantId,
            string userMessage,
            string chatMode = "regular",
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _messageCount = 1;
            _activeSummary = new ChatConversationSummary(
                ConversationId,
                "New conversation",
                userMessage,
                tenantId,
                ChatConversationModes.Normalize(chatMode),
                _messageCount,
                false,
                Timestamp,
                Timestamp);

            return Task.FromResult(_activeSummary);
        }

        public Task<ChatConversationSummary?> AddMessageAsync(
            Guid conversationId,
            string ownerUserId,
            string role,
            string content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_activeSummary is null || conversationId != _activeSummary.ConversationId)
            {
                return Task.FromResult<ChatConversationSummary?>(null);
            }

            _messageCount++;
            _activeSummary = _activeSummary with
            {
                Preview = content,
                MessageCount = _messageCount,
                UpdatedAt = Timestamp.AddSeconds(_messageCount)
            };

            return Task.FromResult<ChatConversationSummary?>(_activeSummary);
        }

        public Task<ChatConversationSummary?> RenameConversationAsync(
            Guid conversationId,
            string ownerUserId,
            string title,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ChatConversationSummary?> UpdateChatModeAsync(
            Guid conversationId,
            string ownerUserId,
            string chatMode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubChatConversationServiceWithCritiqueConversation : IChatConversationService
    {
        private static readonly Guid ConversationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly DateTime Timestamp = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);
        private ChatConversationSummary? _activeSummary;
        private int _messageCount;

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            if (_activeSummary is not null)
            {
                return Task.FromResult<IReadOnlyList<ChatConversationSummary>>([_activeSummary]);
            }

            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    ConversationId,
                    "Critique mode conversation",
                    "User preview",
                    "tenant-alpha",
                    ChatConversationModes.Critique,
                    1,
                    false,
                    Timestamp,
                    Timestamp)
            ]);
        }

        public Task<ChatConversationDetail?> GetConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ChatConversationDetail?>(
                new ChatConversationDetail(
                    conversationId,
                    "Critique mode conversation",
                    "tenant-alpha",
                    ChatConversationModes.Critique,
                    false,
                    Timestamp,
                    Timestamp,
                    [
                        new ChatConversationMessageRecord(
                            Guid.Parse("44444444-4444-4444-4444-444444444444"),
                            ChatConversationRoles.User,
                            "Original critique prompt",
                            1,
                            Timestamp)
                    ]));
        }

        public Task<ChatConversationSummary> StartConversationAsync(
            string ownerUserId,
            string? tenantId,
            string userMessage,
            string chatMode = "regular",
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _messageCount = 1;
            _activeSummary = new ChatConversationSummary(
                ConversationId,
                "Critique mode conversation",
                userMessage,
                tenantId,
                ChatConversationModes.Normalize(chatMode),
                _messageCount,
                false,
                Timestamp,
                Timestamp);

            return Task.FromResult(_activeSummary);
        }

        public Task<ChatConversationSummary?> AddMessageAsync(
            Guid conversationId,
            string ownerUserId,
            string role,
            string content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_activeSummary is null || conversationId != _activeSummary.ConversationId)
            {
                return Task.FromResult<ChatConversationSummary?>(null);
            }

            _messageCount++;
            _activeSummary = _activeSummary with
            {
                Preview = content,
                MessageCount = _messageCount,
                UpdatedAt = Timestamp.AddSeconds(_messageCount)
            };

            return Task.FromResult<ChatConversationSummary?>(_activeSummary);
        }

        public Task<ChatConversationSummary?> RenameConversationAsync(
            Guid conversationId,
            string ownerUserId,
            string title,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ChatConversationSummary?> UpdateChatModeAsync(
            Guid conversationId,
            string ownerUserId,
            string chatMode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubChatConversationServiceWithMixedModes : IChatConversationService
    {
        private static readonly Guid CritiqueConversationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid RegularConversationId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        private static readonly DateTime Timestamp = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    CritiqueConversationId,
                    "Critique mode conversation",
                    "Critique preview",
                    "tenant-alpha",
                    ChatConversationModes.Critique,
                    2,
                    false,
                    Timestamp.AddMinutes(1),
                    Timestamp.AddMinutes(1)),
                new ChatConversationSummary(
                    RegularConversationId,
                    "Regular mode conversation",
                    "Regular preview",
                    "tenant-alpha",
                    ChatConversationModes.Regular,
                    2,
                    false,
                    Timestamp,
                    Timestamp)
            ]);
        }

        public Task<ChatConversationDetail?> GetConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<ChatConversationDetail?>(
                conversationId switch
                {
                    _ when conversationId == CritiqueConversationId => new ChatConversationDetail(
                        CritiqueConversationId,
                        "Critique mode conversation",
                        "tenant-alpha",
                        ChatConversationModes.Critique,
                        false,
                        Timestamp.AddMinutes(1),
                        Timestamp.AddMinutes(1),
                        [
                            new ChatConversationMessageRecord(
                                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                                ChatConversationRoles.User,
                                "Original critique prompt",
                                1,
                                Timestamp.AddMinutes(1))
                        ]),
                    _ when conversationId == RegularConversationId => new ChatConversationDetail(
                        RegularConversationId,
                        "Regular mode conversation",
                        "tenant-alpha",
                        ChatConversationModes.Regular,
                        false,
                        Timestamp,
                        Timestamp,
                        [
                            new ChatConversationMessageRecord(
                                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                                ChatConversationRoles.User,
                                "Original regular prompt",
                                1,
                                Timestamp)
                        ]),
                    _ => null
                });
        }

        public Task<ChatConversationSummary> StartConversationAsync(
            string ownerUserId,
            string? tenantId,
            string userMessage,
            string chatMode = "regular",
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ChatConversationSummary?> AddMessageAsync(
            Guid conversationId,
            string ownerUserId,
            string role,
            string content,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ChatConversationSummary?> RenameConversationAsync(
            Guid conversationId,
            string ownerUserId,
            string title,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ChatConversationSummary?> UpdateChatModeAsync(
            Guid conversationId,
            string ownerUserId,
            string chatMode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}


