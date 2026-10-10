using System.Globalization;

namespace Assistant.Domain;

public sealed class Recurrence : IEquatable<Recurrence>
{
    public const int MaxInterval = 365;

    private static readonly IReadOnlyDictionary<DayOfWeek, string> DayCodes = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday] = "MO",
        [DayOfWeek.Tuesday] = "TU",
        [DayOfWeek.Wednesday] = "WE",
        [DayOfWeek.Thursday] = "TH",
        [DayOfWeek.Friday] = "FR",
        [DayOfWeek.Saturday] = "SA",
        [DayOfWeek.Sunday] = "SU",
    };

    public Recurrence(
        RecurrenceFrequency frequency,
        int interval = 1,
        IEnumerable<DayOfWeek>? days = null,
        DateOnly? until = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, MaxInterval);

        var distinctDays = (days ?? []).Distinct().OrderBy(MondayBasedIndex).ToArray();
        if (distinctDays.Length > 0 && frequency != RecurrenceFrequency.Weekly)
            throw new ArgumentException("Дни недели допустимы только для еженедельного повтора.", nameof(days));

        Frequency = frequency;
        Interval = interval;
        Days = distinctDays;
        Until = until;
    }

    public RecurrenceFrequency Frequency { get; }
    public int Interval { get; }

    public IReadOnlyList<DayOfWeek> Days { get; }

    public DateOnly? Until { get; }

    public IEnumerable<DateOnly> Dates(DateOnly first, DateOnly from, DateOnly to)
    {
        var last = Until is { } until && until < to ? until : to;
        return SeriesDates(first)
            .TakeWhile(date => date <= last)
            .Where(date => date >= from);
    }

    public string ToRRule()
    {
        var parts = new List<string>
        {
            $"FREQ={Frequency.ToString().ToUpperInvariant()}",
            $"INTERVAL={Interval}",
        };
        if (Days.Count > 0)
            parts.Add($"BYDAY={string.Join(',', Days.Select(day => DayCodes[day]))}");
        if (Until is { } until)
            parts.Add($"UNTIL={until.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
        return string.Join(';', parts);
    }

    public static Recurrence FromRRule(string rrule)
    {
        var fields = rrule
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

        var frequency = Enum.Parse<RecurrenceFrequency>(fields["FREQ"], ignoreCase: true);
        var interval = fields.TryGetValue("INTERVAL", out var intervalText)
            ? int.Parse(intervalText, CultureInfo.InvariantCulture)
            : 1;
        var days = fields.TryGetValue("BYDAY", out var daysText)
            ? daysText.Split(',').Select(code => DayCodes.Single(pair => pair.Value == code).Key)
            : null;
        DateOnly? until = fields.TryGetValue("UNTIL", out var untilText)
            ? DateOnly.ParseExact(untilText[..8], "yyyyMMdd", CultureInfo.InvariantCulture)
            : null;

        return new Recurrence(frequency, interval, days, until);
    }

    public bool Equals(Recurrence? other) => other is not null && ToRRule() == other.ToRRule();

    public override bool Equals(object? obj) => Equals(obj as Recurrence);

    public override int GetHashCode() => ToRRule().GetHashCode(StringComparison.Ordinal);

    public override string ToString() => ToRRule();

    private IEnumerable<DateOnly> SeriesDates(DateOnly first) => Frequency switch
    {
        RecurrenceFrequency.Daily => Infinite().Select(step => first.AddDays(step * Interval)),
        RecurrenceFrequency.Weekly => WeeklyDates(first),
        RecurrenceFrequency.Monthly => Infinite().Select(step => first.AddMonths(step * Interval)),
        _ => throw new InvalidOperationException($"Неизвестная частота повтора: {Frequency}."),
    };

    private IEnumerable<DateOnly> WeeklyDates(DateOnly first)
    {
        var days = Days.Count > 0 ? Days : [first.DayOfWeek];
        var firstWeekMonday = first.AddDays(-MondayBasedIndex(first.DayOfWeek));

        return Infinite()
            .Select(step => firstWeekMonday.AddDays(step * Interval * 7))
            .SelectMany(monday => days.Select(day => monday.AddDays(MondayBasedIndex(day))))
            .Where(date => date >= first);
    }

    private static IEnumerable<int> Infinite()
    {
        for (var step = 0; ; step++)
            yield return step;
    }

    private static int MondayBasedIndex(DayOfWeek day) => ((int)day + 6) % 7;
}
