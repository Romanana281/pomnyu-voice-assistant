namespace Assistant.Domain;

public sealed class CalendarSubscription
{
    public const int MaxUrlLength = 2048;
    public const int MaxNameLength = 120;
    public const int MaxErrorLength = 300;

    private CalendarSubscription()
    {
        Url = string.Empty;
        Name = string.Empty;
    }

    private CalendarSubscription(Guid userId, Uri url, string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Url = url.AbsoluteUri;
        Name = name;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Url { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }

    public string SourceKey => $"sub:{Id:N}";

    public static CalendarSubscription Create(Guid userId, Uri url, string name, DateTimeOffset now)
    {
        if (url.AbsoluteUri.Length > MaxUrlLength)
            throw new ArgumentException("Слишком длинная ссылка.", nameof(url));
        var trimmed = name.Trim();
        return new CalendarSubscription(userId, url, trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed, now);
    }

    public void Rename(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length > 0)
            Name = trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    public void RecordSuccess(DateTimeOffset now)
    {
        LastSyncedAt = now;
        LastError = null;
    }

    public void RecordFailure(string error, DateTimeOffset now)
    {
        LastSyncedAt = now;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}
