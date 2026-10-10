using Assistant.Application;
using Assistant.Application.Scheduling;
using Assistant.Domain;
using Assistant.Web.Calendar;
using Assistant.Web.Session;
using QRCoder;

namespace Assistant.Web.Endpoints;

public static class AccountEndpoints
{
    private const string CalendarName = "Ассистент";
    private const int QrPixelsPerModule = 8;

    public static IEndpointRouteBuilder MapAccountApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/account", GetAccountAsync);
        app.MapPost("/api/account/link", CreateLinkAsync);
        app.MapGet("/link/{token}", RedeemLinkAsync);
        app.MapGet("/calendar/{token}.ics", GetCalendarFeedAsync);
        return app;
    }

    public sealed record AccountResponse(string CalendarUrl, string WebcalUrl);

    public sealed record LinkResponse(string Url, string QrSvg, int ExpiresInMinutes);

    private static async Task<IResult> GetAccountAsync(HttpContext http, UserSession session, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var calendarUrl = AbsoluteUrl(http, $"/calendar/{user.CalendarToken}.ics");
        var webcalUrl = "webcal://" + calendarUrl[(calendarUrl.IndexOf("://", StringComparison.Ordinal) + 3)..];
        return Results.Ok(new AccountResponse(calendarUrl, webcalUrl));
    }

    private static async Task<IResult> CreateLinkAsync(
        HttpContext http, UserSession session, UserAccounts accounts, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        var token = await accounts.IssueLinkTokenAsync(user, cancellationToken);
        var url = AbsoluteUrl(http, $"/link/{token}");

        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(
            QrPixelsPerModule, "#1c1c1e", "#ffffff", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);

        return Results.Ok(new LinkResponse(url, svg, (int)AppUser.LinkTokenLifetime.TotalMinutes));
    }

    private static async Task<IResult> RedeemLinkAsync(
        string token, HttpContext http, UserAccounts accounts, CancellationToken cancellationToken)
    {
        var user = await accounts.RedeemLinkTokenAsync(token, cancellationToken);
        if (user is null)
            return Results.Redirect("/?link=expired");

        await UserSession.SignInAsync(http, user);
        return Results.Redirect("/?link=ok");
    }

    private static async Task<IResult> GetCalendarFeedAsync(
        string token, UserAccounts accounts, ScheduleService schedule, CancellationToken cancellationToken)
    {
        var user = await accounts.FindByCalendarTokenAsync(token, cancellationToken);
        if (user is null)
            return Results.NotFound();

        var all = (await schedule.ListAllAsync(user.Id, cancellationToken)).Where(item => !item.IsImported);
        var ics = IcsCalendarWriter.Write(all, user.TimeZoneId, CalendarName);
        return Results.Text(ics, IcsCalendarWriter.ContentType);
    }

    private static string AbsoluteUrl(HttpContext http, string path) =>
        $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}{path}";
}
