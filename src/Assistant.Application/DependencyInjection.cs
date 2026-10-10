using Assistant.Application.Handlers;
using Assistant.Application.Handling;
using Assistant.Application.Scheduling;
using Assistant.Application.Search;
using Assistant.Application.Understanding;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<UserClock>();

        services.AddScoped<UserAccounts>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<ItemSearch>();
        services.AddScoped<ItemResolver>();
        services.AddScoped<PromptBuilder>();
        services.AddScoped<LanguageUnderstanding>();
        services.AddScoped<CommandDispatcher>();
        services.AddScoped<AssistantService>();
        services.AddScoped<ChatHistory>();
        services.AddScoped<Calendar.CalendarImportService>();

        services.AddScoped<ICommandHandler, CreateTaskHandler>();
        services.AddScoped<ICommandHandler, CreateMeetingHandler>();
        services.AddScoped<ICommandHandler, ListItemsHandler>();
        services.AddScoped<ICommandHandler, SearchItemsHandler>();
        services.AddScoped<ICommandHandler, UpdateItemHandler>();
        services.AddScoped<ICommandHandler, DeleteItemHandler>();
        services.AddScoped<ICommandHandler, CompleteItemHandler>();
        services.AddScoped<ICommandHandler, ReplyHandler>();
        services.AddScoped<ICommandHandler, NotUnderstoodHandler>();

        return services;
    }
}
