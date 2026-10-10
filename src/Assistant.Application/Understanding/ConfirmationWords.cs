using Assistant.Application.Dialog;

namespace Assistant.Application.Understanding;

internal static class ConfirmationWords
{
    private static readonly string[] RejectPrefixes =
        ["нет", "не надо", "не нужно", "не стоит", "отмен", "не удаляй", "не добавляй", "не ставь", "стоп"];

    private static readonly string[] AlternativePrefixes = ["оставь", "оставить", "оставляй", "пусть будет", "как было"];

    private static readonly HashSet<string> AcceptWords =
    [
        "да", "ага", "угу", "конечно", "давай", "да давай", "подтверждаю", "верно", "ок", "окей", "хорошо",
        "добавляй", "удаляй", "удали", "да удаляй", "да удали", "да добавляй", "ставь", "да ставь", "согласен", "согласна",
    ];

    public static ConfirmChoice? TryParse(string text, PendingConfirmation pending)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return null;

        var quickReply = pending.QuickReplies.FirstOrDefault(pair => Normalize(pair.Key) == normalized);
        if (quickReply.Key is not null)
            return quickReply.Value;

        if (normalized.Any(char.IsDigit))
            return null;
        if (RejectPrefixes.Any(normalized.StartsWith))
            return ConfirmChoice.Reject;
        if (pending.Alternative is not null && AlternativePrefixes.Any(normalized.StartsWith))
            return ConfirmChoice.Alternative;
        if (AcceptWords.Contains(normalized))
            return ConfirmChoice.Accept;
        return null;
    }

    public static bool IsBareAnswer(string text)
    {
        var normalized = Normalize(text);
        return AcceptWords.Contains(normalized) || normalized is "нет" or "не" or "неа" or "не надо" or "не нужно" or "отмена";
    }

    private static string Normalize(string text)
    {
        var letters = new string(text.ToLowerInvariant().Replace('ё', 'е')
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        return string.Join(' ', letters.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
