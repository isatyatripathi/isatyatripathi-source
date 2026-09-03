using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevSignalStudio.Infrastructure.Security;

namespace DevSignalStudio.Infrastructure.Ai;

public sealed class AiHttpClient : IDisposable
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private readonly UrlSafetyValidator _urlSafety;
    private readonly HttpClient _client;

    public AiHttpClient(UrlSafetyValidator urlSafety)
    {
        _urlSafety = urlSafety;
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("DevSignal-Studio/0.1");
    }

    public async Task<JsonDocument> PostAsync(
        string url,
        object body,
        IReadOnlyDictionary<string, string>? headers,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Uri uri = await _urlSafety.ValidateAsync(url, allowLoopback: true, cancellationToken);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(NormalizeTimeout(timeout));
        using HttpRequestMessage request = new(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body)
        };
        ApplyHeaders(request, headers);

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
        return await ReadJsonResponseAsync(response, timeoutSource.Token);
    }

    public async Task<JsonDocument> GetAsync(
        string url,
        IReadOnlyDictionary<string, string>? headers,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Uri uri = await _urlSafety.ValidateAsync(url, allowLoopback: true, cancellationToken);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(NormalizeTimeout(timeout));
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        ApplyHeaders(request, headers);

        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
        return await ReadJsonResponseAsync(response, timeoutSource.Token);
    }

    private static async Task<JsonDocument> ReadJsonResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string responseText = await ReadBoundedAsync(response.Content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"AI endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(responseText, 500)}");
        }

        try
        {
            return JsonDocument.Parse(responseText);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The AI endpoint returned malformed JSON.", exception);
        }
    }

    private static void ApplyHeaders(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach ((string name, string value) in headers)
        {
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(value);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    private static TimeSpan NormalizeTimeout(TimeSpan timeout) =>
        timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(90) : timeout;

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > MaximumResponseBytes)
        {
            throw new InvalidDataException("AI response exceeded the maximum allowed size.");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] bytes = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumResponseBytes)
            {
                throw new InvalidDataException("AI response exceeded the maximum allowed size.");
            }
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length] + "...";

    public void Dispose() => _client.Dispose();
}
