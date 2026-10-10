using System.Globalization;

namespace Assistant.Application.Understanding;

internal sealed class PromptDates(DateOnly today)
{
    private const string Format = "yyyy-MM-dd";

    public string StartOfWeekDate => Iso(StartOfWeek(today));

    public string EndOfWeek => Iso(StartOfWeek(today).AddDays(6));

    public string NextWeekStart => Iso(StartOfWeek(today).AddDays(7));

    public string NextWeekEnd => Iso(StartOfWeek(today).AddDays(13));

    public static string Iso(DateOnly date) => date.ToString(Format, CultureInfo.InvariantCulture);

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
