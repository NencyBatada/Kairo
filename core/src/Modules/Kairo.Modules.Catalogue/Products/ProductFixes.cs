namespace Kairo.Modules.Catalogue.Products;

internal sealed class ProductFixes
{
    public string? Title { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string? Link { get; set; }

    public string? ImageLink { get; set; }

    public string? Category { get; set; }

    public bool DropSupplied(Product product)
    {
        var dropped = false;
        if (product.Title is not null && Title is not null)
        {
            Title = null;
            dropped = true;
        }

        if (product.Price is not null && Price is not null)
        {
            Price = null;
            Currency = null;
            dropped = true;
        }

        if (product.Link is not null && Link is not null)
        {
            Link = null;
            dropped = true;
        }

        if (product.ImageLink is not null && ImageLink is not null)
        {
            ImageLink = null;
            dropped = true;
        }

        if (product.Category is not null && Category is not null)
        {
            Category = null;
            dropped = true;
        }

        return dropped;
    }
}
