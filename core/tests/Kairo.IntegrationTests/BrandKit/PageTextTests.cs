using System.Text;
using Kairo.Modules.BrandKit.Drafting;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class PageTextTests
{
    private static readonly Uri Page = new("https://acme.example/shop/");

    [Fact]
    public void Reads_the_title_description_headings_and_main_text()
    {
        var content = Read("""
            <html><head><title>  Acme   Shoes </title><meta name="description" content="Shoes for roads."></head>
            <body>
              <nav><a href="/about">About</a></nav>
              <main>
                <h1>Acme Shoes</h1><h2>Our range</h2><h2>Our range</h2>
                <p>Built for your</p><p>longest run.</p>
                <script>var tracking = 1;</script>
                <form><label>Sign up</label></form>
              </main>
              <footer>Copyright</footer>
            </body></html>
            """);

        Assert.Equal("Acme Shoes", content.Title);
        Assert.Equal("Shoes for roads.", content.Description);
        Assert.Equal(new[] { "Acme Shoes", "Our range" }, content.Headings);
        Assert.Contains("Built for your longest run.", content.Text);
        Assert.DoesNotContain("tracking", content.Text);
        Assert.DoesNotContain("Sign up", content.Text);
        Assert.DoesNotContain("About", content.Text);
        Assert.DoesNotContain("Copyright", content.Text);
    }

    [Fact]
    public void Uses_the_body_without_navigation_and_footers_when_there_is_no_main()
    {
        var content = Read("<html><body><nav>Menu</nav><div><p>Hello there.</p></div><footer>Bye</footer></body></html>");

        Assert.Equal("Hello there.", content.Text);
    }

    [Fact]
    public void Cuts_the_text_to_6000_characters()
    {
        var content = Read($"<html><body><p>{new string('a', 7000)}</p></body></html>");

        Assert.Equal(PageText.MaxText, content.Text.Length);
    }

    [Fact]
    public void Resolves_links_against_the_page_and_keeps_only_web_addresses()
    {
        var content = Read("""
            <html><body>
              <a href="/about#team">About us</a>
              <a href="running">Running</a>
              <a href="https://partner.example/x">Partner</a>
              <a href="mailto:hi@acme.example">Mail</a>
              <a href="javascript:void(0)">Menu</a>
            </body></html>
            """);

        Assert.Equal(
            new[] { "https://acme.example/about", "https://acme.example/shop/running", "https://partner.example/x" },
            content.Links.Select(link => link.Url.AbsoluteUri));
        Assert.Equal("About us", content.Links[0].Text);
    }

    [Fact]
    public void Decodes_a_page_in_its_declared_charset()
    {
        var html = "<html><head><meta charset=\"iso-8859-1\"><title>Café</title></head><body><p>Crème</p></body></html>";
        var document = PageText.Parse(Encoding.Latin1.GetBytes(html));

        var content = PageText.Read(document, Page);

        Assert.Equal("Café", content.Title);
        Assert.Equal("Crème", content.Text);
    }

    private static PageContent Read(string html) => PageText.Read(PageText.Parse(Encoding.UTF8.GetBytes(html)), Page);
}
