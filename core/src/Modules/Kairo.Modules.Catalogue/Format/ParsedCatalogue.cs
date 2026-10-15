namespace Kairo.Modules.Catalogue.Format;

internal sealed record ImportProblem(int Row, string Attribute, string Message);

internal sealed record ProductOffer(
    string OfferId,
    string? Title,
    string? Description,
    string? Link,
    string? ImageLink,
    IReadOnlyList<string> AdditionalImageLinks,
    decimal? Price,
    decimal? SalePrice,
    string? Currency,
    string? Availability,
    string? Category,
    string? GoogleCategory,
    string? Brand,
    string? Gtin,
    string? ItemGroupId);

internal sealed record ParsedCatalogue(
    IReadOnlyList<ProductOffer> Offers,
    int Rows,
    int Invalid,
    IReadOnlyList<ImportProblem> Problems);
