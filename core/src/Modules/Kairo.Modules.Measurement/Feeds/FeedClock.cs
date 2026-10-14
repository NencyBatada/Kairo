using System.Globalization;

namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedClock
{
    public const string IndiaTimeZone = "Asia/Kolkata";

    public static DateOnly Today(TimeProvider time, string? timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone(timeZoneId)).DateTime);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TimeZoneInfo Zone(string? timeZoneId) =>
        !string.IsNullOrEmpty(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(IndiaTimeZone);
}

internal sealed record FeedWindow(DateOnly From, DateOnly To)
{
    public static FeedWindow For(DateOnly today, bool firstRun) =>
        new(firstRun ? today.AddMonths(-13) : today.AddDays(-7), today.AddDays(-1));

    public IReadOnlyList<FeedWindow> Months()
    {
        var months = new List<FeedWindow>();
        for (var start = From; start <= To; start = new DateOnly(start.Year, start.Month, 1).AddMonths(1))
        {
            var end = new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
            months.Add(new FeedWindow(start, end < To ? end : To));
        }

        return months;
    }

    public IReadOnlyList<DateOnly> Days()
    {
        var days = new List<DateOnly>();
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }
}
