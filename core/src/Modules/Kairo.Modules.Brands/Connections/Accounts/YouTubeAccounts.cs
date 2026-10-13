using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class YouTubeAccounts(IHttpClientFactory http) : IProductAccounts
{
    public const string NotManaged = "This Google account no longer manages that YouTube channel.";

    public ConnectedProduct Product => ConnectedProduct.YouTube;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true&maxResults=50",
            accessToken,
            null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ChannelPage>(GoogleHttp.Json, cancellationToken);
        return (page?.Items ?? []).Select(channel => new ProductAccount(channel.Id, channel.Snippet?.Title ?? channel.Id)).ToList();
    }

    public async Task<string?> TestAsync(string accessToken, ProductAccount account, CancellationToken cancellationToken) =>
        (await ListAsync(accessToken, cancellationToken)).Any(channel => channel.Id == account.Id) ? null : NotManaged;

    private sealed record ChannelPage(Channel[]? Items);

    private sealed record Channel(string Id, ChannelSnippet? Snippet);

    private sealed record ChannelSnippet(string? Title);
}
