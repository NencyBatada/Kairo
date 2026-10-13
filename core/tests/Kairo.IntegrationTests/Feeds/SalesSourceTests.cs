using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Feeds;

public sealed class SalesSourceTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string CannotRead = "Kairo can't read this table with the connected Google account.";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_admin_chooses_a_table_after_Kairo_checks_it()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("acme-data.shop.daily_orders", saved.GetProperty("table").GetString());
        Assert.Equal("order_date", saved.GetProperty("columns").GetProperty("date").GetString());
        Assert.Equal("net_revenue", saved.GetProperty("columns").GetProperty("revenue").GetString());
        Assert.Equal("asia-south1", saved.GetProperty("location").GetString());
        Assert.Equal(admin.UserId, saved.GetProperty("setBy").GetGuid());

        var read = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal("acme-data.shop.daily_orders", read.GetProperty("table").GetString());
        Assert.Contains("analytics.sales_source_set", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task No_table_reads_as_no_content()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync(Source(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Without_the_BigQuery_connection_a_table_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("daily_orders")]
    [InlineData("acme-data.shop")]
    [InlineData("acme data.shop.daily_orders")]
    [InlineData("acme-data.shop.daily-orders")]
    [InlineData("acme-data.shop.daily_orders.extra")]
    [InlineData("acme-data.shop.`daily_orders`")]
    public async Task A_name_that_is_not_project_dataset_table_is_refused(string table)
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(table: table), Token));

        Assert.True(errors.ContainsKey("table"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_table_Kairo_cannot_read_is_refused(HttpStatusCode answer)
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();
        _api.GoogleApis.BigQuery.BrandTableRead = answer;

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token));

        Assert.Equal(new[] { CannotRead }, errors["table"]);
    }

    [Fact]
    public async Task A_table_that_does_not_exist_is_refused()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(table: "acme-data.shop.missing"), Token));

        Assert.Equal(new[] { CannotRead }, errors["table"]);
    }

    [Fact]
    public async Task A_missing_column_is_refused()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(orders: "order_total"), Token));

        Assert.Equal(new[] { "The table has no column named order_total." }, errors["columns.orders"]);
    }

    [Fact]
    public async Task A_column_of_the_wrong_type_is_refused()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(date: "state"), Token));

        Assert.Equal(new[] { "state is a STRING column, but date needs DATE, DATETIME or TIMESTAMP." }, errors["columns.date"]);
    }

    [Theory]
    [InlineData("net`revenue")]
    [InlineData("net revenue")]
    [InlineData("1revenue")]
    [InlineData("")]
    public async Task A_column_name_that_cannot_be_quoted_safely_is_refused(string revenue)
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(revenue: revenue), Token));

        Assert.True(errors.ContainsKey("columns.revenue"));
    }

    [Fact]
    public async Task Removing_the_table_stops_copying_and_is_safe_to_repeat()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();
        (await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token)).EnsureSuccessStatusCode();

        var first = await admin.Browser.DeleteAsync(Source(brandId), Token);
        var second = await admin.Browser.DeleteAsync(Source(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Single(await Workspaces.AuditActionsAsync(admin.Browser, brandId), action => action == "analytics.sales_source_removed");
    }

    [Fact]
    public async Task Another_workspace_cannot_read_or_change_the_table()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();
        (await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token)).EnsureSuccessStatusCode();
        var (outsider, _) = await WorkspaceAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.DeleteAsync(Source(brandId), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Source(Guid brandId) => $"/api/brands/{brandId}/analytics/sales/source";

    private static object Body(
        string table = "acme-data.shop.daily_orders",
        string date = "order_date",
        string region = "state",
        string orders = "order_count",
        string revenue = "net_revenue") =>
        new { table, columns = new { date, region, orders, revenue } };

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").EnumerateObject()
            .ToDictionary(error => error.Name, error => error.Value.EnumerateArray().Select(message => message.GetString()!).ToArray());
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<(Member Admin, Guid BrandId)> ConnectedWorkspaceAsync()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");
        return (admin, brandId);
    }
}
