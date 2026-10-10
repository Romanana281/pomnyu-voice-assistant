using System.Text.Json;
using System.Text.Json.Serialization;
using Assistant.Application;
using Assistant.Infrastructure;
using Assistant.Infrastructure.Persistence;
using Assistant.Web.Endpoints;
using Assistant.Web.Errors;
using Assistant.Web.Session;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? "data");
Directory.CreateDirectory(dataDirectory);
builder.Configuration["ConnectionStrings:Assistant"] ??= $"Data Source={Path.Combine(dataDirectory, "assistant.db")}";

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")))
    .SetApplicationName("voice-assistant");

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "assistant.user";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(365);
        options.SlidingExpiration = true;
    });

builder.Services.AddScoped<UserSession>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(["text/calendar", "image/svg+xml", "application/manifest+json"]);
});
builder.Services.AddHostedService<Assistant.Web.Calendar.CalendarSyncWorker>();
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AssistantDbContext>().Database;
    await database.MigrateAsync();
    await database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}

app.UseExceptionHandler();
app.UseResponseCompression();
app.Use(async (http, next) =>
{
    var headers = http.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "same-origin";
    headers["Permissions-Policy"] = "microphone=(self), camera=(), geolocation=()";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseAuthentication();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapAssistantApi();
app.MapAccountApi();
app.MapCalendarApi();
app.MapConversationApi();

app.Run();
