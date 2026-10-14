using System.Text;
using Kairo.Modules.BrandKit.Drafting;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class LogoCandidatesTests
{
    private static readonly Uri Page = new("https://acme.example/");

    [Fact]
    public void Finds_logos_in_structured_data_the_header_icons_and_social_images()
    {
        var logos = Find("""
            <html><head>
              <script type="application/ld+json">{"@context":"https://schema.org","@type":"Organization","logo":"https://cdn.acme.example/logo.svg"}</script>
              <link rel="apple-touch-icon" href="/apple.png">
              <link rel="shortcut icon" href="/favicon.ico">
              <meta property="og:image" content="https://acme.example/og.jpg">
            </head>
            <body>
              <header><img src="/img/brand.png" alt="Acme"></header>
              <img class="site-logo" src="/logo-dark.png" alt="">
              <img src="/hero.jpg" alt="Runners">
            </body></html>
            """);

        Assert.Equal(
            new[]
            {
                ("l1", "https://cdn.acme.example/logo.svg", "json-ld"),
                ("l2", "https://acme.example/img/brand.png", "header-img"),
                ("l3", "https://acme.example/logo-dark.png", "logo-img"),
                ("l4", "https://acme.example/apple.png", "apple-touch-icon"),
                ("l5", "https://acme.example/favicon.ico", "icon"),
                ("l6", "https://acme.example/og.jpg", "og-image"),
            },
            logos.Select(logo => (logo.Id, logo.Url.AbsoluteUri, logo.From)));
        Assert.Equal("Acme", logos[1].Alt);
    }

    [Fact]
    public void Reads_organisation_logos_nested_in_a_graph()
    {
        var logos = Find("""
            <html><head><script type="application/ld+json">
            {"@graph":[{"@type":"WebSite"},{"@type":["Organization"],"logo":{"@type":"ImageObject","url":"/brand/logo.png"}}]}
            </script></head><body></body></html>
            """);

        Assert.Equal("https://acme.example/brand/logo.png", Assert.Single(logos).Url.AbsoluteUri);
    }

    [Fact]
    public void Skips_broken_structured_data_data_addresses_and_repeats_and_keeps_ten()
    {
        var images = string.Concat(Enumerable.Range(1, 15).Select(index => $"<img class=\"logo\" src=\"/logo-{index}.png\">"));
        var logos = Find($$"""
            <html><head><script type="application/ld+json">{ not json</script></head>
            <body><header><img src="data:image/png;base64,AAAA"><img src="/logo-1.png"></header>{{images}}</body></html>
            """);

        Assert.Equal(LogoCandidates.Max, logos.Count);
        Assert.Equal(logos.Count, logos.Select(logo => logo.Url).Distinct().Count());
        Assert.Equal("https://acme.example/logo-1.png", logos[0].Url.AbsoluteUri);
    }

    private static IReadOnlyList<LogoCandidate> Find(string html) =>
        LogoCandidates.Find(PageText.Parse(Encoding.UTF8.GetBytes(html)), Page);
}
