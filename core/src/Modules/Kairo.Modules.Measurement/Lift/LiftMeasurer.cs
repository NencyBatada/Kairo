using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Live;
using Kairo.Contracts.Measurement;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Impact;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Lift;

internal sealed record LiftSweep(int Brands, int Early, int Final, int Reports);

internal sealed record RegionResult(string Id, string Name, bool Test, string Status, string? Reason, LiftFigures? Figures, double? Cost);

internal sealed record DayResult(DateOnly Date, double Actual, double Expected);

internal sealed class LiftMeasurer(
    MeasurementDbContext db,
    KairoDatabase database,
    ICampaignTests tests,
    IProofPlans proofPlans,
    IRegionHistory history,
    DataCoverage coverage,
    LiftAgent agent,
    IAuditLog audit,
    ILiveUpdates live,
    TimeProvider time,
    ILogger<LiftMeasurer> logger)
{
    public const int EarlyAfterDays = 7;
    public const int FinalWaitDays = 7;
    public static readonly AuditActor Actor = AuditActor.Agent("Prove agent", "prove");

    public static LiveEvent Changed(Guid roomId) => new("measurement.lift_changed", new { campaignId = roomId }, LiveAudience.Everyone);

    public static string LockName(Guid roomId) => $"measurement-lift:{roomId:N}";

    public async Task<(int Early, int Final)> RunAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var (early, final) = (0, 0);
        var today = Today(brand);
        foreach (var test in await tests.ListRunningAsync(cancellationToken))
        {
            try
            {
                if (test.Running ? await EarlyAsync(brand, test, today, cancellationToken) : await FinalAsync(brand, test, today, cancellationToken))
                {
                    if (test.Running) early++;
                    else final++;
                }
            }
            catch (AgentCallException exception)
            {
                logger.LogWarning("Lift for campaign {RoomId} was skipped: {Reason}", test.RoomId, exception.Message);
            }
        }

        return (early, final);
    }

    private async Task<bool> EarlyAsync(BrandSummary brand, CampaignTest test, DateOnly today, CancellationToken cancellationToken)
    {
        if (test.Start is not { } start || test.End is not { } end) return false;
        if (await coverage.ThroughAsync(Kpi(test.Kpi), cancellationToken) is not { } covered) return false;
        var through = Earliest(Earliest(covered, today.AddDays(-1)), end);
        if (through.DayNumber - start.DayNumber + 1 < EarlyAfterDays) return false;
        if (await db.LiftReadings.AnyAsync(row => row.RoomId == test.RoomId && row.Early && row.ThroughDate == through, cancellationToken)) return false;
        return await MeasureAsync(brand.Id, test, true, start, through, end, cancellationToken);
    }

    private async Task<bool> FinalAsync(BrandSummary brand, CampaignTest test, DateOnly today, CancellationToken cancellationToken)
    {
        if (test.Start is not { } start || test.End is not { } end) return false;
        if (await db.LiftReadings.AnyAsync(row => row.RoomId == test.RoomId && !row.Early, cancellationToken)) return false;
        var last = LastDay(brand, test, end);
        var covered = await coverage.ThroughAsync(Kpi(test.Kpi), cancellationToken);
        DateOnly through;
        if (covered is { } date && date >= last) through = last;
        else if (today.DayNumber > last.DayNumber + FinalWaitDays) through = covered is { } some && some < last ? some : last;
        else return false;
        return await MeasureAsync(brand.Id, test, false, start, through, end, cancellationToken);
    }

    private async Task<bool> MeasureAsync(Guid brandId, CampaignTest test, bool early, DateOnly start, DateOnly through, DateOnly end, CancellationToken cancellationToken)
    {
        if (await proofPlans.GetCurrentAsync(test.RoomId, cancellationToken) is not { } plan) return false;
        var kpi = Kpi(plan.Kpi);
        var reading = new LiftReading
        {
            Id = Guid.NewGuid(), RoomId = test.RoomId, Early = early, StartDate = start, ThroughDate = through, Kpi = kpi, Unit = plan.Unit,
            Days = Math.Max(through.DayNumber - start.DayNumber + 1, 0), PlannedDays = end.DayNumber - start.DayNumber + 1,
            Status = LiftStatuses.InsufficientData, Method = "",
        };
        if (reading.Days < 1)
        {
            reading.Reason = LiftReasons.NoTestDays;
        }
        else
        {
            await AskAsync(brandId, reading, plan, cancellationToken);
        }

        return await SaveAsync(reading, cancellationToken);
    }

    private async Task AskAsync(Guid brandId, LiftReading reading, CurrentProofPlan plan, CancellationToken cancellationToken)
    {
        var from = reading.StartDate.AddDays(-LiftAgent.HistoryDays);
        var unit = plan.Unit == "state" ? RegionUnit.State : RegionUnit.City;
        var series = (await history.DailyAsync(brandId, unit, from, reading.ThroughDate, cancellationToken))
            .Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var places = plan.TestRegions.Select(region => (Region: region, Test: true)).Concat(plan.Holdouts.Select(region => (Region: region, Test: false)))
            .DistinctBy(place => place.Region.Id, StringComparer.Ordinal).ToList();
        if (places.All(place => !place.Test) || places.All(place => place.Test))
        {
            reading.Reason = LiftReasons.NoData;
            return;
        }

        var days = reading.ThroughDate.DayNumber - from.DayNumber + 1;
        var startIndex = LiftAgent.HistoryDays;
        var answer = await agent.MeasureAsync(new LiftRequest(reading.Kpi, days, startIndex, reading.Days,
            [.. places.Select(place => series.GetValueOrDefault(place.Region.Id) is { } found
                ? new LiftRegion(place.Region.Id, place.Test, Outcome(found, reading.Kpi), found.Revenue)
                : new LiftRegion(place.Region.Id, place.Test, null, null))]), cancellationToken);

        reading.Status = answer.Status == LiftStatuses.Ok ? LiftStatuses.Ok : LiftStatuses.InsufficientData;
        reading.Reason = answer.Reason;
        reading.Method = answer.Method;
        if (answer.Outcome is { } figures)
        {
            reading.Conclusive = figures.Conclusive;
            (reading.Lift, reading.Low, reading.High) = (figures.Lift, figures.Low, figures.High);
            (reading.Actual, reading.Expected) = (figures.Actual, figures.Expected);
            (reading.Extra, reading.ExtraLow, reading.ExtraHigh) = (figures.Extra, figures.ExtraLow, figures.ExtraHigh);
            (reading.Placebos, reading.Spread) = (figures.Placebos, figures.Spread);
        }

        reading.Revenue = answer.Revenue is null ? null : Write(answer.Revenue);
        var answers = answer.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        reading.Regions = Write(places.Select(place =>
        {
            var found = answers.GetValueOrDefault(place.Region.Id);
            return new RegionResult(place.Region.Id, place.Region.Name, place.Test, found?.Status ?? LiftStatuses.InsufficientData, found?.Reason,
                found?.Figures, series.GetValueOrDefault(place.Region.Id)?.Cost is { } cost ? Math.Round(cost.Skip(startIndex).Sum(), 2) : null);
        }).ToList());
        reading.Daily = Write(answer.Daily.Select((day, index) => new DayResult(reading.StartDate.AddDays(index), day.Actual, day.Expected)).ToList());
    }

    private async Task<bool> SaveAsync(LiftReading reading, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(reading.RoomId), cancellationToken);
            var taken = reading.Early
                ? await db.LiftReadings.AnyAsync(row => row.RoomId == reading.RoomId && row.Early && row.ThroughDate == reading.ThroughDate, cancellationToken)
                : await db.LiftReadings.AnyAsync(row => row.RoomId == reading.RoomId && !row.Early, cancellationToken);
            if (taken)
            {
                await database.RollbackAsync(CancellationToken.None);
                return false;
            }

            reading.CreatedAt = time.GetUtcNow();
            db.LiftReadings.Add(reading);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditRecord("measurement.lift_measured", "campaign", reading.RoomId.ToString(), null,
                new { readingId = reading.Id, reading.Early, reading.ThroughDate, reading.Status, reading.Lift, reading.Low, reading.High, reading.Conclusive }, Actor),
                cancellationToken);
            await live.PublishAsync(Changed(reading.RoomId), cancellationToken);
            await database.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private DateOnly LastDay(BrandSummary brand, CampaignTest test, DateOnly end)
    {
        if (test.EndedAt is not { } ended) return end;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ended, Zone(brand)).DateTime).AddDays(-1);
        return Earliest(day, end);
    }

    private static double[]? Outcome(RegionSeries series, string kpi) => kpi == LiftKpis.Orders ? series.Orders : series.Conversions;

    private static string Kpi(string kpi) => kpi == LiftKpis.Conversions ? LiftKpis.Conversions : LiftKpis.Orders;

    private static DateOnly Earliest(DateOnly left, DateOnly right) => left < right ? left : right;

    private DateOnly Today(BrandSummary brand) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone(brand)).DateTime);

    private static TimeZoneInfo Zone(BrandSummary brand) => TimeZoneInfo.TryFindSystemTimeZoneById(brand.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    public static string Write(object value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}

internal static class LiftJobs
{
    public static void MapLiftJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/measurement/lift", RunAsync);

    private static async Task<Ok<LiftSweep>> RunAsync(IBrandDirectory brands, IServiceScopeFactory scopes, ILogger<LiftMeasurer> logger, IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var all = await brands.ListAllAsync(stopping);
        var (early, final, reports) = (0, 0, 0);
        foreach (var brand in all)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, stopping);
                var result = await scope.ServiceProvider.GetRequiredService<LiftMeasurer>().RunAsync(brand, stopping);
                early += result.Early;
                final += result.Final;
                reports += await scope.ServiceProvider.GetRequiredService<ImpactReporter>().RunAsync(brand, stopping);
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                logger.LogError(exception, "Lift readings failed for brand {BrandId}.", brand.Id);
            }
        }

        return TypedResults.Ok(new LiftSweep(all.Count, early, final, reports));
    }
}
