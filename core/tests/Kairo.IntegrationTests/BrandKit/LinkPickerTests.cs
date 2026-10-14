using Kairo.Modules.BrandKit.Drafting;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class LinkPickerTests
{
    private static readonly Uri Home = new("https://acme.example/");

    [Fact]
    public void Puts_about_story_and_shop_pages_first_then_keeps_page_order()
    {
        var picked = LinkPicker.Pick(Home, Links(
            ("/blog/news", "News"),
            ("/pages/our-story", "Our story"),
            ("/recommendations", "Picks"),
            ("/c/1", "Women"),
            ("/collections/running", "Running")));

        Assert.Equal(
            new[] { "/pages/our-story", "/c/1", "/collections/running", "/blog/news", "/recommendations" },
            picked.Select(url => url.AbsolutePath));
    }

    [Fact]
    public void Keeps_each_other_page_on_the_same_host_once()
    {
        var picked = LinkPicker.Pick(Home, Links(
            ("/", "Home"),
            ("https://partner.example/about", "Partner"),
            ("/about", "About"),
            ("/about/", "About again"),
            ("/guide.pdf", "Guide"),
            ("/logo.png", "Logo")));

        Assert.Equal(new[] { "https://acme.example/about" }, picked.Select(url => url.AbsoluteUri));
    }

    [Fact]
    public void Picks_at_most_eight_pages()
    {
        var picked = LinkPicker.Pick(Home, Enumerable.Range(1, 20).Select(index => new PageLink(new Uri(Home, $"/p/{index}"), $"Page {index}")));

        Assert.Equal(LinkPicker.MorePages, picked.Count);
    }

    private static IEnumerable<PageLink> Links(params (string Href, string Text)[] links) =>
        links.Select(link => new PageLink(new Uri(Home, link.Href), link.Text));
}
