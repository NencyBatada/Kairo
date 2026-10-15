using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue;

internal static class CatalogueSetup
{
    public static void AddCatalogue(this IServiceCollection services)
    {
        services.AddScoped<CatalogueImporter>();
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
    }
}
