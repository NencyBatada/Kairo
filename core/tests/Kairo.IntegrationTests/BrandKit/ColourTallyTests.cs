using System.Text;
using Kairo.Modules.BrandKit.Drafting;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class ColourTallyTests
{
    [Theory]
    [InlineData("#e45", "#EE4455")]
    [InlineData("#E4572E", "#E4572E")]
    [InlineData(" #1b998b ", "#1B998B")]
    [InlineData("rgb(228, 87, 46)", "#E4572E")]
    [InlineData("rgba(228,87,46,0.5)", "#E4572E")]
    [InlineData("rgb(228 87 46 / 50%)", "#E4572E")]
    [InlineData("rgb(300, 0, 0)", null)]
    [InlineData("#12345", null)]
    [InlineData("red", null)]
    public void Normalises_hex_and_rgb_values(string value, string? expected) =>
        Assert.Equal(expected, ColourTally.Normalise(value));

    [Theory]
    [InlineData("#808080", true)]
    [InlineData("#000000", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#FAFAFA", true)]
    [InlineData("#111111", true)]
    [InlineData("#E4572E", false)]
    [InlineData("#1B998B", false)]
    public void Greys_and_near_black_or_white_are_neutral(string hex, bool neutral) =>
        Assert.Equal(neutral, ColourTally.IsNeutral(hex));

    [Fact]
    public void Counts_each_colour_and_where_it_came_from()
    {
        var tally = new ColourTally();
        tally.AddDocument(PageText.Parse(Encoding.UTF8.GetBytes("""
            <html><head>
              <meta name="theme-color" content="#E4572E">
              <style>:root{--brand:#e4572e;--ink:#111}.btn{background:#1B998B}a:hover{color:#808080}</style>
            </head>
            <body><p style="color:#1b998b">Hi</p></body></html>
            """)));

        var top = tally.Top();

        Assert.Equal(new[] { "#E4572E", "#1B998B" }, top.Select(colour => colour.Hex));
        Assert.Equal(2, top[0].Count);
        Assert.Equal(new[] { "css-variable", "theme-color" }, top[0].From);
        Assert.Equal(new[] { "css", "inline-style" }, top[1].From);
    }

    [Fact]
    public void Keeps_the_twelve_most_used_colours()
    {
        var tally = new ColourTally();
        var css = string.Concat(Enumerable.Range(0, 20).Select(index => $".c{index}{{color:#{(index * 12) + 10:X2}5A2E}}"));
        tally.AddCss(css, "css");
        tally.AddCss(".x{color:#F05A2E}.y{color:#F05A2E}", "css");

        var top = tally.Top();

        Assert.Equal(ColourTally.Max, top.Count);
        Assert.Equal("#F05A2E", top[0].Hex);
    }
}
