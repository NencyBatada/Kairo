namespace Kairo.Modules.Catalogue.Format;

internal static class ProductRows
{
    public const int MaxProblems = 50;
    public const int MaxIdLength = 50;

    public static ParsedCatalogue Check(IReadOnlyList<RawRow> rows, string currency)
    {
        var offers = new List<ProductOffer>(rows.Count);
        var problems = new List<ImportProblem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = 0;
        foreach (var row in rows)
        {
            void Note(string attribute, string message)
            {
                if (problems.Count < MaxProblems)
                {
                    problems.Add(new ImportProblem(row.Row, attribute, message));
                }
            }

            var id = row.First(Attributes.Id);
            var error = id is null ? "The id is missing."
                : id.Length > MaxIdLength ? $"The id is longer than {MaxIdLength} characters."
                : seen.Add(id) ? null
                : $"The id {id} appears more than once.";
            if (error is not null)
            {
                invalid++;
                Note(Attributes.Id, error);
                continue;
            }

            offers.Add(OfferOf(row, id!, currency, Note));
        }

        return new ParsedCatalogue(offers, rows.Count, invalid, problems);
    }

    private static ProductOffer OfferOf(RawRow row, string id, string currency, Action<string, string> warn)
    {
        var title = ProductValues.Text(row.First(Attributes.Title), ProductValues.MaxTitleLength, Attributes.Title, warn);
        var description = ProductValues.Text(row.First(Attributes.Description), ProductValues.MaxDescriptionLength, Attributes.Description, warn);
        var link = ProductValues.Url(row.First(Attributes.Link), Attributes.Link, warn);
        var imageLink = ProductValues.Url(row.First(Attributes.ImageLink), Attributes.ImageLink, warn);
        var additionalImageLinks = ProductValues.AdditionalImages(row.All(Attributes.AdditionalImageLink), warn);
        var price = ProductValues.Amount(row.First(Attributes.Price), currency, Attributes.Price, warn);
        var salePrice = SalePrice(ProductValues.Amount(row.First(Attributes.SalePrice), currency, Attributes.SalePrice, warn), price, warn);
        var availability = ProductValues.Availability(row.First(Attributes.Availability), warn);
        var productType = ProductValues.Text(row.First(Attributes.ProductType), ProductValues.MaxCategoryLength, Attributes.ProductType, warn);
        var googleCategory = ProductValues.Text(row.First(Attributes.GoogleProductCategory), ProductValues.MaxCategoryLength, Attributes.GoogleProductCategory, warn);
        var brand = ProductValues.Text(row.First(Attributes.Brand), ProductValues.MaxBrandLength, Attributes.Brand, warn);
        var gtin = ProductValues.Gtin(row.First(Attributes.Gtin), warn);
        var itemGroupId = ProductValues.Text(row.First(Attributes.ItemGroupId), ProductValues.MaxItemGroupIdLength, Attributes.ItemGroupId, warn);
        var quantity = ProductValues.Quantity(Attributes.QuantityNames.Select(row.First).FirstOrDefault(value => value is not null), warn);
        return new ProductOffer(
            id,
            title,
            description,
            link,
            imageLink,
            additionalImageLinks,
            price?.Amount,
            salePrice?.Amount,
            price?.Currency,
            availability,
            productType ?? googleCategory,
            googleCategory,
            brand,
            gtin,
            itemGroupId,
            quantity);
    }

    private static Money? SalePrice(Money? sale, Money? price, Action<string, string> warn)
    {
        if (sale is null)
        {
            return null;
        }

        if (price is null)
        {
            warn(Attributes.SalePrice, "The sale price was ignored because the product has no price.");
            return null;
        }

        if (sale.Currency != price.Currency)
        {
            warn(Attributes.SalePrice, "The sale price was ignored because it isn't in the same currency as the price.");
            return null;
        }

        return sale;
    }
}
