using Kairo.IntegrationTests.Web;

namespace Kairo.IntegrationTests.BrandKit;

internal static class DraftingSites
{
    public const string Home = "https://acme.example/";
    public const string Logo = "https://acme.example/logo.png";
    public const string Story = "https://acme.example/pages/our-story";
    public const string Running = "https://acme.example/collections/running";
    public const string News = "https://acme.example/blog/news";

    public static readonly string[] PageUrls = [Home, Story, Running, News];

    private const string HomeHtml = """
        <!doctype html>
        <html>
        <head>
          <meta charset="utf-8">
          <title>Acme Shoes — Running shoes for India</title>
          <meta name="description" content="Running shoes built for Indian roads.">
          <meta name="theme-color" content="#E4572E">
          <link rel="stylesheet" href="/styles.css">
          <link rel="icon" href="/favicon.ico">
          <style>:root{--brand:#e4572e;--ink:#111111}.badge{background:#1b998b}</style>
        </head>
        <body>
          <header>
            <a href="/"><img src="/logo.png" alt="Acme Shoes"></a>
            <nav>
              <a href="/pages/our-story">Our story</a>
              <a href="/collections/running">Running</a>
              <a href="/blog/news">News</a>
              <a href="/account">Account</a>
              <a href="https://partner.example/">Partner</a>
            </nav>
          </header>
          <main>
            <h1>Acme Shoes</h1>
            <h2>Built for your longest run</h2>
            <p>Free returns for 30 days on every pair.</p>
          </main>
          <footer>Acme Shoes, Pune</footer>
        </body>
        </html>
        """;

    public static void Acme(FakeWebsites sites)
    {
        sites.Text("https://acme.example/robots.txt", "text/plain", "User-agent: *\nDisallow: /account\n");
        sites.Html(Home, HomeHtml);
        sites.Html(Story, "<html><head><title>Our story</title></head><body><main><h1>Our story</h1><p>Founded in Pune by runners.</p></main></body></html>");
        sites.Html(Running, "<html><head><title>Running shoes</title></head><body><main><h1>Running</h1><p>Road and trail shoes.</p></main></body></html>");
        sites.Html(News, "<html><head><title>News</title></head><body><main><p>Race day tips.</p></main></body></html>");
        sites.Html("https://acme.example/account", "<html><head><title>Account</title></head><body><p>Sign in.</p></body></html>");
        sites.Text("https://acme.example/styles.css", "text/css", ".hero{color:#1B998B}");
        sites.File(Logo, "image/png", BrandKits.Png);
    }
}
