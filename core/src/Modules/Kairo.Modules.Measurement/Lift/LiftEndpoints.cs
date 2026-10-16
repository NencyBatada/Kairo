using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.Measurement.Data;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Measurement.Lift;

internal sealed record LiftReadingView(
    Guid Id,
    bool Early,
    DateOnly StartDate,
    DateOnly ThroughDate,
    string Kpi,
    string Unit,
    int Days,
    int PlannedDays,
    string Status,
    string? Reason,
    bool Conclusive,
    double? Lift,
    double? Low,
    double? High,
    double? Actual,
    double? Expected,
    double? Extra,
    double? ExtraLow,
    double? ExtraHigh,
    int Placebos,
    LiftFigures? Revenue,
    RegionResult[] Regions,
    DayResult[] Daily,
    string Method,
    DateTimeOffset CreatedAt);

internal sealed record LiftView(Guid CampaignId, string Stage, LiftReadingView? Final, LiftReadingView? LatestEarly, LiftReadingView[] Readings);

internal static class LiftEndpoints
{
    public static void MapLift(this IEndpointRouteBuilder api) =>
        api.MapBrandApi().MapGet("/campaigns/{campaignId:guid}/lift", GetAsync);

    public static async Task<Results<Ok<LiftView>, NotFound>> GetAsync(
        Guid campaignId, MeasurementDbContext db, ICampaignTests tests, CancellationToken cancellationToken)
    {
        if (await tests.GetAsync(campaignId, cancellationToken) is not { } test) return TypedResults.NotFound();
        var readings = (await db.LiftReadings.AsNoTracking().Where(row => row.RoomId == campaignId).ToListAsync(cancellationToken))
            .OrderBy(row => row.ThroughDate).ThenBy(row => row.Early ? 0 : 1).Select(View).ToArray();
        return TypedResults.Ok(new LiftView(campaignId, test.Stage, readings.LastOrDefault(row => !row.Early),
            readings.LastOrDefault(row => row.Early), readings));
    }

    public static LiftReadingView View(LiftReading row) => new(
        row.Id, row.Early, row.StartDate, row.ThroughDate, row.Kpi, row.Unit, row.Days, row.PlannedDays, row.Status, row.Reason, row.Conclusive,
        row.Lift, row.Low, row.High, row.Actual, row.Expected, row.Extra, row.ExtraLow, row.ExtraHigh, row.Placebos,
        row.Revenue is null ? null : Read<LiftFigures>(row.Revenue), Read<RegionResult[]>(row.Regions) ?? [], Read<DayResult[]>(row.Daily) ?? [],
        row.Method, row.CreatedAt);

    private static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
}
