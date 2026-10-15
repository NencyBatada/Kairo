using System.Globalization;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Catalogue.Format;

internal sealed record Money(decimal Amount, string Currency);

internal static partial class ProductValues
{
    public const int MaxTitleLength = 150;
    public const int MaxDescriptionLength = 5000;
    public const int MaxBrandLength = 70;
    public const int MaxItemGroupIdLength = 50;
    public const int MaxCategoryLength = 750;
    public const int MaxUrlLength = 2000;
    public const int MaxAdditionalImages = 10;
    public const decimal MaxAmount = 999_999_999_999_999m;
    public const int MaxQuantity = 100_000_000;

    private static readonly string[] Availabilities = ["in_stock", "out_of_stock", "preorder", "backorder"];

    public static string? Text(string? value, int maxLength, string attribute, Action<string, string> warn)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        warn(attribute, $"The {Label(attribute)} is longer than {maxLength.ToString("N0", CultureInfo.InvariantCulture)} characters, so it was left empty.");
        return null;
    }

    public static bool IsWebAddress(string value) =>
        value.Length <= MaxUrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static string? Url(string? value, string attribute, Action<string, string> warn)
    {
        if (value is null || IsWebAddress(value))
        {
            return value;
        }

        warn(attribute, $"The {Label(attribute)} isn't a full web address starting with http:// or https://, so it was left empty.");
        return null;
    }

    public static List<string> AdditionalImages(IReadOnlyList<string> values, Action<string, string> warn)
    {
        var links = new List<string>();
        foreach (var value in values)
        {
            if (Url(value, Attributes.AdditionalImageLink, warn) is not { } link)
            {
                continue;
            }

            if (links.Count == MaxAdditionalImages)
            {
                warn(Attributes.AdditionalImageLink, "Only the first 10 additional image links were kept.");
                break;
            }

            links.Add(link);
        }

        return links;
    }

    public static Money? Amount(string? value, string currency, string attribute, Action<string, string> warn)
    {
        if (value is null)
        {
            return null;
        }

        var match = MoneyPattern().Match(value);
        if (match.Success
            && !(match.Groups["before"].Success && match.Groups["after"].Success)
            && decimal.TryParse(match.Groups["amount"].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && amount > 0
            && amount <= MaxAmount)
        {
            var code = match.Groups["before"].Success ? match.Groups["before"].Value
                : match.Groups["after"].Success ? match.Groups["after"].Value
                : currency;
            return new Money(Math.Round(amount, 4, MidpointRounding.AwayFromZero), code.ToUpperInvariant());
        }

        warn(attribute, $"The {Label(attribute)} isn't an amount above 0 with an optional currency, such as 1499.00 INR, so it was left empty.");
        return null;
    }

    public static string? Availability(string? value, Action<string, string> warn)
    {
        if (value is null)
        {
            return null;
        }

        var normal = value.Trim().ToLowerInvariant().Replace(' ', '_');
        if (Availabilities.Contains(normal))
        {
            return normal;
        }

        warn(Attributes.Availability, "The availability isn't in_stock, out_of_stock, preorder or backorder, so it was left empty.");
        return null;
    }

    public static string? Gtin(string? value, Action<string, string> warn)
    {
        if (value is null || (value.Length is >= 8 and <= 14 && value.All(char.IsAsciiDigit)))
        {
            return value;
        }

        warn(Attributes.Gtin, "The GTIN isn't 8 to 14 digits, so it was left empty.");
        return null;
    }

    public static int? Quantity(string? value, Action<string, string> warn)
    {
        if (value is null)
        {
            return null;
        }

        if (decimal.TryParse(value.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            && number == decimal.Truncate(number)
            && number <= MaxQuantity)
        {
            return (int)number;
        }

        warn(Attributes.Quantity, "The quantity isn't a whole number of units from 0 up, so it was left empty.");
        return null;
    }

    private static string Label(string attribute) => attribute switch
    {
        Attributes.ImageLink => "image link",
        Attributes.AdditionalImageLink => "additional image link",
        Attributes.SalePrice => "sale price",
        Attributes.ProductType => "product type",
        Attributes.GoogleProductCategory => "Google product category",
        Attributes.ItemGroupId => "item group id",
        _ => attribute,
    };

    [GeneratedRegex(@"^(?:(?<before>[A-Za-z]{3})\s*)?(?<amount>\d{1,3}(?:,\d{2,3})*,\d{3}(?:\.\d+)?|\d+(?:\.\d+)?)(?:\s*(?<after>[A-Za-z]{3}))?$")]
    private static partial Regex MoneyPattern();
}
