extern alias web;

using System.Net;
using System.Security.Claims;
using Bunit;
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
using IChatConversationService = web::AspireApp.Web.Services.IChatConversationService;
using TenantContextService = web::AspireApp.Web.Services.TenantContextService;
using TenantManagementService = web::AspireApp.Web.Services.TenantManagementService;

namespace AspireApp.WebTest.Tests;

public sealed class ChatFocusTests
{
    [Fact]
    public void RenameTitleInput_DoesNotRefocusQuestionInputWhileTyping()
    {
        var previousOllamaConnection = Environment.GetEnvironmentVariable("ConnectionStrings__ollama");
        var previousChatConnection = Environment.GetEnvironmentVariable("ConnectionStrings__chat");
        var previousAiEndpoint = Environment.GetEnvironmentVariable("AI-Endpoint");
        var previousAiModel = Environment.GetEnvironmentVariable("AI-Model");

        Environment.SetEnvironmentVariable("ConnectionStrings__ollama", "http://localhost:11434");
        Environment.SetEnvironmentVariable("ConnectionStrings__chat", "phi4-mini:latest");
        Environment.SetEnvironmentVariable("AI-Endpoint", "http://localhost:11434");
        Environment.SetEnvironmentVariable("AI-Model", "phi4-mini:latest");
        HomeConfigurations.ForceReconfigure();

        try
        {
            using var testContext = new Bunit.BunitContext();
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
            var chatConversationService = new StubChatConversationService();

            testContext.Services.AddSingleton<IConfiguration>(configuration);
            testContext.Services.AddSingleton<IJSRuntime>(testContext.JSInterop.JSRuntime);
            testContext.Services.AddSingleton(httpClientFactory);
            testContext.Services.AddSingleton<IHttpClientFactory>(httpClientFactory);
            testContext.Services.AddSingleton(new SpeechService(testContext.JSInterop.JSRuntime));
            testContext.Services.AddSingleton<AuthenticationStateProvider>(new StubAuthenticationStateProvider(CreateUser()));
            testContext.Services.AddSingleton(authenticationContext);
            testContext.Services.AddSingleton<IChatConversationService>(chatConversationService);
            testContext.Services.AddSingleton(tenantContext);
            testContext.Services.AddSingleton(new AiInfoStateService(configuration, httpClientFactory));

            var cut = testContext.Render<Chat>();

            cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='chat-conversation-select']")));
            cut.Find("[data-testid='chat-conversation-select']").Click();
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='chat-conversation-rename']")));

            var focusCallsBeforeRename = CountFocusCalls(testContext);
            cut.Find("[data-testid='chat-conversation-rename']").Click();

            cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='chat-conversation-title-input']")));
            cut.WaitForAssertion(() => Assert.True(CountFocusCalls(testContext) > focusCallsBeforeRename));

            var focusCallsAfterRename = CountFocusCalls(testContext);
            cut.Find("[data-testid='chat-conversation-title-input']").Input("R");

            cut.WaitForAssertion(() =>
            {
                Assert.Equal(focusCallsAfterRename, CountFocusCalls(testContext));
                Assert.Equal("R", cut.Find("[data-testid='chat-conversation-title-input']").GetAttribute("value"));
                Assert.Equal(string.Empty, cut.Find("[data-testid='chat-message-input']").GetAttribute("value") ?? string.Empty);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__ollama", previousOllamaConnection);
            Environment.SetEnvironmentVariable("ConnectionStrings__chat", previousChatConnection);
            Environment.SetEnvironmentVariable("AI-Endpoint", previousAiEndpoint);
            Environment.SetEnvironmentVariable("AI-Model", previousAiModel);
        }
    }

    private static int CountFocusCalls(Bunit.BunitContext testContext)
    {
        return testContext.JSInterop.Invocations.Count(invocation =>
            string.Equals(invocation.Identifier, "focusElement", StringComparison.Ordinal));
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
        private static readonly DateTime Timestamp = new(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);

        public Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ChatConversationSummary>>(
            [
                new ChatConversationSummary(
                    ConversationId,
                    "Existing conversation title",
                    "User preview",
                    "tenant-alpha",
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

            return Task.FromResult<ChatConversationDetail?>(
                new ChatConversationDetail(
                    conversationId,
                    "Existing conversation title",
                    "tenant-alpha",
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

        public Task<bool> DeleteConversationAsync(
            Guid conversationId,
            string ownerUserId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
