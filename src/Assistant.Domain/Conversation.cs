namespace Assistant.Domain;

public sealed class Conversation
{
    public const int MaxIdLength = 64;
    public const int MaxTitleLength = 80;

    private Conversation()
    {
        Id = string.Empty;
        Title = string.Empty;
    }

    private Conversation(Guid userId, string id, string title, DateTimeOffset now)
    {
        UserId = userId;
        Id = id;
        Title = title;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid UserId { get; private set; }
    public string Id { get; private set; }
    public string Title { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= MaxIdLength && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-');

    public static Conversation Start(Guid userId, string id, string firstMessage, DateTimeOffset now)
    {
        if (!IsValidId(id))
            throw new ArgumentException("Некорректный идентификатор чата.", nameof(id));

        var title = string.Join(' ', firstMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.Length == 0)
            title = "Новый чат";
        if (title.Length > MaxTitleLength)
            title = title[..(MaxTitleLength - 1)] + "…";
        return new Conversation(userId, id, title, now);
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
