using System.Text.RegularExpressions;
using Assistant.Application.Commands;

namespace Assistant.Application.Understanding;

internal static partial class IntentOverrides
{
    private static readonly HashSet<string> FillerWords =
    [
        "мне", "мою", "мой", "мои", "эту", "этот", "это", "пожалуйста", "задачу", "задача", "встречу", "встреча",
        "напоминание", "запись", "про", "о", "об", "с", "со", "по", "на", "в", "и", "выполненной", "выполненным",
        "выполнено", "сделанной", "сделанным", "как", "готовой", "готовым",
    ];

    public static AssistantCommand Apply(AssistantCommand command, string text, DateOnly today)
    {
        var normalized = text.ToLowerInvariant().Replace('ё', 'е');
        if (command is UpdateItemCommand { NewDate: { } movedTo, NewTime: null } update
            && BareHour().Match(normalized) is { Success: true } hour
            && int.Parse(hour.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) is var value and >= 0 and <= 23
            && movedTo.Day == value)
        {
            return update with { NewDate = null, NewTime = new TimeOnly(value, 0) };
        }

        if (command is CreateTaskCommand { Kind: Domain.ItemType.Task } task && ReminderWord().IsMatch(normalized))
            return task with { Kind = Domain.ItemType.Reminder };
        if (command is ListItemsCommand { Types.Count: < 3 } typed && !TypeWord().IsMatch(normalized))
            command = typed with { Types = Enum.GetValues<Domain.ItemType>().ToHashSet() };
        if (command is ListItemsCommand weekendList && Weekend().IsMatch(normalized) && RelativeDates.Period("weekend", today) is { } weekend)
            return weekendList with { From = weekend.From, To = weekend.To };
        if (command is ListItemsCommand list && SingleDay(normalized, today) is { } day)
            return list with { From = day, To = day };
        if (command is DeleteItemCommand or CompleteItemCommand or ConfirmCommand or CreateTaskCommand or CreateMeetingCommand)
            return command;

        if (DeleteVerb().Match(normalized) is { Success: true } delete)
            return new DeleteItemCommand(TargetAfter(normalized, delete, command));
        if (CompleteVerb().Match(normalized) is { Success: true } complete)
            return new CompleteItemCommand(TargetAfter(normalized, complete, command));
        return command;
    }

    private static DateOnly? SingleDay(string text, DateOnly today)
    {
        if (PeriodWord().IsMatch(text))
            return null;
        var match = DayWord().Match(text);
        if (match.Success)
        {
            return match.Value switch
            {
                "послезавтра" => today.AddDays(2),
                "завтра" => today.AddDays(1),
                _ => today,
            };
        }

        var weekdays = WeekdayWord().Matches(text);
        if (weekdays.Count != 1)
            return null;
        var keyword = weekdays[0].Groups[1].Value switch
        {
            "понедельник" => "mon",
            "вторник" => "tue",
            "сред" => "wed",
            "четверг" => "thu",
            "пятниц" => "fri",
            "суббот" => "sat",
            _ => "sun",
        };
        return RelativeDates.Day(NextWeek().IsMatch(text) ? $"next_{keyword}" : keyword, today);
    }

    private static ItemRef TargetAfter(string text, Match verb, AssistantCommand command)
    {
        if (command is SearchItemsCommand search)
            return new ItemRef.Description(search.Query);

        var words = WordPattern().Matches(text[(verb.Index + verb.Length)..])
            .Select(match => match.Value)
            .Where(word => !FillerWords.Contains(word))
            .ToArray();
        return words.Length == 0 || words is ["ее"] or ["её"] or ["его"]
            ? new ItemRef.LastMentioned()
            : new ItemRef.Description(string.Join(' ', words));
    }

    [GeneratedRegex(@"(?<!\bне\s)\b(удали|удалить|удаляй|убери|убрать|вычеркни|сотри)\b")]
    private static partial Regex DeleteVerb();

    [GeneratedRegex(@"(?<!\bне\s)\b(отметь|отметить|пометь|пометить)\b(?=.*\b(выполнен|сделан|готов))")]
    private static partial Regex CompleteVerb();

    [GeneratedRegex(@"\b(послезавтра|завтра|сегодня)\b")]
    private static partial Regex DayWord();

    [GeneratedRegex(@"(?<!следующ\w*\s)недел|месяц|выходн|\d")]
    private static partial Regex PeriodWord();

    [GeneratedRegex(@"\b(?:в|во|на)\s+(?:следующ\w+\s+)?(понедельник|вторник|сред|четверг|пятниц|суббот|воскресень)")]
    private static partial Regex WeekdayWord();

    [GeneratedRegex(@"встреч|созвон|совещан|планерк|задач|дел[аоу]?\b|напомин|звонк")]
    private static partial Regex TypeWord();

    [GeneratedRegex(@"\bна (\d{1,2})\s*(?:час\w*)?\s*[.!?]?$")]
    private static partial Regex BareHour();

    [GeneratedRegex(@"выходн")]
    private static partial Regex Weekend();

    [GeneratedRegex(@"следующ")]
    private static partial Regex NextWeek();

    [GeneratedRegex(@"\b(напомни|напоминай|напомнить|напоминание|remind)")]
    private static partial Regex ReminderWord();

    [GeneratedRegex(@"[\p{L}\d]+")]
    private static partial Regex WordPattern();
}
