using Microsoft.AspNetCore.Http.Metadata;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record UploadSizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;

internal static class FeedSetup
{
    public static void AddFeeds(this IServiceCollection services)
    {
        services.AddHttpClient(MeasurementHttp.ClientName, client => client.Timeout = MeasurementHttp.Timeout)
            .RemoveAllResilienceHandlers();
        services.AddSingleton<AnalyticsOptions>();
        services.AddSingleton<IServiceAccessToken, ApplicationDefaultAccessToken>();
        services.AddSingleton<BigQueryClient>();
        services.AddSingleton<BigQueryTables>();
        services.AddSingleton<BigQueryTableCheck>();
        services.AddSingleton<GoogleAdsReports>();
        services.AddSingleton<Ga4Reports>();
        services.AddScoped<IFeed, GoogleAdsFeed>();
        services.AddScoped<IFeed, Ga4Feed>();
        services.AddScoped<IFeed, SalesTableFeed>();
        services.AddScoped<BrandFeeds>();
        services.AddScoped<FeedRunner>();
    }

    public static void MapFeeds(this IEndpointRouteBuilder api)
    {
        var analytics = api.MapBrandApi().MapGroup("/analytics");
        analytics.MapGet("/feeds", FeedEndpoints.StatusAsync);
        var sales = analytics.MapGroup("/sales");
        sales.MapPost("/uploads", SalesEndpoints.UploadAsync)
            .RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer)
            .WithMetadata(new UploadSizeLimit(SalesEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: SalesEndpoints.MaxRequestBytes)
            .DisableAntiforgery();
        sales.MapGet("/uploads", SalesEndpoints.ListUploadsAsync);

        var source = sales.MapGroup("/source");
        source.MapGet("", SalesEndpoints.GetSourceAsync);
        source.MapPut("", SalesEndpoints.SetSourceAsync).RequireBrandRole(BrandRoles.Admin);
        source.MapDelete("", SalesEndpoints.RemoveSourceAsync).RequireBrandRole(BrandRoles.Admin);
    }
}
