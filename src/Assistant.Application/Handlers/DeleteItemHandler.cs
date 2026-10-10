using Assistant.Application.Commands;
using Assistant.Application.Dialog;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;
using Assistant.Application.Search;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public sealed class DeleteItemHandler(ItemResolver resolver, ScheduleService schedule)
    : TargetedCommandHandler<DeleteItemCommand>(resolver)
{
    protected override ItemRef TargetOf(DeleteItemCommand command) => command.Target;

    protected override async Task<AssistantReply> HandleFoundAsync(
        DeleteItemCommand command, ScheduleItem item, ConversationContext context, CancellationToken cancellationToken)
    {
        var type = RussianText.TypeNameAccusative(item.Type);
        var view = ItemView.From(item, context.Today);

        if (!command.Confirmed)
        {
            var series = item.IsRecurring ? " со всеми повторами" : "";
            var question = $"Удалить {type} {ItemPhrases.Describe(item.NextOccurrence(context.Today), context.Today)}{series}?";
            var pending = new PendingConfirmation(
                question,
                new DeleteItemCommand(new ItemRef.ById(item.Id), Confirmed: true),
                null,
                "Хорошо, оставляю.",
                new Dictionary<string, ConfirmChoice>
                {
                    ["Да, удали"] = ConfirmChoice.Accept,
                    ["Нет, оставь"] = ConfirmChoice.Reject,
                });
            context.State.AskConfirmation(pending);
            return AssistantReply.Confirmation(question, new ConfirmDeleteCard(view), pending.QuickReplies.Keys.ToArray());
        }

        await schedule.CancelAsync(item, cancellationToken);
        return AssistantReply.Done($"Удалил {type} {ItemPhrases.Quoted(item)}.", new SavedCard(view, SavedAction.Deleted));
    }
}
