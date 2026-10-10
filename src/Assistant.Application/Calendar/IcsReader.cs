using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Assistant.Domain;

namespace Assistant.Application.Calendar;

public static class IcsReader
{
    public const int MaxEvents = 2000;
    private const string OwnUidSuffix = "@voice-assistant";
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);
    private static readonly int PastDaysToKeep = 60;
    private static readonly int FutureYearsToKeep = 2;

    private static readonly IReadOnlyDictionary<string, DayOfWeek> DayCodes = new Dictionary<string, DayOfWeek>
    {
        ["MO"] = DayOfWeek.Monday,
        ["TU"] = DayOfWeek.Tuesday,
        ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday,
        ["FR"] = DayOfWeek.Friday,
        ["SA"] = DayOfWeek.Saturday,
        ["SU"] = DayOfWeek.Sunday,
    };

    public static IReadOnlyList<CalendarEvent> Read(string content, TimeZoneInfo userTimeZone, DateOnly today)
    {
        if (!content.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Это не файл календаря (iCalendar).");

        var events = new List<CalendarEvent>();
        Dictionary<string, Property>? current = null;
        var depth = 0;

        foreach (var line in Unfold(content))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new Dictionary<string, Property>(StringComparer.OrdinalIgnoreCase);
                depth = 0;
                continue;
            }

            if (current is null)
                continue;

            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
                continue;
            }

            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                if (ToEvent(current, userTimeZone, today) is { } calendarEvent)
                    events.Add(calendarEvent);
                current = null;
                if (events.Count >= MaxEvents)
                    break;
                continue;
            }

            if (depth == 0 && Property.TryParse(line, out var property))
                current.TryAdd(property.Name, property);
        }

        return events
            .GroupBy(calendarEvent => calendarEvent.Uid)
            .Select(group => group.First())
            .ToArray();
    }

    private static CalendarEvent? ToEvent(Dictionary<string, Property> properties, TimeZoneInfo userTimeZone, DateOnly today)
    {
        if (properties.ContainsKey("RECURRENCE-ID"))
            return null;
        if (properties.TryGetValue("STATUS", out var status) && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!properties.TryGetValue("DTSTART", out var startProperty) || ParseMoment(startProperty, userTimeZone) is not { } start)
            return null;

        var title = properties.TryGetValue("SUMMARY", out var summary) ? Unescape(summary.Value) : "";
        title = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.Length == 0)
            title = "Без названия";
        if (title.Length > ScheduleItem.MaxTitleLength)
            title = title[..ScheduleItem.MaxTitleLength];

        var uid = properties.TryGetValue("UID", out var uidProperty) && uidProperty.Value.Length > 0
            ? uidProperty.Value
            : StableId($"{title}|{startProperty.Value}");
        if (uid.EndsWith(OwnUidSuffix, StringComparison.OrdinalIgnoreCase))
            return null;
        if (uid.Length > ScheduleItem.MaxExternalUidLength)
            uid = StableId(uid);

        var recurrence = properties.TryGetValue("RRULE", out var rrule) ? ParseRule(rrule.Value, start.Date, userTimeZone) : null;
        var lastRelevant = recurrence?.Until ?? (recurrence is null ? start.Date : DateOnly.MaxValue);
        if (lastRelevant < today.AddDays(-PastDaysToKeep) || start.Date > today.AddYears(FutureYearsToKeep))
            return null;

        TimeSpan? duration = null;
        if (start.Time is not null)
        {
            var length = ParseDuration(properties, startProperty, start, userTimeZone) ?? DefaultDuration;
            duration = length < ScheduleItem.MinDuration || length > ScheduleItem.MaxDuration ? DefaultDuration : length;
        }

        return new CalendarEvent(uid, title, start.Date, start.Time, duration, recurrence);
    }

    private static TimeSpan? ParseDuration(
        Dictionary<string, Property> properties, Property startProperty, Moment start, TimeZoneInfo userTimeZone)
    {
        if (properties.TryGetValue("DTEND", out var endProperty) && ParseMoment(endProperty, userTimeZone) is { Time: not null } end)
            return end.Date.ToDateTime(end.Time!.Value) - start.Date.ToDateTime(start.Time!.Value);

        if (properties.TryGetValue("DURATION", out var durationProperty))
        {
            try
            {
                return System.Xml.XmlConvert.ToTimeSpan(durationProperty.Value.Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static Moment? ParseMoment(Property property, TimeZoneInfo userTimeZone)
    {
        var value = property.Value.Trim();
        if (value.Length == 8 || property.Parameter("VALUE")?.Equals("DATE", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DateOnly.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? new Moment(day, null)
                : null;
        }

        var isUtc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        var source = isUtc ? TimeZoneInfo.Utc : FindTimeZone(property.Parameter("TZID")) ?? userTimeZone;
        var converted = source.Id == userTimeZone.Id
            ? local
            : TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), source, userTimeZone);
        return new Moment(DateOnly.FromDateTime(converted), new TimeOnly(converted.Hour, converted.Minute));
    }

    private static Recurrence? ParseRule(string rule, DateOnly first, TimeZoneInfo userTimeZone)
    {
        var fields = rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0].ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.First()[1]);

        RecurrenceFrequency? frequency = fields.GetValueOrDefault("FREQ")?.ToUpperInvariant() switch
        {
            "DAILY" => RecurrenceFrequency.Daily,
            "WEEKLY" => RecurrenceFrequency.Weekly,
            "MONTHLY" => RecurrenceFrequency.Monthly,
            _ => null,
        };
        if (frequency is not { } validFrequency)
            return null;

        var interval = int.TryParse(fields.GetValueOrDefault("INTERVAL"), CultureInfo.InvariantCulture, out var parsedInterval)
            ? Math.Clamp(parsedInterval, 1, Recurrence.MaxInterval)
            : 1;
        var days = validFrequency == RecurrenceFrequency.Weekly && fields.TryGetValue("BYDAY", out var byDay)
            ? byDay.Split(',').Select(code => code.Trim().ToUpperInvariant()).Where(DayCodes.ContainsKey).Select(code => DayCodes[code]).ToArray()
            : [];

        DateOnly? until = null;
        if (fields.TryGetValue("UNTIL", out var untilText))
            until = ParseMoment(new Property("UNTIL", untilText, new Dictionary<string, string>()), userTimeZone)?.Date;

        var recurrence = new Recurrence(validFrequency, interval, days, until);
        if (until is null && int.TryParse(fields.GetValueOrDefault("COUNT"), CultureInfo.InvariantCulture, out var count) && count > 0)
        {
            var last = recurrence.Dates(first, first, first.AddYears(10)).Take(count).LastOrDefault(first);
            recurrence = new Recurrence(validFrequency, interval, days, last);
        }

        return recurrence;
    }

    private static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var cleaned = id.Trim('"');
        if (TimeZoneInfo.TryFindSystemTimeZoneById(cleaned, out var timeZone))
            return timeZone;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(cleaned, out var iana) && TimeZoneInfo.TryFindSystemTimeZoneById(iana, out var converted)
            ? converted
            : null;
    }

    private static IEnumerable<string> Unfold(string content)
    {
        var builder = new StringBuilder();
        foreach (var raw in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                builder.Append(raw, 1, raw.Length - 1);
                continue;
            }

            if (builder.Length > 0)
                yield return builder.ToString();
            builder.Clear().Append(raw);
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }

    private static string Unescape(string value) => value
        .Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",", StringComparison.Ordinal)
        .Replace("\\;", ";", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string StableId(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    private sealed record Moment(DateOnly Date, TimeOnly? Time);

    private sealed record Property(string Name, string Value, IReadOnlyDictionary<string, string> Parameters)
    {
        public string? Parameter(string name) => Parameters.GetValueOrDefault(name);

        public static bool TryParse(string line, out Property property)
        {
            property = null!;
            var colon = IndexOfValueSeparator(line);
            if (colon <= 0)
                return false;

            var head = line[..colon].Split(';');
            var parameters = head.Skip(1)
                .Select(part => part.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .GroupBy(pair => pair[0].ToUpperInvariant())
                .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.OrdinalIgnoreCase);
            property = new Property(head[0].ToUpperInvariant(), line[(colon + 1)..], parameters);
            return true;
        }

        private static int IndexOfValueSeparator(string line)
        {
            var quoted = false;
            for (var index = 0; index < line.Length; index++)
            {
                if (line[index] == '"')
                    quoted = !quoted;
                else if (line[index] == ':' && !quoted)
                    return index;
            }

            return -1;
        }
    }
}
