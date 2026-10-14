using System.Net;
using Microsoft.Extensions.Configuration;

namespace Kairo.SharedKernel.Http;

public sealed record SafeFetchOptions(long MaxBytes, TimeSpan Timeout, bool HtmlOnly = false);

public sealed record SafeWebResponse(Uri Url, string? ContentType, byte[] Body, string? Charset = null);

public sealed class SafeWebClient(IHttpClientFactory http, IConfiguration configuration)
{
    public const string ClientName = "kairo-web";
    public const int MaxRedirects = 3;

    private static readonly HashSet<HttpStatusCode> Redirects =
    [
        HttpStatusCode.MovedPermanently,
        HttpStatusCode.Found,
        HttpStatusCode.SeeOther,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect,
    ];

    private string UserAgent => $"KairoBot/1.0 (+{configuration["Kairo:PublicOrigin"]})";

    public static bool IsAllowed(Uri url) =>
        url.IsAbsoluteUri
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && (url.Port == 80 || url.Port == 443)
        && url.UserInfo.Length == 0;

    public async Task<SafeWebResponse> FetchAsync(Uri url, SafeFetchOptions options, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Timeout);
        try
        {
            return await FollowAsync(url, options, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SafeFetchException(SafeFetchErrors.Timeout, $"{url} didn't answer within {options.Timeout.TotalSeconds:0.#} seconds.");
        }
        catch (HttpRequestException exception)
        {
            throw BlockedIn(exception) ?? new SafeFetchException(SafeFetchErrors.Unreachable, $"Couldn't reach {url.Host}.", exception);
        }
        catch (IOException exception)
        {
            throw new SafeFetchException(SafeFetchErrors.Unreachable, $"Couldn't finish reading {url}.", exception);
        }
    }

    private async Task<SafeWebResponse> FollowAsync(Uri url, SafeFetchOptions options, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(ClientName);
        var current = url;
        for (var hop = 0; ; hop++)
        {
            if (!IsAllowed(current))
            {
                throw new SafeFetchException(SafeFetchErrors.BlockedAddress, $"Kairo only reads http and https addresses on the usual ports, not {current}.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", options.HtmlOnly ? "text/html,application/xhtml+xml;q=0.9,*/*;q=0.1" : "*/*");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (Redirects.Contains(response.StatusCode) && response.Headers.Location is { } location)
            {
                if (hop == MaxRedirects)
                {
                    throw new SafeFetchException(SafeFetchErrors.BadStatus, $"{url} redirected more than {MaxRedirects} times.");
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SafeFetchException(SafeFetchErrors.BadStatus, $"{current} answered {(int)response.StatusCode}.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"', ' ');
            if (options.HtmlOnly && !string.Equals(contentType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                throw new SafeFetchException(SafeFetchErrors.NotHtml, $"{current} isn't a web page.");
            }

            var body = await ReadAsync(response.Content, options.MaxBytes, current, cancellationToken);
            return new SafeWebResponse(current, contentType, body, string.IsNullOrEmpty(charset) ? null : charset);
        }
    }

    private static async Task<byte[]> ReadAsync(HttpContent content, long maxBytes, Uri url, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw TooLarge(url);
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw TooLarge(url);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static SafeFetchException TooLarge(Uri url) =>
        new(SafeFetchErrors.TooLarge, $"{url} is larger than Kairo reads.");

    private static SafeFetchException? BlockedIn(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SafeFetchException blocked)
            {
                return blocked;
            }
        }

        return null;
    }
}
