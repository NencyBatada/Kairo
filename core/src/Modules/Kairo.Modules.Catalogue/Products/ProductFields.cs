using System.Globalization;

namespace Kairo.Modules.Catalogue.Products;

internal static class ProductFields
{
    public const string Title = "title";
    public const string Price = "price";
    public const string Currency = "currency";
    public const string Link = "link";
    public const string ImageLink = "imageLink";
    public const string Category = "category";
    public const string AnyMissing = "any";

    private static readonly (MissingDetails Flag, string Name)[] MissingNames =
    [
        (MissingDetails.Title, "title"),
        (MissingDetails.Price, "price"),
        (MissingDetails.Link, "link"),
        (MissingDetails.Image, "image"),
        (MissingDetails.Stock, "stock"),
    ];

    public static string[] Fixed(ProductFixes fixes)
    {
        var fields = new List<string>();
        if (fixes.Title is not null)
        {
            fields.Add(Title);
        }

        if (fixes.Price is not null)
        {
            fields.Add(Price);
        }

        if (fixes.Link is not null)
        {
            fields.Add(Link);
        }

        if (fixes.ImageLink is not null)
        {
            fields.Add(ImageLink);
        }

        if (fixes.Category is not null)
        {
            fields.Add(Category);
        }

        return [.. fields];
    }

    public static string[] Missing(MissingDetails missing) =>
        MissingNames.Where(entry => (missing & entry.Flag) != MissingDetails.None).Select(entry => entry.Name).ToArray();

    public static MissingDetails? ParseMissing(string value) =>
        value == AnyMissing
            ? MissingDetails.All
            : MissingNames.Where(entry => entry.Name == value).Select(entry => (MissingDetails?)entry.Flag).FirstOrDefault();

    public static decimal? Plain(decimal? amount) =>
        amount is { } value
            ? decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;
}
