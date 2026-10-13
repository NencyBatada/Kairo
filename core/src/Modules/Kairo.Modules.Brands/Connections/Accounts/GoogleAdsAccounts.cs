using System.Net;
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class GoogleAdsAccounts(IHttpClientFactory http, IConfiguration configuration) : IProductAccounts
{
    public const string CannotOpen = "This Google account can't open that Google Ads account.";
    public const string CannotChange =
        "This Google account can see the Google Ads account but can't make changes to it. Ask for Standard or Admin access in Google Ads.";

    public ConnectedProduct Product => ConnectedProduct.GoogleAds;

    private string BaseUrl => $"https://googleads.googleapis.com/{configuration["Kairo:Google:AdsApiVersion"] ?? "v25"}";

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        using var listed = await client.CallAsync(HttpMethod.Get, $"{BaseUrl}/customers:listAccessibleCustomers", accessToken, null, cancellationToken);
        listed.EnsureSuccessStatusCode();
        var names = (await listed.Content.ReadFromJsonAsync<AccessibleCustomers>(GoogleHttp.Json, cancellationToken))?.ResourceNames ?? [];

        var accounts = new List<ProductAccount>();
        foreach (var name in names.Take(50))
        {
            var id = name["customers/".Length..];
            if (await DescribeAsync(client, accessToken, id, cancellationToken) is { Manager: false } customer)
            {
                accounts.Add(new ProductAccount(id, customer.DescriptiveName ?? id));
            }
        }

        return accounts;
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        if (await DescribeAsync(client, accessToken, externalId, cancellationToken) is null)
        {
            return CannotOpen;
        }

        using var write = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/customers/{Uri.EscapeDataString(externalId)}/campaignBudgets:mutate",
            accessToken,
            new
            {
                operations = new[]
                {
                    new { create = new { name = $"Kairo access check {Guid.NewGuid():N}", amountMicros = "1000000", deliveryMethod = "STANDARD", explicitlyShared = false } },
                },
                validateOnly = true,
            },
            cancellationToken);
        return write.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? CannotChange : null;
    }

    private async Task<CustomerRow?> DescribeAsync(HttpClient client, string accessToken, string id, CancellationToken cancellationToken)
    {
        using var response = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/customers/{Uri.EscapeDataString(id)}/googleAds:search",
            accessToken,
            new { query = "SELECT customer.id, customer.descriptive_name, customer.manager FROM customer LIMIT 1" },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var page = await response.Content.ReadFromJsonAsync<SearchPage>(GoogleHttp.Json, cancellationToken);
        return page?.Results?.FirstOrDefault()?.Customer;
    }

    private sealed record AccessibleCustomers(string[]? ResourceNames);

    private sealed record SearchPage(SearchRow[]? Results);

    private sealed record SearchRow(CustomerRow? Customer);

    private sealed record CustomerRow(string? Id, string? DescriptiveName, bool Manager);
}
