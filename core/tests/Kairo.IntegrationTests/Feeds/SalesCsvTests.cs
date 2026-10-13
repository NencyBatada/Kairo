using System.Text;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class SalesCsvTests
{
    private const string Header = "date,region,orders,revenue\n";

    private static readonly DateOnly Today = new(2026, 10, 13);

    [Fact]
    public void Reads_the_columns_in_any_order_and_letter_case()
    {
        var file = Parse("Revenue,DATE,orders, Region \n1500.50,2026-10-01,12,Maharashtra\n");

        Assert.True(file.IsValid);
        Assert.Equal(new[] { new SalesRow(new DateOnly(2026, 10, 1), "Maharashtra", 12, 1500.50m) }, file.Rows);
    }

    [Fact]
    public void Reads_a_byte_order_mark_windows_line_endings_quoted_commas_and_a_blank_last_line()
    {
        var text = "date,region,orders,revenue\r\n2026-10-01,\"Delhi, NCR\",3,99\r\n2026-10-02,\"Tamil \"\"Nadu\"\"\",1,10.5\r\n\r\n";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var file = SalesCsv.Parse(new MemoryStream(bytes), Today);

        Assert.True(file.IsValid, string.Join("; ", file.Errors));
        Assert.Equal(
            new[]
            {
                new SalesRow(new DateOnly(2026, 10, 1), "Delhi, NCR", 3, 99m),
                new SalesRow(new DateOnly(2026, 10, 2), "Tamil \"Nadu\"", 1, 10.5m),
            },
            file.Rows);
    }

    [Theory]
    [InlineData("2026-13-01,Goa,1,1", "row 2: date must be YYYY-MM-DD")]
    [InlineData("01/10/2026,Goa,1,1", "row 2: date must be YYYY-MM-DD")]
    [InlineData("2026-10-14,Goa,1,1", "row 2: date can't be after today")]
    [InlineData("2026-10-01,  ,1,1", "row 2: region is empty")]
    [InlineData("2026-10-01,Goa,1.5,1", "row 2: orders must be a whole number, 0 or more")]
    [InlineData("2026-10-01,Goa,-1,1", "row 2: orders must be a whole number, 0 or more")]
    [InlineData("2026-10-01,Goa,١,1", "row 2: orders must be a whole number, 0 or more")]
    [InlineData("2026-10-01,Goa,1,1.234", "row 2: revenue must be a number, 0 or more, with up to 2 decimal places")]
    [InlineData("2026-10-01,Goa,1,1e3", "row 2: revenue must be a number, 0 or more, with up to 2 decimal places")]
    [InlineData("2026-10-01,Goa,1,\"1,000\"", "row 2: revenue must be a number, 0 or more, with up to 2 decimal places")]
    [InlineData("2026-10-01,Goa,1,1,5", "row 2: expected 4 values but found 5")]
    public void Reports_each_broken_rule(string line, string expected)
    {
        var file = Parse(Header + line + "\n");

        Assert.False(file.IsValid);
        Assert.Empty(file.Rows);
        Assert.Equal(new[] { expected }, file.Errors);
    }

    [Fact]
    public void Reports_a_region_over_100_characters()
    {
        var file = Parse(Header + $"2026-10-01,{new string('a', 101)},1,1\n");

        Assert.Equal(new[] { "row 2: region is longer than 100 characters" }, file.Errors);
    }

    [Fact]
    public void Reports_a_repeated_date_and_region_whatever_the_letter_case()
    {
        var file = Parse(Header + "2026-10-01,Goa,1,1\n2026-10-01,GOA,2,2\n");

        Assert.Equal(new[] { "row 3: date and region already appear in row 2" }, file.Errors);
    }

    [Fact]
    public void Stops_at_20_problems()
    {
        var file = Parse(Header + string.Concat(Enumerable.Repeat("bad,Goa,1,1\n", 30)));

        Assert.Equal(SalesCsv.MaxErrors, file.Errors.Count);
        Assert.Equal("row 21: date must be YYYY-MM-DD", file.Errors[^1]);
    }

    [Theory]
    [InlineData("date,region,orders\n2026-10-01,Goa,1\n")]
    [InlineData("date,region,orders,revenue,notes\n2026-10-01,Goa,1,1,x\n")]
    [InlineData("day,region,orders,revenue\n2026-10-01,Goa,1,1\n")]
    public void Refuses_a_header_without_exactly_the_four_columns(string text)
    {
        Assert.Equal(new[] { "row 1: the header must name date, region, orders and revenue" }, Parse(text).Errors);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_UTF8()
    {
        var file = SalesCsv.Parse(new MemoryStream([0x64, 0xFF, 0xFE, 0x00]), Today);

        Assert.Equal(new[] { "the file isn't UTF-8 text" }, file.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Header)]
    [InlineData(Header + "\n\n")]
    public void Refuses_a_file_without_rows(string text)
    {
        var errors = Parse(text).Errors;

        Assert.Single(errors);
        Assert.Contains(errors[0], new[] { "the file is empty", "the file has no rows" });
    }

    [Fact]
    public void Refuses_a_quote_that_is_never_closed()
    {
        Assert.Equal(new[] { "row 2: a quoted value is never closed" }, Parse(Header + "2026-10-01,\"Goa,1,1\n").Errors);
    }

    private static SalesFile Parse(string text) => SalesCsv.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)), Today);
}
