using System.Text.Json;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Lift;

namespace Kairo.Modules.Measurement.Impact;

internal sealed record ImpactReportView(
    Guid Id,
    string Status,
    string Title,
    string Kpi,
    string Currency,
    decimal Spend,
    decimal Results,
    decimal? CostPerResult,
    decimal? CostPerExtra,
    decimal? CostPerExtraLow,
    decimal? CostPerExtraHigh,
    decimal? TargetCost,
    string Outcome,
    string NextStep,
    NextTestPlan? NextTest,
    ImpactFlag[] Flags,
    string? Summary,
    string? NextStepReasoning,
    string? InconclusiveReason,
    string? NextTestAdvice,
    string? Model,
    string? PromptVersion,
    DateTimeOffset? ReadyAt);

internal sealed record ImpactView(Guid CampaignId, string Stage, string State, ImpactReportView? Report, LiftReadingView? Final);

internal static class ImpactStates
{
    public const string Measuring = "measuring";
    public const string Writing = "writing";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

internal static class ImpactEndpoints
{
    public static void MapImpact(this IEndpointRouteBuilder api) =>
        api.MapBrandApi().MapGet("/campaigns/{campaignId:guid}/impact-report", GetAsync);

    public static async Task<Results<Ok<ImpactView>, NotFound>> GetAsync(
        Guid campaignId, MeasurementDbContext db, ICampaignTests tests, CancellationToken cancellationToken)
    {
        if (await tests.GetAsync(campaignId, cancellationToken) is not { } test) return TypedResults.NotFound();
        var final = await db.LiftReadings.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == campaignId && !row.Early, cancellationToken);
        var report = await db.ImpactReports.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == campaignId, cancellationToken);
        var state = report?.Status switch
        {
            ImpactStatuses.Ready => ImpactStates.Ready,
            ImpactStatuses.Failed => ImpactStates.Failed,
            _ => final is null ? ImpactStates.Measuring : ImpactStates.Writing,
        };
        return TypedResults.Ok(new ImpactView(campaignId, test.Stage, state, report is { Status: ImpactStatuses.Ready } ? View(report) : null,
            final is null ? null : LiftEndpoints.View(final)));
    }

    public static ImpactReportView View(ImpactReport row) => new(
        row.Id, row.Status, row.Title, row.Kpi, row.Currency, row.Spend, row.Results, row.CostPerResult, row.CostPerExtra, row.CostPerExtraLow,
        row.CostPerExtraHigh, row.TargetCost, row.Outcome, row.NextStep, row.NextTest is null ? null : Read<NextTestPlan>(row.NextTest),
        Read<ImpactFlag[]>(row.Flags) ?? [], row.Summary, row.NextStepReasoning, row.InconclusiveReason, row.NextTestAdvice, row.Model,
        row.PromptVersion, row.ReadyAt);

    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
}
