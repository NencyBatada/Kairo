namespace Kairo.Modules.Catalogue.Format;

internal static class Attributes
{
    public const string Id = "id";
    public const string Title = "title";
    public const string Description = "description";
    public const string Link = "link";
    public const string ImageLink = "image_link";
    public const string AdditionalImageLink = "additional_image_link";
    public const string Price = "price";
    public const string SalePrice = "sale_price";
    public const string Availability = "availability";
    public const string ProductType = "product_type";
    public const string GoogleProductCategory = "google_product_category";
    public const string Brand = "brand";
    public const string Gtin = "gtin";
    public const string ItemGroupId = "item_group_id";
    public const string Quantity = "quantity";

    public static readonly string[] QuantityNames = [Quantity, "stock_quantity", "sell_on_google_quantity"];

    public static readonly string[] ListValued = [AdditionalImageLink, ProductType];

    public static string Normalise(string name)
    {
        var normal = name.Trim().TrimStart('\uFEFF').Trim().ToLowerInvariant();
        if (normal.StartsWith("g:", StringComparison.Ordinal))
        {
            normal = normal[2..];
        }

        return normal.Replace(' ', '_');
    }
}
