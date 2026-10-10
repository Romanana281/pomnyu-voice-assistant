using System.ComponentModel.DataAnnotations;

namespace Assistant.Infrastructure.GigaChat;

public sealed class GigaChatOptions
{
    public const string SectionName = "GigaChat";

    [Required(ErrorMessage = "Не задан ключ GigaChat: переменная окружения GigaChat__AuthKey.")]
    public string AuthKey { get; init; } = string.Empty;

    [Required]
    public string Scope { get; init; } = "GIGACHAT_API_PERS";

    [Required]
    public Uri AuthUrl { get; init; } = new("https://ngw.devices.sberbank.ru:9443/api/v2/oauth");

    [Required]
    public Uri BaseUrl { get; init; } = new("https://gigachat.devices.sberbank.ru/api/v1/");

    [Required]
    public string ChatModel { get; init; } = "GigaChat-2-Pro";

    public string? FallbackChatModel { get; init; } = "GigaChat-2";

    [Required]
    public string EmbeddingModel { get; init; } = "Embeddings";
}
