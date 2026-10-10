using Assistant.Application.Abstractions;
using Assistant.Application.Calendar;
using Assistant.Domain;
using Assistant.Web.Errors;
using Assistant.Web.Session;

namespace Assistant.Web.Endpoints;

public static class CalendarEndpoints
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapCalendarApi(this IEndpointRouteBuilder app)
    {
        var calendar = app.MapGroup("/api/calendar");
        calendar.MapPost("/import", ImportFileAsync).DisableAntiforgery();
        calendar.MapGet("/subscriptions", ListAsync);
        calendar.MapPost("/subscriptions", SubscribeAsync);
        calendar.MapPost("/subscriptions/{id:guid}/sync", SyncAsync);
        calendar.MapDelete("/subscriptions/{id:guid}", UnsubscribeAsync);
        return app;
    }

    public sealed record SubscribeRequest(string? Url);

    public sealed record SubscriptionView(Guid Id, string Name, string Url, DateTimeOffset? LastSyncedAt, string? LastError);

    public sealed record ImportResponse(ImportSummary Summary, SubscriptionView? Subscription);

    private static async Task<IResult> ImportFileAsync(
        IFormFile? file, HttpContext http, UserSession session, CalendarImportService import, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return ApiErrors.Invalid("file", "Выберите файл календаря .ics.");
        if (file.Length > MaxFileBytes)
            return ApiErrors.Invalid("file", "Файл больше 5 МБ.");

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(cancellationToken);
        var user = await session.GetUserAsync(http, cancellationToken);
        try
        {
            return Results.Ok(new ImportResponse(await import.ImportFileAsync(user, content, cancellationToken), null));
        }
        catch (FormatException exception)
        {
            return ApiErrors.Invalid("file", exception.Message);
        }
    }

    private static async Task<IResult> ListAsync(
        HttpContext http, UserSession session, CalendarImportService import, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var subscriptions = await import.ListAsync(user.Id, cancellationToken);
        return Results.Ok(subscriptions.OrderBy(subscription => subscription.CreatedAt).Select(View).ToArray());
    }

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest request, HttpContext http, UserSession session, CalendarImportService import, CancellationToken cancellationToken)
    {
        if (ParseUrl(request.Url) is not { } url)
            return ApiErrors.Invalid("url", "Вставьте ссылку на календарь в формате iCal (https://… или webcal://…).");

        var user = await session.GetUserAsync(http, cancellationToken);
        try
        {
            var (subscription, summary) = await import.SubscribeAsync(user, url, cancellationToken);
            return Results.Ok(new ImportResponse(summary, View(subscription)));
        }
        catch (Exception exception) when (exception is CalendarFetchException or FormatException)
        {
            return ApiErrors.Invalid("url", exception.Message);
        }
    }

    private static async Task<IResult> SyncAsync(
        Guid id, HttpContext http, UserSession session, CalendarImportService import, ICalendarSubscriptionRepository subscriptions, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        if (await subscriptions.FindAsync(user.Id, id, cancellationToken) is not { } subscription)
            return ApiErrors.NotFound("Календарь не найден.");
        try
        {
            return Results.Ok(new ImportResponse(await import.SyncAsync(user, subscription, cancellationToken), View(subscription)));
        }
        catch (Exception exception) when (exception is CalendarFetchException or FormatException)
        {
            return ApiErrors.Invalid("url", exception.Message);
        }
    }

    private static async Task<IResult> UnsubscribeAsync(
        Guid id, bool? removeItems, HttpContext http, UserSession session, CalendarImportService import, ICalendarSubscriptionRepository subscriptions, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        if (await subscriptions.FindAsync(user.Id, id, cancellationToken) is not { } subscription)
            return ApiErrors.NotFound("Календарь не найден.");
        await import.UnsubscribeAsync(subscription, removeItems ?? true, cancellationToken);
        return Results.NoContent();
    }

    private static Uri? ParseUrl(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > CalendarSubscription.MaxUrlLength)
            return null;
        if (trimmed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            trimmed = "https://" + trimmed["webcal://".Length..];
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var url)
               && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
               && string.IsNullOrEmpty(url.UserInfo)
            ? url
            : null;
    }

    private static SubscriptionView View(CalendarSubscription subscription) =>
        new(subscription.Id, subscription.Name, subscription.Url, subscription.LastSyncedAt, subscription.LastError);
}
