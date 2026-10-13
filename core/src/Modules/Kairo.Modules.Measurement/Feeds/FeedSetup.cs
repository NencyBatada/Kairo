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

    public static void MapFeeds(this IEndpointRouteBuilder api)
    {
        var analytics = api.MapBrandApi().MapGroup("/analytics");
        var sales = analytics.MapGroup("/sales");
        sales.MapPost("/uploads", SalesEndpoints.UploadAsync)
            .RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer)
            .DisableAntiforgery();
        sales.MapGet("/uploads", SalesEndpoints.ListUploadsAsync);
    }
}
