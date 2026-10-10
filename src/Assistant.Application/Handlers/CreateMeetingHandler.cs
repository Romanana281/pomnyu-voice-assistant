using Assistant.Application.Commands;
using Assistant.Application.Dialog;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Scheduling;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public sealed class CreateMeetingHandler(ScheduleService schedule, TimeProvider clock) : CommandHandler<CreateMeetingCommand>
{
    private const int SuggestedSlots = 4;

    protected override async Task<AssistantReply> HandleAsync(
        CreateMeetingCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        var duration = command.Duration ?? ScheduleItem.DefaultMeetingDuration;
        if (command.Date is null && command.Time is { } onlyTime)
            command = command with { Date = onlyTime > TimeOnly.FromDateTime(context.LocalNow) ? context.Today : context.Today.AddDays(1) };
        if (command.Date is not { } date)
        {
            context.State.RememberDraft(command);
            return AssistantReply.Question(
                $"Записываю встречу «{command.Title}». На какой день её поставить?",
                new DraftCard(ItemType.Meeting, command.Title, null, null, MissingField.Date, []),
                ["Сегодня", "Завтра", "Послезавтра"]);
        }

        if (command.Time is not { } time)
        {
            var slots = await schedule.FindFreeSlotsAsync(
                context.UserId, date, duration, context.LocalNow, SuggestedSlots, cancellationToken);
            context.State.RememberDraft(command);
            return AssistantReply.Question(
                $"Записываю встречу «{command.Title}» на {RussianText.WhenAccusative(date, context.Today)}. На какое время поставить?",
                new DraftCard(ItemType.Meeting, command.Title, date, null, MissingField.Time, slots));
        }

        var meeting = ScheduleItem.CreateMeeting(
            context.UserId, command.Title, date, time, duration, command.Recurrence, clock.GetUtcNow());

        if (!command.IgnoreConflicts)
        {
            var conflicts = await schedule.FindConflictsAsync(meeting, cancellationToken);
            if (conflicts.Count > 0)
                return await AskAboutConflictAsync(command, meeting, conflicts, context, cancellationToken);
        }

        await schedule.AddAsync(meeting, cancellationToken);
        context.State.RememberItem(meeting.Id);

        var occurrence = meeting.NextOccurrence(context.Today);
        return AssistantReply.Done(
            $"Добавил встречу {ItemPhrases.Describe(occurrence, context.Today)}.",
            new SavedCard(ItemView.From(occurrence, context.Today), SavedAction.Created));
    }

    private async Task<AssistantReply> AskAboutConflictAsync(
        CreateMeetingCommand command,
        ScheduleItem meeting,
        IReadOnlyList<Occurrence> conflicts,
        ConversationContext context,
        CancellationToken cancellationToken)
    {
        var first = conflicts[0];
        var time = meeting.Time!.Value;
        var suggested = await schedule.SuggestFreeStartAsync(meeting, cancellationToken);
        var clash = $"В {RussianText.Clock(first.Time!.Value)} у вас {ItemPhrases.Quoted(first.Item)} — время пересекается.";
        var keep = command with { IgnoreConflicts = true };

        PendingConfirmation pending;
        string question;
        if (suggested is { } free)
        {
            var freeClock = RussianText.Clock(free);
            question = $"{clash} Поставить «{command.Title}» на {freeClock}, сразу после?";
            pending = new PendingConfirmation(
                question,
                keep with { Time = free },
                keep,
                "Хорошо, встречу не ставлю.",
                new Dictionary<string, ConfirmChoice>
                {
                    [$"Да, на {freeClock}"] = ConfirmChoice.Accept,
                    [$"Оставь в {RussianText.Clock(time)}"] = ConfirmChoice.Alternative,
                    ["Не нужно, отмени"] = ConfirmChoice.Reject,
                },
                command);
        }
        else
        {
            question = $"{clash} Свободного времени рядом нет. Всё равно поставить на {RussianText.Clock(time)}?";
            pending = new PendingConfirmation(
                question,
                keep,
                null,
                "Хорошо, встречу не ставлю.",
                new Dictionary<string, ConfirmChoice>
                {
                    [$"Да, на {RussianText.Clock(time)}"] = ConfirmChoice.Accept,
                    ["Не нужно, отмени"] = ConfirmChoice.Reject,
                },
                command);
        }

        context.State.AskConfirmation(pending);
        var card = new ConflictCard(
            meeting.Date!.Value,
            command.Title,
            time,
            (int)meeting.Duration!.Value.TotalMinutes,
            suggested,
            conflicts.Select(conflict => ItemView.From(conflict, context.Today)).ToArray());
        return AssistantReply.Confirmation(question, card, pending.QuickReplies.Keys.ToArray());
    }
}
