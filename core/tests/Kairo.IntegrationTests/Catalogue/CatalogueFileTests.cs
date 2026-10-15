using System.IO.Compression;
using System.Text;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class CatalogueFileTests
{
    private const string Unreadable = "Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format.";

    [Fact]
    public void Reads_a_spreadsheet_csv_with_a_byte_order_mark_and_quoted_fields()
    {
        var csv = "\uFEFFid,title,description,price,availability\r\n"
            + "SKU-1,Runner,\"Light, quick and \"\"comfy\"\"\nfor every day\",1499.00 INR,in stock\r\n"
            + "SKU-2,Walker,Plain,999 INR,out of stock\r\n";

        var catalogue = Parse(csv);

        Assert.Equal(2, catalogue.Rows);
        Assert.Equal(0, catalogue.Invalid);
        var first = catalogue.Offers[0];
        Assert.Equal("SKU-1", first.OfferId);
        Assert.Equal("Light, quick and \"comfy\"\nfor every day", first.Description);
        Assert.Equal(1499.00m, first.Price);
        Assert.Equal("INR", first.Currency);
        Assert.Equal("in_stock", first.Availability);
        Assert.Equal("out_of_stock", catalogue.Offers[1].Availability);
    }

    [Fact]
    public void Reads_a_tab_separated_file()
    {
        var offer = Assert.Single(Parse("id\ttitle\tprice\tlink\nSKU-1\tRunner, red\t1499 INR\thttps://acme.example/p/1\n").Offers);

        Assert.Equal("Runner, red", offer.Title);
        Assert.Equal(1499m, offer.Price);
        Assert.Equal("https://acme.example/p/1", offer.Link);
    }

    [Fact]
    public void Matches_attribute_names_ignoring_case_a_g_prefix_and_spaces()
    {
        var offer = Assert.Single(Parse("ID,G:Title,Image Link,g:item group id\nSKU-1,Runner,https://acme.example/i/1.jpg,GROUP-1\n").Offers);

        Assert.Equal("Runner", offer.Title);
        Assert.Equal("https://acme.example/i/1.jpg", offer.ImageLink);
        Assert.Equal("GROUP-1", offer.ItemGroupId);
    }

    [Fact]
    public void Ignores_unknown_columns_and_blank_lines()
    {
        var catalogue = Parse("\nid,title,colour\n\nSKU-1,Runner,red\n\n");

        Assert.Equal(1, catalogue.Rows);
        Assert.Equal("Runner", Assert.Single(catalogue.Offers).Title);
    }

    [Fact]
    public void Reads_an_rss_feed()
    {
        const string rss = """

            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0">
              <channel>
                <title>Acme</title>
                <item>
                  <g:id>SKU-1</g:id>
                  <title>Runner</title>
                  <link>https://acme.example/p/1</link>
                  <description>Light shoes</description>
                  <g:price>1499.00 INR</g:price>
                  <g:availability>in_stock</g:availability>
                  <g:additional_image_link>https://acme.example/i/1a.jpg</g:additional_image_link>
                  <g:additional_image_link>https://acme.example/i/1b.jpg</g:additional_image_link>
                </item>
                <item>
                  <g:id>SKU-2</g:id>
                  <title>Plain title</title>
                  <g:title>Walker</g:title>
                </item>
              </channel>
            </rss>
            """;

        var catalogue = Parse(rss);

        Assert.Equal(2, catalogue.Rows);
        var first = catalogue.Offers[0];
        Assert.Equal("Runner", first.Title);
        Assert.Equal("https://acme.example/p/1", first.Link);
        Assert.Equal("Light shoes", first.Description);
        Assert.Equal(1499.00m, first.Price);
        Assert.Equal(["https://acme.example/i/1a.jpg", "https://acme.example/i/1b.jpg"], first.AdditionalImageLinks);
        Assert.Equal("Walker", catalogue.Offers[1].Title);
    }

    [Fact]
    public void Reads_an_atom_feed()
    {
        const string atom = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:g="http://base.google.com/ns/1.0">
              <title>Acme</title>
              <entry>
                <g:id>SKU-1</g:id>
                <title>Runner</title>
                <link rel="alternate" href="https://acme.example/p/1"/>
                <summary>Light shoes</summary>
                <g:price>INR 1499</g:price>
              </entry>
            </feed>
            """;

        var offer = Assert.Single(Parse(atom).Offers);

        Assert.Equal("SKU-1", offer.OfferId);
        Assert.Equal("Runner", offer.Title);
        Assert.Equal("https://acme.example/p/1", offer.Link);
        Assert.Equal("Light shoes", offer.Description);
        Assert.Equal(1499m, offer.Price);
    }

    [Fact]
    public void Unpacks_a_gzipped_file()
    {
        var offer = Assert.Single(CatalogueFile.Parse(Gzip(Encoding.UTF8.GetBytes("id,title\nSKU-1,Runner\n")), "INR").Offers);

        Assert.Equal("Runner", offer.Title);
    }

    [Fact]
    public void Splits_additional_image_links_in_text_and_keeps_ten()
    {
        var links = string.Join(",", Enumerable.Range(1, 12).Select(i => $"https://acme.example/i/{i}.jpg"));

        var catalogue = Parse($"id,additional_image_link\nSKU-1,\"{links}\"\n");

        var offer = Assert.Single(catalogue.Offers);
        Assert.Equal(10, offer.AdditionalImageLinks.Count);
        Assert.Equal("https://acme.example/i/1.jpg", offer.AdditionalImageLinks[0]);
        Assert.Equal(
            new ImportProblem(2, "additional_image_link", "Only the first 10 additional image links were kept."),
            Assert.Single(catalogue.Problems));
    }

    [Fact]
    public void Takes_the_first_product_type_else_the_google_category()
    {
        var catalogue = Parse(
            "id,product_type,google_product_category\n"
            + "SKU-1,\"Shoes > Running,Sale\",Apparel & Accessories > Shoes\n"
            + "SKU-2,,Apparel & Accessories > Shoes\n");

        Assert.Equal("Shoes > Running", catalogue.Offers[0].Category);
        Assert.Equal("Apparel & Accessories > Shoes", catalogue.Offers[0].GoogleCategory);
        Assert.Equal("Apparel & Accessories > Shoes", catalogue.Offers[1].Category);
    }

    [Fact]
    public void A_repeated_id_whatever_its_case_makes_the_later_row_invalid()
    {
        var catalogue = Parse("id,title\nSKU-1,First\nsku-1,Second\n");

        Assert.Equal("First", Assert.Single(catalogue.Offers).Title);
        Assert.Equal(1, catalogue.Invalid);
        Assert.Equal(new ImportProblem(3, "id", "The id sku-1 appears more than once."), Assert.Single(catalogue.Problems));
    }

    [Fact]
    public void A_missing_or_long_id_makes_the_row_invalid()
    {
        var catalogue = Parse($"id,title\n,No id\n{new string('x', 51)},Long id\nSKU-1,Fine\n");

        Assert.Equal(3, catalogue.Rows);
        Assert.Equal(2, catalogue.Invalid);
        Assert.Equal("Fine", Assert.Single(catalogue.Offers).Title);
        Assert.Equal(
            new[] { new ImportProblem(2, "id", "The id is missing."), new ImportProblem(3, "id", "The id is longer than 50 characters.") },
            catalogue.Problems.ToArray());
    }

    [Fact]
    public void A_value_that_breaks_its_rule_is_left_empty_and_the_product_kept()
    {
        var catalogue = Parse($"id,title,link,price,availability,gtin\nSKU-1,{new string('t', 151)},ftp://acme.example/p/1,cheap,maybe,12\n");

        var offer = Assert.Single(catalogue.Offers);
        Assert.Null(offer.Title);
        Assert.Null(offer.Link);
        Assert.Null(offer.Price);
        Assert.Null(offer.Currency);
        Assert.Null(offer.Availability);
        Assert.Null(offer.Gtin);
        Assert.Equal(0, catalogue.Invalid);
        Assert.Equal(new[] { "title", "link", "price", "availability", "gtin" }, catalogue.Problems.Select(p => p.Attribute).ToArray());
        Assert.All(catalogue.Problems, problem => Assert.Equal(2, problem.Row));
    }

    [Fact]
    public void A_sale_price_in_another_currency_or_without_a_price_is_ignored()
    {
        var catalogue = Parse("id,price,sale_price\nSKU-1,1499 INR,999 INR\nSKU-2,1499 INR,12 USD\nSKU-3,,999 INR\n");

        Assert.Equal(new decimal?[] { 999m, null, null }, catalogue.Offers.Select(o => o.SalePrice).ToArray());
        Assert.Equal(
            new[]
            {
                "The sale price was ignored because it isn't in the same currency as the price.",
                "The sale price was ignored because the product has no price.",
            },
            catalogue.Problems.Select(p => p.Message).ToArray());
    }

    [Fact]
    public void Uses_the_brands_currency_when_a_price_has_none()
    {
        var offer = Assert.Single(CatalogueFile.Parse(Encoding.UTF8.GetBytes("id,price\nSKU-1,20\n"), "USD").Offers);

        Assert.Equal("USD", offer.Currency);
    }

    [Fact]
    public void Keeps_only_the_first_50_problems()
    {
        var catalogue = Parse("id,price\n" + string.Concat(Enumerable.Range(1, 60).Select(i => $"SKU-{i},cheap\n")));

        Assert.Equal(60, catalogue.Offers.Count);
        Assert.Equal(50, catalogue.Problems.Count);
        Assert.Equal(51, catalogue.Problems[^1].Row);
    }

    [Fact]
    public void Refuses_a_file_without_an_id_column() =>
        AssertRefused("title,price\nRunner,1499\n", "no_id_column", "The file has no id column. Every product needs an id.");

    [Fact]
    public void Refuses_an_empty_file() => AssertRefused(" \r\n ", "unreadable", Unreadable);

    [Fact]
    public void Refuses_broken_xml() => AssertRefused("<rss><channel><item><g:id>1</g:id>", "unreadable", Unreadable);

    [Fact]
    public void Refuses_xml_that_isnt_a_feed() => AssertRefused("<html><body>Shop</body></html>", "unreadable", Unreadable);

    [Fact]
    public void Refuses_xml_that_declares_a_document_type()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY secret SYSTEM "file:///c:/windows/win.ini">]>
            <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0"><channel><item><g:id>SKU-1</g:id><title>&secret;</title></item></channel></rss>
            """;

        AssertRefused(xxe, "unreadable", Unreadable);
    }

    [Fact]
    public void Refuses_a_broken_gzip()
    {
        var error = Assert.Throws<CatalogueFileException>(() => CatalogueFile.Parse([0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02, 0x03], "INR"));

        Assert.Equal("unreadable", error.Code);
    }

    [Fact]
    public void Refuses_a_file_larger_than_50_MB()
    {
        var error = Assert.Throws<CatalogueFileException>(() => CatalogueFile.Parse(new byte[CatalogueFile.MaxBytes + 1], "INR"));

        Assert.Equal("too_large", error.Code);
        Assert.Equal("The file is larger than 50 MB.", error.Message);
    }

    [Fact]
    public void Refuses_a_file_larger_than_50_MB_once_unpacked()
    {
        var bomb = Gzip(new byte[CatalogueFile.MaxBytes + 1]);

        var error = Assert.Throws<CatalogueFileException>(() => CatalogueFile.Parse(bomb, "INR"));

        Assert.True(bomb.Length < 1024 * 1024);
        Assert.Equal("too_large", error.Code);
    }

    [Fact]
    public void Refuses_more_than_50000_products() =>
        AssertRefused(
            "id\n" + string.Concat(Enumerable.Range(1, CatalogueFile.MaxProducts + 1).Select(i => $"S{i}\n")),
            "too_many_products",
            "The file has more than 50,000 products.");

    [Fact]
    public void Reads_50000_products()
    {
        var catalogue = Parse("id\n" + string.Concat(Enumerable.Range(1, CatalogueFile.MaxProducts).Select(i => $"S{i}\n")));

        Assert.Equal(CatalogueFile.MaxProducts, catalogue.Offers.Count);
    }

    private static ParsedCatalogue Parse(string text) => CatalogueFile.Parse(Encoding.UTF8.GetBytes(text), "INR");

    private static void AssertRefused(string text, string code, string message)
    {
        var error = Assert.Throws<CatalogueFileException>(() => Parse(text));

        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.Message);
    }

    private static byte[] Gzip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }
}
