using Assistant.Domain;

namespace Assistant.Application.Calendar;

public sealed record CalendarEvent(
    string Uid,
    string Title,
    DateOnly Date,
    TimeOnly? Time,
    TimeSpan? Duration,
    Recurrence? Recurrence);
