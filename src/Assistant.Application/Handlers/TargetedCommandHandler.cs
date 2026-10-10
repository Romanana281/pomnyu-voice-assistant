using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Assistant.Application.Presentation;
using Assistant.Application.Search;
using Assistant.Domain;

namespace Assistant.Application.Handlers;

public abstract class TargetedCommandHandler<TCommand>(ItemResolver resolver) : CommandHandler<TCommand>
    where TCommand : AssistantCommand
{
    private static readonly string[] OrdinalReplies = ["Первую", "Вторую", "Третью", "Четвёртую", "Пятую"];

    protected abstract ItemRef TargetOf(TCommand command);

    protected abstract Task<AssistantReply> HandleFoundAsync(
        TCommand command, ScheduleItem item, ConversationContext context, CancellationToken cancellationToken);

    protected sealed override async Task<AssistantReply> HandleAsync(
        TCommand command, ConversationContext context, CancellationToken cancellationToken)
    {
        var resolution = await resolver.ResolveAsync(TargetOf(command), context, cancellationToken);
        switch (resolution)
        {
            case Resolution.Found found:
                context.State.RememberItem(found.Item.Id);
                return await HandleFoundAsync(command, found.Item, context, cancellationToken);

            case Resolution.Ambiguous ambiguous:
                context.State.RememberList(ambiguous.Candidates.Select(item => item.Id));
                var options = ambiguous.Candidates.Select(item => ItemView.From(item, context.Today)).ToArray();
                return AssistantReply.Question(
                    "Нашёл несколько подходящих записей. Какую из них?",
                    new ChoiceCard(options),
                    OrdinalReplies.Take(options.Length).ToArray());

            case Resolution.NotFound notFound:
                return AssistantReply.Failure(notFound.Reason);

            default:
                throw new InvalidOperationException($"Неизвестный результат поиска: {resolution}.");
        }
    }
}
