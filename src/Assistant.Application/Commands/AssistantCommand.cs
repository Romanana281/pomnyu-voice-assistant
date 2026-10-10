using Assistant.Application.Dialog;
using Assistant.Domain;

namespace Assistant.Application.Commands;

public abstract record AssistantCommand;

public interface IDraftCommand
{
    AssistantCommand WithSchedule(DateOnly? date, TimeOnly? time);
}

public sealed record CreateTaskCommand(
    string Title,
    ItemType Kind,
    DateOnly? Date,
    TimeOnly? Time,
    Recurrence? Recurrence) : AssistantCommand, IDraftCommand
{
    public AssistantCommand WithSchedule(DateOnly? date, TimeOnly? time) =>
        this with { Date = date ?? Date, Time = time ?? Time };
}

public sealed record CreateMeetingCommand(
    string Title,
    DateOnly? Date,
    TimeOnly? Time,
    TimeSpan? Duration,
    Recurrence? Recurrence,
    bool IgnoreConflicts = false) : AssistantCommand, IDraftCommand
{
    public AssistantCommand WithSchedule(DateOnly? date, TimeOnly? time) =>
        this with { Date = date ?? Date, Time = time ?? Time, IgnoreConflicts = false };
}

public sealed record ListItemsCommand(
    DateOnly From,
    DateOnly To,
    IReadOnlySet<ItemType> Types) : AssistantCommand;

public sealed record SearchItemsCommand(
    string Query,
    IReadOnlyList<string> RelatedWords,
    IReadOnlySet<ItemType> Types,
    TimeScope Scope) : AssistantCommand;

public sealed record UpdateItemCommand(
    ItemRef Target,
    string? NewTitle,
    DateOnly? NewDate,
    TimeOnly? NewTime,
    TimeSpan? NewDuration) : AssistantCommand;

public sealed record DeleteItemCommand(ItemRef Target, bool Confirmed = false) : AssistantCommand;

public sealed record CompleteItemCommand(ItemRef Target) : AssistantCommand;

public sealed record ConfirmCommand(ConfirmChoice Choice) : AssistantCommand;

public sealed record ReplyCommand(string Text) : AssistantCommand;

public sealed record NotUnderstoodCommand : AssistantCommand;

public sealed record MultiRequestCommand(IReadOnlyList<string> Requests) : AssistantCommand;
