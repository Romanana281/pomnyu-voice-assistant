namespace Assistant.Domain;

public enum ChatRole
{
    User,
    Assistant,
}

public sealed class ChatMessage
{
    public const int MaxTextLength = 4000;
    public const int MaxPayloadLength = 32 * 1024;
    public const int MaxTagLength = 16;

    private ChatMessage()
    {
        ConversationId = string.Empty;
        Text = string.Empty;
    }

    private ChatMessage(Guid userId, string conversationId, ChatRole role, string text, string? via, string? kind, string? payload, DateTimeOffset at)
    {
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];

        Id = Guid.NewGuid();
        UserId = userId;
        ConversationId = conversationId;
        Role = role;
        Text = text;
        Via = Tag(via);
        Kind = Tag(kind);
        Payload = CheckPayload(payload);
        CreatedAt = at;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string ConversationId { get; private set; }
    public ChatRole Role { get; private set; }
    public string Text { get; private set; }
    public string? Via { get; private set; }
    public string? Kind { get; private set; }
    public string? Payload { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ChatMessage FromUser(Guid userId, string conversationId, string text, string? via, DateTimeOffset at) =>
        new(userId, conversationId, ChatRole.User, text, via, null, null, at);

    public static ChatMessage FromAssistant(Guid userId, string conversationId, string text, string? kind, string? payload, DateTimeOffset at) =>
        new(userId, conversationId, ChatRole.Assistant, text, null, kind, payload, at);

    public void ReplacePayload(string payload) => Payload = CheckPayload(payload);

    private static string? Tag(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length > MaxTagLength ? value[..MaxTagLength] : value;

    private static string? CheckPayload(string? payload) =>
        payload is { Length: > MaxPayloadLength }
            ? throw new ArgumentException("Слишком большие данные сообщения.", nameof(payload))
            : payload;
}
