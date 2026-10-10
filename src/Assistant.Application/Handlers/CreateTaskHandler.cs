using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public sealed class CreateTaskHandler(ScheduleService schedule, TimeProvider clock) : CommandHandler<CreateTaskCommand>
{
    private const int SuggestedSlots = 4;
    private static readonly TimeSpan ReminderSlot = TimeSpan.FromMinutes(15);

    protected override async Task<AssistantReply> HandleAsync(
        CreateTaskCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        if (command.Kind == ItemType.Reminder && command.Time is null)
            return await AskReminderTimeAsync(command, context, cancellationToken);

        var item = ScheduleItem.CreateTask(
            context.UserId,
            command.Kind,
            command.Title,
            command.Date ?? DateForTimeOnly(command.Time, context),
            command.Time,
            command.Recurrence,
            clock.GetUtcNow());

        await schedule.AddAsync(item, cancellationToken);
        context.State.RememberItem(item.Id);

        var occurrence = item.NextOccurrence(context.Today);
        var when = ItemPhrases.When(occurrence, context.Today);
        var text = item.Type == ItemType.Reminder
            ? $"Готово, напомню {when}: {item.Title}."
            : item.Date is null
                ? $"Записал задачу {ItemPhrases.Quoted(item)} без срока."
                : $"Записал задачу {ItemPhrases.Quoted(item)} — {when}.";
        return AssistantReply.Done(text, new SavedCard(ItemView.From(occurrence, context.Today), SavedAction.Created));
    }

    private async Task<AssistantReply> AskReminderTimeAsync(
        CreateTaskCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        if (command.Date is not { } date)
        {
            context.State.RememberDraft(command);
            return AssistantReply.Question(
                $"Когда напомнить «{command.Title}»: в какой день и во сколько?",
                new DraftCard(ItemType.Reminder, command.Title, null, null, MissingField.Date, []),
                ["Сегодня", "Завтра", "Послезавтра"]);
        }

        var slots = await schedule.FindFreeSlotsAsync(
            context.UserId, date, ReminderSlot, context.LocalNow, SuggestedSlots, cancellationToken);
        context.State.RememberDraft(command);
        return AssistantReply.Question(
            $"Во сколько напомнить {RussianText.When(date, context.Today)}?",
            new DraftCard(ItemType.Reminder, command.Title, date, null, MissingField.Time, slots));
    }

    private static DateOnly? DateForTimeOnly(TimeOnly? time, ConversationContext context) =>
        time is { } clock
            ? clock > TimeOnly.FromDateTime(context.LocalNow) ? context.Today : context.Today.AddDays(1)
            : null;
}
