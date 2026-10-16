using Kairo.Modules.CampaignRooms.Assets;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class DisclosureRulesTests
{
    [Theory]
    [InlineData(AssetKinds.KeywordSet)]
    [InlineData(AssetKinds.SearchAd)]
    [InlineData(AssetKinds.DemandGenText)]
    [InlineData(AssetKinds.ShoppingTitle)]
    public void Text_is_marked_as_ai_made_without_a_platform_label(string kind)
    {
        var disclosure = DisclosureRules.For(kind, new AssetContent { Headlines = ["Dry feet all monsoon"] });

        Assert.True(disclosure.AiGenerated);
        Assert.False(disclosure.SyntheticMedia);
        Assert.Null(disclosure.Watermark);
        Assert.Empty(disclosure.PlatformLabels);
        Assert.Null(disclosure.Blocker);
        Assert.Equal(DisclosureRules.Version, disclosure.Rules);
    }

    [Fact]
    public void Images_carry_the_google_ads_ai_label_and_a_synthid_watermark()
    {
        var disclosure = DisclosureRules.For(AssetKinds.Image, new AssetContent());

        Assert.True(disclosure.SyntheticMedia);
        Assert.Equal(DisclosureRules.SynthId, disclosure.Watermark);
        Assert.Equal([DisclosureRules.GoogleAdsLabel], disclosure.PlatformLabels);
    }

    [Fact]
    public void Videos_also_set_youtubes_synthetic_media_flag()
    {
        var disclosure = DisclosureRules.For(AssetKinds.Video, new AssetContent());

        Assert.Equal(DisclosureRules.SynthId, disclosure.Watermark);
        Assert.Equal(
            [(DisclosureRules.GoogleAds, DisclosureRules.AiCreated), (DisclosureRules.YouTube, DisclosureRules.ContainsSyntheticMedia)],
            disclosure.PlatformLabels.Select(label => (label.Platform, label.Setting)));
        Assert.All(disclosure.PlatformLabels, label => Assert.StartsWith("https://", label.Source));
    }

    [Theory]
    [InlineData(AssetKinds.Voiceover, null)]
    [InlineData(AssetKinds.Footage, DisclosureRules.SynthId)]
    public void Voiceover_and_footage_are_synthetic_but_travel_inside_the_video(string kind, string? watermark)
    {
        var disclosure = DisclosureRules.For(kind, new AssetContent());

        Assert.True(disclosure.SyntheticMedia);
        Assert.Equal(watermark, disclosure.Watermark);
        Assert.Empty(disclosure.PlatformLabels);
    }

    [Theory]
    [InlineData("Vote in the Lok Sabha election")]
    [InlineData("Read our party manifesto")]
    [InlineData("चुनाव में हमारा साथ दें")]
    [InlineData("ELECTIONS are coming")]
    public void Election_and_political_text_is_blocked(string text)
    {
        Assert.Equal(DisclosureRules.PoliticalBlocker, DisclosureRules.For(AssetKinds.SearchAd, new AssetContent { Headlines = [text] }).Blocker);
    }

    [Fact]
    public void Political_words_in_a_video_script_are_blocked_too()
    {
        var content = new AssetContent { Script = [new AssetScriptLine { Voiceover = "Before the vidhan sabha polls", Caption = "Shop now" }] };

        Assert.NotNull(DisclosureRules.For(AssetKinds.Video, content).Blocker);
    }

    [Theory]
    [InlineData("Vote for your favourite flavour")]
    [InlineData("Selected by our customers")]
    [InlineData("Party wear for the festive season")]
    public void Everyday_shopping_words_are_not_blocked(string text)
    {
        Assert.Null(DisclosureRules.For(AssetKinds.DemandGenText, new AssetContent { Descriptions = [text] }).Blocker);
    }

    [Fact]
    public void The_trail_digests_each_agent_request_the_same_way_every_time()
    {
        var trail = new ProvenanceTrail();
        trail.Called("create/copy", new { language = "en", brand = "Acme" });
        trail.Called("create/copy", new { language = "hi", brand = "Acme" });

        var provenance = trail.For(new Asset { Kind = AssetKinds.SearchAd, BriefVersion = 3, ProductIds = ["a"] }, 0, null);

        Assert.Equal(2, provenance.AgentCalls.Count);
        Assert.Equal(ProvenanceTrail.Digest(new { language = "en", brand = "Acme" }), provenance.AgentCalls[0].Digest);
        Assert.NotEqual(provenance.AgentCalls[0].Digest, provenance.AgentCalls[1].Digest);
        Assert.Equal((3, (int?)null), (provenance.BriefVersion, provenance.PreviousVersion));
        Assert.Equal(["a"], provenance.ProductIds);
    }

    [Fact]
    public void The_trail_gives_each_media_asset_only_its_own_products_photos_and_inputs()
    {
        var trail = new ProvenanceTrail();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        trail.Photos(first, ["https://shop.test/1.jpg", "https://shop.test/1b.jpg"]);
        trail.Photos(second, ["https://shop.test/2.jpg"]);
        trail.BrandKit(4);
        var frame = new Asset { Id = Guid.NewGuid(), Kind = AssetKinds.Image };
        var clip = new Asset { Id = Guid.NewGuid(), Kind = AssetKinds.Footage };
        trail.Used(frame, 1, "drafts/frame");
        trail.Used(clip, 2, "drafts/clip");
        trail.Used(clip, 2, "drafts/clip");

        var image = trail.For(new Asset { Kind = AssetKinds.Image, ProductIds = [first.ToString()] }, 1, null);
        var footage = trail.For(new Asset { Id = Guid.NewGuid(), Kind = AssetKinds.Footage, ProductIds = [second.ToString()] }, 0, null);
        var video = trail.For(new Asset { Kind = AssetKinds.Video, ProductIds = [first.ToString(), second.ToString()] }, 0, null);
        var text = trail.For(new Asset { Kind = AssetKinds.ShoppingTitle, ProductIds = [first.ToString()] }, 2, ["title"]);

        Assert.Equal(["https://shop.test/1.jpg", "https://shop.test/1b.jpg"], image.PhotoUrls);
        Assert.Empty(image.Inputs);
        Assert.Equal(1, image.PreviousVersion);
        Assert.Equal(4, image.BrandKitVersion);
        Assert.Equal([(frame.Id, 1)], footage.Inputs.Select(input => (input.AssetId, input.Version)));
        Assert.Equal([(clip.Id, 2)], video.Inputs.Select(input => (input.AssetId, input.Version)));
        Assert.Equal(3, video.PhotoUrls.Count);
        Assert.Empty(text.PhotoUrls);
        Assert.Equal(["title"], text.ChangedFields);
        Assert.Empty(text.MemoryUses);
    }

    [Fact]
    public void Provenance_survives_a_round_trip_and_old_rows_read_as_empty()
    {
        var provenance = new AssetProvenance { BriefVersion = 2, BrandKitVersion = 5, PhotoUrls = ["https://shop.test/1.jpg"] };

        var read = AssetProvenance.Parse(provenance.Write());

        Assert.Equal((2, 5, "https://shop.test/1.jpg"), (read.BriefVersion, read.BrandKitVersion, read.PhotoUrls.Single()));
        Assert.Empty(AssetProvenance.Parse(null).AgentCalls);
    }
}
