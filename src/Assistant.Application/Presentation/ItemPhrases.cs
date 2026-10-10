using Assistant.Domain;

namespace Assistant.Application.Presentation;

public static class ItemPhrases
{
    public static string Quoted(ScheduleItem item) => $"«{item.Title}»";

    public static string When(Occurrence occurrence, DateOnly today)
    {
        var when = RussianText.When(occurrence.Date, occurrence.Time, today);
        return occurrence.Item.Recurrence is { } recurrence
            ? $"{when}, {RussianText.Describe(recurrence, today)}"
            : when;
    }

    public static string Describe(Occurrence occurrence, DateOnly today) =>
        $"{Quoted(occurrence.Item)} — {When(occurrence, today)}";

    public static string CountByType(IReadOnlyList<Occurrence> occurrences)
    {
        var parts = new[]
            {
                (Type: ItemType.Meeting, Forms: ("встреча", "встречи", "встреч")),
                (Type: ItemType.Task, Forms: ("задача", "задачи", "задач")),
                (Type: ItemType.Reminder, Forms: ("напоминание", "напоминания", "напоминаний")),
            }
            .Select(kind => (kind.Forms, Count: occurrences.Count(occurrence => occurrence.Item.Type == kind.Type)))
            .Where(kind => kind.Count > 0)
            .Select(kind => RussianText.Plural(kind.Count, kind.Forms.Item1, kind.Forms.Item2, kind.Forms.Item3))
            .ToArray();

        return parts.Length switch
        {
            0 => "ничего",
            1 => parts[0],
            _ => $"{string.Join(", ", parts[..^1])} и {parts[^1]}",
        };
    }
}
