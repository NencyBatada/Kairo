namespace Kairo.Modules.Notifications.Scheduling;

internal static class WorkingHours
{
    public const string FallbackTimeZone = "Asia/Kolkata";

    public static readonly TimeOnly DayStart = new(9, 0);

    public static readonly TimeOnly DayEnd = new(18, 0);

    public static bool IsWorkingDay(DayOfWeek day) => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static DateTimeOffset Add(DateTimeOffset start, TimeSpan workingTime, TimeZoneInfo zone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workingTime, TimeSpan.Zero);

        var cursor = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var remaining = workingTime;
        while (true)
        {
            var opens = cursor.Date + DayStart.ToTimeSpan();
            var closes = cursor.Date + DayEnd.ToTimeSpan();
            if (!IsWorkingDay(cursor.DayOfWeek) || cursor >= closes)
            {
                cursor = cursor.Date.AddDays(1) + DayStart.ToTimeSpan();
                continue;
            }

            if (cursor < opens)
            {
                cursor = opens;
            }

            var left = closes - cursor;
            if (remaining <= left)
            {
                return ToUtc(cursor + remaining, zone);
            }

            remaining -= left;
            cursor = cursor.Date.AddDays(1) + DayStart.ToTimeSpan();
        }
    }

    public static TimeZoneInfo ZoneOf(string? timeZoneId) =>
        !string.IsNullOrEmpty(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(FallbackTimeZone);

    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wallClock, zone), TimeSpan.Zero);
    }
}
