using System.Text.Json;
using System.Text.Json.Nodes;

namespace Assistant.Application.Abstractions;

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

public enum LlmRole
{
    System,
    User,
    Assistant,
}

public sealed record LlmMessage(LlmRole Role, string Content);

public sealed record LlmFunction(
    string Name,
    string Description,
    JsonObject Parameters,
    IReadOnlyList<LlmFewShotExample> Examples);

public sealed record LlmFewShotExample(string Request, JsonObject Arguments);

public sealed record LlmRequest(IReadOnlyList<LlmMessage> Messages, IReadOnlyList<LlmFunction> Functions, string? ForcedFunction = null);

public sealed record LlmResponse(string? Text, LlmFunctionCall? FunctionCall);

public sealed record LlmFunctionCall(string Name, JsonElement Arguments);
