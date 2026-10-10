using Assistant.Domain;

namespace Assistant.Application.Scheduling;

public sealed record Agenda(
    IReadOnlyList<Occurrence> Overdue,
    IReadOnlyList<Occurrence> Upcoming,
    IReadOnlyList<Occurrence> Undated);
