using Assistant.Domain;

namespace Assistant.Application;

public sealed class UserClock(TimeProvider clock)
{
    public TimeZoneInfo TimeZoneOf(AppUser user) => TimeZoneInfo.FindSystemTimeZoneById(user.TimeZoneId);

    public DateTime LocalNow(AppUser user) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneOf(user)).DateTime;

    public DateOnly Today(AppUser user) => DateOnly.FromDateTime(LocalNow(user));
}
