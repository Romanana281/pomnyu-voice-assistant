using Assistant.Application.Calendar;

namespace Assistant.Web.Calendar;

public sealed class CalendarSyncWorker(IServiceScopeFactory scopes, ILogger<CalendarSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CalendarImportService>().SyncAllAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Фоновая синхронизация календарей не удалась");
            }
        }
    }
}
