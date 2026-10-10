using Assistant.Domain;

namespace Assistant.Application.Abstractions;

public interface IScheduleRepository
{
    Task<ScheduleItem?> FindAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleItem>> ListForPeriodAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleItem>> ListAllAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleItem>> ListFromSourceAsync(Guid userId, string source, CancellationToken cancellationToken);

    void Add(ScheduleItem item);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
