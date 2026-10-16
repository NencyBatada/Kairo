using Kairo.Contracts.Live;

namespace Kairo.Modules.CampaignRooms;

internal static class CampaignLiveEvents
{
    public static LiveEvent Changed(Guid roomId) => new("campaign.changed", new { campaignId = roomId }, LiveAudience.Everyone);

    public static LiveEvent ListChanged() => new("campaigns.changed", new { }, LiveAudience.Everyone);

    public static LiveEvent CheckSettingsChanged() => new("check_settings.changed", new { }, LiveAudience.Everyone);
}
