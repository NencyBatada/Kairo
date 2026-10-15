using Kairo.Modules.Triggers.Data;

namespace Kairo.Modules.Triggers.Festivals;

internal sealed class FestivalCalendar(TriggersDbContext db, KairoDatabase database, TimeProvider time)
{
    public static string LockName(Guid brandId) => $"festivals:{brandId:N}";

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (await db.BrandStates.AsNoTracking().AnyAsync(cancellationToken))
        {
            return;
        }

        var owned = !database.HasTransaction;
        if (owned)
        {
            await database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            await database.LockAsync(LockName(db.BrandId), cancellationToken);
            if (!await db.BrandStates.AnyAsync(cancellationToken))
            {
                var now = time.GetUtcNow();
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                db.Festivals.AddRange(IndiaFestivals.All
                    .Where(festival => festival.EndsOn >= today)
                    .Select(festival => new FestivalEvent
                    {
                        Id = Guid.NewGuid(),
                        Name = festival.Name,
                        StartsOn = festival.StartsOn,
                        EndsOn = festival.EndsOn,
                        Regions = [.. festival.Regions],
                        Origin = FestivalOrigins.Kairo,
                        CreatedAt = now,
                    }));
                db.BrandStates.Add(new BrandTriggerState { BrandId = db.BrandId, FestivalsSeededAt = now });
                await db.SaveChangesAsync(cancellationToken);
            }

            if (owned)
            {
                await database.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (owned)
            {
                await database.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }

            throw;
        }
    }
}
