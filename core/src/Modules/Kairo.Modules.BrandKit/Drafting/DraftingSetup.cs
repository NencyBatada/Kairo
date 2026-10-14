namespace Kairo.Modules.BrandKit.Drafting;

internal static class DraftingSetup
{
    public static void MapDrafting(this RouteGroupBuilder kit)
    {
        var drafting = kit.MapGroup("/drafting");
        drafting.MapPost("", DraftingEndpoints.StartAsync).RequireBrandRole(BrandRoles.Admin);
        drafting.MapGet("/latest", DraftingEndpoints.LatestAsync);
    }
}
