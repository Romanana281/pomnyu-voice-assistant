using System.Globalization;
using System.Text;
using Assistant.Domain;

namespace Assistant.Web.Calendar;

public static class IcsCalendarWriter
{
    public const string ContentType = "text/calendar; charset=utf-8";

    private const int MaxLineOctets = 75;
    private static readonly TimeSpan TimedTaskLength = TimeSpan.FromMinutes(30);

    public static string Write(IEnumerable<ScheduleItem> items, string timeZoneId, string calendarName)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Voice Assistant//RU",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            $"X-WR-CALNAME:{Escape(calendarName)}",
            $"X-WR-TIMEZONE:{timeZoneId}",
        };

        foreach (var item in items.Where(item => item.Date is not null))
            lines.AddRange(Event(item, timeZoneId));

        lines.Add("END:VCALENDAR");
        return string.Concat(lines.Select(line => Fold(line) + "\r\n"));
    }

    private static IEnumerable<string> Event(ScheduleItem item, string timeZoneId)
    {
        var date = item.Date!.Value;
        var title = item.Status == ItemStatus.Done ? $"✓ {item.Title}" : item.Title;

        yield return "BEGIN:VEVENT";
        yield return $"UID:{item.Id}@voice-assistant";
        yield return $"DTSTAMP:{Utc(item.UpdatedAt)}";
        yield return $"SUMMARY:{Escape(title)}";
        yield return $"CATEGORIES:{CategoryOf(item.Type)}";

        if (item.Time is { } time)
        {
            var start = date.ToDateTime(time);
            var end = start + (item.Duration ?? TimedTaskLength);
            yield return $"DTSTART;TZID={timeZoneId}:{Local(start)}";
            yield return $"DTEND;TZID={timeZoneId}:{Local(end)}";
        }
        else
        {
            yield return $"DTSTART;VALUE=DATE:{Day(date)}";
            yield return $"DTEND;VALUE=DATE:{Day(date.AddDays(1))}";
        }

        if (item.Recurrence is { } recurrence)
            yield return $"RRULE:{RecurrenceRule(recurrence, isTimed: item.Time is not null)}";

        foreach (var completed in item.CompletedOccurrences)
            yield return item.Time is { } clock
                ? $"EXDATE;TZID={timeZoneId}:{Local(completed.ToDateTime(clock))}"
                : $"EXDATE;VALUE=DATE:{Day(completed)}";

        if (item.Type == ItemType.Reminder && item.Time is not null)
        {
            yield return "BEGIN:VALARM";
            yield return "ACTION:DISPLAY";
            yield return $"DESCRIPTION:{Escape(item.Title)}";
            yield return "TRIGGER:PT0M";
            yield return "END:VALARM";
        }

        yield return "END:VEVENT";
    }

    private static string RecurrenceRule(Recurrence recurrence, bool isTimed)
    {
        var rule = recurrence.ToRRule();
        return isTimed && recurrence.Until is { } until
            ? rule.Replace($"UNTIL={Day(until)}", $"UNTIL={Day(until)}T235959Z", StringComparison.Ordinal)
            : rule;
    }

    private static string CategoryOf(ItemType type) => type switch
    {
        ItemType.Meeting => "Встреча",
        ItemType.Task => "Задача",
        ItemType.Reminder => "Напоминание",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static string Day(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Local(DateTime moment) => moment.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxLineOctets)
            return line;

        var folded = new StringBuilder();
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                folded.Append("\r\n ");
                octets = 1;
            }

            folded.Append(rune.ToString());
            octets += size;
        }

        return folded.ToString();
    }
}
