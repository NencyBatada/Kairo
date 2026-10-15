namespace Kairo.Modules.Notifications.Scheduling;

internal static class HourlyGroups
{
    public const int ListedItems = 5;

    public static DateTimeOffset Cutoff(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
