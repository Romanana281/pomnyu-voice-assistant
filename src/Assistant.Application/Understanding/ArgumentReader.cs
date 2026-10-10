using System.Globalization;
using System.Text.Json;
using Assistant.Application.Commands;
using Assistant.Application.Dialog;
using Assistant.Domain;

namespace Assistant.Application.Understanding;

internal sealed class ArgumentReader(JsonElement arguments, DateTime now)
{
    private const int MaxYearsFromToday = 10;

    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];

    private static readonly IReadOnlyDictionary<string, ItemType> TypeNames = new Dictionary<string, ItemType>
    {
        ["task"] = ItemType.Task,
        ["reminder"] = ItemType.Reminder,
        ["meeting"] = ItemType.Meeting,
    };

    private static readonly IReadOnlyDictionary<string, DayOfWeek> DayNames = new Dictionary<string, DayOfWeek>
    {
        ["mo"] = DayOfWeek.Monday,
        ["tu"] = DayOfWeek.Tuesday,
        ["we"] = DayOfWeek.Wednesday,
        ["th"] = DayOfWeek.Thursday,
        ["fr"] = DayOfWeek.Friday,
        ["sa"] = DayOfWeek.Saturday,
        ["su"] = DayOfWeek.Sunday,
    };

    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _weekdayFields = [];
    private readonly DateOnly today = DateOnly.FromDateTime(now);

    public DateTime Now => now;

    public IReadOnlyList<string> Errors => _errors;

    public IReadOnlyList<string> Warnings => _warnings;

    public string? Text(string name, int maxLength, bool required = false)
    {
        var value = Raw(name) is { ValueKind: JsonValueKind.String } element
            ? new string(element.GetString()!.Where(ch => !char.IsControl(ch)).ToArray()).Trim()
            : null;
        if (string.IsNullOrEmpty(value))
        {
            if (required)
                _errors.Add($"{name}: обязательное поле");
            return null;
        }

        if (value.Length > maxLength)
        {
            _errors.Add($"{name}: длиннее {maxLength} символов");
            return null;
        }

        return value;
    }

    public string? Title(string name, bool required = false) =>
        Text(name, ScheduleItem.MaxTitleLength, required) is { Length: > 0 } title
            ? char.ToUpper(title[0], System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + title[1..]
            : null;

    public DateOnly? Date(string name, bool required = false)
    {
        var text = Text(name, maxLength: 32, required);
        if (text is null)
            return null;

        var keyword = text.ToLowerInvariant();
        if (RelativeDates.Day(keyword, today) is { } relative)
        {
            if (RelativeDates.IsWeekday(keyword))
                _weekdayFields.Add(name);
            return relative;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Report(name, $"«{text}» — не дата в формате ГГГГ-ММ-ДД", required);
            return null;
        }

        if (Math.Abs(date.Year - today.Year) > MaxYearsFromToday)
        {
            _errors.Add($"{name}: {text} — слишком далеко от сегодняшнего дня");
            return null;
        }

        return date;
    }

    public bool IsWeekday(string name) => _weekdayFields.Contains(name);

    public (DateOnly From, DateOnly To)? Period(string name)
    {
        var text = Text(name, maxLength: 32)?.ToLowerInvariant();
        if (text is null)
            return null;
        return RelativeDates.Period(text, today) ?? Invalid<(DateOnly, DateOnly)?>(name, $"неизвестный период «{text}»");
    }

    public TimeOnly? Time(string name)
    {
        var text = Text(name, maxLength: 16);
        if (text is null)
            return null;

        if (TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;

        _warnings.Add($"{name}: «{text}» — не время в формате ЧЧ:ММ");
        return null;
    }

    public int? Integer(string name, int min, int max)
    {
        var element = Raw(name);
        int? value = element switch
        {
            { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var parsed) => parsed,
            { ValueKind: JsonValueKind.String } text when int.TryParse(text.GetString(), CultureInfo.InvariantCulture, out var parsed) => parsed,
            null or { ValueKind: JsonValueKind.Null } => null,
            _ => Invalid<int?>(name, "не целое число"),
        };

        if (value is { } actual && (actual < min || actual > max))
            return Invalid<int?>(name, $"должно быть от {min} до {max}");

        return value;
    }

    public ConfirmChoice? Choice(string name) => Text(name, maxLength: 16, required: true)?.ToLowerInvariant() switch
    {
        null => null,
        "accept" => ConfirmChoice.Accept,
        "keep" => ConfirmChoice.Alternative,
        "reject" => ConfirmChoice.Reject,
        var other => Invalid<ConfirmChoice?>(name, $"неизвестный ответ «{other}»"),
    };

    public ItemType Kind(string name, ItemType fallback)
    {
        var text = Text(name, maxLength: 16);
        if (text is null)
            return fallback;
        return TypeNames.TryGetValue(text.ToLowerInvariant(), out var type)
            ? type
            : Invalid<ItemType>(name, $"неизвестный тип «{text}»");
    }

    public IReadOnlySet<ItemType> Types(string name)
    {
        var names = Strings(name);
        if (names.Count == 0)
            return Enum.GetValues<ItemType>().ToHashSet();

        var unknown = names.Where(typeName => !TypeNames.ContainsKey(typeName)).ToArray();
        if (unknown.Length > 0)
            _errors.Add($"{name}: неизвестные типы {string.Join(", ", unknown)}");

        return names.Where(TypeNames.ContainsKey).Select(typeName => TypeNames[typeName]).ToHashSet();
    }

    public TimeScope Scope(string name) => Text(name, maxLength: 16)?.ToLowerInvariant() switch
    {
        null or "any" => TimeScope.Any,
        "past" => TimeScope.Past,
        "future" => TimeScope.Future,
        var other => Invalid<TimeScope>(name, $"неизвестное значение «{other}»"),
    };

    public ItemRef? Target(string name)
    {
        var text = Text(name, ScheduleItem.MaxTitleLength, required: true);
        if (text is null)
            return null;

        if (text.Equals("last", StringComparison.OrdinalIgnoreCase))
            return new ItemRef.LastMentioned();

        return int.TryParse(text, CultureInfo.InvariantCulture, out var number)
            ? new ItemRef.ListPosition(number)
            : new ItemRef.Description(text);
    }

    public Recurrence? Recurrence(string name)
    {
        if (Raw(name) is not { ValueKind: JsonValueKind.Object } element)
            return null;

        var nested = new ArgumentReader(element, now);
        var frequency = nested.Text("frequency", maxLength: 16)?.ToLowerInvariant();
        var interval = nested.Integer("interval", 1, Domain.Recurrence.MaxInterval) ?? 1;
        var days = nested.Strings("days");
        var until = nested.Date("until");
        _errors.AddRange(nested.Errors.Select(error => $"{name}.{error}"));

        RecurrenceFrequency? parsedFrequency = frequency switch
        {
            "daily" => RecurrenceFrequency.Daily,
            "weekly" => RecurrenceFrequency.Weekly,
            "monthly" => RecurrenceFrequency.Monthly,
            _ => Invalid<RecurrenceFrequency?>($"{name}.frequency", $"неизвестная частота «{frequency}»"),
        };

        var unknownDays = days.Where(day => !DayNames.ContainsKey(day)).ToArray();
        if (unknownDays.Length > 0)
            _errors.Add($"{name}.days: неизвестные дни {string.Join(", ", unknownDays)}");

        if (parsedFrequency is not { } validFrequency || nested.Errors.Count > 0 || unknownDays.Length > 0)
            return null;

        var weekDays = days.Select(day => DayNames[day]).ToArray();
        return weekDays.Length > 0
            ? new Recurrence(RecurrenceFrequency.Weekly, interval, weekDays, until)
            : new Recurrence(validFrequency, interval, until: until);
    }

    public IReadOnlyList<string> Phrases(string name, int max, int maxLength) =>
        Raw(name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => new string(item.GetString()!.Where(ch => !char.IsControl(ch)).ToArray()).Trim())
                .Where(item => item.Length > 0 && item.Length <= maxLength)
                .Take(max)
                .ToArray()
            : [];

    public IReadOnlyList<string> Words(string name, int max) =>
        Strings(name).Where(word => word.Length <= 40).Distinct().Take(max).ToArray();

    private IReadOnlyList<string> Strings(string name) => Raw(name) switch
    {
        { ValueKind: JsonValueKind.Array } array => array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim().ToLowerInvariant())
            .Where(item => item.Length > 0)
            .ToArray(),
        { ValueKind: JsonValueKind.String } single => [single.GetString()!.Trim().ToLowerInvariant()],
        _ => [],
    };

    private JsonElement? Raw(string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) ? value : null;

    private void Report(string name, string problem, bool fatal)
    {
        if (fatal)
            _errors.Add($"{name}: {problem}");
        else
            _warnings.Add($"{name}: {problem}");
    }

    private T Invalid<T>(string name, string problem)
    {
        _errors.Add($"{name}: {problem}");
        return default!;
    }
}
