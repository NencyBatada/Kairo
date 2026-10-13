using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class MerchantCenterAccounts(IHttpClientFactory http) : IProductAccounts
{
    public const string CannotOpen = "This Google account can't open that Merchant Center account.";
    public const string CannotChange =
        "This Google account can see the Merchant Center account but can't make changes to it. Ask for Standard or Admin access in Merchant Center.";

    private const string BaseUrl = "https://merchantapi.googleapis.com/accounts/v1/accounts";

    public ConnectedProduct Product => ConnectedProduct.MerchantCenter;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}?pageSize=250", accessToken, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AccountPage>(GoogleHttp.Json, cancellationToken);
        return (page?.Accounts ?? [])
            .Where(account => account.AccountId is not null)
            .Select(account => new ProductAccount(account.AccountId!, account.AccountName ?? account.AccountId!))
            .ToList();
    }

    public async Task<string?> TestAsync(string accessToken, ProductAccount account, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}/{Uri.EscapeDataString(account.Id)}/users/me", accessToken, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return CannotOpen;
        }

        var user = await response.Content.ReadFromJsonAsync<MerchantUser>(GoogleHttp.Json, cancellationToken);
        return (user?.AccessRights ?? []).Any(right => right is "STANDARD" or "ADMIN") ? null : CannotChange;
    }

    private sealed record AccountPage(MerchantAccount[]? Accounts);

    private sealed record MerchantAccount(string? AccountId, string? AccountName);

    private sealed record MerchantUser(string[]? AccessRights);
}
