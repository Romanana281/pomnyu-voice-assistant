using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;
using Assistant.Application.Search;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public sealed class UpdateItemHandler(ItemResolver resolver, ScheduleService schedule)
    : TargetedCommandHandler<UpdateItemCommand>(resolver)
{
    protected override ItemRef TargetOf(UpdateItemCommand command) => command.Target;

    protected override async Task<AssistantReply> HandleFoundAsync(
        UpdateItemCommand command, ScheduleItem item, ConversationContext context, CancellationToken cancellationToken)
    {
        var reschedules = command.NewDate is not null || command.NewTime is not null || command.NewDuration is not null;
        if (command.NewTitle is null && !reschedules)
            return AssistantReply.Question($"Что изменить в {ItemPhrases.Quoted(item)}: название, день или время?");

        if (command.NewDuration is not null && item.Type != ItemType.Meeting && command.NewDate is null && command.NewTime is null && command.NewTitle is null)
            return AssistantReply.Failure("Длительность можно задать только встрече.");

        var previousDate = item.Date;
        var previousTime = item.Time;
        var previousTitle = item.Title;
        var date = command.NewDate ?? item.Date ?? (command.NewTime is not null ? context.Today : null);
        var time = command.NewTime ?? item.Time;
        var previousDuration = item.Duration;
        if (command.NewTitle is null && date == previousDate && time == previousTime
            && (command.NewDuration is null || command.NewDuration == previousDuration))
        {
            var current = item.NextOccurrence(context.Today);
            return AssistantReply.Done(
                $"{RussianText.Capitalize(RussianText.TypeName(item.Type))} {ItemPhrases.Quoted(item)} и так стоит на {RussianText.When(item.Date, item.Time, context.Today)} — ничего не менял.",
                new SavedCard(ItemView.From(current, context.Today), SavedAction.Created));
        }

        await schedule.UpdateAsync(item, command.NewTitle, date, time, command.NewDuration, cancellationToken);

        var occurrence = item.NextOccurrence(context.Today);
        var type = RussianText.TypeNameAccusative(item.Type);
        var text = !reschedules
            ? $"Переименовал: теперь {ItemPhrases.Quoted(item)}."
            : item.Date is null
                ? $"Обновил {ItemPhrases.Quoted(item)}."
            : previousDate == item.Date && item.Time is { } newTime
                ? $"Перенёс {type} {ItemPhrases.Quoted(item)} на {RussianText.Clock(newTime)}."
                : $"Перенёс {type} {ItemPhrases.Quoted(item)} на {RussianText.WhenAccusative(item.Date!.Value, context.Today)}{(item.Time is { } clock ? $", {RussianText.Clock(clock)}" : "")}.";

        var conflicts = reschedules ? await schedule.FindConflictsAsync(item, cancellationToken) : [];
        if (conflicts.Count > 0)
            text += $" Учтите: пересекается с {ItemPhrases.Describe(conflicts[0], context.Today)}.";

        var card = new ChangedCard(
            ItemView.From(occurrence, context.Today),
            previousDate,
            previousTime,
            command.NewTitle is null ? null : previousTitle);
        return AssistantReply.Done(text, card);
    }
}
