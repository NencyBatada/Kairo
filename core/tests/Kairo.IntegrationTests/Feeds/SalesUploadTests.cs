using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class SalesUploadTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string OneRow = SalesFiles.Header + "2026-10-01,Goa,1,10\n";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_upload_loads_the_file_and_is_recorded()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(
            Uploads(brandId),
            SalesFiles.Csv(SalesFiles.Header + "2026-10-01,Goa,3,450.50\n2026-10-02,Goa,1,99\n2026-10-02,Kerala,2,10\n", "october.csv"),
            Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var upload = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("october.csv", upload.GetProperty("fileName").GetString());
        Assert.Equal(3, upload.GetProperty("rows").GetInt32());
        Assert.Equal("2026-10-01", upload.GetProperty("firstDate").GetString());
        Assert.Equal("2026-10-02", upload.GetProperty("lastDate").GetString());
        Assert.Equal(2, upload.GetProperty("days").GetInt32());
        Assert.Equal(admin.UserId, upload.GetProperty("uploadedBy").GetGuid());
        Assert.Equal(
            new[] { "2026-10-01 Goa 3 450.50 upload", "2026-10-02 Goa 1 99 upload", "2026-10-02 Kerala 2 10 upload" },
            SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));

        var listed = await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token);
        Assert.Equal(upload.GetProperty("id").GetGuid(), Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Contains("analytics.sales_uploaded", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Theory]
    [InlineData("marketer", HttpStatusCode.Created)]
    [InlineData("approver", HttpStatusCode.Forbidden)]
    public async Task Admins_and_marketers_can_upload_but_approvers_cannot(string role, HttpStatusCode expected)
    {
        var (admin, brandId) = await WorkspaceAsync();
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var response = await member.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(OneRow), Token);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_file_over_10_MB_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = OneRow + new string(' ', (int)SalesEndpoints.MaxFileBytes);

        var response = await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(huge), Token);

        Assert.Equal(new[] { "the file is larger than 10 MB" }, await FileErrorsAsync(response));
        Assert.Empty(SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));
    }

    [Fact]
    public async Task A_body_over_the_request_limit_is_refused_before_the_upload_runs()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = OneRow + new string(' ', 12 * 1024 * 1024);

        var response = await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(huge), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(SalesEndpoints.TooLarge, await response.Content.ReadAsStringAsync(Token));
        Assert.Empty(_api.GoogleApis.BigQuery.Calls);
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token)).EnumerateArray());
    }

    [Fact]
    public async Task An_invalid_file_lists_its_problems_and_loads_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(
            Uploads(brandId), SalesFiles.Csv(SalesFiles.Header + "2026-10-01,Goa,x,1\n2026-10-01,,1,1\n"), Token);

        Assert.Equal(
            new[] { "row 2: orders must be a whole number, 0 or more", "row 3: region is empty" },
            await FileErrorsAsync(response));
        Assert.Empty(SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token)).EnumerateArray());
    }

    [Fact]
    public async Task A_request_without_a_file_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(
            Uploads(brandId), new MultipartFormDataContent { { new StringContent("x"), "notes" } }, Token);

        Assert.Equal(new[] { "Choose a CSV file to upload." }, await FileErrorsAsync(response));
    }

    [Fact]
    public async Task A_new_upload_replaces_only_the_dates_it_contains()
    {
        var (admin, brandId) = await WorkspaceAsync();
        (await admin.Browser.PostAsync(
            Uploads(brandId),
            SalesFiles.Csv(SalesFiles.Header + "2026-10-01,Goa,1,1\n2026-10-02,Goa,2,2\n2026-10-03,Goa,3,3\n"),
            Token)).EnsureSuccessStatusCode();

        (await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(SalesFiles.Header + "2026-10-02,Goa,20,20\n"), Token))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            new[] { "2026-10-01 Goa 1 1 upload", "2026-10-02 Goa 20 20 upload", "2026-10-03 Goa 3 3 upload" },
            SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));
        Assert.Equal(2, (await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token)).GetArrayLength());
    }

    [Fact]
    public async Task Without_an_analytics_project_uploads_are_unavailable()
    {
        await using var api = new KairoApiFactory(sqlServer.KairoConnectionString) { AnalyticsProject = null };
        var admin = await Workspaces.SignInAsync(api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var response = await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(OneRow), Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_uploads()
    {
        var (admin, brandId) = await WorkspaceAsync();
        (await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(OneRow), Token)).EnsureSuccessStatusCode();
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(Uploads(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Uploads(Guid brandId) => $"/api/brands/{brandId}/analytics/sales/uploads";

    private static async Task<string[]> FileErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").GetProperty("file").EnumerateArray().Select(error => error.GetString()!).ToArray();
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
