using Kairo.Modules.CampaignRooms.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.Stages;

internal sealed class ClaimedStageRun
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }
}

internal sealed class StageDispatcher(
    CampaignRoomsDbContext db,
    KairoDatabase database,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<StageDispatcher> logger)
{
    public const int BatchSize = 4;

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var lease = time.GetUtcNow() + Lease;
        List<ClaimedStageRun> claimed;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            claimed = await ClaimAsync(lease, cancellationToken);
        }

        await Task.WhenAll(claimed.Select(run => RunAsync(run, lease, cancellationToken)));
        return claimed.Count;
    }

    private Task<List<ClaimedStageRun>> ClaimAsync(DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Database.SqlQuery<ClaimedStageRun>($"""
            UPDATE TOP (4) campaign_rooms.StageRuns WITH (READPAST, UPDLOCK, ROWLOCK)
            SET ClaimedUntil = {lease}, Attempts = Attempts + 1, Status = N'running', StartedAt = COALESCE(StartedAt, {now})
            OUTPUT inserted.Id, inserted.BrandId
            WHERE Status IN (N'queued', N'running') AND NextAttemptAt <= {now} AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
            """).ToListAsync(cancellationToken);
    }

    private async Task RunAsync(ClaimedStageRun claimed, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(claimed.BrandId, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<StageRunner>().RunAsync(claimed.Id, lease, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Stage run {RunId} stopped unexpectedly. It runs again when its lease ends.", claimed.Id);
        }
    }
}

internal sealed class StageLoopSwitch
{
    public bool IsOn { get; private set; }

    public void TurnOn() => IsOn = true;
}

internal sealed class StageLoop(
    StageLoopSwitch loopSwitch,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<StageLoop> logger) : BackgroundService
{
    public const string LoopSetting = "Kairo:CampaignRooms:StageLoop";

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
                await scope.ServiceProvider.GetRequiredService<StageDispatcher>().RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Processing stage runs failed. Trying again shortly.");
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
