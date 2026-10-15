using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.ProofPlans;

internal sealed record BriefScope(IReadOnlyList<string> Regions, DateOnly Start, DateOnly? End)
{
    public int? Days => End is { } end ? end.DayNumber - Start.DayNumber + 1 : null;

    public static BriefScope Of(Brief brief, RoomEvidence evidence, TimeZoneInfo zone, DateTimeOffset now)
    {
        var start = Day(now, zone).AddDays(1);
        DateOnly? end = evidence.Trigger?.ConditionEndsAt is { } ends ? Day(ends.AddTicks(-1), zone) : null;
        return new BriefScope(
            brief.Content.Regions.Where(region => !string.IsNullOrWhiteSpace(region)).ToList(),
            start,
            end is { } last && last < start ? start : end);
    }

    private static DateOnly Day(DateTimeOffset moment, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);
}
