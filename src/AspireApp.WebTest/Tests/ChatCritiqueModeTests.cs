extern alias web;

using IBrainChatClient = web::AspireApp.Web.Services.IBrainChatClient;
using BrainChatResponse = web::AspireApp.Web.Services.BrainChatResponse;
using BrainChatEvidence = web::AspireApp.Web.Services.BrainChatEvidence;
using BrainChatReasoningStep = web::AspireApp.Web.Services.BrainChatReasoningStep;
using BrainChatException = web::AspireApp.Web.Services.BrainChatException;
using ConversationMessage = web::AspireApp.Web.Services.ConversationMessage;
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
/// Validates chat-mode UI/product behavior:
/// - Simple is the default for new chats
/// - Enhanced is the user-facing rename for Regular
/// - Saved conversations preserve selected mode, including legacy Regular values
/// - Selected mode propagates to BrainChatClient.ChatAsync
/// - Critique reasoning steps render correctly
/// </summary>
public sealed class ChatCritiqueModeTests
{
    [Fact]
    public void ModeSelector_DefaultsToSimple_ForNewChat()
    {
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        cut.WaitForAssertion(() =>
        {
            var simpleRadio = cut.Find("[data-testid='chat-mode-simple']");
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");

            Assert.NotNull(simpleRadio.GetAttribute("checked"));
            Assert.False(critiqueRadio.HasAttribute("disabled"),
                "Critique mode radio should be enabled after product layer implementation.");
        });
    }

    [Fact]
    public void ModeSelector_UsesEnhancedLabel_ForKnowledgeMode()
    {
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        cut.WaitForAssertion(() =>
        {
            var modeLabels = cut.FindAll(".chat-mode-option label")
                .Select(label => label.TextContent.Trim())
                .ToList();

            Assert.Contains("Simple", modeLabels);
            Assert.Contains("Enhanced", modeLabels);
            Assert.Contains("Critique", modeLabels);
            Assert.DoesNotContain("Regular", modeLabels);
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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='chat-mode-simple']")));

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
    public async Task FollowUpQuestion_InSavedConversation_PassesPriorConversationHistoryToClient()
    {
        var conversationService = new StubChatConversationServiceWithFollowUpHistory();
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(conversationService, chatClient);

        var cut = testContext.Render<Chat>();

        await cut.InvokeAsync(() =>
        {
            var conversationButton = cut.Find("[data-testid='chat-conversation-select']");
            conversationButton.Click();
        });

        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("What about the newly uploaded document?");

            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(chatClient.LastRequest);
            Assert.Equal("What about the newly uploaded document?", chatClient.LastRequest.Value.Query);

            var history = Assert.IsAssignableFrom<IReadOnlyList<ConversationMessage>>(chatClient.LastRequest.Value.ConversationHistory!);
            Assert.Equal(2, history.Count);
            Assert.Equal(ChatConversationRoles.User, history[0].Role);
            Assert.Equal("Original prompt", history[0].Content);
            Assert.Equal(ChatConversationRoles.Assistant, history[1].Role);
            Assert.DoesNotContain(history, message => message.Content.Contains("newly uploaded document", StringComparison.OrdinalIgnoreCase));
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendingMessage_InSimpleMode_PassesSimpleModeToClient()
    {
        var conversationService = new StubChatConversationService();
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(conversationService, chatClient);

        var cut = testContext.Render<Chat>();

        // Ensure Simple mode is selected (default)
        cut.WaitForAssertion(() =>
        {
            var simpleRadio = cut.Find("[data-testid='chat-mode-simple']");
            Assert.NotNull(simpleRadio.GetAttribute("checked"));
        });

        // Act: Send a message
        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Test simple query");
            
            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        cut.WaitForAssertion(() =>
        {
            Assert.False(chatClient.LastRequest.HasValue);
            Assert.Equal(ChatConversationModes.Simple, conversationService.LastStartedChatMode);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendingMessage_InEnhancedMode_PassesEnhancedAliasToClient()
    {
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        await cut.InvokeAsync(() =>
        {
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            enhancedRadio.Change(ChatConversationModes.Enhanced);
        });

        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("Test enhanced query");

            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(chatClient.LastRequest);
            Assert.Equal(ChatConversationModes.Enhanced, chatClient.LastRequest.Value.Mode);
            Assert.Equal("Test enhanced query", chatClient.LastRequest.Value.Query);
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
    public async Task EnhancedResponse_WithoutReasoningSteps_DoesNotRenderReasoningPanel()
    {
        // Arrange
        var chatClient = new RecordingBrainChatClient
        {
            ResponseToReturn = new BrainChatResponse(
                Answer: "Quick answer from enhanced mode.",
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

        // Act: Send a message in Enhanced mode
        await cut.InvokeAsync(async () =>
        {
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            enhancedRadio.Change(ChatConversationModes.Enhanced);

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

        // Assert: Initially shows Simple mode hint
        cut.WaitForAssertion(() =>
        {
            var modeHint = cut.Find(".chat-mode-hint");
            Assert.Contains("Direct model chat with your selected LLM", modeHint.TextContent);
        });

        // Act: Switch to Enhanced mode
        await cut.InvokeAsync(() =>
        {
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            enhancedRadio.Change(ChatConversationModes.Enhanced);
        });

        // Assert: Hint text should change for Enhanced mode
        cut.WaitForAssertion(() =>
        {
            var modeHint = cut.Find(".chat-mode-hint");
            Assert.Contains("Knowledge-enhanced responses with GraphRAG context", modeHint.TextContent);
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
    public async Task ExistingConversation_LoadsLegacyRegularValue_WithEnhancedOptionSelected()
    {
        var conversationService = new StubChatConversationServiceWithLegacyRegularConversation();
        var chatClient = new RecordingBrainChatClient();
        using var testContext = CreateTestContext(conversationService, chatClient);

        var cut = testContext.Render<Chat>();

        await cut.InvokeAsync(() =>
        {
            var conversationButton = cut.Find("[data-testid='chat-conversation-select']");
            conversationButton.Click();
        });

        cut.WaitForAssertion(() =>
        {
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            var modeHint = cut.Find(".chat-mode-hint");

            Assert.NotNull(enhancedRadio.GetAttribute("checked"));
            Assert.Null(critiqueRadio.GetAttribute("checked"));
            Assert.Contains("Knowledge-enhanced responses with GraphRAG context", modeHint.TextContent);
        });
    }

    [Fact]
    public async Task ExistingConversation_RendersPersistedAssistantMetadata_WhenReloaded()
    {
        var conversationService = new StubChatConversationServiceWithPersistedAssistantMetadata();
        using var testContext = CreateTestContext(conversationService, new RecordingBrainChatClient());

        var cut = testContext.Render<Chat>();

        await cut.InvokeAsync(() =>
        {
            var conversationButton = cut.Find("[data-testid='chat-conversation-select']");
            conversationButton.Click();
        });

        cut.WaitForAssertion(() =>
        {
            var evidencePanel = cut.Find("[data-testid='chat-evidence-panel']");
            Assert.Contains("confidence", evidencePanel.TextContent, StringComparison.OrdinalIgnoreCase);

            var evidenceSources = cut.FindAll("[data-testid='chat-evidence-source']");
            Assert.Single(evidenceSources);
            Assert.Contains("document:7/page:3", evidenceSources[0].TextContent, StringComparison.OrdinalIgnoreCase);

            var reasoningSteps = cut.FindAll("[data-testid='chat-reasoning-step']");
            Assert.Single(reasoningSteps);
            Assert.Contains("retrieval", reasoningSteps[0].TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Matched the saved upload context.", reasoningSteps[0].TextContent, StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SelectingSavedConversation_UpdatesModeAcrossSimpleEnhancedAndCritiqueThreads()
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

        await cut.InvokeAsync(() => FindConversationButton(cut, "Simple mode conversation").Click());

        cut.WaitForAssertion(() =>
        {
            var simpleRadio = cut.Find("[data-testid='chat-mode-simple']");
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            Assert.NotNull(simpleRadio.GetAttribute("checked"));
            Assert.Null(enhancedRadio.GetAttribute("checked"));
            Assert.Null(critiqueRadio.GetAttribute("checked"));
        });

        await cut.InvokeAsync(() => FindConversationButton(cut, "Enhanced mode conversation").Click());

        cut.WaitForAssertion(() =>
        {
            var simpleRadio = cut.Find("[data-testid='chat-mode-simple']");
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            Assert.Null(simpleRadio.GetAttribute("checked"));
            Assert.NotNull(enhancedRadio.GetAttribute("checked"));
            Assert.Null(critiqueRadio.GetAttribute("checked"));
        });

        await cut.InvokeAsync(() => FindConversationButton(cut, "Critique mode conversation").Click());

        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            var simpleRadio = cut.Find("[data-testid='chat-mode-simple']");
            Assert.NotNull(critiqueRadio.GetAttribute("checked"));
            Assert.Null(enhancedRadio.GetAttribute("checked"));
            Assert.Null(simpleRadio.GetAttribute("checked"));
        });
    }

    [Fact]
    public async Task ChangingSavedConversationMode_PersistsAcrossReload()
    {
        var conversationService = new StubChatConversationService();
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

        await cut.InvokeAsync(() => FindConversationButton(cut, "Existing conversation title").Click());
        await cut.InvokeAsync(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            critiqueRadio.Change(ChatConversationModes.Critique);
        });
        await cut.InvokeAsync(() => cut.Find("[data-testid='chat-new-conversation']").Click());
        await cut.InvokeAsync(() => FindConversationButton(cut, "Existing conversation title").Click());

        cut.WaitForAssertion(() =>
        {
            var critiqueRadio = cut.Find("[data-testid='chat-mode-critique']");
            Assert.NotNull(critiqueRadio.GetAttribute("checked"));
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

    [Fact]
    public async Task GatewayTimeout_ShowsFriendlyConversationStatus()
    {
        var chatClient = new RecordingBrainChatClient
        {
            ExceptionToThrow = new BrainChatException(
                "The BRAIN gateway timed out before a response was ready. Please try again in a moment.",
                StatusCodes.Status504GatewayTimeout,
                "BRAIN chat timed out")
        };
        using var testContext = CreateTestContext(brainChatClient: chatClient);

        var cut = testContext.Render<Chat>();

        await cut.InvokeAsync(async () =>
        {
            var enhancedRadio = cut.Find("[data-testid='chat-mode-regular']");
            enhancedRadio.Change(ChatConversationModes.Enhanced);

            var input = cut.Find("[data-testid='chat-message-input']");
            input.Input("What changed after the upload?");

            var button = cut.Find("[data-testid='chat-send']");
            await button.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(
                "timed out before a response was ready",
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
        public (string Query, string Mode, string? TenantId, string? ConversationId, int TopK, IReadOnlyList<ConversationMessage>? ConversationHistory)? LastRequest { get; private set; }
        public BrainChatResponse? ResponseToReturn { get; set; }
        public BrainChatException? ExceptionToThrow { get; set; }

        public Task<BrainChatResponse> ChatAsync(
            string query,
            string mode,
            string? tenantId,
            string? conversationId,
            int topK = 5,
            IReadOnlyList<ConversationMessage>? conversationHistory = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = (query, mode, tenantId, conversationId, topK, conversationHistory);

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

        public string? LastStartedChatMode { get; private set; }
        public string? LastUpdatedChatMode { get; private set; }

        private static ChatConversationSummary BuildExistingSummary(string chatMode = ChatConversationModes.Enhanced)
        {
            return new ChatConversationSummary(
                ConversationId,
                "Existing conversation title",
                "User preview",
                "tenant-alpha",
                chatMode,
                1,
                false,
                Timestamp,
                Timestamp);
        }

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_activeSummary is not null)
            {
                return Task.FromResult<IReadOnlyList<ChatConversationSummary>>([_activeSummary]);
            }

            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>([BuildExistingSummary()]);
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
                    BuildExistingSummary().ChatMode,
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
            string chatMode = ChatConversationModes.Simple,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastStartedChatMode = ChatConversationModes.Normalize(chatMode);
            _messageCount = 1;
            _activeSummary = new ChatConversationSummary(
                ConversationId,
                "New conversation",
                userMessage,
                tenantId,
                LastStartedChatMode,
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
            CancellationToken cancellationToken = default,
            BrainChatResponse? assistantResponse = null)
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
            cancellationToken.ThrowIfCancellationRequested();

            if (conversationId != ConversationId)
            {
                return Task.FromResult<ChatConversationSummary?>(null);
            }

            _activeSummary ??= BuildExistingSummary();
            LastUpdatedChatMode = ChatConversationModes.Normalize(chatMode);
            _activeSummary = _activeSummary with
            {
                ChatMode = LastUpdatedChatMode
            };

            return Task.FromResult<ChatConversationSummary?>(_activeSummary);
        }

        public Task<bool> DeleteConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubChatConversationServiceWithLegacyRegularConversation : IChatConversationService
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
                    "Legacy regular mode conversation",
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
            return Task.FromResult<ChatConversationDetail?>(
                new ChatConversationDetail(
                    conversationId,
                    "Legacy regular mode conversation",
                    "tenant-alpha",
                    ChatConversationModes.Regular,
                    false,
                    Timestamp,
                    Timestamp,
                    [
                        new ChatConversationMessageRecord(
                            Guid.Parse("44444444-4444-4444-4444-444444444444"),
                            ChatConversationRoles.User,
                            "Original regular prompt",
                            1,
                            Timestamp)
                    ]));
        }

        public Task<ChatConversationSummary> StartConversationAsync(
            string ownerUserId,
            string? tenantId,
            string userMessage,
            string chatMode = ChatConversationModes.Regular,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _messageCount = 1;
            _activeSummary = new ChatConversationSummary(
                ConversationId,
                "Legacy regular mode conversation",
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
            CancellationToken cancellationToken = default,
            BrainChatResponse? assistantResponse = null)
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

    private sealed class StubChatConversationServiceWithFollowUpHistory : IChatConversationService
    {
        private static readonly Guid ConversationId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        private static readonly DateTime Timestamp = new(2026, 4, 22, 13, 0, 0, DateTimeKind.Utc);

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    ConversationId,
                    "Follow-up ready conversation",
                    "The previous assistant answer",
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
                new ChatConversationDetail(
                    conversationId,
                    "Follow-up ready conversation",
                    "tenant-alpha",
                    ChatConversationModes.Regular,
                    false,
                    Timestamp,
                    Timestamp,
                    [
                        new ChatConversationMessageRecord(
                            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            ChatConversationRoles.User,
                            "Original prompt",
                            1,
                            Timestamp),
                        new ChatConversationMessageRecord(
                            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            ChatConversationRoles.Assistant,
                            "Original assistant answer",
                            2,
                            Timestamp.AddSeconds(1))
                    ]));
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
            CancellationToken cancellationToken = default,
            BrainChatResponse? assistantResponse = null)
        {
            return Task.FromResult<ChatConversationSummary?>(
                new ChatConversationSummary(
                    ConversationId,
                    "Follow-up ready conversation",
                    content,
                    "tenant-alpha",
                    ChatConversationModes.Regular,
                    3,
                    false,
                    Timestamp,
                    Timestamp.AddSeconds(2)));
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

    private sealed class StubChatConversationServiceWithPersistedAssistantMetadata : IChatConversationService
    {
        private static readonly Guid ConversationId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        private static readonly DateTime Timestamp = new(2026, 4, 22, 14, 0, 0, DateTimeKind.Utc);

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    ConversationId,
                    "Saved metadata conversation",
                    "Saved assistant answer",
                    "tenant-alpha",
                    ChatConversationModes.Critique,
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
                new ChatConversationDetail(
                    conversationId,
                    "Saved metadata conversation",
                    "tenant-alpha",
                    ChatConversationModes.Critique,
                    false,
                    Timestamp,
                    Timestamp,
                    [
                        new ChatConversationMessageRecord(
                            Guid.Parse("13131313-1313-1313-1313-131313131313"),
                            ChatConversationRoles.User,
                            "What changed after the upload?",
                            1,
                            Timestamp),
                        new ChatConversationMessageRecord(
                            Guid.Parse("14141414-1414-1414-1414-141414141414"),
                            ChatConversationRoles.Assistant,
                            "The uploaded document adds Neo4j indexing guidance.",
                            2,
                            Timestamp.AddSeconds(1),
                            new BrainChatResponse(
                                Answer: "The uploaded document adds Neo4j indexing guidance.",
                                Confidence: 0.88,
                                Evidence:
                                [
                                    new BrainChatEvidence("Neo4j indexes should match the filtered properties.", 0.84, "document:7/page:3")
                                ],
                                ReasoningSteps:
                                [
                                    new BrainChatReasoningStep("retrieval", "Matched the saved upload context.", "brain-knowledge-retriever", "1 result")
                                ],
                                ProactiveSuggestions: []))
                    ]));
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
            CancellationToken cancellationToken = default,
            BrainChatResponse? assistantResponse = null)
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

    private sealed class StubChatConversationServiceWithMixedModes : IChatConversationService
    {
        private static readonly Guid SimpleConversationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid CritiqueConversationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        private static readonly Guid EnhancedConversationId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        private static readonly DateTime Timestamp = new(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    SimpleConversationId,
                    "Simple mode conversation",
                    "Simple preview",
                    "tenant-alpha",
                    ChatConversationModes.Simple,
                    2,
                    false,
                    Timestamp.AddMinutes(2),
                    Timestamp.AddMinutes(2)),
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
                    EnhancedConversationId,
                    "Enhanced mode conversation",
                    "Enhanced preview",
                    "tenant-alpha",
                    ChatConversationModes.Enhanced,
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
                    _ when conversationId == SimpleConversationId => new ChatConversationDetail(
                        SimpleConversationId,
                        "Simple mode conversation",
                        "tenant-alpha",
                        ChatConversationModes.Simple,
                        false,
                        Timestamp.AddMinutes(2),
                        Timestamp.AddMinutes(2),
                        [
                            new ChatConversationMessageRecord(
                                Guid.Parse("99999999-9999-9999-9999-999999999998"),
                                ChatConversationRoles.User,
                                "Original simple prompt",
                                1,
                                Timestamp.AddMinutes(2))
                        ]),
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
                    _ when conversationId == EnhancedConversationId => new ChatConversationDetail(
                        EnhancedConversationId,
                        "Enhanced mode conversation",
                        "tenant-alpha",
                        ChatConversationModes.Enhanced,
                        false,
                        Timestamp,
                        Timestamp,
                        [
                            new ChatConversationMessageRecord(
                                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                                ChatConversationRoles.User,
                                "Original enhanced prompt",
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
            CancellationToken cancellationToken = default,
            BrainChatResponse? assistantResponse = null)
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


