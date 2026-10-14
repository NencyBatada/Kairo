using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Kairo.IntegrationTests.Web;

public sealed record FakePage(HttpStatusCode Status, string ContentType, byte[] Body, string? Location = null, TimeSpan Delay = default, bool Unsized = false);

public sealed class FakeWebsites : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, FakePage> _pages = new();

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> UserAgents { get; } = new();

    public void Html(string url, string html) =>
        _pages[url] = new FakePage(HttpStatusCode.OK, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    public void Text(string url, string contentType, string text) =>
        _pages[url] = new FakePage(HttpStatusCode.OK, contentType, Encoding.UTF8.GetBytes(text));

    public void File(string url, string contentType, byte[] body) =>
        _pages[url] = new FakePage(HttpStatusCode.OK, contentType, body);

    public void Unsized(string url, string contentType, byte[] body) =>
        _pages[url] = new FakePage(HttpStatusCode.OK, contentType, body, Unsized: true);

    public void Redirect(string url, string location, HttpStatusCode status = HttpStatusCode.Found) =>
        _pages[url] = new FakePage(status, "text/html", [], location);

    public void Status(string url, HttpStatusCode status) =>
        _pages[url] = new FakePage(status, "text/html", []);

    public void Slow(string url, string html, TimeSpan delay) =>
        _pages[url] = new FakePage(HttpStatusCode.OK, "text/html", Encoding.UTF8.GetBytes(html), Delay: delay);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Enqueue(url);
        UserAgents.Enqueue(request.Headers.UserAgent.ToString());
        if (!_pages.TryGetValue(url, out var page))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
        }

        if (page.Delay > TimeSpan.Zero)
        {
            await Task.Delay(page.Delay, cancellationToken);
        }

        HttpContent content = page.Unsized ? new UnsizedContent(page.Body) : new ByteArrayContent(page.Body);
        content.Headers.TryAddWithoutValidation("Content-Type", page.ContentType);
        var response = new HttpResponseMessage(page.Status) { Content = content };
        if (page.Location is not null)
        {
            response.Headers.Location = new Uri(page.Location, UriKind.RelativeOrAbsolute);
        }

        return response;
    }

    private sealed class UnsizedContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
