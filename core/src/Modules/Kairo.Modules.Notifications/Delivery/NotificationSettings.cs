namespace Kairo.Modules.Notifications.Delivery;

internal static class NotificationSettings
{
    public const string Sender = "Kairo:Notifications:Sender";
    public const string SenderRefreshToken = "Kairo:Notifications:SenderRefreshToken";
    public const string DeliveryLoop = "Kairo:Notifications:DeliveryLoop";
    public const string PublicOrigin = "Kairo:PublicOrigin";

    public static bool HasSender(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[Sender]) && !string.IsNullOrWhiteSpace(configuration[SenderRefreshToken]);
}
