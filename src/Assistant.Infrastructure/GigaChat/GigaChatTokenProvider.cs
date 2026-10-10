using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Assistant.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure.GigaChat;

public sealed class GigaChatTokenProvider(
    IHttpClientFactory httpClients,
    IOptions<GigaChatOptions> options,
    TimeProvider clock)
{
    public const string HttpClientName = "GigaChatAuth";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AccessToken? _token;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_token))
            return _token!.Value;

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsFresh(_token))
                _token = await RequestTokenAsync(cancellationToken);
            return _token!.Value;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Invalidate(string rejectedToken)
    {
        if (_token?.Value == rejectedToken)
            _token = null;
    }

    private bool IsFresh(AccessToken? token) => token is not null && token.ExpiresAt - RefreshMargin > clock.GetUtcNow();

    private async Task<AccessToken> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.AuthUrl)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("scope", settings.Scope)]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", settings.AuthKey);
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmUnavailableException(
                    $"GigaChat не выдал токен: HTTP {(int)response.StatusCode}. Проверьте ключ авторизации и scope.");
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new LlmUnavailableException("GigaChat вернул пустой ответ на запрос токена.");
            return new AccessToken(token.AccessToken, DateTimeOffset.FromUnixTimeMilliseconds(token.ExpiresAt));
        }
        catch (HttpRequestException exception)
        {
            throw new LlmUnavailableException("Сервер авторизации GigaChat недоступен.", exception);
        }
    }

    private sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_at")] long ExpiresAt);
}
