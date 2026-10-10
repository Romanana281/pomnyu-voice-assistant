using Assistant.Application.Abstractions;
using Assistant.Domain;
using Microsoft.Extensions.Logging;

namespace Assistant.Application.Calendar;

public sealed record ImportSummary(int Created, int Updated, int Removed, int Total);

public sealed class CalendarImportService(
    IScheduleRepository items,
    ICalendarSubscriptionRepository subscriptions,
    IUserRepository users,
    ICalendarFetcher fetcher,
    UserClock userClock,
    TimeProvider clock,
    ILogger<CalendarImportService> logger)
{
    public const string FileSource = "file";
    public const int MaxSubscriptions = 10;

    public async Task<ImportSummary> ImportFileAsync(AppUser user, string content, CancellationToken cancellationToken)
    {
        var events = Read(content, user);
        return await ApplyAsync(user.Id, FileSource, events, removeMissing: false, cancellationToken);
    }

    public Task<IReadOnlyList<CalendarSubscription>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        subscriptions.ListAsync(userId, cancellationToken);

    public async Task<(CalendarSubscription Subscription, ImportSummary Summary)> SubscribeAsync(
        AppUser user, Uri url, CancellationToken cancellationToken)
    {
        var existing = await subscriptions.ListAsync(user.Id, cancellationToken);
        if (existing.FirstOrDefault(subscription => subscription.Url == url.AbsoluteUri) is { } duplicate)
            return (duplicate, await SyncAsync(user, duplicate, cancellationToken));
        if (existing.Count >= MaxSubscriptions)
            throw new CalendarFetchException($"Можно подключить не больше {MaxSubscriptions} календарей.");

        var content = await fetcher.FetchAsync(url, cancellationToken);
        var events = Read(content, user);
        var subscription = CalendarSubscription.Create(user.Id, url, NameOf(content) ?? url.Host, clock.GetUtcNow());
        subscriptions.Add(subscription);
        var summary = await ApplyAsync(user.Id, subscription.SourceKey, events, removeMissing: true, cancellationToken);
        subscription.RecordSuccess(clock.GetUtcNow());
        await subscriptions.SaveChangesAsync(cancellationToken);
        return (subscription, summary);
    }

    public async Task<ImportSummary> SyncAsync(AppUser user, CalendarSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            var content = await fetcher.FetchAsync(new Uri(subscription.Url), cancellationToken);
            var summary = await ApplyAsync(user.Id, subscription.SourceKey, Read(content, user), removeMissing: true, cancellationToken);
            if (NameOf(content) is { } name)
                subscription.Rename(name);
            subscription.RecordSuccess(clock.GetUtcNow());
            await subscriptions.SaveChangesAsync(cancellationToken);
            return summary;
        }
        catch (Exception exception) when (exception is CalendarFetchException or FormatException)
        {
            subscription.RecordFailure(exception.Message, clock.GetUtcNow());
            await subscriptions.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in await subscriptions.ListAllAsync(cancellationToken))
        {
            if (await users.FindAsync(subscription.UserId, cancellationToken) is not { } user)
                continue;
            try
            {
                await SyncAsync(user, subscription, cancellationToken);
            }
            catch (Exception exception) when (exception is CalendarFetchException or FormatException)
            {
                logger.LogWarning("Календарь {Subscription} не синхронизирован: {Reason}", subscription.Id, exception.Message);
            }
        }
    }

    public async Task UnsubscribeAsync(CalendarSubscription subscription, bool removeItems, CancellationToken cancellationToken)
    {
        if (removeItems)
        {
            var now = clock.GetUtcNow();
            foreach (var item in await items.ListFromSourceAsync(subscription.UserId, subscription.SourceKey, cancellationToken))
                item.Cancel(now);
        }

        subscriptions.Remove(subscription);
        await subscriptions.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyList<CalendarEvent> Read(string content, AppUser user) =>
        IcsReader.Read(content, userClock.TimeZoneOf(user), userClock.Today(user));

    private async Task<ImportSummary> ApplyAsync(
        Guid userId, string source, IReadOnlyList<CalendarEvent> events, bool removeMissing, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var known = (await items.ListFromSourceAsync(userId, source, cancellationToken))
            .Where(item => item.ExternalUid is not null)
            .GroupBy(item => item.ExternalUid!)
            .ToDictionary(group => group.Key, group => group.First());

        int created = 0, updated = 0, removed = 0;
        foreach (var calendarEvent in events)
        {
            var type = calendarEvent.Time is null ? ItemType.Task : ItemType.Meeting;
            if (known.Remove(calendarEvent.Uid, out var item))
            {
                if (item.Status != ItemStatus.Cancelled
                    && item.SyncFromSource(type, calendarEvent.Title, calendarEvent.Date, calendarEvent.Time, calendarEvent.Duration, calendarEvent.Recurrence, now))
                {
                    updated++;
                }

                continue;
            }

            items.Add(ScheduleItem.Import(
                userId, source, calendarEvent.Uid, type, calendarEvent.Title,
                calendarEvent.Date, calendarEvent.Time, calendarEvent.Duration, calendarEvent.Recurrence, now));
            created++;
        }

        if (removeMissing)
        {
            foreach (var stale in known.Values.Where(item => item.Status != ItemStatus.Cancelled))
            {
                stale.Cancel(now);
                removed++;
            }
        }

        await items.SaveChangesAsync(cancellationToken);
        return new ImportSummary(created, updated, removed, events.Count);
    }

    private static string? NameOf(string content)
    {
        var line = content.Split('\n').FirstOrDefault(row => row.StartsWith("X-WR-CALNAME:", StringComparison.OrdinalIgnoreCase));
        var name = line?["X-WR-CALNAME:".Length..].Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
