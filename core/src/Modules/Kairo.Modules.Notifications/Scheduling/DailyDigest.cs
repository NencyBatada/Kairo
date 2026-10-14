namespace Kairo.Modules.Notifications.Scheduling;

internal static class DailyDigest
{
    public static readonly TimeOnly SendFrom = new(7, 0);

    public static DateTimeOffset? TodaysSendTime(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var sendFrom = local.Date + SendFrom.ToTimeSpan();
        return local < sendFrom ? null : WorkingHours.ToUtc(sendFrom, zone);
    }
}
