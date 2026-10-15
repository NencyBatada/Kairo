namespace Kairo.Modules.Triggers;

internal sealed class TriggerSettings(IConfiguration configuration)
{
    public const string WeatherProjectSetting = "Kairo:Triggers:WeatherProject";

    public string? WeatherProject => configuration[WeatherProjectSetting] is { Length: > 0 } project ? project : null;
}
