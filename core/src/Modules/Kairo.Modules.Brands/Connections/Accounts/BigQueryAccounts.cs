using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class BigQueryAccounts(IHttpClientFactory http) : IProductAccounts
{
    public const string CannotRead =
        "This Google account can't read datasets in that BigQuery project. Ask for the BigQuery Data Viewer role.";
    public const string CannotQuery =
        "This Google account can't run queries in that BigQuery project. Ask for the BigQuery Job User role.";

    private const string BaseUrl = "https://bigquery.googleapis.com/bigquery/v2/projects";

    public ConnectedProduct Product => ConnectedProduct.BigQuery;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}?maxResults=1000", accessToken, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ProjectPage>(GoogleHttp.Json, cancellationToken);
        return (page?.Projects ?? []).Select(project => new ProductAccount(project.Id, project.FriendlyName ?? project.Id)).ToList();
    }

    public async Task<string?> TestAsync(string accessToken, ProductAccount account, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        var project = Uri.EscapeDataString(account.Id);
        using var datasets = await client.CallAsync(HttpMethod.Get, $"{BaseUrl}/{project}/datasets?maxResults=1", accessToken, null, cancellationToken);
        if (!datasets.IsSuccessStatusCode)
        {
            return CannotRead;
        }

        using var job = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/{project}/jobs",
            accessToken,
            new { configuration = new { dryRun = true, query = new { query = "SELECT 1", useLegacySql = false } } },
            cancellationToken);
        return job.IsSuccessStatusCode ? null : CannotQuery;
    }

    private sealed record ProjectPage(Project[]? Projects);

    private sealed record Project(string Id, string? FriendlyName);
}
