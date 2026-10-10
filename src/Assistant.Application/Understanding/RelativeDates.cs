namespace Assistant.Application.Understanding;

internal static class RelativeDates
{
    private static readonly IReadOnlyDictionary<string, DayOfWeek> Weekdays = new Dictionary<string, DayOfWeek>
    {
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday,
    };

    public static bool IsWeekday(string keyword) => Weekdays.ContainsKey(keyword);

    public static DateOnly? Day(string keyword, DateOnly today)
    {
        if (Weekdays.TryGetValue(keyword, out var weekday))
            return Nearest(weekday, today);
        if (keyword.StartsWith("next_", StringComparison.Ordinal) && Weekdays.TryGetValue(keyword[5..], out var nextWeekday))
            return StartOfWeek(today).AddDays(7 + MondayBased(nextWeekday));

        return keyword switch
        {
            "today" => today,
            "tomorrow" => today.AddDays(1),
            "day_after_tomorrow" => today.AddDays(2),
            "end_of_week" => StartOfWeek(today).AddDays(6),
            "next_week" => StartOfWeek(today).AddDays(7),
            "end_of_month" => new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            "next_month" => new DateOnly(today.Year, today.Month, 1).AddMonths(1),
            _ => null,
        };
    }

    public static (DateOnly From, DateOnly To)? Period(string keyword, DateOnly today)
    {
        var monday = StartOfWeek(today);
        return keyword switch
        {
            "today" => (today, today),
            "tomorrow" => (today.AddDays(1), today.AddDays(1)),
            "day_after_tomorrow" => (today.AddDays(2), today.AddDays(2)),
            "this_week" => (today, monday.AddDays(6)),
            "next_week" => (monday.AddDays(7), monday.AddDays(13)),
            "weekend" => (Max(today, monday.AddDays(5)), monday.AddDays(6)),
            "this_month" => (today, new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
            "next_month" => MonthOf(new DateOnly(today.Year, today.Month, 1).AddMonths(1)),
            _ => Day(keyword, today) is { } day ? (day, day) : null,
        };
    }

    private static (DateOnly, DateOnly) MonthOf(DateOnly first) =>
        (first, new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)));

    private static DateOnly Nearest(DayOfWeek weekday, DateOnly today) =>
        today.AddDays(((int)weekday - (int)today.DayOfWeek + 7) % 7);

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-MondayBased(date.DayOfWeek));

    private static int MondayBased(DayOfWeek day) => ((int)day + 6) % 7;

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
}
