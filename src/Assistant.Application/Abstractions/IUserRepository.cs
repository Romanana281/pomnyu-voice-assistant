using Assistant.Domain;

namespace Assistant.Application.Abstractions;

public interface IUserRepository
{
    Task<AppUser?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task<AppUser?> FindByCalendarTokenAsync(string calendarToken, CancellationToken cancellationToken);

    Task<AppUser?> FindByLinkTokenAsync(string linkToken, CancellationToken cancellationToken);

    void Add(AppUser user);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
