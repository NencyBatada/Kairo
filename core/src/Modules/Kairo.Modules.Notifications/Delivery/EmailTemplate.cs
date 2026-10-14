using System.Globalization;
using System.Net;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;
using Kairo.Modules.Notifications.Publishing;
using Kairo.Modules.Notifications.Scheduling;

namespace Kairo.Modules.Notifications.Delivery;

internal static class EmailTemplate
{
    public const string DigestKind = "digest";

    public const string InvitationKind = "invitation";

    private const string Required = "Emails like this can't be turned off.";

    public static EmailMessage Render(
        InAppNotification notification,
        NotificationKind kind,
        BrandSummary workspace,
        BrandMember member,
        string publicOrigin)
    {
        var workspaceUrl = WorkspaceUrl(publicOrigin, workspace);
        var actionUrl = workspaceUrl + notification.Link;
        var settingsUrl = workspaceUrl + "/settings/notifications";
        var reason = MemberReason(workspace);

        var html = Layout(
            workspace.Name,
            notification.Title,
            Paragraph(notification.Body),
            "Open in Kairo",
            actionUrl,
            Footer(reason, kind.CanTurnOff, settingsUrl));

        var lines = new List<string> { workspace.Name, "", notification.Title, "" };
        if (notification.Body.Length > 0)
        {
            lines.AddRange([notification.Body, ""]);
        }

        lines.AddRange([$"Open in Kairo: {actionUrl}", "", reason]);
        lines.Add(kind.CanTurnOff ? $"Change email settings: {settingsUrl}" : Required);

        return new EmailMessage(member.UserId, kind.Key, member.Email, member.Name, notification.Title, html, string.Join("\n", lines));
    }

    public static EmailMessage RenderDigest(
        IReadOnlyList<InAppNotification> items,
        BrandSummary workspace,
        BrandMember member,
        string publicOrigin)
    {
        var workspaceUrl = WorkspaceUrl(publicOrigin, workspace);
        var settingsUrl = workspaceUrl + "/settings/notifications";
        var todayUrl = workspaceUrl + "/today";
        var reason = MemberReason(workspace);
        var canTurnOff = items.All(item => NotificationKinds.Find(item.Kind)?.CanTurnOff ?? true);
        var title = items.Count == 1 ? "Your daily digest: 1 update" : $"Your daily digest: {items.Count} updates";

        var list = string.Concat(items.Select(item =>
            $"""<p style="margin:0 0 16px;font-size:15px;line-height:1.5;color:#1E1A15;"><a href="{Encode(workspaceUrl + item.Link)}" style="color:#A8461F;font-weight:600;text-decoration:underline;">{Encode(item.Title)}</a>{(item.Body.Length == 0 ? "" : "<br>" + Encode(item.Body).Replace("\n", "<br>", StringComparison.Ordinal))}</p>"""));
        var html = Layout(workspace.Name, title, list + "<div style=\"height:8px;\"></div>", "Open Today in Kairo", todayUrl, Footer(reason, canTurnOff, settingsUrl));

        var lines = new List<string> { workspace.Name, "", title, "" };
        foreach (var item in items)
        {
            lines.Add(item.Title);
            if (item.Body.Length > 0)
            {
                lines.Add(item.Body);
            }

            lines.AddRange([workspaceUrl + item.Link, ""]);
        }

        lines.AddRange([$"Open Today in Kairo: {todayUrl}", "", reason]);
        lines.Add(canTurnOff ? $"Change email settings: {settingsUrl}" : Required);

        return new EmailMessage(member.UserId, DigestKind, member.Email, member.Name, title, html, string.Join("\n", lines));
    }

    public static EmailMessage RenderInvitation(string address, InvitationDetails invitation, BrandSummary workspace, string publicOrigin)
    {
        var joinUrl = publicOrigin.TrimEnd('/') + "/welcome";
        var title = $"{invitation.InvitedBy} invited you to {workspace.Name} on Kairo";
        var roles = RoleWords(invitation.Roles);
        var expires = TimeZoneInfo.ConvertTime(invitation.ExpiresAt, WorkingHours.ZoneOf(workspace.TimeZone))
            .ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var body = $"You've been invited to join {workspace.Name} {roles}. Sign in to Kairo with Google as {address} to accept. The invitation expires on {expires}.";
        var reason = $"You're getting this because {invitation.InvitedBy} invited {address} to {workspace.Name}. If you weren't expecting it, you can ignore this email.";

        var html = Layout(workspace.Name, title, Paragraph(body), $"Join {workspace.Name}", joinUrl, Encode(reason));
        var text = string.Join("\n", workspace.Name, "", title, "", body, "", $"Join {workspace.Name}: {joinUrl}", "", reason);

        return new EmailMessage(null, InvitationKind, address, "", title, html, text);
    }

    private static string RoleWords(BrandRoles roles)
    {
        var names = roles.ToList().Select(role => role.ToString()).ToList();
        var joined = names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names[..^1]) + " and " + names[^1],
        };
        return names.Count == 1 ? $"with the {joined} role" : $"with the {joined} roles";
    }

    private static string WorkspaceUrl(string publicOrigin, BrandSummary workspace) => $"{publicOrigin.TrimEnd('/')}/{workspace.Handle}";

    private static string MemberReason(BrandSummary workspace) => $"You're getting this because you're a member of {workspace.Name}.";

    private static string Footer(string reason, bool canTurnOff, string settingsUrl) =>
        canTurnOff
            ? $"""{Encode(reason)} <a href="{Encode(settingsUrl)}" style="color:#574F45;text-decoration:underline;">Change email settings</a>"""
            : $"{Encode(reason)} {Encode(Required)}";

    private static string Paragraph(string text) =>
        text.Length == 0
            ? ""
            : $"""<p style="margin:0 0 24px;font-size:15px;line-height:1.5;color:#1E1A15;">{Encode(text).Replace("\n", "<br>", StringComparison.Ordinal)}</p>""";

    private static string Layout(string workspaceName, string title, string body, string action, string actionUrl, string footer) =>
        $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{Encode(title)}</title></head>
            <body style="margin:0;padding:0;background:#F7F3ED;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F3ED;padding:32px 16px;">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#FFFDF9;border:1px solid #E6DDD0;border-radius:12px;">
            <tr><td style="padding:32px;font-family:Arial,Helvetica,sans-serif;color:#1E1A15;">
            <p style="margin:0 0 8px;font-size:13px;color:#574F45;">{Encode(workspaceName)}</p>
            <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;font-weight:600;color:#1E1A15;">{Encode(title)}</h1>
            {body}
            <a href="{Encode(actionUrl)}" style="display:inline-block;background:#A8461F;color:#FFFDF9;text-decoration:none;font-size:15px;font-weight:600;padding:12px 20px;border-radius:8px;">{Encode(action)}</a>
            </td></tr>
            <tr><td style="padding:20px 32px;border-top:1px solid #E6DDD0;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#574F45;">{footer}</td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
