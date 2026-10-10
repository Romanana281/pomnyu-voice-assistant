using Assistant.Domain;

namespace Assistant.Application.Abstractions;

public interface ICalendarSubscriptionRepository
{
    Task<IReadOnlyList<CalendarSubscription>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarSubscription>> ListAllAsync(CancellationToken cancellationToken);

    Task<CalendarSubscription?> FindAsync(Guid userId, Guid subscriptionId, CancellationToken cancellationToken);

    void Add(CalendarSubscription subscription);

    void Remove(CalendarSubscription subscription);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
