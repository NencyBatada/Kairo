namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedSetup
{
    public static void AddFeeds(this IServiceCollection services)
    {
        services.AddHttpClient(MeasurementHttp.ClientName);
        services.AddSingleton<AnalyticsOptions>();
        services.AddSingleton<IServiceAccessToken, ApplicationDefaultAccessToken>();
        services.AddSingleton<BigQueryClient>();
        services.AddSingleton<BigQueryTables>();
    }
}
