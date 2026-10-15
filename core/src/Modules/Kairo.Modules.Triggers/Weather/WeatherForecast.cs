namespace Kairo.Modules.Triggers.Weather;

internal sealed class WeatherForecast
{
    public Guid Id { get; set; }

    public string RegionId { get; set; } = "";

    public DateTimeOffset Hour { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public List<ForecastDay> Days { get; set; } = [];
}

internal sealed class ForecastDay
{
    public DateOnly Date { get; set; }

    public double RainMillimetres { get; set; }

    public double? MaxCelsius { get; set; }
}
