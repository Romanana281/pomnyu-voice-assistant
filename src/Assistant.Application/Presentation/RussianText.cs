using Assistant.Domain;

namespace Assistant.Application.Presentation;

public static class RussianText
{
    private static readonly string[] MonthsGenitive =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    ];

    private static readonly IReadOnlyDictionary<DayOfWeek, string> DaysNominative = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday] = "понедельник",
        [DayOfWeek.Tuesday] = "вторник",
        [DayOfWeek.Wednesday] = "среда",
        [DayOfWeek.Thursday] = "четверг",
        [DayOfWeek.Friday] = "пятница",
        [DayOfWeek.Saturday] = "суббота",
        [DayOfWeek.Sunday] = "воскресенье",
    };

    private static readonly IReadOnlyDictionary<DayOfWeek, string> DaysAccusative = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday] = "в понедельник",
        [DayOfWeek.Tuesday] = "во вторник",
        [DayOfWeek.Wednesday] = "в среду",
        [DayOfWeek.Thursday] = "в четверг",
        [DayOfWeek.Friday] = "в пятницу",
        [DayOfWeek.Saturday] = "в субботу",
        [DayOfWeek.Sunday] = "в воскресенье",
    };

    private static readonly IReadOnlyDictionary<DayOfWeek, string> DaysAccusativeBare = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday] = "понедельник",
        [DayOfWeek.Tuesday] = "вторник",
        [DayOfWeek.Wednesday] = "среду",
        [DayOfWeek.Thursday] = "четверг",
        [DayOfWeek.Friday] = "пятницу",
        [DayOfWeek.Saturday] = "субботу",
        [DayOfWeek.Sunday] = "воскресенье",
    };

    private static readonly IReadOnlyDictionary<DayOfWeek, string> DaysDativePlural = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday] = "понедельникам",
        [DayOfWeek.Tuesday] = "вторникам",
        [DayOfWeek.Wednesday] = "средам",
        [DayOfWeek.Thursday] = "четвергам",
        [DayOfWeek.Friday] = "пятницам",
        [DayOfWeek.Saturday] = "субботам",
        [DayOfWeek.Sunday] = "воскресеньям",
    };

    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static string Plural(int count, string one, string few, string many)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = lastTwo % 10;
        var form = lastTwo is >= 11 and <= 14 ? many
            : last == 1 ? one
            : last is >= 2 and <= 4 ? few
            : many;
        return $"{count} {form}";
    }

    public static string DayName(DayOfWeek day) => DaysNominative[day];

    public static string CalendarDate(DateOnly date, DateOnly today) =>
        date.Year == today.Year
            ? $"{date.Day} {MonthsGenitive[date.Month - 1]}"
            : $"{date.Day} {MonthsGenitive[date.Month - 1]} {date.Year} года";

    public static string When(DateOnly date, DateOnly today)
    {
        var daysAhead = date.DayNumber - today.DayNumber;
        return daysAhead switch
        {
            0 => "сегодня",
            1 => "завтра",
            2 => "послезавтра",
            -1 => "вчера",
            > 2 and < 7 => $"{DaysAccusative[date.DayOfWeek]}, {CalendarDate(date, today)}",
            _ => CalendarDate(date, today),
        };
    }

    public static string WhenAccusative(DateOnly date, DateOnly today) =>
        (date.DayNumber - today.DayNumber) switch
        {
            0 => "сегодня",
            1 => "завтра",
            2 => "послезавтра",
            > 2 and < 7 => $"{DaysAccusativeBare[date.DayOfWeek]}, {CalendarDate(date, today)}",
            _ => CalendarDate(date, today),
        };

    public static string When(DateOnly? date, TimeOnly? time, DateOnly today) =>
        (date, time) switch
        {
            (null, _) => "без срока",
            ({ } day, null) => When(day, today),
            ({ } day, { } clock) => $"{When(day, today)} в {Clock(clock)}",
        };

    public static string DayHeading(DateOnly date, DateOnly today)
    {
        var daysAhead = date.DayNumber - today.DayNumber;
        var heading = daysAhead switch
        {
            0 => "сегодня",
            1 => "завтра",
            -1 => "вчера",
            _ => $"{DaysNominative[date.DayOfWeek]}, {CalendarDate(date, today)}",
        };
        return Capitalize(heading);
    }

    public static string Clock(TimeOnly time) => time.ToString("HH:mm");

    public static string Duration(TimeSpan duration) =>
        duration.TotalMinutes < 60 || duration.TotalMinutes % 60 != 0
            ? Plural((int)duration.TotalMinutes, "минута", "минуты", "минут")
            : Plural((int)duration.TotalHours, "час", "часа", "часов");

    public static string Describe(Recurrence recurrence, DateOnly today)
    {
        var period = recurrence.Frequency switch
        {
            RecurrenceFrequency.Daily => recurrence.Interval == 1
                ? "каждый день"
                : $"каждые {Plural(recurrence.Interval, "день", "дня", "дней")}",
            RecurrenceFrequency.Weekly => DescribeWeekly(recurrence),
            RecurrenceFrequency.Monthly => recurrence.Interval == 1
                ? "каждый месяц"
                : $"каждые {Plural(recurrence.Interval, "месяц", "месяца", "месяцев")}",
            _ => throw new InvalidOperationException($"Неизвестная частота повтора: {recurrence.Frequency}."),
        };

        return recurrence.Until is { } until ? $"{period} до {CalendarDate(until, today)}" : period;
    }

    public static string TypeName(ItemType type) => type switch
    {
        ItemType.Task => "задача",
        ItemType.Reminder => "напоминание",
        ItemType.Meeting => "встреча",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string TypeNameAccusative(ItemType type) => type switch
    {
        ItemType.Task => "задачу",
        ItemType.Reminder => "напоминание",
        ItemType.Meeting => "встречу",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string DescribeWeekly(Recurrence recurrence)
    {
        if (recurrence.Days.Count == 0)
        {
            return recurrence.Interval == 1
                ? "каждую неделю"
                : $"каждые {Plural(recurrence.Interval, "неделю", "недели", "недель")}";
        }

        var days = recurrence.Days.SequenceEqual(Weekdays)
            ? "по будням"
            : $"по {JoinWithAnd(recurrence.Days.Select(day => DaysDativePlural[day]).ToArray())}";

        return recurrence.Interval == 1
            ? days
            : $"каждые {Plural(recurrence.Interval, "неделю", "недели", "недель")} {days}";
    }

    private static string JoinWithAnd(IReadOnlyList<string> parts) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} и {parts[^1]}";
}
