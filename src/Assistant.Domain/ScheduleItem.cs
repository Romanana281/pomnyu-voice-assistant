namespace Assistant.Domain;

public sealed class ScheduleItem
{
    public const int MaxTitleLength = 200;
    public const int MaxExternalSourceLength = 64;
    public const int MaxExternalUidLength = 512;
    public static readonly TimeSpan DefaultMeetingDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    private readonly List<DateOnly> _completedOccurrences = [];

    private ScheduleItem() => Title = string.Empty;

    private ScheduleItem(Guid userId, ItemType type, string title, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Type = type;
        Title = NormalizeTitle(title);
        Status = ItemStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ItemType Type { get; private set; }
    public string Title { get; private set; }
    public DateOnly? Date { get; private set; }
    public TimeOnly? Time { get; private set; }
    public TimeSpan? Duration { get; private set; }
    public Recurrence? Recurrence { get; private set; }
    public ItemStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public float[]? Embedding { get; private set; }

    public string? ExternalSource { get; private set; }

    public string? ExternalUid { get; private set; }

    public bool IsImported => ExternalSource is not null;

    public IReadOnlyList<DateOnly> CompletedOccurrences => _completedOccurrences;

    public bool IsRecurring => Recurrence is not null;

    public static ScheduleItem CreateTask(
        Guid userId,
        ItemType type,
        string title,
        DateOnly? date,
        TimeOnly? time,
        Recurrence? recurrence,
        DateTimeOffset now)
    {
        if (type == ItemType.Meeting)
            throw new ArgumentException("Встреча создаётся через CreateMeeting.", nameof(type));

        var item = new ScheduleItem(userId, type, title, now);
        item.SetSchedule(date, time, duration: null, recurrence);
        return item;
    }

    public static ScheduleItem CreateMeeting(
        Guid userId,
        string title,
        DateOnly date,
        TimeOnly time,
        TimeSpan duration,
        Recurrence? recurrence,
        DateTimeOffset now)
    {
        var item = new ScheduleItem(userId, ItemType.Meeting, title, now);
        item.SetSchedule(date, time, duration, recurrence);
        return item;
    }

    public static ScheduleItem Import(
        Guid userId,
        string source,
        string uid,
        ItemType type,
        string title,
        DateOnly date,
        TimeOnly? time,
        TimeSpan? duration,
        Recurrence? recurrence,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxExternalSourceLength)
            throw new ArgumentException("Некорректный источник импорта.", nameof(source));
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > MaxExternalUidLength)
            throw new ArgumentException("Некорректный идентификатор события.", nameof(uid));

        var item = new ScheduleItem(userId, type, title, now) { ExternalSource = source, ExternalUid = uid };
        item.SetSchedule(date, time, duration, recurrence);
        return item;
    }

    public bool SyncFromSource(
        ItemType type, string title, DateOnly date, TimeOnly? time, TimeSpan? duration, Recurrence? recurrence, DateTimeOffset now)
    {
        var normalized = NormalizeTitle(title);
        if (Type == type && Title == normalized && Date == date && Time == time && Duration == duration && Equals(Recurrence, recurrence))
            return false;

        if (Title != normalized)
            Embedding = null;
        Type = type;
        Title = normalized;
        SetSchedule(date, time, duration, recurrence);
        Touch(now);
        return true;
    }

    public void Rename(string title, DateTimeOffset now)
    {
        Title = NormalizeTitle(title);
        Embedding = null;
        Touch(now);
    }

    public void Reschedule(DateOnly? date, TimeOnly? time, TimeSpan? duration, DateTimeOffset now)
    {
        SetSchedule(date, time, Type == ItemType.Meeting ? duration ?? Duration : null, AlignRecurrence(date));
        Touch(now);
    }

    public void ChangeType(ItemType type, DateOnly? date, TimeOnly? time, TimeSpan? duration, DateTimeOffset now)
    {
        Type = type;
        SetSchedule(date, time, type == ItemType.Meeting ? duration ?? Duration ?? DefaultMeetingDuration : null, Recurrence);
        Touch(now);
    }

    public void Complete(DateOnly? occurrenceDate, DateTimeOffset now)
    {
        if (!IsRecurring)
        {
            Status = ItemStatus.Done;
        }
        else
        {
            var date = occurrenceDate
                ?? throw new ArgumentNullException(nameof(occurrenceDate), "Для повторяющейся записи нужна дата вхождения.");
            if (!_completedOccurrences.Contains(date))
                _completedOccurrences.Add(date);
        }

        Touch(now);
    }

    public void Reopen(DateOnly? occurrenceDate, DateTimeOffset now)
    {
        if (Status == ItemStatus.Done)
            Status = ItemStatus.Active;
        if (occurrenceDate is { } date)
            _completedOccurrences.Remove(date);
        Touch(now);
    }

    public void Restore(DateTimeOffset now)
    {
        if (Status == ItemStatus.Cancelled)
            Status = ItemStatus.Active;
        Touch(now);
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = ItemStatus.Cancelled;
        Touch(now);
    }

    public void SetEmbedding(float[] embedding) => Embedding = embedding;

    public bool IsDoneOn(DateOnly? date) =>
        Status == ItemStatus.Done || (date is { } day && _completedOccurrences.Contains(day));

    public IEnumerable<Occurrence> OccurrencesBetween(DateOnly from, DateOnly to)
    {
        if (Date is not { } first)
            return [];

        var dates = Recurrence is null
            ? first >= from && first <= to ? [first] : []
            : Recurrence.Dates(first, from, to);

        return dates.Select(date => OccurrenceOn(date));
    }

    public Occurrence NextOccurrence(DateOnly onOrAfter)
    {
        if (Recurrence is null || Date is not { } first)
            return OccurrenceOn(Date);

        var next = Recurrence.Dates(first, onOrAfter, DateOnly.MaxValue).Cast<DateOnly?>().FirstOrDefault();
        return OccurrenceOn(next ?? Date);
    }

    private Occurrence OccurrenceOn(DateOnly? date) => new(this, date, Time, IsDoneOn(date));

    private void SetSchedule(DateOnly? date, TimeOnly? time, TimeSpan? duration, Recurrence? recurrence)
    {
        if (time is not null && date is null)
            throw new ArgumentException("Время без даты не имеет смысла.", nameof(time));
        if (recurrence is not null && date is null)
            throw new ArgumentException("У повторяющейся записи должна быть дата начала.", nameof(recurrence));

        if (Type == ItemType.Meeting)
        {
            if (date is null || time is null)
                throw new ArgumentException("У встречи должны быть дата и время.");
            if (duration is not { } length || length < MinDuration || length > MaxDuration)
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Недопустимая длительность встречи.");
        }
        else if (duration is not null)
        {
            throw new ArgumentException("Длительность есть только у встречи.", nameof(duration));
        }

        Date = date;
        Time = time;
        Duration = duration;
        Recurrence = recurrence;
    }

    private Recurrence? AlignRecurrence(DateOnly? date) =>
        Recurrence is { Frequency: RecurrenceFrequency.Weekly, Days.Count: 1 } weekly && date is { } day && weekly.Days[0] != day.DayOfWeek
            ? new Recurrence(RecurrenceFrequency.Weekly, weekly.Interval, [day.DayOfWeek], weekly.Until)
            : Recurrence;

    private void Touch(DateTimeOffset now) => UpdatedAt = now;

    private static string NormalizeTitle(string title)
    {
        var normalized = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Where(ch => !char.IsControl(ch)).ToArray()))
            .Where(part => part.Length > 0));
        if (normalized.Length == 0)
            throw new ArgumentException("Название не может быть пустым.", nameof(title));
        if (normalized.Length > MaxTitleLength)
            throw new ArgumentException($"Название длиннее {MaxTitleLength} символов.", nameof(title));
        return normalized;
    }
}
