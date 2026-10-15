namespace Kairo.Contracts.Audit;

public static class AuditActions
{
    public static IReadOnlyList<KeyValuePair<string, string>> All { get; } =
    [
        new("brand.created", "Created the workspace"),
        new("brand.updated", "Changed the workspace details"),
        new("invitation.sent", "Sent an invitation"),
        new("invitation.revoked", "Revoked an invitation"),
        new("invitation.accepted", "Accepted an invitation"),
        new("invitation.declined", "Declined an invitation"),
        new("member.roles_changed", "Changed a member's roles"),
        new("member.removed", "Removed a member"),
        new("member.left", "Left the workspace"),
        new("approval_settings.changed", "Changed the approval settings"),
        new("connection.connected", "Connected an account"),
        new("connection.account_chosen", "Chose an account"),
        new("connection.disconnected", "Disconnected an account"),
        new("connection.needs_reconnect", "Marked an account as needing to reconnect"),
        new("brand_kit.draft_saved", "Saved a Brand Kit draft"),
        new("brand_kit.draft_discarded", "Discarded a Brand Kit draft"),
        new("brand_kit.approved", "Approved the Brand Kit"),
        new("brand_kit.restored", "Restored an earlier Brand Kit version"),
        new("brand_kit.file_uploaded", "Uploaded a Brand Kit file"),
        new("brand_kit.file_deleted", "Deleted a Brand Kit file"),
        new("brand_kit.drafting_started", "Started drafting the Brand Kit"),
        new("catalogue.file_imported", "Imported a catalogue file"),
        new("catalogue.feed_set", "Set the catalogue feed link"),
        new("catalogue.feed_removed", "Stopped the catalogue feed"),
        new("catalogue.feed_refreshed", "Refreshed the catalogue feed"),
        new("catalogue.product_fixed", "Fixed a product's missing details"),
        new("analytics.sales_uploaded", "Uploaded sales data"),
        new("analytics.sales_source_set", "Set the sales data source"),
        new("analytics.sales_source_removed", "Removed the sales data source"),
        new("analytics.feed_started", "Started a data feed refresh"),
        new("analytics.feed_succeeded", "Refreshed a data feed"),
        new("analytics.feed_failed", "A data feed refresh failed"),
        new("notifications.email_settings_changed", "Changed notification email settings"),
        new("opportunities.refreshed", "Refreshed the opportunities"),
        new("opportunity.accepted", "Accepted an opportunity"),
        new("opportunity.saved", "Saved an opportunity"),
        new("opportunity.unsaved", "Took an opportunity off the saved list"),
        new("opportunity.dismissed", "Dismissed an opportunity"),
        new("opportunity_topic.added", "Added a watched or ignored topic"),
        new("opportunity_topic.removed", "Removed a watched or ignored topic"),
        new("campaign_room.opened", "Opened a campaign room"),
        new("trigger_rule.created", "Created a trigger rule"),
        new("trigger_rule.updated", "Changed a trigger rule"),
        new("trigger_rule.paused", "Paused a trigger rule"),
        new("trigger_rule.resumed", "Resumed a trigger rule"),
        new("trigger_rule.activated", "Turned on a suggested trigger rule"),
        new("trigger_rule.deleted", "Deleted a trigger rule"),
        new("trigger_rule.suggested", "Suggested a trigger rule"),
        new("trigger.fired", "Fired a trigger rule"),
        new("festival.added", "Added a festival or event"),
        new("festival.updated", "Changed a festival or event"),
        new("festival.removed", "Removed a festival or event"),
    ];

    private static readonly Dictionary<string, string> Labels = All.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public static string LabelOf(string action) => Labels.GetValueOrDefault(action, action);

    public static bool IsKnown(string action) => Labels.ContainsKey(action);
}
