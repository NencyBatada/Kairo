namespace Kairo.Modules.BrandKit.Kit;

internal static class KitSetup
{
    public static void AddBrandKit(this IServiceCollection services)
    {
        services.AddScoped<KitVersions>();
    }

    public static void MapBrandKit(this IEndpointRouteBuilder api)
    {
        var kit = api.MapBrandApi().MapGroup("/brand-kit");
        kit.MapGet("", KitEndpoints.GetAsync);

        var draft = kit.MapGroup("/draft").RequireBrandRole(BrandRoles.Admin);
        draft.MapPut("", KitEndpoints.SaveDraftAsync);
        draft.MapDelete("", KitEndpoints.DiscardDraftAsync);
        draft.MapPost("/approve", KitEndpoints.ApproveAsync);
    }
}
