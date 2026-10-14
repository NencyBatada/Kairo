namespace Kairo.Modules.BrandKit.Drafting;

internal static class DraftingSetup
{
    public const string LoopSetting = "Kairo:BrandKit:DraftingLoop";

    public static void AddDrafting(this IServiceCollection services)
    {
        services.AddHttpClient(BestAds.ClientName, client => client.Timeout = BestAds.Timeout).RemoveAllResilienceHandlers();
        services.AddScoped<WebsiteReader>();
        services.AddScoped<BestAds>();
        services.AddScoped<DraftAgent>();
        services.AddScoped<LogoSaver>();
        services.AddScoped<DraftRunner>();
        services.AddScoped<DraftDispatcher>();
        services.AddSingleton<DraftingSwitch>();
        services.AddHostedService<DraftingLoop>();
    }

    public static void MapDrafting(this RouteGroupBuilder kit)
    {
        var drafting = kit.MapGroup("/drafting");
        drafting.MapPost("", DraftingEndpoints.StartAsync).RequireBrandRole(BrandRoles.Admin);
        drafting.MapGet("/latest", DraftingEndpoints.LatestAsync);
    }
}
