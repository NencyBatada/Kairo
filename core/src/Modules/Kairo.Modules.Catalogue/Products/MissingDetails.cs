namespace Kairo.Modules.Catalogue.Products;

[Flags]
internal enum MissingDetails
{
    None = 0,
    Title = 1,
    Price = 2,
    Link = 4,
    Image = 8,
    Stock = 16,
    All = Title | Price | Link | Image | Stock,
}

internal static class ProductStatuses
{
    public const string Active = "active";
    public const string Removed = "removed";
}

internal static class Availabilities
{
    public const string InStock = "in_stock";
    public const string OutOfStock = "out_of_stock";
    public const string Preorder = "preorder";
    public const string Backorder = "backorder";

    public static readonly string[] All = [InStock, OutOfStock, Preorder, Backorder];
}
