using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed class DraftingSwitch
{
    public bool IsOn { get; private set; }

    public void TurnOn() => IsOn = true;
}

internal sealed class DraftingLoop(
    DraftingSwitch loopSwitch,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<DraftingLoop> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!loopSwitch.IsOn || string.Equals(configuration[DraftingSetup.LoopSetting], "off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DraftDispatcher>().RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Processing Brand Kit drafting runs failed. Trying again shortly.");
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
