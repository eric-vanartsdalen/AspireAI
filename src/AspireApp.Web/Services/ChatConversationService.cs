using System.Text.Json;
using System.Text.RegularExpressions;
using AspireApp.Web.Data;
using AspireApp.Web.Shared;
using Microsoft.EntityFrameworkCore;

namespace AspireApp.Web.Services;

public static class ChatConversationRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";

    public static string Normalize(string role)
    {
        if (string.Equals(role, User, StringComparison.OrdinalIgnoreCase))
        {
            return User;
        }

        if (string.Equals(role, Assistant, StringComparison.OrdinalIgnoreCase))
        {
            return Assistant;
        }

        throw new ArgumentOutOfRangeException(nameof(role), $"Chat role '{role}' is not supported.");
    }
}

public static class ChatConversationTitleSources
{
    public const string Fallback = "fallback";
    public const string Generated = "generated";
    public const string User = "user";
}

public static class ChatConversationModes
{
    public const string Simple = "simple";
    public const string Enhanced = "regular";
    public const string Regular = Enhanced;
    public const string Critique = "critique";

    public static string Normalize(string? mode)
    {
        if (string.Equals(mode, Simple, StringComparison.OrdinalIgnoreCase))
        {
            return Simple;
        }

        if (string.Equals(mode, Enhanced, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "enhanced", StringComparison.OrdinalIgnoreCase))
        {
            return Enhanced;
        }

        if (string.Equals(mode, Critique, StringComparison.OrdinalIgnoreCase))
        {
            return Critique;
        }

        return Simple;
    }

    public static bool IsSimple(string? mode)
    {
        return string.Equals(Normalize(mode), Simple, StringComparison.Ordinal);
    }

    public static string GetHint(string? mode)
    {
        return Normalize(mode) switch
        {
            Simple => "Direct model chat with your selected LLM",
            Enhanced => "Knowledge-enhanced responses with GraphRAG context",
            Critique => "Thorough, agent-verified answers",
            _ => "Direct model chat with your selected LLM"
        };
    }
}

public sealed record ChatConversationSummary(
    Guid ConversationId,
    string Title,
    string Preview,
    string? TenantId,
    string ChatMode,
    int MessageCount,
    bool HasUserEditedTitle,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ChatConversationMessageRecord(
    Guid MessageId,
    string Role,
    string Content,
    int Sequence,
    DateTime CreatedAt,
    BrainChatResponse? AssistantResponse = null);

public sealed record ChatConversationDetail(
    Guid ConversationId,
    string Title,
    string? TenantId,
    string ChatMode,
    bool HasUserEditedTitle,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ChatConversationMessageRecord> Messages);

public interface IChatConversationService
{
    Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(string ownerUserId, CancellationToken cancellationToken = default);
    Task<ChatConversationDetail?> GetConversationAsync(Guid conversationId, string ownerUserId, CancellationToken cancellationToken = default);
    Task<ChatConversationSummary> StartConversationAsync(string ownerUserId, string? tenantId, string userMessage, string chatMode = ChatConversationModes.Simple, CancellationToken cancellationToken = default);
    Task<ChatConversationSummary?> AddMessageAsync(Guid conversationId, string ownerUserId, string role, string content, CancellationToken cancellationToken = default, BrainChatResponse? assistantResponse = null);
    Task<ChatConversationSummary?> UpdateChatModeAsync(Guid conversationId, string ownerUserId, string chatMode, CancellationToken cancellationToken = default);
    Task<ChatConversationSummary?> RenameConversationAsync(Guid conversationId, string ownerUserId, string title, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid conversationId, string ownerUserId, CancellationToken cancellationToken = default);
}

public sealed class ChatConversationService(
    UploadDbContext dbContext,
    IChatTitleGenerator titleGenerator,
    ILogger<ChatConversationService> logger) : IChatConversationService
{
    private static readonly JsonSerializerOptions AssistantResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly UploadDbContext _dbContext = dbContext;
    private readonly IChatTitleGenerator _titleGenerator = titleGenerator;
    private readonly ILogger<ChatConversationService> _logger = logger;

    public async Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);

        var conversations = await _dbContext.ChatConversations
            .AsNoTracking()
            .Where(conversation => conversation.OwnerUserId == normalizedOwnerUserId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ToListAsync(cancellationToken);

        return await BuildSummariesAsync(normalizedOwnerUserId, conversations, cancellationToken);
    }

    public async Task<ChatConversationDetail?> GetConversationAsync(
        Guid conversationId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);

        var conversation = await _dbContext.ChatConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == normalizedOwnerUserId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var messages = await _dbContext.ChatConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.OwnerUserId == normalizedOwnerUserId)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);

        return new ChatConversationDetail(
            conversation.Id,
            conversation.Title,
            conversation.TenantId,
            conversation.ChatMode,
            string.Equals(conversation.TitleSource, ChatConversationTitleSources.User, StringComparison.OrdinalIgnoreCase),
            conversation.CreatedAt,
            conversation.UpdatedAt,
            messages.Select(MapMessageRecord).ToList());
    }

    public async Task<ChatConversationSummary> StartConversationAsync(
        string ownerUserId,
        string? tenantId,
        string userMessage,
        string chatMode = ChatConversationModes.Simple,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);
        var normalizedMessage = NormalizeMessageContent(userMessage);
        var now = DateTime.UtcNow;

        var conversation = new ChatConversation
        {
            Id = Guid.NewGuid(),
            OwnerUserId = normalizedOwnerUserId,
            TenantId = NormalizeTenantId(tenantId),
            Title = ChatConversationTitleHelper.BuildFallbackTitle(normalizedMessage),
            TitleSource = ChatConversationTitleSources.Fallback,
            ChatMode = ChatConversationModes.Normalize(chatMode),
            CreatedAt = now,
            UpdatedAt = now,
            LastMessageAt = now
        };

        var message = new ChatConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            OwnerUserId = normalizedOwnerUserId,
            Role = ChatConversationRoles.User,
            Content = normalizedMessage,
            Sequence = 1,
            CreatedAt = now
        };

        _dbContext.ChatConversations.Add(conversation);
        _dbContext.ChatConversationMessages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Created chat conversation {ConversationId} for user {UserId}",
                conversation.Id,
                normalizedOwnerUserId);
        }

        return BuildSummary(conversation, [message]);
    }

    public async Task<ChatConversationSummary?> AddMessageAsync(
        Guid conversationId,
        string ownerUserId,
        string role,
        string content,
        CancellationToken cancellationToken = default,
        BrainChatResponse? assistantResponse = null)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);
        var normalizedRole = ChatConversationRoles.Normalize(role);
        var normalizedContent = NormalizeMessageContent(content);

        var conversation = await _dbContext.ChatConversations
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == normalizedOwnerUserId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var nextSequence = (await _dbContext.ChatConversationMessages
            .Where(message => message.ConversationId == conversationId && message.OwnerUserId == normalizedOwnerUserId)
            .Select(message => (int?)message.Sequence)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var now = DateTime.UtcNow;
        _dbContext.ChatConversationMessages.Add(new ChatConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            OwnerUserId = normalizedOwnerUserId,
            Role = normalizedRole,
            Content = normalizedContent,
            AssistantResponseJson = SerializeAssistantResponse(normalizedRole, assistantResponse),
            Sequence = nextSequence,
            CreatedAt = now
        });

        conversation.UpdatedAt = now;
        conversation.LastMessageAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (string.Equals(normalizedRole, ChatConversationRoles.Assistant, StringComparison.Ordinal))
        {
            await TryApplyGeneratedTitleAsync(conversationId, normalizedOwnerUserId, cancellationToken);
        }

        return await GetConversationSummaryAsync(conversationId, normalizedOwnerUserId, cancellationToken);
    }

    public async Task<ChatConversationSummary?> UpdateChatModeAsync(
        Guid conversationId,
        string ownerUserId,
        string chatMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);
        var normalizedMode = ChatConversationModes.Normalize(chatMode);

        var conversation = await _dbContext.ChatConversations
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == normalizedOwnerUserId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        conversation.ChatMode = normalizedMode;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetConversationSummaryAsync(conversationId, normalizedOwnerUserId, cancellationToken);
    }

    public async Task<ChatConversationSummary?> RenameConversationAsync(
        Guid conversationId,
        string ownerUserId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);
        var normalizedTitle = ChatConversationTitleHelper.NormalizeUserProvidedTitle(title);
        if (normalizedTitle is null)
        {
            return null;
        }

        var conversation = await _dbContext.ChatConversations
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == normalizedOwnerUserId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        conversation.Title = normalizedTitle;
        conversation.TitleSource = ChatConversationTitleSources.User;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetConversationSummaryAsync(conversationId, normalizedOwnerUserId, cancellationToken);
    }

    public async Task<bool> DeleteConversationAsync(
        Guid conversationId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwnerUserId = NormalizeOwnerUserId(ownerUserId);

        var conversation = await _dbContext.ChatConversations
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == normalizedOwnerUserId,
                cancellationToken);

        if (conversation is null)
        {
            return false;
        }

        _dbContext.ChatConversations.Remove(conversation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<ChatConversationSummary>> BuildSummariesAsync(
        string ownerUserId,
        IReadOnlyList<ChatConversation> conversations,
        CancellationToken cancellationToken)
    {
        if (conversations.Count == 0)
        {
            return [];
        }

        var conversationIds = conversations
            .Select(conversation => conversation.Id)
            .ToList();

        var messages = await _dbContext.ChatConversationMessages
            .AsNoTracking()
            .Where(message => message.OwnerUserId == ownerUserId && conversationIds.Contains(message.ConversationId))
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);

        var messagesByConversation = messages
            .GroupBy(message => message.ConversationId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ChatConversationMessage>)group.ToList());

        return conversations
            .Select(conversation => BuildSummary(
                conversation,
                messagesByConversation.TryGetValue(conversation.Id, out var groupedMessages)
                    ? groupedMessages
                    : []))
            .ToList();
    }

    private async Task<ChatConversationSummary?> GetConversationSummaryAsync(
        Guid conversationId,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var conversation = await _dbContext.ChatConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == ownerUserId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var messages = await _dbContext.ChatConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.OwnerUserId == ownerUserId)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);

        return BuildSummary(conversation, messages);
    }

    private async Task TryApplyGeneratedTitleAsync(
        Guid conversationId,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        var conversation = await _dbContext.ChatConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == ownerUserId,
                cancellationToken);

        if (conversation is null ||
            string.Equals(conversation.TitleSource, ChatConversationTitleSources.User, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var messages = await _dbContext.ChatConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.OwnerUserId == ownerUserId)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);

        if (messages.Count < 2)
        {
            return;
        }

        var generatedTitle = ChatConversationTitleHelper.NormalizeGeneratedTitle(
            await _titleGenerator.TryGenerateTitleAsync(messages.Select(MapMessageRecord).ToList(), cancellationToken));

        if (string.IsNullOrWhiteSpace(generatedTitle) ||
            string.Equals(generatedTitle, conversation.Title, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trackedConversation = await _dbContext.ChatConversations
            .SingleOrDefaultAsync(
                existing => existing.Id == conversationId && existing.OwnerUserId == ownerUserId,
                cancellationToken);

        if (trackedConversation is null ||
            string.Equals(trackedConversation.TitleSource, ChatConversationTitleSources.User, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        trackedConversation.Title = generatedTitle;
        trackedConversation.TitleSource = ChatConversationTitleSources.Generated;
        trackedConversation.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ChatConversationSummary BuildSummary(
        ChatConversation conversation,
        IReadOnlyList<ChatConversationMessage> messages)
    {
        return new ChatConversationSummary(
            conversation.Id,
            conversation.Title,
            ChatConversationTitleHelper.BuildPreview(messages.LastOrDefault()?.Content),
            conversation.TenantId,
            conversation.ChatMode,
            messages.Count,
            string.Equals(conversation.TitleSource, ChatConversationTitleSources.User, StringComparison.OrdinalIgnoreCase),
            conversation.CreatedAt,
            conversation.UpdatedAt);
    }

    private ChatConversationMessageRecord MapMessageRecord(ChatConversationMessage message)
    {
        return new ChatConversationMessageRecord(
            message.Id,
            message.Role,
            message.Content,
            message.Sequence,
            message.CreatedAt,
            DeserializeAssistantResponse(message));
    }

    private BrainChatResponse? DeserializeAssistantResponse(ChatConversationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.AssistantResponseJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BrainChatResponse>(message.AssistantResponseJson, AssistantResponseJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Ignoring invalid assistant response metadata for chat message {MessageId}",
                message.Id);
            return null;
        }
    }

    private static string? SerializeAssistantResponse(string normalizedRole, BrainChatResponse? assistantResponse)
    {
        if (!string.Equals(normalizedRole, ChatConversationRoles.Assistant, StringComparison.Ordinal) ||
            assistantResponse is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(assistantResponse, AssistantResponseJsonOptions);
    }

    private static string NormalizeOwnerUserId(string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user ID cannot be empty.", nameof(ownerUserId));
        }

        return ownerUserId.Trim();
    }

    private static string NormalizeMessageContent(string content)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Chat message content cannot be empty.", nameof(content));
        }

        return normalized;
    }

    private static string? NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? null
            : tenantId.Trim();
    }
}

internal static partial class ChatConversationTitleHelper
{
    private const string DefaultTitle = "New conversation";
    private const int MaxTitleLength = 200;
    private const int MaxPreviewLength = 90;
    private static readonly Regex MarkdownNoiseRegex = new(@"[`*_>#\[\]\(\)]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string BuildFallbackTitle(string firstUserMessage)
    {
        var cleaned = Cleanup(firstUserMessage);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return DefaultTitle;
        }

        var sentence = cleaned
            .Split(['.', '?', '!', '\n', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var candidate = string.IsNullOrWhiteSpace(sentence)
            ? cleaned
            : sentence;

        var words = candidate
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(6)
            .ToArray();

        if (words.Length == 0)
        {
            return DefaultTitle;
        }

        return NormalizeTitle(string.Join(" ", words)) ?? DefaultTitle;
    }

    public static string? NormalizeGeneratedTitle(string? title) => NormalizeTitle(title);

    public static string? NormalizeUserProvidedTitle(string? title) => NormalizeTitle(title);

    public static string BuildPreview(string? content)
    {
        var cleaned = Cleanup(content);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "No messages yet";
        }

        return cleaned.Length <= MaxPreviewLength
            ? cleaned
            : $"{cleaned[..(MaxPreviewLength - 1)]}…";
    }

    private static string? NormalizeTitle(string? title)
    {
        var cleaned = Cleanup(title);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var trimmed = cleaned.Trim().Trim('"', '\'').TrimEnd('.', '?', '!', ':', ';', ',');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var candidate = trimmed.Length <= MaxTitleLength
            ? trimmed
            : trimmed[..MaxTitleLength].TrimEnd();

        return char.ToUpperInvariant(candidate[0]) + candidate[1..];
    }

    private static string Cleanup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = MarkdownNoiseRegex.Replace(value, " ");
        return WhitespaceRegex.Replace(cleaned, " ").Trim();
    }
}
