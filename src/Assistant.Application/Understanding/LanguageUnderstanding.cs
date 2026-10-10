using Assistant.Application.Abstractions;
using Assistant.Application.Commands;
using Assistant.Application.Handling;
using Microsoft.Extensions.Logging;

namespace Assistant.Application.Understanding;

public sealed class LanguageUnderstanding(ILlmClient llm, PromptBuilder prompts, ILogger<LanguageUnderstanding> logger)
{
    private async Task<IReadOnlyList<string>?> SplitAsync(string text, ConversationContext context, CancellationToken cancellationToken)
    {
        var request = await prompts.BuildAsync(text, context, allowSeveral: true, cancellationToken);
        var response = await llm.CompleteAsync(
            request with { Functions = [AssistantFunctions.Splitter], ForcedFunction = AssistantFunctions.SeveralRequests },
            cancellationToken);
        if (response.FunctionCall is not { } call
            || CommandParser.Parse(call, context.LocalNow) is not ParseResult.Parsed { Command: MultiRequestCommand multi })
        {
            return null;
        }

        logger.LogInformation("Реплика разбита на {Count} просьбы: {Requests}", multi.Requests.Count, string.Join(" | ", multi.Requests));
        return multi.Requests;
    }

    public async Task<AssistantCommand> InterpretAsync(string text, ConversationContext context, CancellationToken cancellationToken, bool allowSeveral = true)
    {
        if (context.State.PendingConfirmation is { } pending && ConfirmationWords.TryParse(text, pending) is { } choice)
            return new ConfirmCommand(choice);
        if (context.State.PendingConfirmation is null && ConfirmationWords.IsBareAnswer(text))
            return new ReplyCommand("Хорошо. Что запланировать или напомнить?");

        if (allowSeveral && MultiIntentDetector.MaybeSeveral(text) && await SplitAsync(text, context, cancellationToken) is { Count: > 1 } parts)
            return new MultiRequestCommand(parts);

        var request = await prompts.BuildAsync(text, context, allowSeveral: false, cancellationToken);
        var response = await llm.CompleteAsync(request, cancellationToken);

        if (response.FunctionCall is not { } call)
        {
            return string.IsNullOrWhiteSpace(response.Text)
                ? new NotUnderstoodCommand()
                : new ReplyCommand(response.Text.Trim());
        }

        switch (CommandParser.Parse(call, context.LocalNow))
        {
            case ParseResult.Parsed { Command: MultiRequestCommand multi } when !allowSeveral || multi.Requests.Count < 2:
                return multi.Requests.Count == 1 && allowSeveral
                    ? await InterpretAsync(multi.Requests[0], context, cancellationToken, allowSeveral: false)
                    : new NotUnderstoodCommand();
            case ParseResult.Parsed parsed:
                var command = IntentOverrides.Apply(MentionGuard.Apply(parsed.Command, $"{context.State.QuestionContext} {text}"), text, context.Today);
                logger.LogInformation("Реплика понята как {Command}", command);
                return command;
            case ParseResult.Invalid invalid:
                logger.LogWarning(
                    "Модель вызвала {Function} с неверными аргументами {Arguments}: {Errors}",
                    call.Name, call.Arguments.GetRawText(), string.Join("; ", invalid.Errors));
                return new NotUnderstoodCommand();
            default:
                throw new InvalidOperationException("Неизвестный результат разбора.");
        }
    }
}
