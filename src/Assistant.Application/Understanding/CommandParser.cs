using System.Text.Json;
using Assistant.Application.Abstractions;
using Assistant.Application.Commands;
using Assistant.Domain;

namespace Assistant.Application.Understanding;

public abstract record ParseResult
{
    public sealed record Parsed(AssistantCommand Command) : ParseResult;

    public sealed record Invalid(IReadOnlyList<string> Errors) : ParseResult;
}

internal static class CommandParser
{
    private const int MaxQueryLength = 200;
    private const int MinDurationMinutes = 5;
    private const int MaxDurationMinutes = 24 * 60;

    private static readonly IReadOnlyDictionary<string, Func<ArgumentReader, AssistantCommand?>> Parsers =
        new Dictionary<string, Func<ArgumentReader, AssistantCommand?>>
        {
            [AssistantFunctions.CreateTask] = ParseCreateTask,
            [AssistantFunctions.CreateMeeting] = ParseCreateMeeting,
            [AssistantFunctions.ListItems] = ParseListItems,
            [AssistantFunctions.SearchItems] = ParseSearchItems,
            [AssistantFunctions.UpdateItem] = ParseUpdateItem,
            [AssistantFunctions.DeleteItem] = args => args.Target("item") is { } target ? new DeleteItemCommand(target) : null,
            [AssistantFunctions.CompleteItem] = args => args.Target("item") is { } target ? new CompleteItemCommand(target) : null,
            [AssistantFunctions.SeveralRequests] = ParseSeveral,
            [AssistantFunctions.Confirm] = args => args.Choice("choice") is { } choice ? new ConfirmCommand(choice) : null,
        };

    private const int MaxRelatedWords = 10;
    private const int DefaultListDays = 7;
    private const int MaxSubRequests = 5;
    private const int MaxSubRequestLength = 300;
    private const int MaxInMinutes = 60 * 24 * 30;

    public static ParseResult Parse(LlmFunctionCall call, DateTime now)
    {
        if (!Parsers.TryGetValue(call.Name, out var parse))
            return new ParseResult.Invalid([$"неизвестная функция {call.Name}"]);

        var reader = new ArgumentReader(call.Arguments, now);
        var command = parse(reader);
        return reader.Errors.Count == 0 && command is not null
            ? new ParseResult.Parsed(command)
            : new ParseResult.Invalid(reader.Errors.DefaultIfEmpty("не хватает обязательных аргументов").ToArray());
    }

    private static AssistantCommand? ParseCreateTask(ArgumentReader args)
    {
        var title = args.Title("title", required: true);
        var kind = args.Kind("kind", fallback: ItemType.Task);
        var (date, time) = When(args, "date", "time", "in_minutes");
        var recurrence = args.Recurrence("recurrence");

        if (kind == ItemType.Meeting)
            return null;
        return title is null ? null : new CreateTaskCommand(title, kind, date, time, recurrence);
    }

    private static AssistantCommand? ParseCreateMeeting(ArgumentReader args)
    {
        var title = args.Title("title", required: true);
        var (date, time) = When(args, "date", "time", "in_minutes");
        var minutes = args.Integer("duration_minutes", MinDurationMinutes, MaxDurationMinutes);
        var recurrence = args.Recurrence("recurrence");

        return title is null
            ? null
            : new CreateMeetingCommand(title, date, time, ToDuration(minutes), recurrence);
    }

    private static AssistantCommand? ParseListItems(ArgumentReader args)
    {
        var today = DateOnly.FromDateTime(args.Now);
        var period = args.Period("period");
        var from = period?.From ?? args.Date("from") ?? today;
        var to = period?.To ?? args.Date("to") ?? from.AddDays(DefaultListDays - 1);
        var types = args.Types("types");

        return from <= to
            ? new ListItemsCommand(from, to, types)
            : new ListItemsCommand(to, from, types);
    }

    private static AssistantCommand? ParseSearchItems(ArgumentReader args)
    {
        var query = args.Text("query", MaxQueryLength, required: true);
        var types = args.Types("types");
        var scope = args.Scope("when");
        var related = args.Words("related_words", MaxRelatedWords);
        return query is null ? null : new SearchItemsCommand(query, related, types, scope);
    }

    private static AssistantCommand? ParseUpdateItem(ArgumentReader args)
    {
        var target = args.Target("item");
        var title = args.Title("new_title");
        var (date, time) = When(args, "new_date", "new_time", "new_in_minutes");
        var minutes = args.Integer("new_duration_minutes", MinDurationMinutes, MaxDurationMinutes);

        return target is null ? null : new UpdateItemCommand(target, title, date, time, ToDuration(minutes));
    }

    private static AssistantCommand? ParseSeveral(ArgumentReader args)
    {
        var requests = args.Phrases("requests", MaxSubRequests, MaxSubRequestLength);
        return requests.Count == 0 ? null : new MultiRequestCommand(requests);
    }

    private static (DateOnly? Date, TimeOnly? Time) When(ArgumentReader args, string dateField, string timeField, string inMinutesField)
    {
        if (args.Integer(inMinutesField, 1, MaxInMinutes) is { } inMinutes)
        {
            var moment = args.Now.AddMinutes(inMinutes);
            return (DateOnly.FromDateTime(moment), new TimeOnly(moment.Hour, moment.Minute));
        }

        var date = args.Date(dateField);
        var time = args.Time(timeField);
        var today = DateOnly.FromDateTime(args.Now);
        if (date == today && args.IsWeekday(dateField) && time < TimeOnly.FromDateTime(args.Now))
            date = today.AddDays(7);
        return (date, time);
    }

    private static TimeSpan? ToDuration(int? minutes) => minutes is { } value ? TimeSpan.FromMinutes(value) : null;
}
