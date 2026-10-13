namespace Kairo.Modules.Notifications.Delivery;

internal static class EmailRetries
{
    public const int MaxAttempts = 6;

    private static readonly TimeSpan[] Waits =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    public static TimeSpan? After(int failedAttempts) =>
        failedAttempts is >= 1 and < MaxAttempts ? Waits[failedAttempts - 1] : null;
}
