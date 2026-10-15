using Kairo.Modules.Catalogue.Imports;
using Kairo.Modules.Catalogue.Products;
using Kairo.Modules.Catalogue.Refresh;

namespace Kairo.Modules.Catalogue;

internal static class CatalogueSetup
{
    public static void AddCatalogue(this IServiceCollection services)
    {
        services.AddScoped<CatalogueImporter>();
        services.AddScoped<FeedFetcher>();
        services.AddScoped<FeedRefresher>();
        services.AddSingleton<RefreshRunner>();
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

        catalogue.MapGet("/summary", SummaryEndpoints.GetAsync);
        var products = catalogue.MapGroup("/products");
        products.MapGet("", ProductEndpoints.ListAsync);
        products.MapGet("/{productId:guid}", ProductEndpoints.GetAsync);
        products.MapPatch("/{productId:guid}/fixes", ProductEndpoints.FixAsync).RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer);
    }
}
