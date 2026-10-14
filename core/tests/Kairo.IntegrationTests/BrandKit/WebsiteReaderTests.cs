using System.Diagnostics;
using System.Net;
using Kairo.IntegrationTests.Web;
using Kairo.Modules.BrandKit.Drafting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class WebsiteReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_the_homepage_and_then_the_best_pages_on_the_same_host()
    {
        var sites = new FakeWebsites();
        DraftingSites.Acme(sites);

        var reading = await Reader(sites).ReadAsync(new Uri(DraftingSites.Home), Token);

        Assert.Equal(DraftingSites.PageUrls, reading.Pages.Select(page => page.Url.AbsoluteUri));
        Assert.Equal(new[] { "p1", "p2", "p3", "p4" }, reading.Pages.Select(page => page.Id));
        var home = reading.Pages[0];
        Assert.Equal("Acme Shoes — Running shoes for India", home.Title);
        Assert.Equal("Running shoes built for Indian roads.", home.Description);
        Assert.Equal(new[] { "Acme Shoes", "Built for your longest run" }, home.Headings);
        Assert.Contains("Free returns for 30 days on every pair.", home.Text);
        Assert.Equal(new[] { "#E4572E", "#1B998B" }, reading.Colours.Select(colour => colour.Hex));
        Assert.Equal(new[] { "css-variable", "theme-color" }, reading.Colours[0].From);
        Assert.Equal(2, reading.Colours[1].Count);
        Assert.Equal(
            new[] { ("l1", DraftingSites.Logo, "header-img", "Acme Shoes"), ("l2", "https://acme.example/favicon.ico", "icon", "") },
            reading.Logos.Select(logo => (logo.Id, logo.Url.AbsoluteUri, logo.From, logo.Alt)));
        Assert.DoesNotContain("https://partner.example/", sites.Requests);
    }

    [Fact]
    public async Task Honours_robots_txt_and_reads_it_once()
    {
        var sites = new FakeWebsites();
        DraftingSites.Acme(sites);

        await Reader(sites).ReadAsync(new Uri(DraftingSites.Home), Token);

        Assert.DoesNotContain("https://acme.example/account", sites.Requests);
        Assert.Single(sites.Requests, url => url == "https://acme.example/robots.txt");
    }

    [Fact]
    public async Task A_homepage_robots_txt_forbids_gives_nothing()
    {
        var sites = new FakeWebsites();
        DraftingSites.Acme(sites);
        sites.Text("https://acme.example/robots.txt", "text/plain", "User-agent: KairoBot\nDisallow: /\n");

        var reading = await Reader(sites).ReadAsync(new Uri(DraftingSites.Home), Token);

        Assert.Empty(reading.Pages);
        Assert.DoesNotContain(DraftingSites.Home, sites.Requests);
    }

    [Fact]
    public async Task Follows_a_homepage_redirect_and_reads_links_on_the_new_host()
    {
        var sites = new FakeWebsites();
        sites.Redirect("https://acme.example/", "https://www.acme.example/", HttpStatusCode.MovedPermanently);
        sites.Html("https://www.acme.example/", "<html><body><a href=\"/about\">About us</a><a href=\"https://acme.example/old\">Old</a></body></html>");
        sites.Html("https://www.acme.example/about", "<html><head><title>About</title></head><body><p>We make shoes.</p></body></html>");

        var reading = await Reader(sites).ReadAsync(new Uri("https://acme.example/"), Token);

        Assert.Equal(new[] { "https://www.acme.example/", "https://www.acme.example/about" }, reading.Pages.Select(page => page.Url.AbsoluteUri));
    }

    [Fact]
    public async Task Skips_pages_that_fail_or_are_not_web_pages()
    {
        var sites = new FakeWebsites();
        sites.Html("https://acme.example/", "<html><body><a href=\"/about\">About</a><a href=\"/story\">Story</a><a href=\"/shop\">Shop</a></body></html>");
        sites.Status("https://acme.example/about", HttpStatusCode.InternalServerError);
        sites.File("https://acme.example/story", "application/pdf", "%PDF-1.7"u8.ToArray());
        sites.Html("https://acme.example/shop", "<html><head><title>Shop</title></head><body><p>Shoes.</p></body></html>");

        var reading = await Reader(sites).ReadAsync(new Uri("https://acme.example/"), Token);

        Assert.Equal(new[] { "https://acme.example/", "https://acme.example/shop" }, reading.Pages.Select(page => page.Url.AbsoluteUri));
        Assert.Equal(new[] { "p1", "p2" }, reading.Pages.Select(page => page.Id));
    }

    [Fact]
    public async Task An_unreadable_homepage_gives_nothing()
    {
        var reading = await Reader(new FakeWebsites()).ReadAsync(new Uri("https://nothing.example/"), Token);

        Assert.Empty(reading.Pages);
        Assert.Empty(reading.Colours);
        Assert.Empty(reading.Logos);
    }

    [Fact]
    public async Task Keeps_what_it_read_when_the_time_runs_out()
    {
        var sites = new FakeWebsites();
        sites.Html("https://acme.example/", "<html><body><a href=\"/about\">About</a><a href=\"/collections\">Shop</a><a href=\"/news\">News</a></body></html>");
        sites.Html("https://acme.example/about", "<html><head><title>About</title></head><body><p>Hi.</p></body></html>");
        sites.Slow("https://acme.example/collections", "<html><body>late</body></html>", TimeSpan.FromSeconds(30));
        sites.Html("https://acme.example/news", "<html><body>news</body></html>");
        var reader = Reader(sites);
        reader.Budget = TimeSpan.FromSeconds(1);
        var clock = Stopwatch.StartNew();

        var reading = await reader.ReadAsync(new Uri("https://acme.example/"), Token);

        Assert.Equal(new[] { "https://acme.example/", "https://acme.example/about" }, reading.Pages.Select(page => page.Url.AbsoluteUri));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"Reading took {clock.Elapsed}.");
    }

    private static WebsiteReader Reader(FakeWebsites sites) => new(SafeWeb.Client(sites), NullLogger<WebsiteReader>.Instance);
}
