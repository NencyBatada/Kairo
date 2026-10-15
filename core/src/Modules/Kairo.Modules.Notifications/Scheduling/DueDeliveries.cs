using Kairo.Contracts.Brands;
using Kairo.Modules.Notifications.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Scheduling;

internal sealed class DueDeliveries(IServiceScopeFactory scopes, IBrandDirectory brands, TimeProvider time, ILogger<DueDeliveries> logger)
{
    public async Task<DueRun> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await BrandsWithWorkAsync(now, cancellationToken);
        var targets = due.Count == 0 ? [] : await brands.GetAsync(due, cancellationToken);
        var totals = new DueCounts(0, 0, 0);
        var failed = 0;
        foreach (var brand in targets)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var counts = await scope.ServiceProvider.GetRequiredService<BrandDeliveries>().RunAsync(brand, now, cancellationToken);
                totals = new DueCounts(totals.Reminders + counts.Reminders, totals.Groups + counts.Groups, totals.Digests + counts.Digests);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(exception, "Delivering due notifications failed for brand {BrandId}.", brand.Id);
            }
        }

        return new DueRun(targets.Count, failed, totals.Reminders, totals.Groups, totals.Digests);
    }

    private async Task<List<Guid>> BrandsWithWorkAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var cutoff = HourlyGroups.Cutoff(now);
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Notifications
                .Where(notification => (notification.RemindAt != null && notification.RemindAt <= now) || notification.AwaitsDigest)
                .Select(notification => notification.BrandId)
                .Union(db.HeldNotifications.Where(item => item.CreatedAt < cutoff).Select(item => item.BrandId))
                .Distinct()
                .ToListAsync(cancellationToken);
        }
    }
}

internal sealed record DueRun(int Brands, int Failed, int Reminders, int Groups, int Digests);
