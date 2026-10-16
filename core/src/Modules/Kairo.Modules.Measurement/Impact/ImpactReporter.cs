using System.Net;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Measurement;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Lift;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Impact;

internal sealed class ImpactReporter(
    MeasurementDbContext db,
    KairoDatabase database,
    ICampaignTests tests,
    IProofPlans proofPlans,
    ICampaignChanges changes,
    AccountActivity activity,
    ReportAgent agent,
    INotifications notifications,
    IAuditLog audit,
    ILiveUpdates live,
    TimeProvider time,
    ILogger<ImpactReporter> logger)
{
    public const int MaxAttempts = 3;
    public const string ReadyKind = "report.ready";
    public const string ProblemKind = "connection.problem";
    public const int MaxFlagDetail = 300;

    public static LiveEvent Changed(Guid roomId) => new("measurement.report_changed", new { campaignId = roomId }, LiveAudience.Everyone);

    public static string LockName(Guid roomId) => $"measurement-impact:{roomId:N}";

    public static string Link(BrandSummary brand, Guid roomId) => $"/{brand.Handle}/campaigns/{roomId}/results";

    public static string Subject(Guid roomId) => $"impact:{roomId:N}";

    public async Task<int> RunAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var finals = await db.LiftReadings.AsNoTracking().Where(row => !row.Early).ToListAsync(cancellationToken);
        var settled = (await db.ImpactReports.AsNoTracking().Where(row => row.Status != ImpactStatuses.Pending).Select(row => row.RoomId).ToListAsync(cancellationToken))
            .ToHashSet();
        var written = 0;
        foreach (var reading in finals.Where(reading => !settled.Contains(reading.RoomId)))
        {
            try
            {
                if (await ReportAsync(brand, reading, cancellationToken)) written++;
            }
            catch (Exception exception) when (Retryable(exception))
            {
                logger.LogWarning("The impact report for campaign {RoomId} wasn't written: {Reason}", reading.RoomId, exception.Message);
                await FailAsync(brand, reading, exception.Message, cancellationToken);
            }
        }

        return written;
    }

    private async Task<bool> ReportAsync(BrandSummary brand, LiftReading reading, CancellationToken cancellationToken)
    {
        if (await tests.GetAsync(reading.RoomId, cancellationToken) is not { } test || test.Running) return false;
        var plan = await proofPlans.GetCurrentAsync(reading.RoomId, cancellationToken);
        var campaigns = await changes.ReadAsync(reading.RoomId, reading.StartDate, reading.ThroughDate, cancellationToken);
        var spend = campaigns.Sum(campaign => campaign.Cost);
        var results = campaigns.Sum(campaign => campaign.Conversions);
        var input = new ImpactInput(reading.Status == LiftStatuses.Ok, reading.Conclusive, reading.Lift, reading.Low, reading.High, reading.Extra,
            reading.ExtraLow, reading.ExtraHigh, reading.Spread, reading.Days, reading.PlannedDays, plan?.MinDetectableLift, spend, results, TargetCost(campaigns));
        var decision = ImpactRules.Decide(input);
        var flags = await FlagsAsync(brand, test, plan, reading, campaigns, cancellationToken);
        var regions = Read<RegionResult[]>(reading.Regions) ?? [];
        var revenue = reading.Revenue is null ? null : Read<LiftFigures>(reading.Revenue);
        var answer = await agent.WriteAsync(new ReportRequest(test.Title, reading.Kpi, brand.Currency, reading.Days, Math.Max(reading.PlannedDays, 1),
            decision.Outcome, decision.NextStep, reading.Reason, reading.Lift, reading.Low, reading.High, reading.Actual, reading.Expected, reading.Extra,
            reading.ExtraLow, reading.ExtraHigh, revenue?.Lift, revenue?.Extra, spend, results, decision.CostPerResult, decision.CostPerExtra,
            decision.CostPerExtraLow, decision.CostPerExtraHigh, input.TargetCost, [.. flags.Select(flag => new ReportFlag(flag.Code, flag.Detail))],
            decision.NextTest is { } next ? new ReportNextTest(next.Days, next.AddRegions, next.Reasons) : null,
            [.. regions.Select(region => new ReportRegion(region.Name, region.Test, region.Figures?.Lift, region.Figures?.Low, region.Figures?.High,
                region.Figures?.Conclusive ?? false))]), cancellationToken);

        return await SaveAsync(reading.RoomId, report =>
        {
            report.ReadingId = reading.Id;
            report.Status = ImpactStatuses.Ready;
            report.Problem = null;
            report.Title = test.Title;
            report.Kpi = reading.Kpi;
            report.Currency = brand.Currency;
            (report.Spend, report.Results, report.TargetCost) = (spend, results, input.TargetCost);
            (report.CostPerResult, report.CostPerExtra, report.CostPerExtraLow, report.CostPerExtraHigh) =
                (decision.CostPerResult, decision.CostPerExtra, decision.CostPerExtraLow, decision.CostPerExtraHigh);
            (report.Outcome, report.NextStep) = (decision.Outcome, decision.NextStep);
            report.NextTest = decision.NextTest is null ? null : LiftMeasurer.Write(decision.NextTest);
            report.Flags = LiftMeasurer.Write(flags);
            (report.Summary, report.NextStepReasoning) = (answer.Summary, answer.NextStepReasoning);
            (report.InconclusiveReason, report.NextTestAdvice) = (answer.InconclusiveReason, answer.NextTestAdvice);
            (report.Model, report.PromptVersion) = (answer.Model, answer.PromptVersion);
            report.ReadyAt = time.GetUtcNow();
        }, async report =>
        {
            await audit.RecordAsync(new AuditRecord("measurement.report_ready", "campaign", report.RoomId.ToString(), null,
                new { reportId = report.Id, report.Outcome, report.NextStep, report.CostPerExtra, report.Flags, report.Model, report.PromptVersion }, LiftMeasurer.Actor),
                cancellationToken);
            await notifications.NotifyAsync(new Notification(ReadyKind, LiveAudience.Roles(BrandRoles.Marketer | BrandRoles.Approver),
                Cut($"Impact report ready for {test.Title}", 200), Cut(report.Summary!, 1000), Link(brand, report.RoomId), Subject(report.RoomId)), cancellationToken);
        }, cancellationToken);
    }

    private async Task FailAsync(BrandSummary brand, LiftReading reading, string problem, CancellationToken cancellationToken)
    {
        var title = (await tests.GetAsync(reading.RoomId, cancellationToken))?.Title ?? "a campaign";
        await SaveAsync(reading.RoomId, report =>
        {
            report.ReadingId = reading.Id;
            report.Attempts++;
            report.Problem = problem.Length > 1000 ? problem[..1000] : problem;
            if (report.Attempts >= MaxAttempts) report.Status = ImpactStatuses.Failed;
        }, async report =>
        {
            if (report.Status != ImpactStatuses.Failed) return;
            await audit.RecordAsync(new AuditRecord("measurement.report_failed", "campaign", report.RoomId.ToString(), null,
                new { reportId = report.Id, report.Attempts, report.Problem }, LiftMeasurer.Actor), cancellationToken);
            await notifications.NotifyAsync(new Notification(ProblemKind, LiveAudience.Roles(BrandRoles.Admin),
                Cut($"Impact report for {title} couldn't be written", 200),
                Cut($"Kairo measured the test but couldn't write its impact report after {MaxAttempts} tries. Check the Google Ads connection and the agent service. Details: {report.Problem}", 1000),
                Link(brand, report.RoomId), Subject(report.RoomId)), cancellationToken);
        }, cancellationToken);
    }

    private async Task<bool> SaveAsync(Guid roomId, Action<ImpactReport> change, Func<ImpactReport, Task> after, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(roomId), cancellationToken);
            var report = await db.ImpactReports.SingleOrDefaultAsync(row => row.RoomId == roomId, cancellationToken);
            if (report is { Status: not ImpactStatuses.Pending })
            {
                await database.RollbackAsync(CancellationToken.None);
                return false;
            }

            var now = time.GetUtcNow();
            if (report is null)
            {
                report = new ImpactReport { Id = Guid.NewGuid(), RoomId = roomId, CreatedAt = now };
                db.ImpactReports.Add(report);
            }
            else
            {
                report.Version++;
            }

            change(report);
            report.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await after(report);
            await live.PublishAsync(Changed(roomId), cancellationToken);
            await database.CommitAsync(cancellationToken);
            return report.Status == ImpactStatuses.Ready;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<List<ImpactFlag>> FlagsAsync(BrandSummary brand, CampaignTest test, CurrentProofPlan? plan, LiftReading reading,
        IReadOnlyList<CampaignState> campaigns, CancellationToken cancellationToken)
    {
        var flags = new List<ImpactFlag>();
        if (plan is { Changes.Count: > 0 })
            flags.Add(new ImpactFlag(ImpactFlags.ChangedAfterLock, "The test plan changed after launch: " + string.Join("; ", plan.Changes.Select(change => change.Reason))));
        if (plan is { ApprovedWithWarnings: true })
        {
            var warnings = plan.Warnings.Where(warning => warning != ImpactFlags.ChangedAfterLock).ToList();
            flags.Add(new ImpactFlag(ImpactFlags.ApprovedWithWarnings,
                "The test was approved despite warnings, so it is weaker than planned" + (warnings.Count > 0 ? ": " + string.Join(", ", warnings) : ".")));
        }

        if (test.EndedEarly) flags.Add(new ImpactFlag(ImpactFlags.EndedEarly, $"The test was ended early, after {reading.Days} of {reading.PlannedDays} planned days."));
        else if (reading.Days < reading.PlannedDays)
            flags.Add(new ImpactFlag(ImpactFlags.ShortWindow, $"Data covered only {reading.Days} of {reading.PlannedDays} planned days."));
        if (await AccountSpendAsync(brand, reading, campaigns, cancellationToken) is { Changed: true } spend)
            flags.Add(new ImpactFlag(ImpactFlags.AccountChange,
                $"Spend on the account's other campaigns moved from about {decimal.Round(spend.BeforePerDay)} to {decimal.Round(spend.DuringPerDay)} a day during the test, which may have influenced the result."));
        return [.. flags.Select(flag => flag with { Detail = Cut(flag.Detail, MaxFlagDetail) })];
    }

    private async Task<AccountSpend?> AccountSpendAsync(BrandSummary brand, LiftReading reading, IReadOnlyList<CampaignState> campaigns, CancellationToken cancellationToken)
    {
        var resources = campaigns.Select(campaign => campaign.Campaign?.Split('/')).Where(parts => parts is { Length: 4 } && parts[0] == "customers").ToList();
        if (resources.Count == 0) return null;
        return await activity.SpendAsync(brand.Id, resources[0]![1], [.. resources.Select(parts => parts![3])], reading.StartDate, reading.ThroughDate, cancellationToken);
    }

    private static decimal? TargetCost(IReadOnlyList<CampaignState> campaigns)
    {
        var targeted = campaigns.Where(campaign => campaign is { BiddingStrategy: "target_cpa", Target: > 0 }).ToList();
        if (targeted.Count == 0) return null;
        var spend = targeted.Sum(campaign => campaign.Cost);
        return decimal.Round(spend > 0 ? targeted.Sum(campaign => campaign.Target!.Value * campaign.Cost) / spend : targeted.Average(campaign => campaign.Target!.Value), 2);
    }

    private static bool Retryable(Exception exception) =>
        exception is AgentCallException or CampaignChangeException || exception is GoogleApiException { Status: not HttpStatusCode.NotFound };

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

    private static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web);
}
