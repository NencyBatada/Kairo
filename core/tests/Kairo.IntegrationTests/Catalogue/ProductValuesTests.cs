using System.Globalization;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class ProductValuesTests
{
    private readonly List<(string Attribute, string Message)> _warnings = [];

    [Theory]
    [InlineData("1499.00 INR", "1499.00", "INR")]
    [InlineData("INR 1499", "1499", "INR")]
    [InlineData("1499", "1499", "INR")]
    [InlineData("20 usd", "20", "USD")]
    [InlineData("1499.00INR", "1499.00", "INR")]
    [InlineData("1,499.50 INR", "1499.50", "INR")]
    [InlineData("1,49,999 INR", "149999", "INR")]
    [InlineData("1,234,567.25 INR", "1234567.25", "INR")]
    [InlineData("12.12345 INR", "12.1235", "INR")]
    public void Reads_an_amount_with_an_optional_currency(string value, string amount, string currency)
    {
        var money = ProductValues.Amount(value, "INR", "price", Warn);

        Assert.NotNull(money);
        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), money.Amount);
        Assert.Equal(currency, money.Currency);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("cheap")]
    [InlineData("0")]
    [InlineData("-5 INR")]
    [InlineData("12,34 EUR")]
    [InlineData("INR 10 USD")]
    [InlineData("₹1499")]
    [InlineData("1000000000000000 INR")]
    [InlineData("99999999999999999999999999999999999")]
    public void Leaves_a_bad_amount_empty_with_a_warning(string value)
    {
        Assert.Null(ProductValues.Amount(value, "INR", "price", Warn));

        Assert.Equal(
            ("price", "The price isn't an amount above 0 with an optional currency, such as 1499.00 INR, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Theory]
    [InlineData("in_stock", "in_stock")]
    [InlineData("in stock", "in_stock")]
    [InlineData("IN_STOCK", "in_stock")]
    [InlineData("Out Of Stock", "out_of_stock")]
    [InlineData("preorder", "preorder")]
    [InlineData("Backorder", "backorder")]
    public void Reads_availability_accepting_spaces_and_any_case(string value, string expected)
    {
        Assert.Equal(expected, ProductValues.Availability(value, Warn));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Leaves_an_unknown_availability_empty_with_a_warning()
    {
        Assert.Null(ProductValues.Availability("maybe", Warn));

        Assert.Equal(
            ("availability", "The availability isn't in_stock, out_of_stock, preorder or backorder, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("00012345678905", true)]
    [InlineData("1234567", false)]
    [InlineData("123456789012345", false)]
    [InlineData("1234-5678", false)]
    public void Accepts_a_gtin_of_8_to_14_digits(string value, bool accepted)
    {
        Assert.Equal(accepted ? value : null, ProductValues.Gtin(value, Warn));
        Assert.Equal(accepted ? 0 : 1, _warnings.Count);
    }

    [Fact]
    public void Leaves_text_over_its_limit_empty_with_a_warning()
    {
        Assert.Null(ProductValues.Text(new string('d', 5001), ProductValues.MaxDescriptionLength, "description", Warn));
        Assert.Equal("x", ProductValues.Text("x", ProductValues.MaxTitleLength, "title", Warn));

        Assert.Equal(
            ("description", "The description is longer than 5,000 characters, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Theory]
    [InlineData("ftp://acme.example/i/1.jpg")]
    [InlineData("acme.example/i/1.jpg")]
    [InlineData("/i/1.jpg")]
    public void Leaves_an_address_that_isnt_a_web_address_empty_with_a_warning(string value)
    {
        Assert.Null(ProductValues.Url(value, "image_link", Warn));

        Assert.Equal(
            ("image_link", "The image link isn't a full web address starting with http:// or https://, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Fact]
    public void Refuses_a_web_address_over_2000_characters()
    {
        Assert.True(ProductValues.IsWebAddress("https://acme.example/p/1"));
        Assert.False(ProductValues.IsWebAddress("https://acme.example/" + new string('p', 2000)));
    }

    private void Warn(string attribute, string message) => _warnings.Add((attribute, message));
}
