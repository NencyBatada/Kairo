using System.Text;
using AngleSharp.Html.Dom;
using Kairo.Modules.BrandKit.Kit;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed class WebsiteReader(SafeWebClient web, ILogger<WebsiteReader> logger)
{
    public const long MaxPageBytes = 2 * 1024 * 1024;
    public const long MaxStylesheetBytes = 500 * 1024;
    public const long MaxRobotsBytes = 500 * 1024;
    public const int Stylesheets = 3;

    public static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(10);

    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(60);

    public async Task<WebsiteReading> ReadAsync(Uri website, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        var robots = new Dictionary<string, RobotsTxt>(StringComparer.OrdinalIgnoreCase);
        var pages = new List<ReadPage>();
        var colours = new ColourTally();
        IReadOnlyList<LogoCandidate> logos = [];
        try
        {
            if (await LoadAsync(website, robots, budget.Token) is not { } home)
            {
                return new WebsiteReading([], [], []);
            }

            Keep(home, pages, colours);
            logos = LogoCandidates.Find(home.Document, home.Url);
            await AddStylesheetsAsync(home, colours, budget.Token);
            foreach (var link in LinkPicker.Pick(home.Url, home.Content.Links))
            {
                if (await LoadAsync(link, robots, budget.Token) is { } page && pages.All(read => read.Url != page.Url))
                {
                    Keep(page, pages, colours);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopped reading {Website} after {Seconds} seconds, with {Pages} pages read.", website, Budget.TotalSeconds, pages.Count);
        }

        return new WebsiteReading(pages, colours.Top(), logos);
    }

    private static void Keep(LoadedPage page, List<ReadPage> pages, ColourTally colours)
    {
        var content = page.Content;
        pages.Add(new ReadPage($"p{pages.Count + 1}", page.Url, content.Title, content.Description, content.Headings, content.Text));
        colours.AddDocument(page.Document);
    }

    private async Task<LoadedPage?> LoadAsync(Uri url, Dictionary<string, RobotsTxt> robots, CancellationToken cancellationToken)
    {
        if (!(await RobotsAsync(url, robots, cancellationToken)).Allows(url.PathAndQuery))
        {
            logger.LogInformation("Skipped {Url}: robots.txt asks robots not to read it.", url);
            return null;
        }

        try
        {
            var response = await web.FetchAsync(url, new SafeFetchOptions(MaxPageBytes, PageTimeout, HtmlOnly: true), cancellationToken);
            if (response.Url.AbsoluteUri.Length > KitContentRules.UrlLength)
            {
                return null;
            }

            var document = PageText.Parse(response.Body, response.Charset);
            return new LoadedPage(response.Url, document, PageText.Read(document, response.Url));
        }
        catch (SafeFetchException exception)
        {
            logger.LogInformation("Skipped {Url}: {Reason}.", url, exception.Code);
            return null;
        }
    }

    private async Task<RobotsTxt> RobotsAsync(Uri url, Dictionary<string, RobotsTxt> robots, CancellationToken cancellationToken)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        if (robots.TryGetValue(origin, out var known))
        {
            return known;
        }

        RobotsTxt rules;
        try
        {
            var response = await web.FetchAsync(new Uri($"{origin}/robots.txt"), new SafeFetchOptions(MaxRobotsBytes, PageTimeout), cancellationToken);
            rules = RobotsTxt.Parse(Encoding.UTF8.GetString(response.Body));
        }
        catch (SafeFetchException)
        {
            rules = RobotsTxt.AllowAll;
        }

        robots[origin] = rules;
        return rules;
    }

    private async Task AddStylesheetsAsync(LoadedPage home, ColourTally colours, CancellationToken cancellationToken)
    {
        var sheets = home.Document.QuerySelectorAll("link[rel~='stylesheet'][href]")
            .Select(link => Uri.TryCreate(home.Url, link.GetAttribute("href"), out var url) ? url : null)
            .OfType<Uri>()
            .Where(url => string.Equals(url.Host, home.Url.Host, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(Stylesheets)
            .ToList();
        foreach (var sheet in sheets)
        {
            try
            {
                var response = await web.FetchAsync(sheet, new SafeFetchOptions(MaxStylesheetBytes, PageTimeout), cancellationToken);
                colours.AddCss(Encoding.UTF8.GetString(response.Body), "css");
            }
            catch (SafeFetchException exception)
            {
                logger.LogInformation("Skipped the stylesheet {Url}: {Reason}.", sheet, exception.Code);
            }
        }
    }

    private sealed record LoadedPage(Uri Url, IHtmlDocument Document, PageContent Content);
}
