using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class Ga4Accounts(IHttpClientFactory http) : IProductAccounts
{
    public const string CannotRead = "This Google account can't read that Google Analytics property.";

    private const string BaseUrl = "https://analyticsadmin.googleapis.com/v1beta";

    public ConnectedProduct Product => ConnectedProduct.Ga4;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}/accountSummaries?pageSize=200", accessToken, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<SummaryPage>(GoogleHttp.Json, cancellationToken);
        return (page?.AccountSummaries ?? [])
            .SelectMany(account => (account.PropertySummaries ?? []).Select(property => new ProductAccount(
                property.Property["properties/".Length..],
                $"{property.DisplayName} ({account.DisplayName})")))
            .ToList();
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}/properties/{Uri.EscapeDataString(externalId)}", accessToken, null, cancellationToken);
        return response.IsSuccessStatusCode ? null : CannotRead;
    }

    private sealed record SummaryPage(AccountSummary[]? AccountSummaries);

    private sealed record AccountSummary(string? DisplayName, PropertySummary[]? PropertySummaries);

    private sealed record PropertySummary(string Property, string? DisplayName);
}
