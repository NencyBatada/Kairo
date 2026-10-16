using Kairo.Modules.Launch.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Launch;

internal sealed class ClaimedLaunch
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
}

internal sealed class LaunchDispatcher(LaunchDbContext db, KairoDatabase database, IServiceScopeFactory scopes, TimeProvider time,
    ILogger<LaunchDispatcher> logger)
{
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        List<ClaimedLaunch> claimed;
        var claim = Guid.NewGuid();
        var now = time.GetUtcNow();
        var until = now + LaunchRunner.Lease;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            claimed = await db.Database.SqlQuery<ClaimedLaunch>($"""
                UPDATE TOP (4) launch.Launches WITH (READPAST, UPDLOCK, ROWLOCK)
                SET Status = N'running', ClaimToken = {claim}, ClaimedUntil = {until}, Attempts = Attempts + 1
                OUTPUT inserted.Id, inserted.BrandId
                WHERE Status IN (N'queued', N'running') AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
                """).ToListAsync(cancellationToken);
        }

        await Task.WhenAll(claimed.Select(async row =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(row.BrandId, cancellationToken);
                await scope.ServiceProvider.GetRequiredService<LaunchRunner>().RunAsync(row.Id, claim, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Launch {LaunchId} stopped. Its persisted step will be checked when the lease expires.", row.Id);
            }
        }));
        return claimed.Count;
    }
}

internal sealed class LaunchLoopSwitch
{
    public bool Enabled { get; set; }
}

internal sealed class LaunchLoop(LaunchLoopSwitch loopSwitch, IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time,
    ILogger<LaunchLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!loopSwitch.Enabled || configuration["Kairo:Launch:Loop"] == "off") return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Checking queued launches failed.");
                await Task.Delay(TimeSpan.FromSeconds(10), time, stoppingToken);
            }
        }
    }
}
