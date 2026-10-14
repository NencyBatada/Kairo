namespace Kairo.Modules.BrandKit.Drafting;

internal static class DraftingSetup
{
    public static void AddDrafting(this IServiceCollection services)
    {
        services.AddHttpClient(BestAds.ClientName, client => client.Timeout = BestAds.Timeout).RemoveAllResilienceHandlers();
        services.AddScoped<WebsiteReader>();
        services.AddScoped<BestAds>();
        services.AddScoped<DraftAgent>();
        services.AddScoped<LogoSaver>();
    }

    public static void MapDrafting(this RouteGroupBuilder kit)
    {
        var drafting = kit.MapGroup("/drafting");
        drafting.MapPost("", DraftingEndpoints.StartAsync).RequireBrandRole(BrandRoles.Admin);
        drafting.MapGet("/latest", DraftingEndpoints.LatestAsync);
    }
}
