using Kairo.Contracts.BrandKit;
using Kairo.Modules.BrandKit.Drafting;
using Kairo.Modules.BrandKit.Files;

namespace Kairo.Modules.BrandKit.Kit;

internal static class KitSetup
{
    public static void AddBrandKit(this IServiceCollection services)
    {
        services.AddScoped<KitVersions>();
        services.AddScoped<IBrandKit, BrandKitReader>();
    }

    public static void MapBrandKit(this IEndpointRouteBuilder api)
    {
        var kit = api.MapBrandApi().MapGroup("/brand-kit");
        kit.MapGet("", KitEndpoints.GetAsync);

        var draft = kit.MapGroup("/draft").RequireBrandRole(BrandRoles.Admin);
        draft.MapPut("", KitEndpoints.SaveDraftAsync);
        draft.MapDelete("", KitEndpoints.DiscardDraftAsync);
        draft.MapPost("/approve", KitEndpoints.ApproveAsync);

        var history = kit.MapGroup("/versions");
        history.MapGet("", KitEndpoints.HistoryAsync);
        history.MapGet("/{number:int}", KitEndpoints.GetVersionAsync);
        history.MapPost("/{number:int}/restore", KitEndpoints.RestoreAsync).RequireBrandRole(BrandRoles.Admin);

        var files = kit.MapGroup("/files");
        files.MapPost("", KitFileEndpoints.UploadAsync)
            .RequireBrandRole(BrandRoles.Admin)
            .WithMetadata(new UploadSizeLimit(KitFileEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: KitFileEndpoints.MaxRequestBytes)
            .DisableAntiforgery();
        files.MapGet("", KitFileEndpoints.ListAsync);
        files.MapGet("/{fileId:guid}/download", KitFileEndpoints.DownloadAsync);
        files.MapDelete("/{fileId:guid}", KitFileEndpoints.DeleteAsync).RequireBrandRole(BrandRoles.Admin);

        kit.MapDrafting();
    }
}
