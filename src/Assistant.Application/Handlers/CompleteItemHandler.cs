using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;
using Assistant.Application.Search;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public sealed class CompleteItemHandler(ItemResolver resolver, ScheduleService schedule)
    : TargetedCommandHandler<CompleteItemCommand>(resolver)
{
    protected override ItemRef TargetOf(CompleteItemCommand command) => command.Target;

    protected override async Task<AssistantReply> HandleFoundAsync(
        CompleteItemCommand command, ScheduleItem item, ConversationContext context, CancellationToken cancellationToken)
    {
        var occurrence = item.NextOccurrence(context.Today);
        if (occurrence.IsDone)
        {
            return AssistantReply.Done(
                $"{ItemPhrases.Quoted(item)} уже отмечено как выполненное.",
                new SavedCard(ItemView.From(occurrence, context.Today), SavedAction.Completed));
        }

        await schedule.CompleteAsync(item, occurrence.Date, cancellationToken);

        var text = item.IsRecurring && occurrence.Date is { } date
            ? $"Отметил {ItemPhrases.Quoted(item)} выполненным на {RussianText.CalendarDate(date, context.Today)}."
            : $"Отметил {ItemPhrases.Quoted(item)} выполненным.";
        return AssistantReply.Done(
            text, new SavedCard(ItemView.From(item.NextOccurrence(context.Today), context.Today), SavedAction.Completed));
    }
}
