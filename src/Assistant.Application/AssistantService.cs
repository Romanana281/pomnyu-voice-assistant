using System.Collections.Concurrent;
using Assistant.Application.Abstractions;
using Assistant.Application.Commands;
using Assistant.Application.Dialog;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Understanding;
using Assistant.Domain;

namespace Assistant.Application;

public sealed class AssistantService(
    IDialogStateStore dialogs,
    LanguageUnderstanding understanding,
    CommandDispatcher dispatcher,
    UserClock userClock)
{
    public const int MaxMessageLength = 500;
    public const string DefaultConversation = "default";

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UserGates = new();

    public async Task<AssistantReply> HandleAsync(AppUser user, string conversationId, string text, CancellationToken cancellationToken)
    {
        var gate = UserGates.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var context = new ConversationContext(
                user.Id, userClock.LocalNow(user), userClock.TimeZoneOf(user), dialogs.Get(user.Id, conversationId));

            var command = await understanding.InterpretAsync(text, context, cancellationToken);
            if (command is not MultiRequestCommand multi)
                return await ExecuteAsync(text, command, context, cancellationToken);

            var replies = new List<AssistantReply>();
            foreach (var request in multi.Requests)
            {
                var step = await understanding.InterpretAsync(request, context, cancellationToken, allowSeveral: false);
                replies.Add(await ExecuteAsync(request, step, context, cancellationToken));
            }

            return replies[0] with { FollowUps = replies.Skip(1).ToArray() };
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AssistantReply> ExecuteAsync(
        string text, AssistantCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        command = ApplyToDraft(command, context.State);
        command = ResolveConfirmation(command, context.State);
        var reply = await dispatcher.DispatchAsync(command, context, cancellationToken);

        var awaitsAnswer = reply.Kind is ReplyKind.Question or ReplyKind.Confirmation;
        context.State.RememberQuestionContext(awaitsAnswer ? $"{context.State.QuestionContext} {text}".Trim() : null);
        context.State.AppendExchange(text, reply.Text);
        return reply;
    }

    private static AssistantCommand ApplyToDraft(AssistantCommand command, DialogState state)
    {
        var draft = state.PendingConfirmation?.Draft ?? state.PendingDraft;
        state.RememberDraft(null);

        return draft is not null
               && command is UpdateItemCommand { Target: ItemRef.LastMentioned, NewTitle: null } update
               && (update.NewDate is not null || update.NewTime is not null)
            ? draft.WithSchedule(update.NewDate, update.NewTime)
            : command;
    }

    private static AssistantCommand ResolveConfirmation(AssistantCommand command, DialogState state)
    {
        var pending = state.PendingConfirmation;
        state.ClearPendingConfirmation();

        if (command is not ConfirmCommand confirm)
            return command;
        if (pending is null)
            return new ReplyCommand("Сейчас нечего подтверждать. Чем помочь?");

        return pending.CommandFor(confirm.Choice)
            ?? new ReplyCommand(confirm.Choice == ConfirmChoice.Reject ? pending.RejectReply : "Хорошо, ничего не меняю.");
    }
}
