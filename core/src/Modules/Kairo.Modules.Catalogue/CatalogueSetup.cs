using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue;

internal static class CatalogueSetup
{
    public static void AddCatalogue(this IServiceCollection services)
    {
        services.AddScoped<CatalogueImporter>();
        services.AddScoped<FeedFetcher>();
        services.AddScoped<FeedRefresher>();
    }

    public static void MapCatalogue(this IEndpointRouteBuilder api)
    {
        var catalogue = api.MapBrandApi().MapGroup("/catalogue");

        var imports = catalogue.MapGroup("/imports");
        imports.MapPost("", ImportEndpoints.ImportFileAsync)
            .RequireBrandRole(BrandRoles.Admin)
            .WithMetadata(new UploadSizeLimit(ImportEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: ImportEndpoints.MaxRequestBytes)
            .DisableAntiforgery();
        imports.MapGet("", ImportEndpoints.ListAsync);
        imports.MapGet("/{importId:guid}", ImportEndpoints.GetAsync);

        catalogue.MapGet("/source", SourceEndpoints.GetAsync);
        var feed = catalogue.MapGroup("/feed").RequireBrandRole(BrandRoles.Admin);
        feed.MapPut("", SourceEndpoints.SetFeedAsync);
        feed.MapDelete("", SourceEndpoints.RemoveFeedAsync);
        feed.MapPost("/refresh", SourceEndpoints.RefreshAsync);
    }
}
