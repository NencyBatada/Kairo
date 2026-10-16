using System.Text.Json;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Catalogue;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record KeyedProduct(string Key, CatalogueProduct Product);

internal sealed record CopyContext(
    Brief Brief,
    BriefContent Content,
    string Brand,
    string Language,
    IReadOnlyList<string> CampaignTypes,
    IReadOnlyList<KeyedProduct> Products,
    JsonElement? BrandKit,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Languages)
{
    public string FeedLanguage() => CreativeLanguages.FeedLanguage(Products.Select(entry => entry.Product.Title), Languages);

    public CopyBriefData BriefData() => new(
        CopyDesk.Cut(Content.Title, 200) is { Length: > 0 } title ? title : "Campaign",
        CopyDesk.Cut(Content.Objective, 500),
        [.. (Content.Audiences ?? []).Where(text => !string.IsNullOrWhiteSpace(text)).Take(3).Select(text => CopyDesk.Cut(text, 200))],
        [.. (Content.KeyMessages ?? []).Where(text => !string.IsNullOrWhiteSpace(text)).Take(4).Select(text => CopyDesk.Cut(text, 200))],
        string.IsNullOrWhiteSpace(Content.Offer) ? null : CopyDesk.Cut(Content.Offer, 300),
        [.. (Content.LandingPages ?? []).Where(page => page.Length is > 0 and <= 2000).Take(6)]);

    public IReadOnlyList<CopyProductData> ProductData() =>
    [
        .. Products.Select(entry => new CopyProductData(
            entry.Key,
            CopyDesk.Cut(entry.Product.Title, 300) is { Length: > 0 } title ? title : entry.Product.OfferId,
            CopyDesk.Optional(entry.Product.Description, 2000),
            CopyDesk.Optional(entry.Product.Brand, 100),
            CopyDesk.Optional(entry.Product.Category, 750),
            entry.Product.Price,
            entry.Product.SalePrice,
            entry.Product.Currency is { Length: 3 } currency ? currency : null,
            entry.Product.Link is { Length: <= 2000 } link ? link : null,
            CopyDesk.Optional(entry.Product.Gtin, 50))),
    ];

    public string? KeyOf(string productId) => Products.FirstOrDefault(entry => entry.Product.Id.ToString() == productId)?.Key;

    public CatalogueProduct? ProductOf(string? key) => Products.FirstOrDefault(entry => entry.Key == key)?.Product;
}

internal sealed class CopyDesk(
    CampaignRoomsDbContext db,
    ICatalogue catalogue,
    IBrandDirectory brands,
    IBrandKit brandKit,
    ProvenanceTrail trail)
{
    public const int MaxProducts = 12;
    public const int MaxKeywords = 20;
    public const string DefaultLanguage = "en";

    public async Task<CopyContext?> ReadAsync(Room room, CancellationToken cancellationToken)
    {
        var brief = await db.Briefs.AsNoTracking()
            .Where(row => row.RoomId == room.Id)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (brief is null)
        {
            return null;
        }

        var content = brief.Content.Normalized();
        var ids = (content.ProductIds ?? [])
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var stocked = (await catalogue.GetInStockAsync(ids, cancellationToken)).ToDictionary(product => product.Id);
        var products = ids
            .Where(stocked.ContainsKey)
            .Take(MaxProducts)
            .Select((id, index) => new KeyedProduct($"p{index + 1}", stocked[id]))
            .ToList();
        var chosen = (content.CampaignTypes ?? []).Where(BriefChoices.IsCampaignType).Distinct().ToList();
        List<string> types = chosen.Count == 0
            ? [BriefChoices.Search]
            : chosen.Where(type => type != BriefChoices.Shopping || products.Count > 0).ToList();

        var brand = (await brands.GetAsync([room.BrandId], cancellationToken)).Single();
        var kit = await brandKit.GetApprovedAsync(cancellationToken);
        trail.BrandKit(kit?.Version);
        var evidence = RoomEvidence.Read(room.Evidence);
        var languages = CreativeLanguages.Of(content, DefaultLanguage);
        return new CopyContext(
            brief,
            content,
            Cut(brand.Name, 200),
            languages[0],
            types,
            products,
            kit?.Content,
            [.. (evidence.Keywords ?? []).Where(keyword => !string.IsNullOrWhiteSpace(keyword)).Select(keyword => Cut(keyword, 100)).Distinct().Take(MaxKeywords)],
            languages);
    }

    public static string Cut(string? text, int length)
    {
        var trimmed = (text ?? "").Trim();
        return trimmed.Length > length ? trimmed[..length].TrimEnd() : trimmed;
    }

    public static string? Optional(string? text, int length) => Cut(text, length) is { Length: > 0 } value ? value : null;
}
