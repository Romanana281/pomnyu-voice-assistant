using Assistant.Domain;

namespace Assistant.Application.Presentation;

public enum ReplyKind
{
    Done,
    Question,
    Confirmation,
    Failure,
}

public sealed record AssistantReply(
    string Text,
    ReplyKind Kind,
    ReplyCard? Card,
    IReadOnlyList<string> QuickReplies)
{
    public IReadOnlyList<AssistantReply> FollowUps { get; init; } = [];

    public Guid? MessageId { get; init; }

    public Guid? UserMessageId { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ItemView> Items => Card?.Items ?? [];

    public static AssistantReply Done(string text, ReplyCard? card = null) => new(text, ReplyKind.Done, card, []);

    public static AssistantReply Question(string text, ReplyCard? card = null, IReadOnlyList<string>? quickReplies = null) =>
        new(text, ReplyKind.Question, card, quickReplies ?? []);

    public static AssistantReply Confirmation(string text, ReplyCard card, IReadOnlyList<string> quickReplies) =>
        new(text, ReplyKind.Confirmation, card, quickReplies);

    public static AssistantReply Failure(string text) => new(text, ReplyKind.Failure, null, []);
}

public sealed record ItemView(
    Guid Id,
    ItemType Type,
    string Title,
    DateOnly? Date,
    TimeOnly? Time,
    int? DurationMinutes,
    string? Recurrence,
    bool IsDone)
{
    public static ItemView From(Occurrence occurrence, DateOnly today) => new(
        occurrence.Item.Id,
        occurrence.Item.Type,
        occurrence.Item.Title,
        occurrence.Date,
        occurrence.Time,
        occurrence.Item.Duration is { } duration ? (int)duration.TotalMinutes : null,
        occurrence.Item.Recurrence is { } recurrence ? RussianText.Describe(recurrence, today) : null,
        occurrence.IsDone);

    public static ItemView From(ScheduleItem item, DateOnly today) => From(item.NextOccurrence(today), today);
}
