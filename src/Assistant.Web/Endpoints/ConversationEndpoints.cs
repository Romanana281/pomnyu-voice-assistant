using System.Text.Json;
using System.Text.Json.Nodes;
using Assistant.Application;
using Assistant.Application.Presentation;
using Assistant.Domain;
using Assistant.Web.Errors;
using Assistant.Web.Session;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Assistant.Web.Endpoints;

public static class ConversationEndpoints
{
    private const int MaxImportMessages = 200;

    public static IEndpointRouteBuilder MapConversationApi(this IEndpointRouteBuilder app)
    {
        var conversations = app.MapGroup("/api/conversations");
        conversations.MapGet("", ListAsync);
        conversations.MapGet("/{id}/messages", MessagesAsync);
        conversations.MapDelete("/{id}", DeleteAsync);
        conversations.MapPut("/{id}/messages/{messageId:guid}/card", UpdateCardAsync);
        conversations.MapPost("/{id}/import", ImportAsync);
        return app;
    }

    public sealed record MessageView(
        Guid Id, string Role, string Text, string? Via, string? Kind, DateTimeOffset At, JsonNode? Card, JsonNode? QuickReplies);

    public sealed record ImportMessage(string? Role, string? Text, string? Via, string? Kind, DateTimeOffset? At, JsonObject? Card, JsonArray? QuickReplies);

    public sealed record ImportRequest(IReadOnlyList<ImportMessage>? Messages);

    public static string Payload(AssistantReply reply, JsonOptions json) =>
        new JsonObject
        {
            ["card"] = reply.Card is null ? null : JsonSerializer.SerializeToNode(reply.Card, json.SerializerOptions),
            ["quickReplies"] = JsonSerializer.SerializeToNode(reply.QuickReplies, json.SerializerOptions),
        }.ToJsonString();

    private static async Task<IResult> ListAsync(HttpContext http, UserSession session, ChatHistory history, CancellationToken cancellationToken)
    {
        var user = await session.GetUserAsync(http, cancellationToken);
        return Results.Ok(await history.ListAsync(user.Id, cancellationToken));
    }

    private static async Task<IResult> MessagesAsync(
        string id, HttpContext http, UserSession session, ChatHistory history, CancellationToken cancellationToken)
    {
        if (!Conversation.IsValidId(id))
            return ApiErrors.NotFound("Чат не найден.");

        var user = await session.GetUserAsync(http, cancellationToken);
        var messages = await history.MessagesAsync(user.Id, id, cancellationToken);
        return messages is null ? ApiErrors.NotFound("Чат не найден.") : Results.Ok(messages.Select(View).ToArray());
    }

    private static async Task<IResult> DeleteAsync(
        string id, HttpContext http, UserSession session, ChatHistory history, CancellationToken cancellationToken)
    {
        if (!Conversation.IsValidId(id))
            return ApiErrors.NotFound("Чат не найден.");

        var user = await session.GetUserAsync(http, cancellationToken);
        await history.DeleteAsync(user.Id, id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateCardAsync(
        string id, Guid messageId, JsonObject? card, HttpContext http, UserSession session, ChatHistory history, IOptions<JsonOptions> json, CancellationToken cancellationToken)
    {
        if (card is null || card.ToJsonString().Length > ChatMessage.MaxPayloadLength / 2)
            return ApiErrors.Invalid("card", "Некорректная карточка.");

        var user = await session.GetUserAsync(http, cancellationToken);
        var messages = await history.MessagesAsync(user.Id, id, cancellationToken);
        var message = messages?.FirstOrDefault(candidate => candidate.Id == messageId);
        if (message is null)
            return ApiErrors.NotFound("Сообщение не найдено.");

        var payload = ParsePayload(message.Payload) ?? new JsonObject();
        payload["card"] = card.DeepClone();
        return await history.UpdatePayloadAsync(user.Id, messageId, payload.ToJsonString(), cancellationToken)
            ? Results.NoContent()
            : ApiErrors.NotFound("Сообщение не найдено.");
    }

    private static async Task<IResult> ImportAsync(
        string id, ImportRequest request, HttpContext http, UserSession session, ChatHistory history, CancellationToken cancellationToken)
    {
        if (!Conversation.IsValidId(id))
            return ApiErrors.Invalid("id", "Некорректный идентификатор чата.");
        if (request.Messages is not { Count: > 0 and <= MaxImportMessages } source)
            return ApiErrors.Invalid("messages", $"Нужно от 1 до {MaxImportMessages} сообщений.");

        var messages = new List<IncomingMessage>();
        foreach (var message in source)
        {
            var text = message.Text?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > ChatMessage.MaxTextLength)
                return ApiErrors.Invalid("messages", "Пустое или слишком длинное сообщение.");

            var role = message.Role switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                _ => (ChatRole?)null,
            };
            if (role is null)
                return ApiErrors.Invalid("messages", "Неизвестная роль сообщения.");

            string? payload = null;
            if (role == ChatRole.Assistant)
            {
                payload = new JsonObject { ["card"] = message.Card?.DeepClone(), ["quickReplies"] = message.QuickReplies?.DeepClone() ?? new JsonArray() }.ToJsonString();
                if (payload.Length > ChatMessage.MaxPayloadLength)
                    return ApiErrors.Invalid("messages", "Слишком большая карточка.");
            }

            messages.Add(new IncomingMessage(role.Value, text, message.Via == "voice" ? "voice" : "text", message.Kind, payload, message.At));
        }

        var user = await session.GetUserAsync(http, cancellationToken);
        return await history.ImportAsync(user.Id, id, messages, cancellationToken)
            ? Results.NoContent()
            : Results.Conflict(new ApiError("exists", "Такой чат уже есть."));
    }

    private static MessageView View(ChatMessage message)
    {
        var payload = ParsePayload(message.Payload);
        return new MessageView(
            message.Id,
            message.Role == ChatRole.User ? "user" : "assistant",
            message.Text,
            message.Via,
            message.Kind,
            message.CreatedAt,
            payload?["card"]?.DeepClone(),
            payload?["quickReplies"]?.DeepClone());
    }

    private static JsonObject? ParsePayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return null;
        try
        {
            return JsonNode.Parse(payload) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
