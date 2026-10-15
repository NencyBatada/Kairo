using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Modules.Catalogue.Format;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal static class ProductFixRequest
{
    public const string CantFixStock = "Stock can't be fixed here. Change it in the file or feed.";
    public const string UnknownField = "Kairo can't fix this field.";
    public const string BadTitle = "Use a title of up to 150 characters.";
    public const string BadPrice = "Use a price above 0.";
    public const string BadCurrency = "Use a three-letter currency code, such as INR.";
    public const string CurrencyWithoutPrice = "Send the currency with a price.";
    public const string BadCategory = "Use a category of up to 750 characters.";

    public static string Supplied(string field) =>
        $"The import already supplies the {(field == ProductFields.ImageLink ? "image link" : field)}, so it can't be fixed here.";

    public static Dictionary<string, string[]> Apply(JsonObject body, Product product, string brandCurrency)
    {
        var errors = new Dictionary<string, string[]>();
        var changes = new List<Action<ProductFixes>>();

        void Text(string field, JsonNode? node, int maxLength, string invalid, bool supplied, Action<ProductFixes, string?> set)
        {
            if (!TryReadText(node, out var text) || text?.Length > maxLength)
            {
                errors[field] = [invalid];
            }
            else if (text is not null && supplied)
            {
                errors[field] = [Supplied(field)];
            }
            else
            {
                changes.Add(fixes => set(fixes, text));
            }
        }

        void Address(string field, JsonNode? node, bool supplied, Action<ProductFixes, string?> set)
        {
            if (!TryReadText(node, out var text) || (text is not null && !ProductValues.IsWebAddress(text)))
            {
                errors[field] = [FeedUrls.BadUrl];
            }
            else if (text is not null && supplied)
            {
                errors[field] = [Supplied(field)];
            }
            else
            {
                changes.Add(fixes => set(fixes, text));
            }
        }

        void Price(JsonNode? node, JsonNode? currencyNode)
        {
            if (node is null)
            {
                changes.Add(fixes =>
                {
                    fixes.Price = null;
                    fixes.Currency = null;
                });
                return;
            }

            if (node is not JsonValue value
                || value.GetValueKind() != JsonValueKind.Number
                || !value.TryGetValue<decimal>(out var amount)
                || amount <= 0
                || amount > ProductValues.MaxAmount)
            {
                errors[ProductFields.Price] = [BadPrice];
                return;
            }

            var currency = brandCurrency;
            if (currencyNode is not null)
            {
                if (!TryReadText(currencyNode, out var code) || code is { Length: not 3 } || (code is not null && !code.All(char.IsAsciiLetter)))
                {
                    errors[ProductFields.Currency] = [BadCurrency];
                    return;
                }

                currency = code?.ToUpperInvariant() ?? brandCurrency;
            }

            if (product.Price is not null)
            {
                errors[ProductFields.Price] = [Supplied(ProductFields.Price)];
                return;
            }

            var rounded = Math.Round(amount, 4, MidpointRounding.AwayFromZero);
            changes.Add(fixes =>
            {
                fixes.Price = rounded;
                fixes.Currency = currency;
            });
        }

        foreach (var (field, node) in body)
        {
            switch (field)
            {
                case ProductFields.Title:
                    Text(field, node, ProductValues.MaxTitleLength, BadTitle, product.Title is not null, (fixes, text) => fixes.Title = text);
                    break;
                case ProductFields.Link:
                    Address(field, node, product.Link is not null, (fixes, text) => fixes.Link = text);
                    break;
                case ProductFields.ImageLink:
                    Address(field, node, product.ImageLink is not null, (fixes, text) => fixes.ImageLink = text);
                    break;
                case ProductFields.Category:
                    Text(field, node, ProductValues.MaxCategoryLength, BadCategory, product.Category is not null, (fixes, text) => fixes.Category = text);
                    break;
                case ProductFields.Price:
                    Price(node, body[ProductFields.Currency]);
                    break;
                case ProductFields.Currency:
                    if (!body.ContainsKey(ProductFields.Price))
                    {
                        errors[field] = [CurrencyWithoutPrice];
                    }

                    break;
                case "stock" or "availability":
                    errors[field] = [CantFixStock];
                    break;
                default:
                    errors[field] = [UnknownField];
                    break;
            }
        }

        if (errors.Count == 0)
        {
            foreach (var change in changes)
            {
                change(product.Fixes);
            }
        }

        return errors;
    }

    private static bool TryReadText(JsonNode? node, out string? text)
    {
        text = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        var trimmed = value.GetValue<string>().Trim();
        text = trimmed.Length == 0 ? null : trimmed;
        return true;
    }
}
