using Assistant.Domain;

namespace Assistant.Application.Abstractions;

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ConversationSummary>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<Conversation?> FindAsync(Guid userId, string conversationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid userId, string conversationId, CancellationToken cancellationToken);

    Task<ChatMessage?> FindMessageAsync(Guid userId, Guid messageId, CancellationToken cancellationToken);

    void Add(Conversation conversation);

    void Add(IEnumerable<ChatMessage> messages);

    Task RemoveAsync(Guid userId, string conversationId, CancellationToken cancellationToken);

    Task TrimAsync(Guid userId, string conversationId, int keepMessages, int keepConversations, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ConversationSummary(string Id, string Title, DateTimeOffset UpdatedAt, int MessageCount, string? LastText);
