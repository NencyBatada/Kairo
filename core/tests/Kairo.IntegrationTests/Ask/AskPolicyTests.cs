using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Modules.Ask;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Assets;

namespace Kairo.IntegrationTests.Ask;

public sealed class AskPolicyTests
{
    [Theory]
    [InlineData("campaign")]
    [InlineData("brief")]
    [InlineData("report")]
    public void Campaign_context_uses_the_validated_resource_id(string kind)
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, AskPolicy.Campaign(new(kind, id, Guid.NewGuid())));
    }

    [Fact]
    public void Only_asset_context_uses_its_explicit_campaign_id()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, AskPolicy.Campaign(new("asset", Guid.NewGuid(), id)));
        Assert.Null(AskPolicy.Campaign(new("opportunity", Guid.NewGuid(), id)));
        Assert.Null(AskPolicy.Campaign(new("workspace", CampaignId: id)));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("launch")]
    [InlineData("budget")]
    [InlineData("settings")]
    [InlineData("../campaign")]
    public void Forbidden_targets_cannot_be_tools(string kind) => Assert.Throws<AskResourceException>(() => AskPolicy.Target(new AskTarget(kind, Guid.NewGuid())));

    [Fact]
    public void Citations_must_come_from_retrieved_evidence()
    {
        Evidence[] evidence = [new("e1", new AskTarget("workspace"), "Acme", "/acme/today", AskJson.Element(new { lift = 0.12 }))];
        Assert.Equal(["e1"], AskPolicy.Segment("Lift was 12%.", ["e1"], evidence).EvidenceIds);
        Assert.Throws<AskResourceException>(() => AskPolicy.Segment("Lift was 12%.", ["e2"], evidence));
        Assert.Throws<AskResourceException>(() => AskPolicy.Segment("Lift was 12%.", [], evidence));
        Assert.Empty(AskPolicy.Segment("There is insufficient evidence for that conclusion.", [], evidence).EvidenceIds);
    }

    [Theory]
    [InlineData("See https://attacker.example")]
    [InlineData("[Evidence](javascript:alert(1))")]
    [InlineData("<img src='data:x'>")]
    [InlineData("<script>alert(1)</script>")]
    public void Answer_links_cannot_be_invented_by_the_model(string text) =>
        Assert.Throws<AskResourceException>(() => AskPolicy.Segment(text, [], []));

    [Theory]
    [InlineData(100, 101)]
    [InlineData(null, 1)]
    public void Chat_cannot_raise_draft_spend(int? before, int after) =>
        Assert.Throws<AskResourceException>(() => BriefAskResource.Budget(before, after));

    [Fact]
    public void Equal_or_reduced_draft_budget_is_allowed()
    {
        BriefAskResource.Budget(100, 100);
        BriefAskResource.Budget(100, 90);
        BriefAskResource.Budget(null, null);
    }

    [Theory]
    [InlineData("{\"title\":\"Safe\",\"status\":\"approved\"}")]
    [InlineData("{\"title\":\"One\",\"title\":\"Two\"}")]
    public void Changes_cannot_smuggle_unknown_or_duplicate_fields(string json) =>
        Assert.Throws<AskResourceException>(() => AskJson.Only(JsonDocument.Parse(json).RootElement, "title"));

    [Fact]
    public void Stale_and_missing_items_cannot_be_confirmed()
    {
        var document = new AskDocument("Item", "today", AskJson.Element(new { }), "current");
        Assert.Throws<AskResourceException>(() => AskJson.Unchanged("old", document));
        Assert.Throws<AskResourceException>(() => AskJson.Unchanged("current", null));
        AskJson.Unchanged("current", document);
    }

    [Fact]
    public void Linked_sources_come_from_stored_data_and_use_only_web_urls()
    {
        var data = AskJson.Element(new { sources = new[] { "https://example.test/research", "javascript:alert(1)", "https://user:secret@example.test", "https://example.test/research" } });
        Assert.Equal(["https://example.test/research"], AskPolicy.Sources(data));
    }

    [Fact]
    public void A_generated_preview_preserves_the_brand_kit_version_across_job_scopes()
    {
        var generation = new ProvenanceTrail();
        generation.BrandKit(7);
        var state = generation.Take();
        var confirmation = new ProvenanceTrail();
        confirmation.Restore(AskJson.Read<ProvenanceState>(AskJson.Element(state)));
        Assert.Equal(7, confirmation.For(new Asset { Kind = AssetKinds.Image, BriefVersion = 3 }, 1, null).BrandKitVersion);
    }
}
