using System.Net;
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class GoogleAdsAccounts(IHttpClientFactory http, IConfiguration configuration) : IProductAccounts
{
    public const string CannotOpen = "This Google account can't open that Google Ads account.";
    public const string CannotChange =
        "This Google account can see the Google Ads account but can't make changes to it. Ask for Standard or Admin access in Google Ads.";

    private const string LoginHeader = "login-customer-id";

    public ConnectedProduct Product => ConnectedProduct.GoogleAds;

    private string BaseUrl => $"https://googleads.googleapis.com/{configuration["Kairo:Google:AdsApiVersion"] ?? "v25"}";

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        using var listed = await client.CallAsync(HttpMethod.Get, $"{BaseUrl}/customers:listAccessibleCustomers", accessToken, null, cancellationToken);
        listed.EnsureSuccessStatusCode();
        var names = (await listed.Content.ReadFromJsonAsync<AccessibleCustomers>(GoogleHttp.Json, cancellationToken))?.ResourceNames ?? [];

        var direct = new List<ProductAccount>();
        var managed = new List<ProductAccount>();
        foreach (var name in names.Take(50))
        {
            var id = name["customers/".Length..];
            if (await DescribeAsync(client, accessToken, id, null, cancellationToken) is not { } customer)
            {
                continue;
            }

            if (!customer.Manager)
            {
                direct.Add(new ProductAccount(id, customer.DescriptiveName ?? id));
                continue;
            }

            var managerName = customer.DescriptiveName ?? id;
            foreach (var clientAccount in await ClientsAsync(client, accessToken, id, cancellationToken))
            {
                managed.Add(new ProductAccount(clientAccount.Id!, $"{clientAccount.DescriptiveName ?? clientAccount.Id} (through {managerName})", id));
            }
        }

        var directIds = direct.Select(account => account.Id).ToHashSet();
        return direct
            .Concat(managed.Where(account => !directIds.Contains(account.Id)).DistinctBy(account => account.Id))
            .ToList();
    }

    public async Task<string?> TestAsync(string accessToken, ProductAccount account, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        if (await DescribeAsync(client, accessToken, account.Id, account.ManagerId, cancellationToken) is null)
        {
            return CannotOpen;
        }

        using var write = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/customers/{Uri.EscapeDataString(account.Id)}/campaignBudgets:mutate",
            accessToken,
            new
            {
                operations = new[]
                {
                    new { create = new { name = $"Kairo access check {Guid.NewGuid():N}", amountMicros = "1000000", deliveryMethod = "STANDARD", explicitlyShared = false } },
                },
                validateOnly = true,
            },
            cancellationToken,
            Login(account.ManagerId));
        return write.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? CannotChange : null;
    }

    private async Task<CustomerRow?> DescribeAsync(
        HttpClient client,
        string accessToken,
        string id,
        string? managerId,
        CancellationToken cancellationToken)
    {
        var page = await SearchAsync(
            client,
            accessToken,
            id,
            managerId,
            "SELECT customer.id, customer.descriptive_name, customer.manager FROM customer LIMIT 1",
            cancellationToken);
        return page?.Results?.FirstOrDefault()?.Customer;
    }

    private async Task<IReadOnlyList<CustomerRow>> ClientsAsync(HttpClient client, string accessToken, string managerId, CancellationToken cancellationToken)
    {
        var page = await SearchAsync(
            client,
            accessToken,
            managerId,
            managerId,
            "SELECT customer_client.id, customer_client.descriptive_name, customer_client.manager FROM customer_client "
                + "WHERE customer_client.manager = FALSE AND customer_client.status = 'ENABLED'",
            cancellationToken);
        return (page?.Results ?? [])
            .Select(row => row.CustomerClient)
            .OfType<CustomerRow>()
            .Where(row => row is { Id: not null, Manager: false })
            .ToList();
    }

    private async Task<SearchPage?> SearchAsync(
        HttpClient client,
        string accessToken,
        string id,
        string? managerId,
        string query,
        CancellationToken cancellationToken)
    {
        using var response = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/customers/{Uri.EscapeDataString(id)}/googleAds:search",
            accessToken,
            new { query },
            cancellationToken,
            Login(managerId));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SearchPage>(GoogleHttp.Json, cancellationToken)
            : null;
    }

    private static (string Name, string Value)[] Login(string? managerId) =>
        managerId is null ? [] : [(LoginHeader, managerId)];

    private sealed record AccessibleCustomers(string[]? ResourceNames);

    private sealed record SearchPage(SearchRow[]? Results);

    private sealed record SearchRow(CustomerRow? Customer, CustomerRow? CustomerClient);

    private sealed record CustomerRow(string? Id, string? DescriptiveName, bool Manager);
}
