using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionNotices
{
    private const string Continue = "Reconnect it so refreshes and changes can continue.";

    public static string Explain(ConnectedProduct product, string? reason)
    {
        var name = Products.Get(product).Name;
        return reason switch
        {
            ReconnectReasons.TokenRejected => $"Google stopped accepting Kairo's access to {name}.",
            ReconnectReasons.TokenUnreadable => $"Kairo can no longer read its stored access to {name}.",
            ReconnectReasons.AdminLeft => $"{name} was connected by an admin who has left or is no longer an admin.",
            ReconnectReasons.AccessLost => $"The Google account that connected {name} no longer has access to the chosen account.",
            _ => $"Kairo lost its access to {name}.",
        };
    }

    public static Notification NeedsReconnect(ConnectedProduct product, string reason) =>
        new(
            "connection.problem",
            LiveAudience.Roles(BrandRoles.Admin),
            $"{Products.Get(product).Name} needs reconnecting",
            $"{Explain(product, reason)} {Continue}",
            "/settings/connections");
}
