namespace Kairo.Modules.Notifications.Kinds;

internal sealed record NotificationKind(
    string Key,
    string Name,
    string Description,
    BrandRoles UsualRecipients,
    bool Email,
    bool CanTurnOff);

internal static class NotificationKinds
{
    public static readonly NotificationKind[] All =
    [
        new("opportunities.ready", "New top opportunities", "Kairo found new top opportunities in the daily refresh.", BrandRoles.Marketer, true, true),
        new("trigger.fired", "Trigger fired", "A trigger rule's conditions were met.", BrandRoles.Marketer, true, true),
        new("approval.needed", "Approval needed", "Something is waiting for your approval.", BrandRoles.Approver, true, false),
        new("work.returned", "Work sent back", "An approver sent a brief or assets back for changes.", BrandRoles.Marketer, true, true),
        new("launch.finished", "Launch finished", "A campaign is live in Google Ads.", BrandRoles.Marketer, true, true),
        new("launch.failed", "Launch failed", "A campaign could not be launched.", BrandRoles.Marketer, true, false),
        new("health.problem", "Health-check problem", "A campaign's check a day after launch found a problem.", BrandRoles.Marketer, true, true),
        new("stock.pause_changed", "Ad paused or resumed for stock", "Kairo paused or resumed ads because stock changed.", BrandRoles.Marketer, false, false),
        new("mass_pause.held", "Mass pause held for confirmation", "A stock change would pause many ads at once and is waiting for confirmation.", BrandRoles.Admin | BrandRoles.Marketer, true, true),
        new("pacing.alert", "Pacing alert", "A campaign is spending faster or slower than planned.", BrandRoles.Marketer | BrandRoles.Approver, true, true),
        new("report.ready", "Recommendation or impact report ready", "A test window ended and its report or recommendation is ready.", BrandRoles.Marketer | BrandRoles.Approver, true, true),
        new("impact_lab.ready", "Impact Lab result ready", "An Impact Lab analysis you ran has finished.", BrandRoles.Marketer | BrandRoles.Admin, false, false),
        new("connection.problem", "Connection or data problem", "A connected account needs reconnecting, or a data feed failed.", BrandRoles.Admin, true, true),
    ];

    public static NotificationKind? Find(string key) => All.FirstOrDefault(kind => kind.Key == key);
}
