using Kairo.Modules.CampaignRooms.Assets;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class AdLimitsTests
{
    [Theory]
    [InlineData("Dry feet", 8)]
    [InlineData("बारिश के जूते", 13)]
    [InlineData("雨靴", 4)]
    [InlineData("레인부츠", 8)]
    public void Length_counts_double_width_characters_twice(string text, int length) =>
        Assert.Equal(length, AdLimits.Length(text));

    [Fact]
    public void A_search_ad_needs_enough_headlines_and_descriptions_within_their_lengths()
    {
        var valid = new AssetContent
        {
            Headlines = ["Waterproof Running Shoes", "Stay Dry", "Dries Fast"],
            Descriptions = ["Keeps feet dry.", "Dries in minutes."],
            Path1 = "rain-shoes",
        };

        Assert.Empty(AdLimits.Check(AssetKinds.SearchAd, valid));
        Assert.Contains("headlines", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Headlines = ["One", "Two"])).Keys);
        Assert.Contains("headlines", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Headlines = ["A headline over thirty characters", "Two", "Three"])).Keys);
        Assert.Contains("headlines", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Headlines = ["Same", "same", "Other"])).Keys);
        Assert.Contains("headlines", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Headlines = ["One", "", "Three"])).Keys);
        Assert.Contains("descriptions", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Descriptions = [new string('a', 91), "Two"])).Keys);
        Assert.Contains("path1", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Path1 = "rain shoes")).Keys);
        Assert.Contains("path2", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => (content.Path1, content.Path2) = (null, "runners"))).Keys);
        Assert.Contains("headlines", AdLimits.Check(AssetKinds.SearchAd, Changed(valid, content => content.Headlines = [.. Enumerable.Range(1, 16).Select(index => $"Headline {index}")])).Keys);
    }

    [Fact]
    public void Demand_gen_text_needs_a_short_headline_a_business_name_and_a_known_call_to_action()
    {
        var valid = new AssetContent
        {
            Headlines = ["Monsoon-ready shoes that dry very fast", "Dry feet"],
            LongHeadlines = ["Waterproof runners for every rainy commute"],
            Descriptions = ["Mesh that keeps water out."],
            BusinessName = "Acme Shoes",
            CallToAction = "shop_now",
        };

        Assert.Empty(AdLimits.Check(AssetKinds.DemandGenText, valid));
        Assert.Contains("headlines", AdLimits.Check(AssetKinds.DemandGenText, Changed(valid, content => content.Headlines = ["Monsoon-ready shoes that dry very fast"])).Keys);
        Assert.Contains("longHeadlines", AdLimits.Check(AssetKinds.DemandGenText, Changed(valid, content => content.LongHeadlines = [])).Keys);
        Assert.Contains("businessName", AdLimits.Check(AssetKinds.DemandGenText, Changed(valid, content => content.BusinessName = "Acme Waterproof Footwear Company")).Keys);
        Assert.Contains("callToAction", AdLimits.Check(AssetKinds.DemandGenText, Changed(valid, content => content.CallToAction = "click_here")).Keys);
    }

    [Fact]
    public void Keywords_and_shopping_titles_have_their_own_limits()
    {
        var keywords = new AssetContent { Keywords = [new AssetKeyword { Text = "rain boots", Match = "exact" }] };

        Assert.Empty(AdLimits.Check(AssetKinds.KeywordSet, keywords));
        Assert.Contains("keywords", AdLimits.Check(AssetKinds.KeywordSet, new AssetContent { Keywords = [] }).Keys);
        Assert.Contains("keywords", AdLimits.Check(AssetKinds.KeywordSet, new AssetContent { Keywords = [new AssetKeyword { Text = "one two three four five six seven eight nine ten eleven" }] }).Keys);
        Assert.Contains("keywords", AdLimits.Check(AssetKinds.KeywordSet, new AssetContent { Keywords = [new AssetKeyword { Text = new string('a', 81) }] }).Keys);
        Assert.Contains("keywords", AdLimits.Check(AssetKinds.KeywordSet, new AssetContent { Keywords = [new AssetKeyword { Text = "rain", Match = "fuzzy" }] }).Keys);
        Assert.Contains("keywords", AdLimits.Check(AssetKinds.KeywordSet, new AssetContent { Keywords = [new AssetKeyword { Text = "rain" }, new AssetKeyword { Text = "Rain" }] }).Keys);
        Assert.Empty(AdLimits.Check(AssetKinds.ShoppingTitle, new AssetContent { Title = new string('a', 150) }));
        Assert.Contains("title", AdLimits.Check(AssetKinds.ShoppingTitle, new AssetContent { Title = new string('a', 151) }).Keys);
        Assert.Contains("title", AdLimits.Check(AssetKinds.ShoppingTitle, new AssetContent()).Keys);
        Assert.Empty(AdLimits.Check(AssetKinds.Image, new AssetContent()));
    }

    [Fact]
    public void Tidy_collapses_spaces() =>
        Assert.Equal("Stay dry today", AdLimits.Tidy("  Stay   dry\ttoday "));

    private static AssetContent Changed(AssetContent content, Action<AssetContent> change)
    {
        var copy = content.Copy();
        change(copy);
        return copy;
    }
}
