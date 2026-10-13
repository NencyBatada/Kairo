using System.Net;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;

namespace Kairo.Modules.Notifications.Delivery;

internal static class EmailTemplate
{
    public static EmailMessage Render(
        InAppNotification notification,
        NotificationKind kind,
        BrandSummary workspace,
        BrandMember member,
        string publicOrigin)
    {
        var workspaceUrl = $"{publicOrigin.TrimEnd('/')}/{workspace.Handle}";
        var actionUrl = workspaceUrl + notification.Link;
        var settingsUrl = workspaceUrl + "/settings/notifications";
        var reason = $"You're getting this because you're a member of {workspace.Name}.";
        var required = "Emails like this can't be turned off.";

        var footer = kind.CanTurnOff
            ? $"""{Encode(reason)} <a href="{Encode(settingsUrl)}" style="color:#574F45;text-decoration:underline;">Change email settings</a>"""
            : $"{Encode(reason)} {Encode(required)}";
        var body = notification.Body.Length == 0
            ? ""
            : $"""<p style="margin:0 0 24px;font-size:15px;line-height:1.5;color:#1E1A15;">{Encode(notification.Body).Replace("\n", "<br>", StringComparison.Ordinal)}</p>""";

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{Encode(notification.Title)}</title></head>
            <body style="margin:0;padding:0;background:#F7F3ED;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F7F3ED;padding:32px 16px;">
            <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#FFFDF9;border:1px solid #E6DDD0;border-radius:12px;">
            <tr><td style="padding:32px;font-family:Arial,Helvetica,sans-serif;color:#1E1A15;">
            <p style="margin:0 0 8px;font-size:13px;color:#574F45;">{Encode(workspace.Name)}</p>
            <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;font-weight:600;color:#1E1A15;">{Encode(notification.Title)}</h1>
            {body}
            <a href="{Encode(actionUrl)}" style="display:inline-block;background:#A8461F;color:#FFFDF9;text-decoration:none;font-size:15px;font-weight:600;padding:12px 20px;border-radius:8px;">Open in Kairo</a>
            </td></tr>
            <tr><td style="padding:20px 32px;border-top:1px solid #E6DDD0;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#574F45;">{footer}</td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;

        var lines = new List<string> { workspace.Name, "", notification.Title, "" };
        if (notification.Body.Length > 0)
        {
            lines.AddRange([notification.Body, ""]);
        }

        lines.AddRange([$"Open in Kairo: {actionUrl}", "", reason]);
        lines.Add(kind.CanTurnOff ? $"Change email settings: {settingsUrl}" : required);

        return new EmailMessage(member.UserId, kind.Key, member.Email, member.Name, notification.Title, html, string.Join("\n", lines));
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
