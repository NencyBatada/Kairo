using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed record ProductAccount(string Id, string Name);

internal interface IProductAccounts
{
    ConnectedProduct Product { get; }

    Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken);

    Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken);
}

internal sealed class ProductCatalog(IEnumerable<IProductAccounts> all)
{
    public IProductAccounts For(ConnectedProduct product) => all.Single(accounts => accounts.Product == product);
}
