using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.ProofPlans;

internal sealed record BriefScope(IReadOnlyList<string> Regions, DateOnly Start, DateOnly? End, DateOnly HistoryEnd)
{
    public int? Days => End is { } end ? end.DayNumber - Start.DayNumber + 1 : null;

    public static BriefScope Of(Brief brief, RoomEvidence evidence, TimeZoneInfo zone, DateTimeOffset now)
    {
        var content = brief.Content.Normalized();
        var today = Day(now, zone);
        var start = content.StartDate ?? today.AddDays(1);
        DateOnly? end = content.EndDate ?? (evidence.Trigger?.ConditionEndsAt is { } ends ? Day(ends.AddTicks(-1), zone) : null);
        var beforeStart = start.AddDays(-2);
        var yesterday = today.AddDays(-1);
        return new BriefScope(
            (content.Regions ?? []).Where(region => !string.IsNullOrWhiteSpace(region)).ToList(),
            start,
            end is { } last && last < start ? start : end,
            beforeStart < yesterday ? beforeStart : yesterday);
    }

    private static DateOnly Day(DateTimeOffset moment, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);
}
