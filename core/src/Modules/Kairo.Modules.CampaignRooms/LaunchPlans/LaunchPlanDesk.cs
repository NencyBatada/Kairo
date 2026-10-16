using System.Text.Json;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.LaunchPlans;

internal sealed class LaunchPlanDraft : IBrandOwned
{
    public Guid RoomId { get; set; }
    public Guid BrandId { get; set; }
    public string Settings { get; set; } = "{}";
}

internal sealed class LaunchPlanDesk(CampaignRoomsDbContext db, ApprovalSnapshots snapshots, IApprovalLocks approvals,
    IConnections connections, IBrandDirectory brands) : ILaunchPlans
{
    public async Task<LaunchPlan?> GetAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == roomId, cancellationToken);
        var brief = await db.Briefs.AsNoTracking().Where(row => row.RoomId == roomId).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        var proof = await db.ProofPlans.AsNoTracking().Where(row => row.RoomId == roomId).OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        if (room is null || brief is null || proof is null || brief.Content.Budget is null || brief.Content.StartDate is null || brief.Content.EndDate is null)
        {
            return null;
        }

        var brand = (await brands.GetAsync([room.BrandId], cancellationToken)).Single();
        var draft = await db.LaunchPlans.AsNoTracking().SingleOrDefaultAsync(row => row.RoomId == roomId, cancellationToken);
        var settings = draft is null ? new LaunchSettings([]) : JsonSerializer.Deserialize<LaunchSettings>(draft.Settings, JsonSerializerOptions.Web)!;
        var ads = await connections.GetStatusAsync(ConnectedProduct.GoogleAds, cancellationToken);
        var merchant = await connections.GetStatusAsync(ConnectedProduct.MerchantCenter, cancellationToken);
        var youtube = await connections.GetStatusAsync(ConnectedProduct.YouTube, cancellationToken);
        var content = brief.Content.Normalized();
        var plan = proof.Read();
        return new LaunchPlan(roomId, room.BrandId, room.Title, brief.Version, proof.Version, content.Budget!.Amount,
            content.Budget.Currency, brand.TimeZone, content.StartDate!.Value, content.EndDate!.Value,
            [.. content.Languages], [.. plan.TestRegions.OrderBy(region => region.Id).Select(region => new LaunchRegion(settings.GeoTargetIds?.GetValueOrDefault(region.Id) ?? region.Id, region.Name))],
            [.. plan.Holdouts.OrderBy(region => region.Id).Select(region => new LaunchRegion(settings.GeoTargetIds?.GetValueOrDefault(region.Id) ?? region.Id, region.Name))],
            ads.State == ConnectionState.Connected ? ads.ExternalId : null,
            merchant.State == ConnectionState.Connected ? merchant.ExternalId : null,
            youtube.State == ConnectionState.Connected ? youtube.ExternalId : null,
            [.. content.ProductIds.Order(StringComparer.Ordinal)], [.. content.LandingPages], settings, await snapshots.AssetsAsync(roomId, cancellationToken));
    }

    public async Task<bool> IsApprovedAsync(Guid roomId, CancellationToken cancellationToken) =>
        await GetAsync(roomId, cancellationToken) is { } plan
        && await snapshots.PriorGatesMatchAsync(roomId, cancellationToken)
        && await approvals.MatchesAsync(roomId, RoomGates.Launch, Serialize(plan), cancellationToken);

    public async Task<string?> BlockerAsync(Guid roomId, CancellationToken cancellationToken)
    {
        if (await GetAsync(roomId, cancellationToken) is not { } plan)
        {
            return "The brief and test plan must be complete before launch approval.";
        }

        if (Validate(plan) is { } error)
        {
            return error;
        }

        var types = await db.Briefs.Where(row => row.RoomId == roomId && row.Version == plan.BriefVersion)
            .Select(row => row.Content.CampaignTypes).SingleAsync(cancellationToken);
        if (!types.ToHashSet(StringComparer.Ordinal).SetEquals(plan.Settings.Channels.Select(channel => channel.CampaignType)))
        {
            return "Launch campaign types must match the approved brief.";
        }

        return await snapshots.PriorGatesMatchAsync(roomId, cancellationToken) ? null
            : "The brief or assets have changed or have no approval evidence. Send the campaign back and approve them again.";
    }

    public static string Serialize(LaunchPlan plan) => JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);

    public static string? Validate(LaunchPlan plan)
    {
        if (plan.GoogleAdsAccountId is null) return "Connect Google Ads before asking for launch approval.";
        if (plan.Start > plan.End || plan.Budget <= 0 || plan.Budget > 1_000_000_000m) return "Set valid dates and a budget up to one billion before launch.";
        if (plan.Regions.Length == 0 || plan.Holdouts.Length == 0 || plan.Regions.Concat(plan.Holdouts).Any(region => !long.TryParse(region.Id, out var id) || id <= 0)) return "Resolve the test and holdout regions to Google Ads locations before launch.";
        if (plan.Regions.Select(region => region.Id).Intersect(plan.Holdouts.Select(region => region.Id)).Any()) return "Test and holdout regions must not overlap.";
        var channels = plan.Settings.Channels;
        if (channels.Length == 0 || channels.Length > 3 || channels.Select(channel => channel.CampaignType).Distinct().Count() != channels.Length) return "Set a budget and bidding strategy for each campaign type.";
        if (channels.Any(channel => channel.Budget <= 0 || channel.Budget > plan.Budget) || channels.Sum(channel => channel.Budget) != plan.Budget) return "Channel budgets must add up to the approved brief budget.";
        foreach (var channel in channels)
        {
            if (channel.CampaignType is not ("search" or "shopping" or "demand_gen")) return "Choose Search, Shopping or Demand Gen.";
            if (channel.BiddingStrategy is not ("maximize_clicks" or "target_cpa" or "target_roas")) return "Choose a supported bidding strategy.";
            if (channel.CampaignType == "shopping" && channel.BiddingStrategy == "target_cpa") return "Shopping doesn't support target CPA bidding.";
            if (channel.BiddingStrategy != "maximize_clicks" && channel.Target is not (> 0 and <= 1_000_000_000m)) return "Set a positive target up to one billion for target CPA or target ROAS bidding.";
            if (decimal.Floor(channel.Budget * 1_000_000m / (plan.End.DayNumber - plan.Start.DayNumber + 1)) < 1) return "Each channel needs at least one micro of daily budget.";
            if (channel.BiddingStrategy == "maximize_clicks" && channel.Target is not null) return "Maximize clicks has no bid target in this launch plan.";
        }

        if ((plan.Settings.AudienceIds ?? []).Any(id => !long.TryParse(id, out var number) || number <= 0)) return "Use Google Ads user-list IDs for audiences.";
        if (channels.Any(channel => channel.CampaignType == "shopping") && (plan.MerchantAccountId is null || plan.Settings.SalesCountry is not { Length: 2 } country || country.Any(character => character is < 'A' or > 'Z'))) return "Connect Merchant Center and choose the Shopping sales country.";
        if (plan.Assets.Length == 0 || plan.Assets.Any(asset => asset.CheckState is not ("passed" or "overridden") || asset.DisclosureBlocker is not null)) return "Approve a checked asset set before launch.";
        if (plan.Assets.Any(asset => asset.Kind == "video") && plan.YouTubeChannelId is null) return "Reconnect YouTube before launching the approved videos.";
        return null;
    }
}
