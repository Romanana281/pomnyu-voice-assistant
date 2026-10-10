using Assistant.Application.Abstractions;
using Assistant.Domain;
using Microsoft.Extensions.Logging;

namespace Assistant.Application.Scheduling;

public sealed class ScheduleService(
    IScheduleRepository repository,
    IEmbeddingProvider embeddings,
    TimeProvider clock,
    ILogger<ScheduleService> logger)
{
    private const int ConflictHorizonDays = 60;
    private static readonly TimeOnly DayEnd = new(21, 0);
    private static readonly TimeOnly[] PreferredStarts =
        [new(10, 0), new(12, 0), new(14, 0), new(16, 0), new(11, 0), new(15, 0), new(17, 0), new(9, 0), new(13, 0), new(18, 0), new(19, 0)];

    public async Task AddAsync(ScheduleItem item, CancellationToken cancellationToken)
    {
        repository.Add(item);
        await TryEmbedAsync([item], cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public Task<ScheduleItem?> FindAsync(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
        repository.FindAsync(userId, itemId, cancellationToken);

    public Task<IReadOnlyList<ScheduleItem>> ListAllAsync(Guid userId, CancellationToken cancellationToken) =>
        repository.ListAllAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<Occurrence>> ListOccurrencesAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        IReadOnlySet<ItemType> types,
        CancellationToken cancellationToken)
    {
        var items = await repository.ListForPeriodAsync(userId, from, to, cancellationToken);
        return items
            .Where(item => types.Contains(item.Type))
            .SelectMany(item => item.OccurrencesBetween(from, to))
            .OrderBy(occurrence => occurrence.Date)
            .ThenBy(occurrence => occurrence.Time ?? TimeOnly.MaxValue)
            .ThenBy(occurrence => occurrence.Item.CreatedAt)
            .ToArray();
    }

    public async Task<Agenda> GetAgendaAsync(
        Guid userId, DateOnly today, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var items = await repository.ListAllAsync(userId, cancellationToken);

        var overdue = from > today
            ? []
            : items
                .Where(item => item.Type != ItemType.Meeting && !item.IsRecurring)
                .Where(item => item.Status == ItemStatus.Active && item.Date < today)
                .OrderBy(item => item.Date)
                .Select(item => item.NextOccurrence(today))
                .ToArray();

        var upcoming = items
            .SelectMany(item => item.OccurrencesBetween(from, to))
            .OrderBy(occurrence => occurrence.Date)
            .ThenBy(occurrence => occurrence.Time ?? TimeOnly.MaxValue)
            .ThenBy(occurrence => occurrence.Item.CreatedAt)
            .ToArray();

        var undated = from > today
            ? []
            : items
                .Where(item => item.Date is null && item.Status == ItemStatus.Active)
                .OrderBy(item => item.CreatedAt)
                .Select(item => item.NextOccurrence(today))
                .ToArray();

        return new Agenda(overdue, upcoming, undated);
    }

    public async Task<IReadOnlyList<Occurrence>> FindConflictsAsync(
        ScheduleItem meeting, CancellationToken cancellationToken)
    {
        if (meeting.Type != ItemType.Meeting || meeting.Date is not { } first)
            return [];

        var last = meeting.IsRecurring ? first.AddDays(ConflictHorizonDays) : first;
        var candidates = meeting.OccurrencesBetween(first, last).ToArray();
        var others = await ListOccurrencesAsync(
            meeting.UserId, first, last, new HashSet<ItemType> { ItemType.Meeting }, cancellationToken);

        return others
            .Where(other => other.Item.Id != meeting.Id && !other.IsDone)
            .Where(other => candidates.Any(candidate => candidate.OverlapsWith(other)))
            .ToArray();
    }

    public async Task<IReadOnlyList<TimeOnly>> FindFreeSlotsAsync(
        Guid userId, DateOnly date, TimeSpan duration, DateTime localNow, int count, CancellationToken cancellationToken)
    {
        var busy = await BusyIntervalsAsync(userId, date, excludeId: null, cancellationToken);
        var slots = new SortedSet<TimeOnly>();
        foreach (var preferred in PreferredStarts)
        {
            if (FirstFreeStart(date.ToDateTime(preferred), duration, busy) is { } start
                && start > localNow
                && TimeOnly.FromDateTime(start + duration) <= DayEnd
                && DateOnly.FromDateTime(start) == date)
            {
                slots.Add(TimeOnly.FromDateTime(start));
            }

            if (slots.Count == count)
                break;
        }

        return slots.ToArray();
    }

    public async Task<TimeOnly?> SuggestFreeStartAsync(ScheduleItem meeting, CancellationToken cancellationToken)
    {
        if (meeting.IsRecurring || meeting.Date is not { } date || meeting.Time is not { } time || meeting.Duration is not { } duration)
            return null;

        var busy = await BusyIntervalsAsync(meeting.UserId, date, meeting.Id, cancellationToken);
        return FirstFreeStart(date.ToDateTime(time), duration, busy) is { } start
               && DateOnly.FromDateTime(start) == date
               && TimeOnly.FromDateTime(start + duration) <= DayEnd
            ? TimeOnly.FromDateTime(start)
            : null;
    }

    public async Task RestoreAsync(ScheduleItem item, CancellationToken cancellationToken)
    {
        item.Restore(clock.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(ScheduleItem item, DateOnly? occurrenceDate, CancellationToken cancellationToken)
    {
        item.Complete(occurrenceDate, clock.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task ReopenAsync(ScheduleItem item, DateOnly? occurrenceDate, CancellationToken cancellationToken)
    {
        item.Reopen(occurrenceDate, clock.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(ScheduleItem item, CancellationToken cancellationToken)
    {
        item.Cancel(clock.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        ScheduleItem item,
        string? newTitle,
        DateOnly? date,
        TimeOnly? time,
        TimeSpan? duration,
        CancellationToken cancellationToken,
        ItemType? newType = null)
    {
        var now = clock.GetUtcNow();
        if (newTitle is not null)
            item.Rename(newTitle, now);
        if (newType is { } type && type != item.Type)
            item.ChangeType(type, date, time, duration, now);
        else
            item.Reschedule(date, time, duration, now);

        if (newTitle is not null)
            await TryEmbedAsync([item], cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<(DateTime Start, DateTime End)>> BusyIntervalsAsync(
        Guid userId, DateOnly date, Guid? excludeId, CancellationToken cancellationToken)
    {
        var meetings = await ListOccurrencesAsync(
            userId, date, date, new HashSet<ItemType> { ItemType.Meeting }, cancellationToken);
        return meetings
            .Where(occurrence => occurrence.Item.Id != excludeId && !occurrence.IsDone)
            .Where(occurrence => occurrence.Start is not null && occurrence.End is not null)
            .Select(occurrence => (occurrence.Start!.Value, occurrence.End!.Value))
            .ToArray();
    }

    private static DateTime? FirstFreeStart(DateTime start, TimeSpan duration, IReadOnlyList<(DateTime Start, DateTime End)> busy)
    {
        var candidate = start;
        for (var attempt = 0; attempt <= busy.Count; attempt++)
        {
            var blocking = busy.Where(interval => interval.Start < candidate + duration && candidate < interval.End).ToArray();
            if (blocking.Length == 0)
                return candidate;
            candidate = blocking.Max(interval => interval.End);
        }

        return null;
    }

    public async Task TryEmbedAsync(IReadOnlyList<ScheduleItem> items, CancellationToken cancellationToken)
    {
        var missing = items.Where(item => item.Embedding is null).ToArray();
        if (missing.Length == 0 || !embeddings.IsAvailable)
            return;

        try
        {
            var vectors = await embeddings.EmbedAsync(missing.Select(item => item.Title).ToArray(), cancellationToken);
            foreach (var (item, vector) in missing.Zip(vectors))
                item.SetEmbedding(vector);
        }
        catch (LlmUnavailableException exception)
        {
            logger.LogWarning("Эмбеддинги для {Count} записей не посчитаны, поиск будет по словам: {Reason}", missing.Length, exception.Message);
        }
    }
}
