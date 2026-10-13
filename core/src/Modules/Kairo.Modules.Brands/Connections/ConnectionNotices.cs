using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionNotices
{
    private const string Continue = "Reconnect it so refreshes and changes can continue.";

    public static Notification NeedsReconnect(ConnectedProduct product, string reason)
    {
        var name = Products.Get(product).Name;
        var cause = reason switch
        {
            ReconnectReasons.TokenRejected => $"Google stopped accepting Kairo's access to {name}.",
            ReconnectReasons.TokenUnreadable => $"Kairo can no longer read its stored access to {name}.",
            ReconnectReasons.AdminLeft => $"{name} was connected by an admin who has left or is no longer an admin.",
            _ => $"Kairo lost its access to {name}.",
        };

        return new Notification(
            "connection.problem",
            LiveAudience.Roles(BrandRoles.Admin),
            $"{name} needs reconnecting",
            $"{cause} {Continue}",
            "/settings/connections");
    }
}
