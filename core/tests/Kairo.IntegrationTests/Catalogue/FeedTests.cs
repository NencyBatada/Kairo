using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Notifications;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class FeedTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string FeedUrl = "https://shop.acme.example/feeds/products.csv";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ConnectionString => sqlServer.KairoConnectionString;

    [Fact]
    public async Task Setting_a_feed_imports_it_and_saves_the_source()
    {
        var (admin, brandId) = await WorkspaceAsync();
        Serve(Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var source = body.GetProperty("source");
        Assert.Equal("feed", source.GetProperty("kind").GetString());
        Assert.Equal(FeedUrl, source.GetProperty("feedUrl").GetString());
        Assert.Equal(admin.UserId, source.GetProperty("setBy").GetGuid());
        Assert.Equal(0, source.GetProperty("failuresInRow").GetInt32());
        Assert.Equal(_api.Clock.GetUtcNow(), source.GetProperty("lastSucceededAt").GetDateTimeOffset());
        var import = body.GetProperty("import");
        Assert.Equal("feed", import.GetProperty("kind").GetString());
        Assert.Equal(FeedUrl, import.GetProperty("feedUrl").GetString());
        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(2, import.GetProperty("added").GetInt32());
        var saved = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal(FeedUrl, saved.GetProperty("feedUrl").GetString());
        Assert.Contains("catalogue.feed_set", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task A_workspace_with_no_source_says_so()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync(Source(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("shop.acme.example/feed.csv")]
    [InlineData("ftp://shop.acme.example/feed.csv")]
    [InlineData("https://shop.acme.example:8443/feed.csv")]
    [InlineData("https://user:secret@shop.acme.example/feed.csv")]
    public async Task A_feed_address_that_isnt_a_public_web_address_is_refused(string url)
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url }, Token);

        Assert.Equal(new[] { "Use a full web address starting with http:// or https://." }, await Catalogues.ErrorsAsync(response, "url"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    [Fact]
    public async Task A_feed_that_cant_be_fetched_is_refused_and_not_saved()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);

        Assert.Equal(new[] { "The feed address answered with an error." }, await Catalogues.ErrorsAsync(response, "url"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Empty(await CatalogueRows.ImportsAsync(ConnectionString, brandId));
    }

    [Fact]
    public async Task A_feed_that_isnt_a_product_file_is_refused_and_not_saved()
    {
        var (admin, brandId) = await WorkspaceAsync();
        _api.Websites.Html(FeedUrl, "<!doctype html><html><body>Acme</body></html>");

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);

        Assert.Equal(
            new[] { "Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format." },
            await Catalogues.ErrorsAsync(response, "url"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    [Fact]
    public async Task A_gzipped_xml_feed_is_imported()
    {
        var (admin, brandId) = await WorkspaceAsync();
        const string rss = """
            <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0"><channel>
              <item><g:id>SKU-1</g:id><title>Runner</title><g:price>1499 INR</g:price></item>
              <item><g:id>SKU-2</g:id><title>Walker</title><g:price>999 INR</g:price></item>
            </channel></rss>
            """;
        _api.Websites.File("https://shop.acme.example/feeds/products.xml.gz", "application/gzip", Gzip(rss));

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = "https://shop.acme.example/feeds/products.xml.gz" }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("import").GetProperty("added").GetInt32());
        Assert.Equal(new[] { "Runner", "Walker" }, (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Select(p => p.Title).ToArray());
    }

    [Fact]
    public async Task Refreshing_imports_the_feed_again()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        Serve(Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var response = await admin.Browser.PostAsync(Refresh(brandId), null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var import = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(1, import.GetProperty("added").GetInt32());
        Assert.Equal(admin.UserId, import.GetProperty("startedBy").GetGuid());
        Assert.Contains("catalogue.feed_refreshed", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Refreshing_without_a_feed_is_a_conflict()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var response = await admin.Browser.PostAsync(Refresh(brandId), null, Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("There's no feed link to refresh.", await Catalogues.ProblemTitleAsync(response));
    }

    [Fact]
    public async Task Two_refreshes_at_once_import_once()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        _api.Websites.Slow(FeedUrl, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")), TimeSpan.FromSeconds(2));
        var other = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "admin");

        var responses = await Task.WhenAll(
            admin.Browser.PostAsync(Refresh(brandId), null, Token),
            other.Browser.PostAsync(Refresh(brandId), null, Token));

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(2, (await CatalogueRows.ImportsAsync(ConnectionString, brandId)).Count);
    }

    [Fact]
    public async Task Removing_the_feed_keeps_the_products()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var response = await admin.Browser.DeleteAsync(Feed(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Equal("active", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Status);
        Assert.Contains("catalogue.feed_removed", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Uploading_a_file_replaces_the_feed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var source = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal("file", source.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, source.GetProperty("feedUrl").ValueKind);
    }

    [Fact]
    public async Task Three_failed_refreshes_in_a_row_alert_the_admins_once()
    {
        var (admin, brandId, handle) = await WorkspaceWithHandleAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        _api.Websites.Status(FeedUrl, HttpStatusCode.InternalServerError);

        var statuses = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var import = await (await admin.Browser.PostAsync(Refresh(brandId), null, Token)).Content.ReadFromJsonAsync<JsonElement>(Token);
            statuses.Add(import.GetProperty("status").GetString()!);
        }

        Assert.All(statuses, status => Assert.Equal("failed", status));
        var source = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal(4, source.GetProperty("failuresInRow").GetInt32());
        var alert = Assert.Single(await NotificationRows.NotificationsAsync(ConnectionString, brandId), row => row.Kind == "connection.problem");
        Assert.Equal(admin.UserId, alert.UserId);
        Assert.Equal("Your product feed isn't refreshing", alert.Title);
        Assert.Equal("Kairo couldn't refresh your product feed 3 times in a row.", alert.Body);
        Assert.Equal($"/{handle}/brand-kit/catalogue", alert.Link);
    }

    [Fact]
    public async Task A_successful_refresh_clears_the_failures()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        _api.Websites.Status(FeedUrl, HttpStatusCode.InternalServerError);
        await admin.Browser.PostAsync(Refresh(brandId), null, Token);
        await admin.Browser.PostAsync(Refresh(brandId), null, Token);
        Serve(Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await (await admin.Browser.PostAsync(Refresh(brandId), null, Token)).Content.ReadFromJsonAsync<JsonElement>(Token);

        Assert.Equal("unchanged", import.GetProperty("status").GetString());
        var source = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal(0, source.GetProperty("failuresInRow").GetInt32());
    }

    [Theory]
    [InlineData("marketer")]
    [InlineData("approver")]
    public async Task Only_admins_manage_the_feed(string role)
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Browser.PostAsync(Refresh(brandId), null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Browser.DeleteAsync(Feed(brandId), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Feed(Guid brandId) => $"{Catalogues.Base(brandId)}/feed";

    private static string Refresh(Guid brandId) => $"{Catalogues.Base(brandId)}/feed/refresh";

    private static string Source(Guid brandId) => $"{Catalogues.Base(brandId)}/source";

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private void Serve(string csv) => _api.Websites.Text(FeedUrl, "text/csv", csv);

    private async Task SetFeedAsync(HttpClient admin, Guid brandId, string csv)
    {
        Serve(csv);
        var response = await admin.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var (admin, brandId, _) = await WorkspaceWithHandleAsync();
        return (admin, brandId);
    }

    private async Task<(Member Admin, Guid BrandId, string Handle)> WorkspaceWithHandleAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        return (admin, await Workspaces.CreateAsync(admin.Browser, handle), handle);
    }
}
