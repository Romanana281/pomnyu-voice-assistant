namespace Assistant.Domain;

public sealed record Occurrence(ScheduleItem Item, DateOnly? Date, TimeOnly? Time, bool IsDone)
{
    public DateTime? Start => Date is { } date && Time is { } time ? date.ToDateTime(time) : null;

    public DateTime? End => Start + Item.Duration;

    public bool OverlapsWith(Occurrence other) =>
        Start < other.End && other.Start < End;
}
