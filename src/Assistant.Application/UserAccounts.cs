using Assistant.Application.Abstractions;
using Assistant.Domain;

namespace Assistant.Application;

public sealed class UserAccounts(IUserRepository users, TimeProvider clock)
{
    public async Task<AppUser> GetOrCreateAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is { } id && await users.FindAsync(id, cancellationToken) is { } existing)
            return existing;

        var user = AppUser.Create(clock.GetUtcNow());
        users.Add(user);
        await users.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task SyncTimeZoneAsync(AppUser user, string? timeZoneId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || timeZoneId == user.TimeZoneId
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            return;
        }

        user.ChangeTimeZone(timeZone);
        await users.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> IssueLinkTokenAsync(AppUser user, CancellationToken cancellationToken)
    {
        var token = user.IssueLinkToken(clock.GetUtcNow());
        await users.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<AppUser?> RedeemLinkTokenAsync(string token, CancellationToken cancellationToken)
    {
        var user = await users.FindByLinkTokenAsync(token, cancellationToken);
        if (user is null)
            return null;

        var isValid = user.RedeemLinkToken(token, clock.GetUtcNow());
        await users.SaveChangesAsync(cancellationToken);
        return isValid ? user : null;
    }

    public Task<AppUser?> FindByCalendarTokenAsync(string token, CancellationToken cancellationToken) =>
        users.FindByCalendarTokenAsync(token, cancellationToken);
}
