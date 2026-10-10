using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Assistant.Application.Abstractions;

namespace Assistant.Infrastructure.Calendar;

public sealed class SafeCalendarFetcher(HttpClient http) : ICalendarFetcher
{
    public const string HttpClientName = "CalendarFetcher";
    public const int MaxBytes = 5 * 1024 * 1024;

    public async Task<string> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new CalendarFetchException($"Календарь ответил ошибкой HTTP {(int)response.StatusCode}. Проверьте, что ссылка публичная или секретная iCal-ссылка.");
            if (response.Content.Headers.ContentLength > MaxBytes)
                throw new CalendarFetchException("Календарь слишком большой (больше 5 МБ).");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                    throw new CalendarFetchException("Календарь слишком большой (больше 5 МБ).");
                buffer.Write(chunk, 0, read);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (HttpRequestException exception)
        {
            throw new CalendarFetchException("Не удалось скачать календарь по ссылке.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CalendarFetchException("Календарь не ответил вовремя.", exception);
        }
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = ConnectToPublicAddressAsync,
    };

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var target = addresses.FirstOrDefault(IsPublic)
            ?? throw new HttpRequestException("Адрес календаря ведёт во внутреннюю сеть — такие ссылки не поддерживаются.");

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (address.GetAddressBytes()[0] & 0xFE) == 0xFC);

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            >= 224 => false,
            _ => true,
        };
    }
}
