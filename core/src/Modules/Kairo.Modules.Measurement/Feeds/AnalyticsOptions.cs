namespace Kairo.Modules.Measurement.Feeds;

internal sealed class AnalyticsOptions(IConfiguration configuration)
{
    public const string NotSetUp = "Analytics data isn't set up in this environment.";
    public const string DefaultLocation = "asia-south1";

    public string? Project => configuration["Kairo:Analytics:Project"] is { Length: > 0 } project ? project : null;

    public bool Enabled => Project is not null;

    public string Location => configuration["Kairo:Analytics:Location"] is { Length: > 0 } location ? location : DefaultLocation;

    public string AdsApiVersion => configuration["Kairo:Google:AdsApiVersion"] is { Length: > 0 } version ? version : "v25";

    public static string DatasetFor(Guid brandId) => $"brand_{brandId:N}";
}
