using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Ask;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal sealed class CatalogueAskResource(CatalogueDbContext db, CatalogueImporter importer, IBrandDirectory brands,
    CurrentMember member, IAuditLog audit, ILiveUpdates live, TimeProvider time) : IAskResource
{
    public string Kind => "catalogue";
    public BrandRoles Editors => BrandRoles.Admin | BrandRoles.Marketer;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var product = await ProductAsync(target, ct);
        return product is null ? null : new(product.Title ?? product.Fixes.Title ?? "Product", $"brand-kit/catalogue/products/{product.Id}",
            AskJson.Element(ProductView.Of(product)), AskJson.Revision(product));
    }
    public async Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct)
    {
        AskJson.Only(changes, "title", "link", "imageLink", "category", "price", "currency");
        var product = await ProductAsync(target, ct) ?? throw new AskResourceException("This product is unavailable.", 404);
        var before = AskJson.Element(ProductView.Of(product));
        var currency = (await brands.GetAsync([member.BrandId], ct)).Single().Currency;
        var errors = ProductFixRequest.Apply(JsonNode.Parse(changes.GetRawText())!.AsObject(), product, currency);
        if (errors.Count > 0) throw new AskResourceException(JsonSerializer.Serialize(errors), 400);
        product.RefreshMissing();
        return new(before, AskJson.Element(ProductView.Of(product)), changes.Clone(), ["Imported fields and stock remain controlled by the feed."]);
    }
    public async Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct)
    {
        if (!await importer.TryLockAsync(ct)) throw new AskResourceException("The catalogue is updating. Try again shortly.");
        AskJson.Unchanged(revision, await ReadAsync(target, ct));
        AskJson.Result(await ProductEndpoints.FixAsync(target.Id!.Value, JsonNode.Parse(payload.GetRawText())!.AsObject(), db, importer, brands, member, audit, live, time, ct));
    }
    private Task<Product?> ProductAsync(AskTarget target, CancellationToken ct) => db.BrandProducts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id, ct);
}
