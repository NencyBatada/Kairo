namespace Kairo.Modules.Catalogue.Format;

internal static class CatalogueFileErrors
{
    public const string TooLarge = "too_large";
    public const string Unreadable = "unreadable";
    public const string NoIdColumn = "no_id_column";
    public const string TooManyProducts = "too_many_products";
}

internal sealed class CatalogueFileException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static CatalogueFileException TooLarge() =>
        new(CatalogueFileErrors.TooLarge, "The file is larger than 50 MB.");

    public static CatalogueFileException Unreadable() =>
        new(CatalogueFileErrors.Unreadable, "Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format.");

    public static CatalogueFileException NoIdColumn() =>
        new(CatalogueFileErrors.NoIdColumn, "The file has no id column. Every product needs an id.");

    public static CatalogueFileException TooManyProducts() =>
        new(CatalogueFileErrors.TooManyProducts, "The file has more than 50,000 products.");
}
