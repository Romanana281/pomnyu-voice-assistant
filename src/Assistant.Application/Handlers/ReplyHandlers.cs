using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;

namespace Assistant.Application.Handlers;

public sealed class ReplyHandler : CommandHandler<ReplyCommand>
{
    protected override Task<AssistantReply> HandleAsync(
        ReplyCommand command, ConversationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(AssistantReply.Done(command.Text));
}

public sealed class NotUnderstoodHandler : CommandHandler<NotUnderstoodCommand>
{
    protected override Task<AssistantReply> HandleAsync(
        NotUnderstoodCommand command, ConversationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(AssistantReply.Failure(
            "Не совсем понял. Скажите иначе, например: «Добавь встречу с командой завтра в 15:00»."));
}
