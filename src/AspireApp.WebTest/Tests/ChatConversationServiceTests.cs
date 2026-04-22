extern alias web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using BrainChatEvidence = web::AspireApp.Web.Services.BrainChatEvidence;
using BrainChatReasoningStep = web::AspireApp.Web.Services.BrainChatReasoningStep;
using BrainChatResponse = web::AspireApp.Web.Services.BrainChatResponse;
using ChatConversation = web::AspireApp.Web.Data.ChatConversation;
using ChatConversationMessage = web::AspireApp.Web.Data.ChatConversationMessage;
using ChatConversationModes = web::AspireApp.Web.Services.ChatConversationModes;
using ChatConversationRoles = web::AspireApp.Web.Services.ChatConversationRoles;
using ChatConversationService = web::AspireApp.Web.Services.ChatConversationService;
using ChatConversationTitleSources = web::AspireApp.Web.Services.ChatConversationTitleSources;
using IChatTitleGenerator = web::AspireApp.Web.Services.IChatTitleGenerator;
using UploadDbContext = web::AspireApp.Web.Shared.UploadDbContext;

namespace AspireApp.WebTest.Tests;

public sealed class ChatConversationServiceTests
{
    [Fact]
    public async Task StartConversationAsync_PersistsOwnerScopedConversationWithFallbackTitle_AndDefaultsToSimpleMode()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);

        var summary = await service.StartConversationAsync(
            "demo-taylor-jones",
            "tenant-alpha",
            "How do I configure Neo4j indexes for better performance?",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("How do I configure Neo4j indexes", summary.Title);
        Assert.Equal("tenant-alpha", summary.TenantId);
        Assert.Equal(ChatConversationModes.Simple, summary.ChatMode);
        Assert.Equal(1, summary.MessageCount);

        var conversation = await context.ChatConversations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("demo-taylor-jones", conversation.OwnerUserId);
        Assert.Equal("tenant-alpha", conversation.TenantId);
        Assert.Equal(ChatConversationModes.Simple, conversation.ChatMode);
        Assert.Equal(ChatConversationTitleSources.Fallback, conversation.TitleSource);

        var message = await context.ChatConversationMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ChatConversationRoles.User, message.Role);
        Assert.Equal("demo-taylor-jones", message.OwnerUserId);
    }

    [Fact]
    public async Task AddMessageAsync_UpdatesGeneratedTitle_WhenGeneratorReturnsCandidate()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context, generatedTitle: "Neo4j index tuning plan");

        var started = await service.StartConversationAsync(
            "demo-taylor-jones",
            "tenant-alpha",
            "How do I configure Neo4j indexes for better performance?",
            cancellationToken: TestContext.Current.CancellationToken);

        var updated = await service.AddMessageAsync(
            started.ConversationId,
            "demo-taylor-jones",
            ChatConversationRoles.Assistant,
            "Use a composite index on the properties you filter on together.",
            TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal("Neo4j index tuning plan", updated.Title);

        var conversation = await context.ChatConversations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ChatConversationTitleSources.Generated, conversation.TitleSource);
        Assert.Equal("Neo4j index tuning plan", conversation.Title);
    }

    [Fact]
    public async Task AddMessageAsync_PersistsAssistantMetadata_AndReloadsIt()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);

        var started = await service.StartConversationAsync(
            "demo-taylor-jones",
            "tenant-alpha",
            "Summarize the current document state.",
            cancellationToken: TestContext.Current.CancellationToken);

        var response = new BrainChatResponse(
            Answer: "The document covers Neo4j indexing guidance.",
            Confidence: 0.87,
            Evidence:
            [
                new BrainChatEvidence("Index on frequently filtered properties.", 0.82, "document:7/page:2")
            ],
            ReasoningSteps:
            [
                new BrainChatReasoningStep("retrieval", "Matched the indexing section.", "brain-knowledge-retriever", "1 result")
            ],
            ProactiveSuggestions: []);

        var updated = await service.AddMessageAsync(
            started.ConversationId,
            "demo-taylor-jones",
            ChatConversationRoles.Assistant,
            response.Answer,
            TestContext.Current.CancellationToken,
            response);

        Assert.NotNull(updated);

        var storedAssistantMessage = await context.ChatConversationMessages
            .SingleAsync(
                message => message.ConversationId == started.ConversationId &&
                           message.Role == ChatConversationRoles.Assistant,
                TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(storedAssistantMessage.AssistantResponseJson));

        var reloaded = await service.GetConversationAsync(
            started.ConversationId,
            "demo-taylor-jones",
            TestContext.Current.CancellationToken);

        var assistantMessage = Assert.Single(reloaded!.Messages, message => message.Role == ChatConversationRoles.Assistant);
        Assert.NotNull(assistantMessage.AssistantResponse);
        Assert.Equal(0.87, assistantMessage.AssistantResponse!.Confidence);
        Assert.Single(assistantMessage.AssistantResponse.Evidence);
        Assert.Single(assistantMessage.AssistantResponse.ReasoningSteps);
    }

    [Fact]
    public async Task StartConversationAsync_AndUpdateChatModeAsync_PersistSelectedModeAcrossReloads()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context);

        var started = await service.StartConversationAsync(
            "demo-taylor-jones",
            "tenant-alpha",
            "Walk me through the available chat modes",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ChatConversationModes.Simple, started.ChatMode);

        var createdConversation = await context.ChatConversations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ChatConversationModes.Simple, createdConversation.ChatMode);

        var enhanced = await service.UpdateChatModeAsync(
            started.ConversationId,
            "demo-taylor-jones",
            "enhanced",
            TestContext.Current.CancellationToken);

        Assert.NotNull(enhanced);
        Assert.Equal(ChatConversationModes.Enhanced, enhanced!.ChatMode);

        var critique = await service.UpdateChatModeAsync(
            started.ConversationId,
            "demo-taylor-jones",
            "CRITIQUE",
            TestContext.Current.CancellationToken);

        Assert.NotNull(critique);
        Assert.Equal(ChatConversationModes.Critique, critique!.ChatMode);

        var reloaded = await service.GetConversationAsync(
            started.ConversationId,
            "demo-taylor-jones",
            TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.Equal(ChatConversationModes.Critique, reloaded!.ChatMode);
        Assert.Equal(ChatConversationModes.Critique, createdConversation.ChatMode);
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("REGULAR")]
    [InlineData("enhanced")]
    [InlineData("ENHANCED")]
    public void ChatConversationModes_Normalize_MapsLegacyRegularAndEnhancedValues_ToEnhancedAlias(string persistedMode)
    {
        Assert.Equal(ChatConversationModes.Enhanced, ChatConversationModes.Normalize(persistedMode));
    }

    [Fact]
    public async Task RenameConversationAsync_PreservesUserTitle_WhenAssistantMessagesArriveLater()
    {
        await using var context = CreateDbContext();
        var service = CreateService(context, generatedTitle: "Generated title should not win");

        var started = await service.StartConversationAsync(
            "demo-taylor-jones",
            "tenant-alpha",
            "Help me plan my document ingestion workflow",
            cancellationToken: TestContext.Current.CancellationToken);

        var renamed = await service.RenameConversationAsync(
            started.ConversationId,
            "demo-taylor-jones",
            "Doc ingestion plan",
            TestContext.Current.CancellationToken);

        Assert.NotNull(renamed);
        Assert.Equal("Doc ingestion plan", renamed.Title);

        var updated = await service.AddMessageAsync(
            started.ConversationId,
            "demo-taylor-jones",
            ChatConversationRoles.Assistant,
            "Start with chunking rules, then map the extracted entities to Neo4j.",
            TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal("Doc ingestion plan", updated.Title);

        var conversation = await context.ChatConversations.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ChatConversationTitleSources.User, conversation.TitleSource);
        Assert.Equal("Doc ingestion plan", conversation.Title);
    }

    [Fact]
    public async Task ListAndGetConversationAsync_ReturnOnlyOwnerRecords()
    {
        await using var context = CreateDbContext();
        SeedConversation(context, Guid.Parse("11111111-1111-1111-1111-111111111111"), "owner-a", "Owner A title", "user");
        SeedConversation(context, Guid.Parse("22222222-2222-2222-2222-222222222222"), "owner-b", "Owner B title", "assistant");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var list = await service.ListConversationsAsync("owner-a", TestContext.Current.CancellationToken);
        var detail = await service.GetConversationAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "owner-a",
            TestContext.Current.CancellationToken);

        var conversation = Assert.Single(list);
        Assert.Equal("Owner A title", conversation.Title);
        Assert.Null(detail);
    }

    [Fact]
    public async Task AddMessageAsync_DoesNotAllowAnotherUserToContinueConversation()
    {
        await using var context = CreateDbContext();
        var conversationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SeedConversation(context, conversationId, "owner-a", "Owner A title", "user");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var blockedUpdate = await service.AddMessageAsync(
            conversationId,
            "owner-b",
            ChatConversationRoles.User,
            "Attempt to continue another user's thread.",
            TestContext.Current.CancellationToken);

        Assert.Null(blockedUpdate);

        var ownerConversation = await service.GetConversationAsync(
            conversationId,
            "owner-a",
            TestContext.Current.CancellationToken);

        Assert.NotNull(ownerConversation);
        Assert.Single(ownerConversation!.Messages);
        Assert.DoesNotContain(
            ownerConversation.Messages,
            message => message.Content.Contains("Attempt to continue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteConversationAsync_RemovesOnlyOwnedConversationAndMessages()
    {
        await using var context = CreateDbContext();
        var conversationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SeedConversation(context, conversationId, "owner-a", "Owner A title", "assistant");
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = CreateService(context);

        var blockedDelete = await service.DeleteConversationAsync(
            conversationId,
            "owner-b",
            TestContext.Current.CancellationToken);
        var allowedDelete = await service.DeleteConversationAsync(
            conversationId,
            "owner-a",
            TestContext.Current.CancellationToken);

        Assert.False(blockedDelete);
        Assert.True(allowedDelete);
        Assert.Empty(await context.ChatConversations.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.ChatConversationMessages.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static ChatConversationService CreateService(UploadDbContext context, string? generatedTitle = null)
    {
        return new ChatConversationService(
            context,
            new StubChatTitleGenerator(generatedTitle),
            NullLogger<ChatConversationService>.Instance);
    }

    private static void SeedConversation(UploadDbContext context, Guid conversationId, string ownerUserId, string title, string previewRole)
    {
        var createdAt = DateTime.UtcNow;
        context.ChatConversations.Add(new ChatConversation
        {
            Id = conversationId,
            OwnerUserId = ownerUserId,
            TenantId = "tenant-alpha",
            Title = title,
            TitleSource = ChatConversationTitleSources.Fallback,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            LastMessageAt = createdAt
        });

        context.ChatConversationMessages.Add(new ChatConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            OwnerUserId = ownerUserId,
            Role = previewRole,
            Content = $"Preview for {title}",
            Sequence = 1,
            CreatedAt = createdAt
        });
    }

    private static UploadDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<UploadDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new UploadDbContext(options);
    }

    private sealed class StubChatTitleGenerator(string? generatedTitle) : IChatTitleGenerator
    {
        private readonly string? _generatedTitle = generatedTitle;

        public Task<string?> TryGenerateTitleAsync(
            IReadOnlyList<web::AspireApp.Web.Services.ChatConversationMessageRecord> messages,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_generatedTitle);
        }
    }
}
