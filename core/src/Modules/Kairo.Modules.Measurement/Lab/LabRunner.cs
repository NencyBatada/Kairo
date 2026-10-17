using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Lift;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Lab;

internal sealed class ClaimedAnalysis
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }
}

internal sealed class LabDispatcher(
    MeasurementDbContext db,
    KairoDatabase database,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LabDispatcher> logger)
{
    public const int BatchSize = 2;

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var lease = time.GetUtcNow() + Lease;
        List<ClaimedAnalysis> claimed;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var now = time.GetUtcNow();
            claimed = await db.Database.SqlQuery<ClaimedAnalysis>($"""
                UPDATE TOP (2) measurement.LabAnalyses WITH (READPAST, UPDLOCK, ROWLOCK)
                SET ClaimedUntil = {lease}, Attempts = Attempts + 1
                OUTPUT inserted.Id, inserted.BrandId
                WHERE State = N'running' AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
                """).ToListAsync(cancellationToken);
        }

        await Task.WhenAll(claimed.Select(analysis => RunAsync(analysis, lease, cancellationToken)));
        return claimed.Count;
    }

    private async Task RunAsync(ClaimedAnalysis claimed, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(claimed.BrandId, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<LabRunner>().RunAsync(claimed.Id, lease, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Impact Lab analysis {AnalysisId} stopped unexpectedly. It runs again when its lease ends.", claimed.Id);
        }
    }
}

internal sealed class LabRunner(
    MeasurementDbContext db,
    KairoDatabase database,
    ImpactLabAgent agent,
    IBrandDirectory brands,
    INotifications notifications,
    IAuditLog audit,
    ILiveUpdates live,
    TimeProvider time,
    ILogger<LabRunner> logger)
{
    public const int MaxAttempts = 3;
    public const string NotificationKind = "impact_lab.ready";
    public const string FailedMessage = "Kairo couldn't finish this analysis. Check the agent service, then run it again.";

    public static readonly TimeSpan Backoff = TimeSpan.FromSeconds(30);

    public async Task RunAsync(Guid analysisId, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var analysis = await db.LabAnalyses.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == analysisId && row.ClaimedUntil == lease && row.State == LabStates.Running, cancellationToken);
        if (analysis is null)
        {
            return;
        }

        if (analysis.Attempts > MaxAttempts)
        {
            await FinishAsync(analysisId, lease, null, FailedMessage, cancellationToken);
            return;
        }

        LabRunAnswer answer;
        try
        {
            answer = await agent.RunAsync(await RequestAsync(analysis, cancellationToken), cancellationToken);
        }
        catch (AgentCallException exception)
        {
            logger.LogWarning(exception, "Impact Lab analysis {AnalysisId} failed on attempt {Attempt}: {Code}.", analysisId, analysis.Attempts, exception.Code);
            if (analysis.Attempts < MaxAttempts)
            {
                await db.LabAnalyses
                    .Where(row => row.Id == analysisId && row.ClaimedUntil == lease)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ClaimedUntil, time.GetUtcNow() + Backoff), cancellationToken);
                return;
            }

            await FinishAsync(analysisId, lease, null, $"{FailedMessage} ({exception.Code})", cancellationToken);
            return;
        }

        await FinishAsync(analysisId, lease, answer, null, cancellationToken);
    }

    private async Task<LabRunRequest> RequestAsync(LabAnalysis analysis, CancellationToken cancellationToken)
    {
        var dataset = await db.LabDatasets.AsNoTracking().SingleAsync(row => row.AnalysisId == analysis.Id, cancellationToken);
        var affected = LabViews.Keys(analysis.Affected);
        var comparison = LabViews.Keys(analysis.Comparison);
        var wanted = affected.Concat(comparison).ToHashSet(StringComparer.Ordinal);
        var series = (LabViews.Read<LabSeriesData[]>(dataset.Series ?? "[]") ?? []).Where(item => wanted.Contains(item.Key)).ToList();
        var spec = LabSourceSpec.Read(analysis.Source);
        return new LabRunRequest(
            series[0].Values.Length,
            analysis.HistoryDays!.Value,
            analysis.ChangeDays!.Value,
            LabViews.PlannedDays(analysis),
            [.. series.Select(item => new LabSeries(item.Key, item.Name, item.Values))],
            affected,
            comparison,
            analysis.Name,
            analysis.WhatChanged,
            LabMetrics.Label(spec),
            spec.Kind,
            LabMetrics.IsMoney(spec.Metric) ? dataset.Currency ?? "" : "");
    }

    private async Task FinishAsync(Guid analysisId, DateTimeOffset lease, LabRunAnswer? answer, string? problem, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var analysis = await db.LabAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId && row.ClaimedUntil == lease, cancellationToken);
            if (analysis is not { State: LabStates.Running } || analysis.RunBy is not { } runBy)
            {
                await database.RollbackAsync(CancellationToken.None);
                return;
            }

            var now = time.GetUtcNow();
            analysis.ClaimedUntil = null;
            analysis.FinishedAt = now;
            analysis.UpdatedAt = now;
            analysis.Version++;
            if (answer is not null)
            {
                analysis.State = LabStates.Result;
                analysis.Outcome = answer.Outcome;
                analysis.Conclusive = answer.Figures?.Conclusive == true && answer.Outcome != LabOutcomes.Inconclusive;
                analysis.Result = LabViews.Write(new LabStoredResult(answer.Status, answer.Reason, answer.Figures, answer.Daily));
                analysis.Fit = answer.Fit ?? analysis.Fit;
                analysis.Detectable = answer.Detectable ?? analysis.Detectable;
                analysis.Placebos = answer.Placebos;
                analysis.Warnings = LabViews.Write(answer.Warnings);
                analysis.Summary = Cut(answer.Summary, 1200);
                analysis.Explanation = Cut(answer.Explanation, 1000);
                analysis.InconclusiveReason = Cut(answer.InconclusiveReason, 800);
                analysis.NextAdvice = Cut(answer.NextAdvice, 800);
                analysis.Method = Cut(answer.Method, 40);
                analysis.Model = Cut(answer.Model, 80);
                analysis.PromptVersion = Cut(answer.PromptVersion, 80);
                analysis.Problem = null;
            }
            else
            {
                analysis.State = LabStates.Failed;
                analysis.Problem = Cut(problem, 1000);
            }

            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                answer is null
                    ? new AuditRecord("impact_lab.analysis_failed", LabViews.AuditTarget, analysis.Id.ToString(), After: new { analysis.Attempts, analysis.Problem }, Actor: LiftMeasurer.Actor)
                    : new AuditRecord("impact_lab.result_ready", LabViews.AuditTarget, analysis.Id.ToString(),
                        After: new { analysis.Outcome, analysis.Conclusive, answer.Figures?.Lift, answer.Figures?.Low, answer.Figures?.High, analysis.Model, analysis.PromptVersion },
                        Actor: LiftMeasurer.Actor),
                cancellationToken);
            await live.PublishAsync(LabViews.Changed(analysis.Id), cancellationToken);
            var brand = (await brands.GetAsync([analysis.BrandId], cancellationToken)).Single();
            await notifications.NotifyAsync(
                new Notification(
                    NotificationKind,
                    LiveAudience.Users(runBy),
                    Cut(answer is null ? $"Impact Lab couldn't finish {analysis.Name}" : $"Impact Lab result ready for {analysis.Name}", 200)!,
                    Cut(answer is null ? analysis.Problem : analysis.Summary, 1000) ?? "",
                    $"/{brand.Handle}/impact-lab/{analysis.Id}",
                    $"impact-lab:{analysis.Id:N}"),
                cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static string? Cut(string? text, int length) => text is null ? null : text.Length > length ? text[..length] : text;
}

internal sealed class LabLoopSwitch
{
    public bool IsOn { get; private set; }

    public void TurnOn() => IsOn = true;
}

internal sealed class LabLoop(
    LabLoopSwitch loopSwitch,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<LabLoop> logger) : BackgroundService
{
    public const string LoopSetting = "Kairo:Measurement:LabLoop";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!loopSwitch.IsOn || string.Equals(configuration[LoopSetting], "off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LabDispatcher>().RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Processing Impact Lab analyses failed. Trying again shortly.");
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
