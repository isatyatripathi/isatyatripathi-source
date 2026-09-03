using System.Net;
using System.Text;

namespace DevSignalStudio.Infrastructure.Security;

public sealed class SafeHttpFetcher : IDisposable
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private readonly UrlSafetyValidator _urlSafety;
    private readonly HttpClient _client;

    public SafeHttpFetcher(UrlSafetyValidator urlSafety)
    {
        _urlSafety = urlSafety;
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("DevSignal-Studio/0.1 (+local content workspace)");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/json, text/xml, */*;q=0.5");
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        Uri current = await _urlSafety.ValidateAsync(url, allowLoopback: false, cancellationToken);
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            using HttpResponseMessage response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or
                HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
                HttpStatusCode.PermanentRedirect)
            {
                Uri? location = response.Headers.Location;
                if (location is null)
                {
                    throw new HttpRequestException("The source returned a redirect without a Location header.");
                }
                Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                current = await _urlSafety.ValidateAsync(next.AbsoluteUri, allowLoopback: false, cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length > MaximumResponseBytes)
            {
                throw new InvalidDataException($"The response is larger than {MaximumResponseBytes} bytes.");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] block = new byte[16 * 1024];
            int total = 0;
            while (true)
            {
                int read = await stream.ReadAsync(block.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }
                total += read;
                if (total > MaximumResponseBytes)
                {
                    throw new InvalidDataException($"The response exceeded {MaximumResponseBytes} bytes.");
                }
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        throw new HttpRequestException("The source returned too many redirects.");
    }

    public void Dispose() => _client.Dispose();
}
