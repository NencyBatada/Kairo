using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionEvents
{
    public static LiveEvent Changed(ConnectedProduct product) =>
        new("connection.changed", new { product = Products.Get(product).Slug }, LiveAudience.Everyone);
}
