using Kairo.Modules.Triggers.Data;
using Kairo.Modules.Triggers.Regions;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Triggers.Weather;

internal sealed class ForecastCache(TriggersDbContext db, WeatherClient client, TimeProvider time, ILogger<ForecastCache> logger)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(2);

    public static DateTimeOffset HourOf(DateTimeOffset at) => new(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);

    public async Task<IReadOnlyList<ForecastDay>?> DaysAsync(Region city, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().ToUniversalTime();
        var hour = HourOf(now);
        var cached = await db.Forecasts.AsNoTracking().SingleOrDefaultAsync(forecast => forecast.RegionId == city.Id && forecast.Hour == hour, cancellationToken);
        if (cached is not null)
        {
            return cached.Days;
        }

        List<ForecastDay> days;
        try
        {
            days = await client.DaysAsync(city, cancellationToken);
        }
        catch (WeatherUnavailableException exception)
        {
            logger.LogWarning(exception, "The forecast for {Region} is unavailable.", city.Name);
            return null;
        }

        var forecast = new WeatherForecast { Id = Guid.NewGuid(), RegionId = city.Id, Hour = hour, FetchedAt = now, Days = days };
        db.Forecasts.Add(forecast);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogDebug(exception, "Another check saved the forecast for {Region} first.", city.Name);
        }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.Entity is WeatherForecast or ForecastDay).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        return days;
    }
}
