using Assistant.Application.Abstractions;
using Assistant.Infrastructure.Calendar;
using Assistant.Infrastructure.Dialog;
using Assistant.Infrastructure.GigaChat;
using Assistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Assistant")
            ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Assistant.");
        services.AddDbContext<AssistantDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();
        services.AddScoped<ICalendarSubscriptionRepository, CalendarSubscriptionRepository>();
        services.AddHttpClient<ICalendarFetcher, SafeCalendarFetcher>(http =>
            {
                http.Timeout = TimeSpan.FromSeconds(20);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PomnyuCalendarSync/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(SafeCalendarFetcher.CreateHandler);

        services.AddMemoryCache();
        services.AddSingleton<IDialogStateStore, MemoryDialogStateStore>();

        AddGigaChat(services, configuration);
        return services;
    }

    private static void AddGigaChat(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GigaChatOptions>()
            .Bind(configuration.GetSection(GigaChatOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(GigaChatTokenProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(RussianTrustedCertificates.CreateHandler)
            .AddStandardResilienceHandler();
        services.AddSingleton<GigaChatTokenProvider>();
        services.AddTransient<GigaChatAuthHandler>();

        var chat = services.AddHttpClient<GigaChatClient>((provider, http) =>
                http.BaseAddress = provider.GetRequiredService<IOptions<GigaChatOptions>>().Value.BaseUrl)
            .ConfigurePrimaryHttpMessageHandler(RussianTrustedCertificates.CreateHandler);
        chat.AddStandardResilienceHandler(ConfigureLlmResilience);
        chat.AddHttpMessageHandler<GigaChatAuthHandler>();

        services.AddTransient<ILlmClient>(provider => provider.GetRequiredService<GigaChatClient>());
        services.AddTransient<IEmbeddingProvider>(provider => provider.GetRequiredService<GigaChatClient>());
    }

    private static void ConfigureLlmResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(25);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.MinimumThroughput = 20;
        options.CircuitBreaker.FailureRatio = 0.8;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
    }
}
