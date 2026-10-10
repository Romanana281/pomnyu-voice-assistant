using System.Security.Cryptography;

namespace Assistant.Domain;

public sealed class AppUser
{
    public const string DefaultTimeZoneId = "Europe/Moscow";
    public static readonly TimeSpan LinkTokenLifetime = TimeSpan.FromMinutes(10);

    private AppUser()
    {
        TimeZoneId = DefaultTimeZoneId;
        CalendarToken = string.Empty;
    }

    private AppUser(DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TimeZoneId = DefaultTimeZoneId;
        CalendarToken = NewToken();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public string TimeZoneId { get; private set; }
    public string CalendarToken { get; private set; }
    public string? LinkToken { get; private set; }
    public DateTimeOffset? LinkTokenExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AppUser Create(DateTimeOffset now) => new(now);

    public void ChangeTimeZone(TimeZoneInfo timeZone) => TimeZoneId = timeZone.Id;

    public string IssueLinkToken(DateTimeOffset now)
    {
        LinkToken = NewToken();
        LinkTokenExpiresAt = now + LinkTokenLifetime;
        return LinkToken;
    }

    public bool RedeemLinkToken(string token, DateTimeOffset now)
    {
        var isValid = LinkToken is not null
            && LinkTokenExpiresAt > now
            && CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(LinkToken),
                System.Text.Encoding.UTF8.GetBytes(token));

        LinkToken = null;
        LinkTokenExpiresAt = null;
        return isValid;
    }

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
}
