namespace Kairo.Modules.Opportunities.Refresh;

internal static class RefreshClock
{
    public const string IndiaTimeZone = "Asia/Kolkata";

    public static DateTime Local(DateTimeOffset at, string? timeZoneId) => TimeZoneInfo.ConvertTime(at, Zone(timeZoneId)).DateTime;

    public static bool IsDue(DateTimeOffset now, string? timeZoneId, DateTimeOffset? lastSuccess)
    {
        if (lastSuccess is not { } last)
        {
            return true;
        }

        var today = Local(now, timeZoneId);
        return today.Hour >= OpportunityOptions.RefreshFromHour && DateOnly.FromDateTime(today) > DateOnly.FromDateTime(Local(last, timeZoneId));
    }

    private static TimeZoneInfo Zone(string? timeZoneId) =>
        !string.IsNullOrEmpty(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(IndiaTimeZone);
}
