using System.Net;
using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Launch;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.BrandKit;
using Kairo.IntegrationTests.CampaignRooms;
using Kairo.Modules.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Launch;

public sealed class CampaignBuildTests(SqlServer sqlServer) : IAsyncLifetime
{
    private CampaignWorld _world = null!;
    private readonly FakeLaunchGoogle _google = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync()
    {
        _world = await CampaignWorld.CreateAsync(sqlServer);
        _world.LoadCityHistory();
        _world.Api.GoogleApis.LaunchResponse = _google.Respond;
    }

    public ValueTask DisposeAsync() => _world.DisposeAsync();

    [Fact]
    public async Task Nothing_is_created_before_gate_three_then_search_and_shopping_launch_with_total_budgets()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.ReadyForLaunchAsync(roomId);
        Assert.Empty(_google.Mutations);
        Assert.Null(await RecordAsync(roomId));
        await _world.LaunchPreparedAsync(roomId);
        Assert.Empty(_google.Mutations);
        Assert.Equal("queued", (await RecordAsync(roomId))!.Status);

        Assert.Equal(1, await RunAsync());

        var record = (await RecordAsync(roomId))!;
        Assert.Equal("live", record.Status);
        Assert.NotNull(record.ActivatedAt);
        Assert.Equal("live", Campaigns.Stage(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
        var writes = Writes();
        var build = Assert.Single(writes, IsBuild);
        Assert.False(build.GetProperty("partialFailure").GetBoolean());
        var campaigns = Creates(build, "campaignOperation");
        Assert.Equal(2, campaigns.Length);
        Assert.All(campaigns, campaign => Assert.Equal("PAUSED", campaign.GetProperty("status").GetString()));
        var budgets = Creates(build, "campaignBudgetOperation");
        var plan = JsonSerializer.Deserialize<LaunchPlan>(record.Plan, JsonSerializerOptions.Web)!;
        Assert.Equal(plan.Budget * 1_000_000m, budgets.Sum(budget => decimal.Parse(budget.GetProperty("totalAmountMicros").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.All(budgets, budget => Assert.Equal("CUSTOM", budget.GetProperty("period").GetString()));
        Assert.All(_google.Resources.Where(pair => pair.Key.Contains("/assets/", StringComparison.Ordinal) && !pair.Value.ContainsKey("callToActionAsset")), pair =>
            Assert.Equal("IS_SYNTHETIC", pair.Value["syntheticContentInfo"]!["advertiserAttestation"]!["status"]!.GetValue<string>()));
        foreach (var holdout in plan.Holdouts)
            Assert.Equal(2, Creates(build, "campaignCriterionOperation").Count(criterion => criterion.TryGetProperty("negative", out var negative) && negative.GetBoolean() && criterion.GetProperty("location").GetProperty("geoTargetConstant").GetString() == $"geoTargetConstants/{holdout.Id}"));
        var activate = Assert.Single(writes, IsActivation);
        Assert.Equal(2, activate.GetProperty("mutateOperations").GetArrayLength());
        await using var scope = await ScopeAsync();
        Assert.NotEmpty(await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().ProductAds.Where(row => row.RoomId == roomId).ToListAsync(Token));
        Assert.Equal(0, await RunAsync());
    }

    [Fact]
    public async Task A_missing_merchant_link_fails_before_any_google_write_and_explains_the_fix()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        _google.MerchantLinked = false;
        Assert.Equal(1, await RunAsync());
        var record = (await RecordAsync(roomId))!;
        Assert.Equal(("failed", "merchant_not_linked"), (record.Status, record.ErrorCode));
        Assert.Contains("Link", record.SuggestedFix);
        Assert.Empty(Writes());
        Assert.Equal("needs_attention", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Build_or_activation_failure_never_leaves_a_campaign_spending(bool failBuild)
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        _google.FailBuild = failBuild;
        _google.FailActivation = !failBuild;
        await RunAsync();
        Assert.Equal("failed", (await RecordAsync(roomId))!.Status);
        Assert.Contains("policy finding", (await RecordAsync(roomId))!.ErrorMessage);
        var campaigns = _google.Resources.Where(pair => pair.Key.Contains("/campaigns/", StringComparison.Ordinal)).Select(pair => pair.Value).ToArray();
        if (failBuild) Assert.Empty(campaigns);
        else Assert.Equal(2, campaigns.Length);
        Assert.All(campaigns, campaign => Assert.Equal("PAUSED", campaign["status"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_three_campaign_types_support_image_only_or_unlisted_video_demand_gen(bool video)
    {
        if (video) await Connections.ConnectAsync(_world.Api, _world.Admin, _world.BrandId, "youtube");
        var logo = await BrandKits.UploadAsync(_world.Admin, _world.BrandId, BrandKits.Png, "logo.png", "logo");
        await BrandKits.ApproveNewAsync(_world.Admin, _world.BrandId, new { logo = new { fileId = logo }, colours = new[] { new { hex = "#003366", name = "Navy", role = "primary" } } });
        _world.Plan.Answer = request =>
        {
            var draft = JsonSerializer.SerializeToNode(FakeBriefPlan.Draft(request))!.AsObject();
            draft["campaignTypes"] = JsonSerializer.SerializeToNode(new[] { "search", "shopping", "demand_gen" });
            return draft;
        };
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        await RunAsync();
        var record = (await RecordAsync(roomId))!;
        Assert.True(record.Status == "live", $"{record.ErrorCode}: {record.ErrorMessage} {record.SuggestedFix}");
        var build = Assert.Single(Writes(), IsBuild);
        Assert.Equal(3, Creates(build, "campaignOperation").Length);
        Assert.Contains(Creates(build, "adGroupAdOperation"), ad => ad.GetProperty("ad").TryGetProperty("demandGenMultiAssetAd", out _));
        Assert.All(Creates(build, "adGroupAdOperation").Where(ad => ad.GetProperty("ad").TryGetProperty("demandGenMultiAssetAd", out _)), ad =>
            Assert.Equal("OPTED_OUT", ad.GetProperty("adGroupAdAssetAutomationSettings")[0].GetProperty("assetAutomationStatus").GetString()));
        Assert.Equal(video ? 1 : 0, _google.VideoUploads);
        if (video)
        {
            Assert.Equal("unlisted", _google.UploadMetadata!.Value.GetProperty("status").GetProperty("privacyStatus").GetString());
            Assert.True(_google.UploadMetadata.Value.GetProperty("status").GetProperty("containsSyntheticMedia").GetBoolean());
            Assert.Contains(Creates(build, "adGroupAdOperation"), ad => ad.GetProperty("ad").TryGetProperty("demandGenVideoResponsiveAd", out _));
            Assert.Contains(Creates(build, "assetOperation"), asset => asset.TryGetProperty("callToActionAsset", out var action) && action.GetProperty("callToAction").GetString() == "SHOP_NOW");
        }
    }

    [Fact]
    public async Task A_lost_build_response_is_not_replayed_by_http_resilience()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        _google.LoseBuildResponse = true;
        await RunAsync();
        var record = (await RecordAsync(roomId))!;
        Assert.True(record.Uncertain);
        Assert.Equal("build", record.AttemptingStep);
        Assert.Single(Writes(), IsBuild);
        Assert.DoesNotContain(Writes(), IsActivation);
    }

    [Fact]
    public async Task A_failed_local_checkpoint_preserves_the_external_intent_and_marks_the_room_for_attention()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        await using var connection = new SqlConnection(_world.Database);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE launch.Launches ADD CONSTRAINT FailBuildCheckpoint CHECK (JSON_VALUE(Resources, '$.built') IS NULL)
            """;
        await command.ExecuteNonQueryAsync(Token);

        await RunAsync();

        var record = (await RecordAsync(roomId))!;
        Assert.Equal(("failed", "checkpoint_failed", "build"), (record.Status, record.ErrorCode, record.AttemptingStep));
        Assert.True(record.Uncertain);
        Assert.Single(Writes(), IsBuild);
        Assert.DoesNotContain(Writes(), IsActivation);
        Assert.Equal("needs_attention", Campaigns.Status(await Campaigns.DetailAsync(_world.Marketer, _world.BrandId, roomId)));
    }

    [Fact]
    public async Task Launch_records_and_product_mappings_are_hidden_from_other_tenants()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        await RunAsync();
        await using var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        Assert.Empty(await db.Launches.Where(row => row.RoomId == roomId).ToListAsync(Token));
        Assert.Empty(await db.ProductAds.Where(row => row.RoomId == roomId).ToListAsync(Token));
    }

    [Fact]
    public async Task Concurrent_sweeps_claim_one_launch_once_and_changed_approvals_block_every_write()
    {
        var roomId = await _world.OpenWithBriefAsync();
        await _world.LaunchAsync(roomId);
        await using (var scope = await ScopeAsync())
            await scope.ServiceProvider.GetRequiredService<IApprovalLocks>().InvalidateAsync(roomId, ["launch"], Token);
        var results = await Task.WhenAll(RunAsync(), RunAsync());
        Assert.Equal(1, results.Sum());
        Assert.Equal("approval_changed", (await RecordAsync(roomId))!.ErrorCode);
        Assert.Empty(Writes());
    }

    private async Task<int> RunAsync()
    {
        await using var scope = _world.Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LaunchDispatcher>().RunDueAsync(Token);
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var scope = _world.Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(_world.BrandId, Token);
        return scope;
    }

    private async Task<LaunchRecord?> RecordAsync(Guid roomId)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<LaunchDbContext>().Launches.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == roomId, Token);
    }

    private JsonElement[] Writes() => _google.Mutations.Where(call => !call.GetProperty("validateOnly").GetBoolean()).ToArray();
    private static bool IsBuild(JsonElement call) => call.GetProperty("mutateOperations").EnumerateArray().Any(op => op.TryGetProperty("campaignOperation", out var campaign) && campaign.TryGetProperty("create", out _));
    private static bool IsActivation(JsonElement call) => call.GetProperty("mutateOperations").EnumerateArray().Any(op => op.TryGetProperty("campaignOperation", out var campaign) && campaign.TryGetProperty("update", out _));
    private static JsonElement[] Creates(JsonElement call, string type) => call.GetProperty("mutateOperations").EnumerateArray().Where(op => op.TryGetProperty(type, out var value) && value.TryGetProperty("create", out _)).Select(op => op.GetProperty(type).GetProperty("create")).ToArray();
}
