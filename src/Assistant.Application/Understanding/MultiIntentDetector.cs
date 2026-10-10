using System.Text.RegularExpressions;

namespace Assistant.Application.Understanding;

internal static partial class MultiIntentDetector
{
    public static bool MaybeSeveral(string text)
    {
        var normalized = text.ToLowerInvariant().Replace('ё', 'е');
        if (!Connector().IsMatch(normalized))
            return false;

        var actions = ActionWord().Matches(normalized).Count;
        var days = DayWord().Matches(normalized).Count;
        return actions >= 2 || (actions >= 1 && days >= 2) || days >= 2 && normalized.Contains(" и ", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\sи\s|,|также|потом|затем|еще|дополнительно|плюс")]
    private static partial Regex Connector();

    [GeneratedRegex(@"\b(назнач|постав|добав|напомн|удал|убер|перенес|сдвин|покаж|найд|отмет|запиш|создай|созда|что у меня|какие у меня|есть ли|какие планы|когда у меня)")]
    private static partial Regex ActionWord();

    [GeneratedRegex(@"\b(сегодня|завтра|послезавтра|понедельник|вторник|сред[уа]|четверг|пятниц|суббот|воскресень|недел|\d{1,2}[:.]\d{2})")]
    private static partial Regex DayWord();
}
