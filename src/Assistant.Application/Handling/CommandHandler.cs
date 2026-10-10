using Assistant.Application.Commands;
using Assistant.Application.Presentation;

namespace Assistant.Application.Handling;

public interface ICommandHandler
{
    Type CommandType { get; }

    Task<AssistantReply> HandleAsync(AssistantCommand command, ConversationContext context, CancellationToken cancellationToken);
}

public abstract class CommandHandler<TCommand> : ICommandHandler
    where TCommand : AssistantCommand
{
    public Type CommandType => typeof(TCommand);

    public Task<AssistantReply> HandleAsync(
        AssistantCommand command, ConversationContext context, CancellationToken cancellationToken) =>
        HandleAsync((TCommand)command, context, cancellationToken);

    protected abstract Task<AssistantReply> HandleAsync(
        TCommand command, ConversationContext context, CancellationToken cancellationToken);
}
