using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Assistant.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Assistant.Infrastructure.GigaChat;

public sealed class GigaChatClient(
    HttpClient http,
    IOptions<GigaChatOptions> options,
    ILogger<GigaChatClient> logger) : ILlmClient, IEmbeddingProvider
{
    private const double Temperature = 0.1;
    private static readonly TimeSpan EmbeddingsRetryAfter = TimeSpan.FromHours(1);
    private static long _embeddingsBlockedUntilTicks;
    private static readonly SemaphoreSlim ConcurrencyGate = new(1, 1);
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        try
        {
            return await CompleteAsync(request, settings.ChatModel, cancellationToken);
        }
        catch (PaymentRequiredException) when (settings.FallbackChatModel is { Length: > 0 } fallback && fallback != settings.ChatModel)
        {
            logger.LogWarning("Модель {Model} недоступна по тарифу, используем {Fallback}", settings.ChatModel, fallback);
            return await CompleteAsync(request, fallback, cancellationToken);
        }
    }

    private async Task<LlmResponse> CompleteAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        var body = new ChatRequest(
            model,
            request.Messages.Select(message => new ChatMessage(RoleName(message.Role), message.Content)).ToArray(),
            request.Functions.Select(ToFunction).ToArray(),
            request.ForcedFunction is { } forced
                ? new JsonObject { ["name"] = forced }
                : request.Functions.Count > 0 ? JsonValue.Create("auto") : null,
            Temperature);

        var response = await PostAsync<ChatResponse>("chat/completions", body, cancellationToken);
        var message = response.Choices.FirstOrDefault()?.Message
            ?? throw new LlmUnavailableException("GigaChat вернул ответ без вариантов.");

        if (message.FunctionCall is { } call)
        {
            logger.LogDebug("GigaChat вызвал {Function} с {Arguments}", call.Name, call.Arguments);
            return new LlmResponse(null, new LlmFunctionCall(call.Name, ParseArguments(call.Arguments)));
        }

        return new LlmResponse(message.Content, null);
    }

    public bool IsAvailable => DateTimeOffset.UtcNow >= new DateTimeOffset(Volatile.Read(ref _embeddingsBlockedUntilTicks), TimeSpan.Zero);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
            return [];
        if (!IsAvailable)
            throw new LlmUnavailableException("Эмбеддинги GigaChat недоступны по тарифу.");

        EmbeddingsResponse response;
        try
        {
            response = await PostAsync<EmbeddingsResponse>(
                "embeddings", new EmbeddingsRequest(options.Value.EmbeddingModel, texts), cancellationToken);
        }
        catch (PaymentRequiredException)
        {
            Volatile.Write(ref _embeddingsBlockedUntilTicks, DateTimeOffset.UtcNow.Add(EmbeddingsRetryAfter).UtcTicks);
            logger.LogWarning("Эмбеддинги недоступны по тарифу — поиск работает по словам и синонимам ещё {Hours} ч", EmbeddingsRetryAfter.TotalHours);
            throw;
        }

        if (response.Data.Count != texts.Count)
            throw new LlmUnavailableException($"GigaChat вернул {response.Data.Count} эмбеддингов вместо {texts.Count}.");

        return response.Data.OrderBy(item => item.Index).Select(item => item.Embedding).ToArray();
    }

    private async Task<TResponse> PostAsync<TResponse>(string path, object body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        if (!await ConcurrencyGate.WaitAsync(QueueTimeout, cancellationToken))
            throw new LlmUnavailableException("GigaChat перегружен: слишком много запросов в очереди.");
        try
        {
            using var response = await http.PostAsync(path, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("GigaChat {Path}: HTTP {Status} {Details}", path, (int)response.StatusCode, details);
                if (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired)
                    throw new PaymentRequiredException(path);
                throw new LlmUnavailableException($"GigaChat ответил HTTP {(int)response.StatusCode} на {path}.");
            }

            return await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                ?? throw new LlmUnavailableException($"GigaChat вернул пустой ответ на {path}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException
                                              or BrokenCircuitException or JsonException)
        {
            throw new LlmUnavailableException($"GigaChat недоступен ({path}).", exception);
        }
        finally
        {
            ConcurrencyGate.Release();
        }
    }

    private sealed class PaymentRequiredException(string path)
        : LlmUnavailableException($"GigaChat: модель недоступна по тарифу ({path}).");

    private static JsonElement ParseArguments(JsonElement? arguments) => arguments switch
    {
        { ValueKind: JsonValueKind.String } text => JsonDocument.Parse(text.GetString()!).RootElement.Clone(),
        { } element => element,
        null => JsonDocument.Parse("{}").RootElement.Clone(),
    };

    private static ChatFunction ToFunction(LlmFunction function) => new(
        function.Name,
        function.Description,
        function.Parameters,
        function.Examples.Select(example => new FewShotExample(example.Request, example.Arguments)).ToArray());

    private static string RoleName(LlmRole role) => role switch
    {
        LlmRole.System => "system",
        LlmRole.User => "user",
        LlmRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("functions")] IReadOnlyList<ChatFunction> Functions,
        [property: JsonPropertyName("function_call")] JsonNode? FunctionCall,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatFunction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonObject Parameters,
        [property: JsonPropertyName("few_shot_examples")] IReadOnlyList<FewShotExample> FewShotExamples);

    private sealed record FewShotExample(
        [property: JsonPropertyName("request")] string Request,
        [property: JsonPropertyName("params")] JsonObject Params);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("function_call")] FunctionCallPayload? FunctionCall);

    private sealed record FunctionCallPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] JsonElement? Arguments);

    private sealed record EmbeddingsRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingItem> Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("embedding")] float[] Embedding,
        [property: JsonPropertyName("index")] int Index);
}
