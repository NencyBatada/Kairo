namespace Kairo.Modules.Live.Delivery;

internal static class LiveTimings
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan AccessCheckInterval = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan GapPatience = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
}
