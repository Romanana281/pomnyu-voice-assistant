using System.Text.RegularExpressions;
using Assistant.Application.Commands;

namespace Assistant.Application.Understanding;

internal static partial class MentionGuard
{
    public static AssistantCommand Apply(AssistantCommand command, string spokenText) => command switch
    {
        CreateTaskCommand task => task with
        {
            Date = MentionsDay(spokenText) || MentionsTime(spokenText) ? task.Date : null,
            Time = MentionsTime(spokenText) ? task.Time : null,
        },
        CreateMeetingCommand meeting => meeting with
        {
            Date = MentionsDay(spokenText) ? meeting.Date : null,
            Time = MentionsTime(spokenText) ? meeting.Time : null,
        },
        UpdateItemCommand update => update with
        {
            NewDate = MentionsDay(spokenText) ? update.NewDate : null,
            NewTime = MentionsTime(spokenText) ? update.NewTime : null,
        },
        _ => command,
    };

    public static bool MentionsDay(string text) => DayPattern().IsMatch(Normalize(text));

    public static bool MentionsTime(string text) => TimePattern().IsMatch(Normalize(text));

    private static string Normalize(string text) => text.ToLowerInvariant().Replace('ё', 'е');

    [GeneratedRegex(@"\d|сегодня|завтра|вчера|понедельник|вторник|сред[ауые]|четверг|пятниц|суббот|воскресень|недел|месяц|выходн|будн|утром|вечером|днем|ночью|через|январ|феврал|март|апрел|ма[яйе]\b|июн|июл|август|сентябр|октябр|ноябр|декабр|числ|кажд|ежедневн|еженедельн")]
    private static partial Regex DayPattern();

    [GeneratedRegex(@"\d|час|утр|дня\b|днем|вечер|ночи|ночью|полдень|полдн|полночь|половин|четверт|минут|через|\bв (один|одн|два|две|три|четыре|пять|шесть|семь|восемь|девять|десять|одиннадцать|двенадцать)\b")]
    private static partial Regex TimePattern();
}
