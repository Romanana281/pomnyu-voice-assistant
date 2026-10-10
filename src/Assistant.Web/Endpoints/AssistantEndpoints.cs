using System.Text.Json;
using Assistant.Application;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Assistant.Application.Commands;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;
using Assistant.Application.Search;
using Assistant.Domain;
using Assistant.Web.Errors;
using Assistant.Web.Session;

namespace Assistant.Web.Endpoints;

public static class AssistantEndpoints
{
    private const int MaxQueryLength = 200;
    private const int MaxYearsFromToday = 10;
    private const int MinDurationMinutes = 5;
    private const int MaxDurationMinutes = 24 * 60;

    private static readonly IReadOnlySet<ItemType> AllTypes = Enum.GetValues<ItemType>().ToHashSet();

    public static IEndpointRouteBuilder MapAssistantApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapPost("/chat", ChatAsync);
        api.MapGet("/agenda", GetAgendaAsync);
        api.MapGet("/search", SearchAsync);
        api.MapPost("/items", CreateAsync);
        api.MapPut("/items/{id:guid}", UpdateAsync);
        api.MapPost("/items/{id:guid}/complete", CompleteAsync);
        api.MapPost("/items/{id:guid}/reopen", ReopenAsync);
        api.MapPost("/items/{id:guid}/restore", RestoreAsync);
        api.MapDelete("/items/{id:guid}", DeleteAsync);
        return app;
    }

    public sealed record ChatRequest(string? Text, string? ConversationId, string? Via);

    public sealed record OccurrenceRequest(DateOnly? Date);

    public sealed record ItemRequest(ItemType? Type, string? Title, DateOnly? Date, TimeOnly? Time, int? DurationMinutes);

    public sealed record ItemResponse(ItemView Item, string? Warning);

    public sealed record AgendaDayView(DateOnly Date, IReadOnlyList<ItemView> Items);

    public sealed record AgendaResponse(
        string Range,
        DateOnly Today,
        TimeOnly Now,
        DateOnly From,
        DateOnly To,
        IReadOnlyList<ItemView> Overdue,
        IReadOnlyList<AgendaDayView> Days,
        IReadOnlyList<ItemView> Undated);

    private static async Task<IResult> ChatAsync(
        ChatRequest request,
        HttpContext http,
        UserSession session,
        AssistantService assistant,
        ChatHistory history,
        IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return ApiErrors.Invalid("text", "Сообщение пустое.");
        if (text.Length > AssistantService.MaxMessageLength)
            return ApiErrors.Invalid("text", $"Сообщение длиннее {AssistantService.MaxMessageLength} символов.");

        var conversation = string.IsNullOrEmpty(request.ConversationId) ? AssistantService.DefaultConversation : request.ConversationId;
        if (!Conversation.IsValidId(conversation))
            return ApiErrors.Invalid("conversationId", "Некорректный идентификатор чата.");

        var user = await session.GetUserAsync(http, cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        var reply = await assistant.HandleAsync(user, conversation, text, cancellationToken);
        var parts = reply.FollowUps.Prepend(reply).ToArray();

        var via = request.Via == "voice" ? "voice" : "text";
        var messages = parts
            .Select(part => new IncomingMessage(ChatRole.Assistant, part.Text, null, KindName(part.Kind), ConversationEndpoints.Payload(part, json.Value), null))
            .Prepend(new IncomingMessage(ChatRole.User, text, via, null, null, startedAt))
            .ToArray();
        var ids = await history.AppendAsync(user.Id, conversation, messages, CancellationToken.None);

        return Results.Ok(reply with
        {
            UserMessageId = ids[0],
            MessageId = ids[1],
            FollowUps = reply.FollowUps.Select((part, index) => part with { MessageId = ids[index + 2] }).ToArray(),
        });
    }

    private static async Task<IResult> GetAgendaAsync(
        string? range, HttpContext http, UserSession session, UserClock clock, ScheduleService schedule, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var now = clock.LocalNow(user);
        var today = DateOnly.FromDateTime(now);

        (DateOnly From, DateOnly To)? period = (range ?? "today") switch
        {
            "today" => (today, today),
            "tomorrow" => (today.AddDays(1), today.AddDays(1)),
            "week" => (today, today.AddDays(6)),
            _ => null,
        };
        if (period is not { } bounds)
            return ApiErrors.Invalid("range", "Период — today, tomorrow или week.");

        var agenda = await schedule.GetAgendaAsync(user.Id, today, bounds.From, bounds.To, cancellationToken);
        var days = agenda.Upcoming
            .GroupBy(occurrence => occurrence.Date!.Value)
            .Select(day => new AgendaDayView(day.Key, day.Select(occurrence => ItemView.From(occurrence, today)).ToArray()))
            .ToArray();

        return Results.Ok(new AgendaResponse(
            range ?? "today",
            today,
            TimeOnly.FromDateTime(now),
            bounds.From,
            bounds.To,
            agenda.Overdue.Select(occurrence => ItemView.From(occurrence, today)).ToArray(),
            days,
            agenda.Undated.Select(occurrence => ItemView.From(occurrence, today)).ToArray()));
    }

    private static async Task<IResult> SearchAsync(
        string? q, HttpContext http, UserSession session, UserClock clock, ItemSearch search, CancellationToken cancellationToken)
    {
        var query = q?.Trim();
        if (string.IsNullOrEmpty(query))
            return ApiErrors.Invalid("q", "Введите, что найти.");
        if (query.Length > MaxQueryLength)
            return ApiErrors.Invalid("q", $"Запрос длиннее {MaxQueryLength} символов.");

        var user = await session.GetUserAsync(http, cancellationToken);
        var today = clock.Today(user);
        var hits = await search.SearchAsync(user.Id, query, AllTypes, TimeScope.Any, today, cancellationToken);
        return Results.Ok(hits
            .Select(hit => new SearchHitView(ItemView.From(hit.Item, today), hit.IsExact, TextMatcher.MatchedWords(query, hit.Item.Title)))
            .ToArray());
    }

    private static async Task<IResult> CreateAsync(
        ItemRequest request,
        HttpContext http,
        UserSession session,
        UserClock clock,
        TimeProvider time,
        ScheduleService schedule,
        CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var today = clock.Today(user);
        if (Validate(request, today, requireType: true) is { } invalid)
            return invalid;

        var title = request.Title!.Trim();
        var item = request.Type == ItemType.Meeting
            ? ScheduleItem.CreateMeeting(
                user.Id, title, request.Date!.Value, request.Time!.Value, DurationOf(request), null, time.GetUtcNow())
            : ScheduleItem.CreateTask(user.Id, request.Type!.Value, title, request.Date, request.Time, null, time.GetUtcNow());

        await schedule.AddAsync(item, cancellationToken);
        var warning = await ConflictWarningAsync(item, today, schedule, cancellationToken);
        return Results.Created($"/api/items/{item.Id}", new ItemResponse(ItemView.From(item, today), warning));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ItemRequest request,
        HttpContext http,
        UserSession session,
        UserClock clock,
        ScheduleService schedule,
        CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var item = await schedule.FindAsync(user.Id, id, cancellationToken);
        if (item is null || item.Status == ItemStatus.Cancelled)
            return ApiErrors.NotFound("Запись не найдена — возможно, её уже удалили.");

        var today = clock.Today(user);
        var type = request.Type ?? item.Type;
        if (Validate(request with { Type = type }, today, requireType: false) is { } invalid)
            return invalid;
        if (item.IsRecurring && request.Date is null)
            return ApiErrors.Invalid("date", "У повторяющейся записи должен быть день начала.");

        var title = request.Title!.Trim();
        await schedule.UpdateAsync(
            item,
            title == item.Title ? null : title,
            request.Date,
            request.Time,
            type == ItemType.Meeting ? DurationOf(request) : null,
            cancellationToken,
            type);

        var warning = await ConflictWarningAsync(item, today, schedule, cancellationToken);
        return Results.Ok(new ItemResponse(ItemView.From(item, today), warning));
    }

    private static Task<IResult> CompleteAsync(
        Guid id, OccurrenceRequest request, HttpContext http, UserSession session, ScheduleService schedule, CancellationToken cancellationToken) =>
        WithItemAsync(id, http, session, schedule, allowCancelled: false, cancellationToken,
            item => schedule.CompleteAsync(item, item.IsRecurring ? request.Date ?? item.Date : null, cancellationToken));

    private static Task<IResult> ReopenAsync(
        Guid id, OccurrenceRequest request, HttpContext http, UserSession session, ScheduleService schedule, CancellationToken cancellationToken) =>
        WithItemAsync(id, http, session, schedule, allowCancelled: false, cancellationToken,
            item => schedule.ReopenAsync(item, request.Date, cancellationToken));

    private static Task<IResult> RestoreAsync(
        Guid id, HttpContext http, UserSession session, ScheduleService schedule, CancellationToken cancellationToken) =>
        WithItemAsync(id, http, session, schedule, allowCancelled: true, cancellationToken,
            item => schedule.RestoreAsync(item, cancellationToken));

    private static Task<IResult> DeleteAsync(
        Guid id, HttpContext http, UserSession session, ScheduleService schedule, CancellationToken cancellationToken) =>
        WithItemAsync(id, http, session, schedule, allowCancelled: false, cancellationToken,
            item => schedule.CancelAsync(item, cancellationToken));

    private static async Task<IResult> WithItemAsync(
        Guid id,
        HttpContext http,
        UserSession session,
        ScheduleService schedule,
        bool allowCancelled,
        CancellationToken cancellationToken,
        Func<ScheduleItem, Task> action)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var item = await schedule.FindAsync(user.Id, id, cancellationToken);
        if (item is null || (!allowCancelled && item.Status == ItemStatus.Cancelled))
            return ApiErrors.NotFound("Запись не найдена — возможно, её уже удалили.");

        await action(item);
        return Results.NoContent();
    }

    private static string KindName(ReplyKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    private static IResult? Validate(ItemRequest request, DateOnly today, bool requireType)
    {
        if (requireType && request.Type is null)
            return ApiErrors.Invalid("type", "Выберите тип записи.");

        var title = string.Join(' ', (request.Title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.Length == 0 || title.All(char.IsControl))
            return ApiErrors.Invalid("title", "Напишите, что нужно сделать.");
        if (title.Length > ScheduleItem.MaxTitleLength)
            return ApiErrors.Invalid("title", $"Название длиннее {ScheduleItem.MaxTitleLength} символов.");

        if (request.Date is { } date && Math.Abs(date.Year - today.Year) > MaxYearsFromToday)
            return ApiErrors.Invalid("date", "Дата слишком далеко от сегодняшнего дня.");
        if (request.Time is not null && request.Date is null)
            return ApiErrors.Invalid("date", "Укажите день — время без даты не сохранить.");

        if (request.Type == ItemType.Meeting)
        {
            if (request.Date is null)
                return ApiErrors.Invalid("date", "У встречи должен быть день.");
            if (request.Time is null)
                return ApiErrors.Invalid("time", "У встречи должно быть время.");
            if (request.DurationMinutes is { } minutes && (minutes < MinDurationMinutes || minutes > MaxDurationMinutes))
                return ApiErrors.Invalid("durationMinutes", $"Длительность — от {MinDurationMinutes} минут до 24 часов.");
        }
        else if (request.Type == ItemType.Reminder && request.Time is null)
        {
            return ApiErrors.Invalid("time", "Укажите, во сколько напомнить.");
        }

        return null;
    }

    private static TimeSpan DurationOf(ItemRequest request) =>
        request.DurationMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : ScheduleItem.DefaultMeetingDuration;

    private static async Task<string?> ConflictWarningAsync(
        ScheduleItem item, DateOnly today, ScheduleService schedule, CancellationToken cancellationToken)
    {
        var conflicts = await schedule.FindConflictsAsync(item, cancellationToken);
        return conflicts.Count == 0
            ? null
            : $"Пересекается с {ItemPhrases.Describe(conflicts[0], today)}.";
    }
}
