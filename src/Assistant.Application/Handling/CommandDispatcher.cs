using Assistant.Application.Commands;
using Assistant.Application.Presentation;

namespace Assistant.Application.Handling;

public sealed class CommandDispatcher
{
    private readonly IReadOnlyDictionary<Type, ICommandHandler> _handlers;

    public CommandDispatcher(IEnumerable<ICommandHandler> handlers) =>
        _handlers = handlers.ToDictionary(handler => handler.CommandType);

    public Task<AssistantReply> DispatchAsync(
        AssistantCommand command, ConversationContext context, CancellationToken cancellationToken) =>
        _handlers.TryGetValue(command.GetType(), out var handler)
            ? handler.HandleAsync(command, context, cancellationToken)
            : throw new InvalidOperationException($"Нет обработчика для {command.GetType().Name}.");
}
