using System.Net;
using System.Text;
using Kairo.SharedKernel.Http;

namespace Kairo.IntegrationTests.Web;

public sealed class SafeWebClientTests
{
    private static readonly SafeFetchOptions Page = new(1024, TimeSpan.FromSeconds(5), HtmlOnly: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_a_page_as_KairoBot()
    {
        var sites = new FakeWebsites();
        sites.Html("https://acme.example/", "<html><title>Acme</title></html>");

        var response = await SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/"), Page, Token);

        Assert.Equal("https://acme.example/", response.Url.AbsoluteUri);
        Assert.Equal("text/html", response.ContentType);
        Assert.Equal("utf-8", response.Charset);
        Assert.Contains("Acme", Encoding.UTF8.GetString(response.Body));
        Assert.Equal($"KairoBot/1.0 (+{SafeWeb.PublicOrigin})", Assert.Single(sites.UserAgents));
    }

    [Fact]
    public async Task Follows_up_to_three_redirects()
    {
        var sites = new FakeWebsites();
        sites.Redirect("https://acme.example/a", "/b");
        sites.Redirect("https://acme.example/b", "https://www.acme.example/c", HttpStatusCode.MovedPermanently);
        sites.Redirect("https://www.acme.example/c", "https://www.acme.example/d", HttpStatusCode.PermanentRedirect);
        sites.Html("https://www.acme.example/d", "<p>d</p>");

        var response = await SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/a"), Page, Token);

        Assert.Equal("https://www.acme.example/d", response.Url.AbsoluteUri);
    }

    [Fact]
    public async Task Stops_after_three_redirects()
    {
        var sites = new FakeWebsites();
        sites.Redirect("https://acme.example/1", "/2");
        sites.Redirect("https://acme.example/2", "/3");
        sites.Redirect("https://acme.example/3", "/4");
        sites.Redirect("https://acme.example/4", "/5");
        sites.Html("https://acme.example/5", "<p>5</p>");

        var error = await Assert.ThrowsAsync<SafeFetchException>(() =>
            SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/1"), Page, Token));

        Assert.Equal(SafeFetchErrors.BadStatus, error.Code);
        Assert.DoesNotContain("https://acme.example/5", sites.Requests);
    }

    [Theory]
    [InlineData("ftp://acme.example/files")]
    [InlineData("https://acme.example:8443/")]
    [InlineData("file:///etc/passwd")]
    public async Task Checks_every_redirect_before_following_it(string location)
    {
        var sites = new FakeWebsites();
        sites.Redirect("https://acme.example/", location);

        var error = await Assert.ThrowsAsync<SafeFetchException>(() =>
            SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/"), Page, Token));

        Assert.Equal(SafeFetchErrors.BlockedAddress, error.Code);
        Assert.Single(sites.Requests);
    }

    [Theory]
    [InlineData("ftp://acme.example/")]
    [InlineData("https://acme.example:8080/")]
    [InlineData("https://user:secret@acme.example/")]
    [InlineData("file:///etc/passwd")]
    public async Task Refuses_addresses_that_are_not_plain_web_addresses(string url)
    {
        var sites = new FakeWebsites();

        var error = await Assert.ThrowsAsync<SafeFetchException>(() => SafeWeb.Client(sites).FetchAsync(new Uri(url), Page, Token));

        Assert.Equal(SafeFetchErrors.BlockedAddress, error.Code);
        Assert.Empty(sites.Requests);
    }

    [Fact]
    public async Task Refuses_a_body_whose_length_is_over_the_limit()
    {
        var sites = new FakeWebsites();
        sites.File("https://acme.example/", "text/html", new byte[2048]);

        var error = await Assert.ThrowsAsync<SafeFetchException>(() =>
            SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/"), Page, Token));

        Assert.Equal(SafeFetchErrors.TooLarge, error.Code);
    }

    [Fact]
    public async Task Stops_reading_a_body_without_a_length_at_the_limit()
    {
        var sites = new FakeWebsites();
        sites.Unsized("https://acme.example/", "text/html", new byte[2048]);

        var error = await Assert.ThrowsAsync<SafeFetchException>(() =>
            SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/"), Page, Token));

        Assert.Equal(SafeFetchErrors.TooLarge, error.Code);
    }

    [Fact]
    public async Task Gives_up_on_a_slow_answer()
    {
        var sites = new FakeWebsites();
        sites.Slow("https://acme.example/", "<p>late</p>", TimeSpan.FromSeconds(10));

        var error = await Assert.ThrowsAsync<SafeFetchException>(() => SafeWeb.Client(sites).FetchAsync(
            new Uri("https://acme.example/"), new SafeFetchOptions(1024, TimeSpan.FromMilliseconds(200), HtmlOnly: true), Token));

        Assert.Equal(SafeFetchErrors.Timeout, error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Reports_an_error_status(HttpStatusCode status)
    {
        var sites = new FakeWebsites();
        sites.Status("https://acme.example/", status);

        var error = await Assert.ThrowsAsync<SafeFetchException>(() =>
            SafeWeb.Client(sites).FetchAsync(new Uri("https://acme.example/"), Page, Token));

        Assert.Equal(SafeFetchErrors.BadStatus, error.Code);
    }

    [Fact]
    public async Task Refuses_anything_but_a_web_page_only_when_asked()
    {
        var sites = new FakeWebsites();
        sites.File("https://acme.example/guide.pdf", "application/pdf", "%PDF-1.7"u8.ToArray());
        var client = SafeWeb.Client(sites);

        var error = await Assert.ThrowsAsync<SafeFetchException>(() =>
            client.FetchAsync(new Uri("https://acme.example/guide.pdf"), Page, Token));
        var file = await client.FetchAsync(new Uri("https://acme.example/guide.pdf"), Page with { HtmlOnly = false }, Token);

        Assert.Equal(SafeFetchErrors.NotHtml, error.Code);
        Assert.Equal("application/pdf", file.ContentType);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/computeMetadata/v1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://10.0.0.5/")]
    public async Task Never_connects_to_local_or_private_addresses(string url)
    {
        var error = await Assert.ThrowsAsync<SafeFetchException>(() => SafeWeb.Client().FetchAsync(new Uri(url), Page, Token));

        Assert.Equal(SafeFetchErrors.BlockedAddress, error.Code);
    }
}
