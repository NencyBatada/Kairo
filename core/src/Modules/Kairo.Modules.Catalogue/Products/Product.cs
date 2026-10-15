using Kairo.Modules.Catalogue.Format;

namespace Kairo.Modules.Catalogue.Products;

internal sealed class Product : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string OfferId { get; set; } = "";

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Link { get; set; }

    public string? ImageLink { get; set; }

    public List<string> AdditionalImageLinks { get; set; } = [];

    public decimal? Price { get; set; }

    public decimal? SalePrice { get; set; }

    public string? Currency { get; set; }

    public string? Availability { get; set; }

    public string? Category { get; set; }

    public string? GoogleCategory { get; set; }

    public string? Brand { get; set; }

    public string? Gtin { get; set; }

    public string? ItemGroupId { get; set; }

    public ProductFixes Fixes { get; set; } = new();

    public MissingDetails Missing { get; set; }

    public string Status { get; set; } = ProductStatuses.Active;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid LastImportId { get; set; }

    public string? StockState => Status == ProductStatuses.Removed ? ProductStatuses.Removed : Availability;

    public static Product From(ProductOffer offer, Guid importId, DateTimeOffset now)
    {
        var product = new Product { Id = Guid.NewGuid(), FirstSeenAt = now };
        product.Apply(offer, importId, now);
        return product;
    }

    public bool Apply(ProductOffer offer, Guid importId, DateTimeOffset now)
    {
        var changed = Status != ProductStatuses.Active
            || OfferId != offer.OfferId
            || Title != offer.Title
            || Description != offer.Description
            || Link != offer.Link
            || ImageLink != offer.ImageLink
            || !AdditionalImageLinks.SequenceEqual(offer.AdditionalImageLinks)
            || Price != offer.Price
            || SalePrice != offer.SalePrice
            || Currency != offer.Currency
            || Availability != offer.Availability
            || Category != offer.Category
            || GoogleCategory != offer.GoogleCategory
            || Brand != offer.Brand
            || Gtin != offer.Gtin
            || ItemGroupId != offer.ItemGroupId;

        OfferId = offer.OfferId;
        Title = offer.Title;
        Description = offer.Description;
        Link = offer.Link;
        ImageLink = offer.ImageLink;
        AdditionalImageLinks = [.. offer.AdditionalImageLinks];
        Price = offer.Price;
        SalePrice = offer.SalePrice;
        Currency = offer.Currency;
        Availability = offer.Availability;
        Category = offer.Category;
        GoogleCategory = offer.GoogleCategory;
        Brand = offer.Brand;
        Gtin = offer.Gtin;
        ItemGroupId = offer.ItemGroupId;
        Status = ProductStatuses.Active;
        RemovedAt = null;
        changed |= Fixes.DropSupplied(this);
        changed |= RefreshMissing();
        if (changed)
        {
            UpdatedAt = now;
            LastImportId = importId;
        }

        return changed;
    }

    public void Remove(Guid importId, DateTimeOffset now)
    {
        Status = ProductStatuses.Removed;
        RemovedAt = now;
        UpdatedAt = now;
        LastImportId = importId;
    }

    public bool RefreshMissing()
    {
        var missing = MissingDetails.None;
        if (Title is null && Fixes.Title is null)
        {
            missing |= MissingDetails.Title;
        }

        if (Price is null && Fixes.Price is null)
        {
            missing |= MissingDetails.Price;
        }

        if (Link is null && Fixes.Link is null)
        {
            missing |= MissingDetails.Link;
        }

        if (ImageLink is null && Fixes.ImageLink is null)
        {
            missing |= MissingDetails.Image;
        }

        if (Availability is null)
        {
            missing |= MissingDetails.Stock;
        }

        var changed = missing != Missing;
        Missing = missing;
        return changed;
    }
}
