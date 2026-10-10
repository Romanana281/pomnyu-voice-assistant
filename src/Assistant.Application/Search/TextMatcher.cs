using System.Text.RegularExpressions;

namespace Assistant.Application.Search;

public static partial class TextMatcher
{
    private const int ShortWordLength = 5;
    private const int StrippedEndingLength = 2;

    private static readonly HashSet<string> StopWords =
    [
        "а", "в", "во", "и", "к", "ко", "на", "о", "об", "по", "про", "с", "со", "у", "за", "до", "для", "из", "от",
        "я", "мне", "меня", "мой", "моя", "мои", "мою", "мы", "нас", "это", "эту", "этот", "то", "та", "тот",
        "был", "была", "было", "были", "есть", "когда", "какой", "какая", "какую", "где", "что",
        "найди", "найти", "покажи", "ставил", "ставила", "когда-то", "задача", "задачу", "задачи",
        "встреча", "встречу", "встречи", "напоминание", "запись", "записи", "ее", "её",
    ];

    public static double Score(string query, string text)
    {
        var queryWords = QueryWords(query);
        if (queryWords.Length == 0)
            return 0;

        var textWords = Words(text).Select(Normalize).ToArray();
        var matched = queryWords.Count(queryWord => textWords.Any(textWord => SameRoot(queryWord, textWord)));
        return (double)matched / queryWords.Length;
    }

    public static IReadOnlyList<string> MatchedWords(string query, string text)
    {
        var queryWords = QueryWords(query);
        return Words(text)
            .Where(word => queryWords.Any(queryWord => SameRoot(queryWord, Normalize(word))))
            .Distinct()
            .ToArray();
    }

    private static string[] QueryWords(string query)
    {
        var words = Words(query).Select(Normalize).Distinct().ToArray();
        var meaningful = words.Where(word => !StopWords.Contains(word)).ToArray();
        return meaningful.Length > 0 ? meaningful : words.Where(word => word.Length >= MinWordLength).ToArray();
    }

    private const int MinWordLength = 3;

    private static bool SameRoot(string left, string right) =>
        left.Length >= MinWordLength && right.Length >= MinWordLength && (StopWords.Contains(left) || !StopWords.Contains(right)) &&
        (Contains(right, Stem(left)) || Contains(left, Stem(right)));

    private const int MinInnerStemLength = 4;

    private static bool Contains(string word, string stem) =>
        stem.Length >= MinInnerStemLength ? word.Contains(stem, StringComparison.Ordinal) : word.StartsWith(stem, StringComparison.Ordinal);

    private static string Stem(string word) => word.Length switch
    {
        <= MinWordLength => word,
        <= ShortWordLength => word[..^1],
        _ => word[..Math.Max(ShortWordLength - 1, word.Length - StrippedEndingLength)],
    };

    private static string Normalize(string word) => word.ToLowerInvariant().Replace('ё', 'е');

    private static IEnumerable<string> Words(string text) => WordPattern().Matches(text).Select(match => match.Value);

    [GeneratedRegex(@"[\p{L}\d]+(?:-[\p{L}]+)?")]
    private static partial Regex WordPattern();
}
