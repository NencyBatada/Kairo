using Kairo.Modules.Notifications.Scheduling;

namespace Kairo.IntegrationTests.Notifications;

public sealed class WorkingHoursTests
{
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    public static TheoryData<string, string> FourWorkingHours => new()
    {
        { "2030-01-07T10:00", "2030-01-07T14:00" },
        { "2030-01-07T14:00", "2030-01-07T18:00" },
        { "2030-01-07T16:00", "2030-01-08T11:00" },
        { "2030-01-07T17:59", "2030-01-08T12:59" },
        { "2030-01-08T07:00", "2030-01-08T13:00" },
        { "2030-01-08T20:00", "2030-01-09T13:00" },
        { "2030-01-08T00:30", "2030-01-08T13:00" },
        { "2030-01-11T16:00", "2030-01-14T11:00" },
        { "2030-01-11T19:00", "2030-01-14T13:00" },
        { "2030-01-12T12:00", "2030-01-14T13:00" },
        { "2030-01-13T23:59", "2030-01-14T13:00" },
    };

    [Theory]
    [MemberData(nameof(FourWorkingHours))]
    public void Four_working_hours_count_only_weekday_office_hours_in_the_workspace_time_zone(string start, string due)
    {
        var result = WorkingHours.Add(Local(start), TimeSpan.FromHours(4), Kolkata);

        Assert.Equal(Local(due), result);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void Working_hours_are_weekdays_from_nine_to_six()
    {
        Assert.Equal(new TimeOnly(9, 0), WorkingHours.DayStart);
        Assert.Equal(new TimeOnly(18, 0), WorkingHours.DayEnd);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            Enum.GetValues<DayOfWeek>().Where(WorkingHours.IsWorkingDay).Order());
    }

    [Fact]
    public void A_long_wait_spans_several_working_days()
    {
        var due = WorkingHours.Add(Local("2030-01-10T15:00"), TimeSpan.FromHours(20), Kolkata);

        Assert.Equal(Local("2030-01-14T17:00"), due);
    }

    [Fact]
    public void No_wait_outside_hours_lands_on_the_next_opening()
    {
        Assert.Equal(Local("2030-01-14T09:00"), WorkingHours.Add(Local("2030-01-12T10:00"), TimeSpan.Zero, Kolkata));
        Assert.Equal(Local("2030-01-07T10:00"), WorkingHours.Add(Local("2030-01-07T10:00"), TimeSpan.Zero, Kolkata));
    }

    [Fact]
    public void The_start_is_read_in_the_workspace_time_zone_whatever_its_offset()
    {
        var start = new DateTimeOffset(2030, 1, 7, 4, 30, 0, TimeSpan.Zero);

        Assert.Equal(Local("2030-01-07T14:00"), WorkingHours.Add(start, TimeSpan.FromHours(4), Kolkata));
    }

    [Fact]
    public void Clock_changes_follow_local_office_hours()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var fridayAfternoon = new DateTimeOffset(2030, 3, 29, 16, 0, 0, TimeSpan.Zero);

        var due = WorkingHours.Add(fridayAfternoon, TimeSpan.FromHours(4), london);

        Assert.Equal(new DateTimeOffset(2030, 4, 1, 10, 0, 0, TimeSpan.Zero), due);
    }

    [Fact]
    public void A_negative_wait_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkingHours.Add(Local("2030-01-07T10:00"), TimeSpan.FromHours(-1), Kolkata));
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_India()
    {
        Assert.Equal(Kolkata.BaseUtcOffset, WorkingHours.ZoneOf("Not/AZone").BaseUtcOffset);
        Assert.Equal(Kolkata.BaseUtcOffset, WorkingHours.ZoneOf(null).BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(-5), WorkingHours.ZoneOf("America/New_York").BaseUtcOffset);
    }

    [Fact]
    public void The_daily_digest_is_due_from_seven_in_the_morning_local_time()
    {
        Assert.Equal(new TimeOnly(7, 0), DailyDigest.SendFrom);
        Assert.Null(DailyDigest.TodaysSendTime(Local("2030-01-07T06:59"), Kolkata));
        Assert.Equal(Local("2030-01-07T07:00"), DailyDigest.TodaysSendTime(Local("2030-01-07T07:00"), Kolkata));
        Assert.Equal(Local("2030-01-07T07:00"), DailyDigest.TodaysSendTime(Local("2030-01-07T23:30"), Kolkata));
        Assert.Equal(Local("2030-01-12T07:00"), DailyDigest.TodaysSendTime(Local("2030-01-12T08:00"), Kolkata));
    }

    [Fact]
    public void Hourly_groups_close_at_the_top_of_each_hour()
    {
        Assert.Equal(
            new DateTimeOffset(2030, 1, 7, 10, 0, 0, TimeSpan.Zero),
            HourlyGroups.Cutoff(new DateTimeOffset(2030, 1, 7, 10, 59, 59, TimeSpan.Zero)));
        Assert.Equal(
            new DateTimeOffset(2030, 1, 7, 4, 0, 0, TimeSpan.Zero),
            HourlyGroups.Cutoff(new DateTimeOffset(2030, 1, 7, 9, 45, 0, Ist)));
    }

    private static DateTimeOffset Local(string value) => new(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Ist);
}
