using Assistant.Application.Abstractions;
using Assistant.Domain;

namespace Assistant.Application;

public sealed record IncomingMessage(ChatRole Role, string Text, string? Via, string? Kind, string? Payload, DateTimeOffset? At);

public sealed class ChatHistory(IChatHistoryRepository history, TimeProvider clock)
{
    public const int MaxMessagesPerConversation = 200;
    public const int MaxConversations = 50;

    public Task<IReadOnlyList<ConversationSummary>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        history.ListAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<ChatMessage>?> MessagesAsync(Guid userId, string conversationId, CancellationToken cancellationToken) =>
        await history.FindAsync(userId, conversationId, cancellationToken) is null
            ? null
            : await history.MessagesAsync(userId, conversationId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> AppendAsync(
        Guid userId, string conversationId, IReadOnlyList<IncomingMessage> messages, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var conversation = await history.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
        {
            var firstUser = messages.FirstOrDefault(message => message.Role == ChatRole.User)?.Text ?? "";
            conversation = Conversation.Start(userId, conversationId, firstUser, now);
            history.Add(conversation);
        }

        var stored = messages
            .Select((message, index) => message.Role == ChatRole.User
                ? ChatMessage.FromUser(userId, conversationId, message.Text, message.Via, message.At ?? now.AddTicks(index))
                : ChatMessage.FromAssistant(userId, conversationId, message.Text, message.Kind, message.Payload, message.At ?? now.AddTicks(index)))
            .ToArray();
        history.Add(stored);
        conversation.Touch(now);
        await history.SaveChangesAsync(cancellationToken);
        await history.TrimAsync(userId, conversationId, MaxMessagesPerConversation, MaxConversations, cancellationToken);
        return stored.Select(message => message.Id).ToArray();
    }

    public async Task<bool> ImportAsync(
        Guid userId, string conversationId, IReadOnlyList<IncomingMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0 || await history.FindAsync(userId, conversationId, cancellationToken) is not null)
            return false;

        await AppendAsync(userId, conversationId, messages.TakeLast(MaxMessagesPerConversation).ToArray(), cancellationToken);
        return true;
    }

    public async Task<bool> UpdatePayloadAsync(Guid userId, Guid messageId, string payload, CancellationToken cancellationToken)
    {
        var message = await history.FindMessageAsync(userId, messageId, cancellationToken);
        if (message is null || message.Role != ChatRole.Assistant)
            return false;

        message.ReplacePayload(payload);
        await history.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task DeleteAsync(Guid userId, string conversationId, CancellationToken cancellationToken) =>
        history.RemoveAsync(userId, conversationId, cancellationToken);
}
