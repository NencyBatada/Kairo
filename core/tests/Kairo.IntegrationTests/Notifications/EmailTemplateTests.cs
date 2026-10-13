using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Delivery;
using Kairo.Modules.Notifications.Kinds;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailTemplateTests
{
    private static readonly BrandSummary Workspace = new(Guid.NewGuid(), "acme-shoes", "Acme Shoes");
    private static readonly BrandMember Priya = new(Guid.NewGuid(), "priya@acme.example", "Priya Shah", BrandRoles.Admin);

    [Fact]
    public void The_email_goes_to_the_member_with_the_title_as_its_subject()
    {
        var email = Render(Notification());

        Assert.Equal((Priya.UserId, "connection.problem", "priya@acme.example", "Priya Shah"), (email.UserId, email.Kind, email.To, email.ToName));
        Assert.Equal("Google Ads needs reconnecting", email.Subject);
    }

    [Fact]
    public void The_button_opens_the_notification_s_screen_in_the_workspace()
    {
        var email = Render(Notification(), publicOrigin: "https://kairo.test/");

        Assert.Contains("href=\"https://kairo.test/acme-shoes/settings/connections\"", email.Html);
        Assert.Contains("Open in Kairo", email.Html);
        Assert.Contains("https://kairo.test/acme-shoes/settings/connections", email.Text);
    }

    [Fact]
    public void The_email_shows_the_workspace_title_and_body()
    {
        var email = Render(Notification());

        Assert.Contains("Acme Shoes", email.Html);
        Assert.Contains("Google Ads needs reconnecting", email.Html);
        Assert.Contains("Reconnect it so refreshes and changes can continue.", email.Html);
        Assert.Contains("Acme Shoes", email.Text);
        Assert.Contains("Reconnect it so refreshes and changes can continue.", email.Text);
    }

    [Fact]
    public void A_kind_that_can_be_turned_off_links_to_email_settings()
    {
        var email = Render(Notification());

        Assert.Contains("You&#39;re getting this because you&#39;re a member of Acme Shoes.", email.Html);
        Assert.Contains("You're getting this because you're a member of Acme Shoes.", email.Text);
        Assert.Contains("href=\"https://kairo.test/acme-shoes/settings/notifications\"", email.Html);
        Assert.Contains("Change email settings", email.Html);
        Assert.Contains("https://kairo.test/acme-shoes/settings/notifications", email.Text);
    }

    [Fact]
    public void A_kind_that_can_t_be_turned_off_says_so_and_offers_no_settings_link()
    {
        var email = Render(Notification(kind: "approval.needed"));

        Assert.Contains("Emails like this can&#39;t be turned off.", email.Html);
        Assert.Contains("Emails like this can't be turned off.", email.Text);
        Assert.DoesNotContain("settings/notifications", email.Html);
        Assert.DoesNotContain("Change email settings", email.Html);
    }

    [Fact]
    public void Markup_in_any_value_is_shown_as_text()
    {
        var email = Render(
            Notification(title: "<b>Pacing</b> alert", body: "Budget \"Diwali\" <script>alert(1)</script> & more"),
            workspace: Workspace with { Name = "Acme <Shoes>" });

        Assert.Contains("&lt;b&gt;Pacing&lt;/b&gt; alert", email.Html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; more", email.Html);
        Assert.Contains("Acme &lt;Shoes&gt;", email.Html);
        Assert.DoesNotContain("<b>Pacing", email.Html);
        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("<b>Pacing</b> alert", email.Text);
    }

    [Fact]
    public void The_layout_uses_the_design_system_colours()
    {
        var html = Render(Notification()).Html;

        foreach (var colour in new[] { "#F7F3ED", "#FFFDF9", "#E6DDD0", "#1E1A15", "#574F45", "#A8461F" })
        {
            Assert.Contains(colour, html);
        }
    }

    private static InAppNotification Notification(
        string kind = "connection.problem",
        string title = "Google Ads needs reconnecting",
        string body = "Reconnect it so refreshes and changes can continue.") =>
        new() { Id = 7, UserId = Priya.UserId, Kind = kind, Title = title, Body = body, Link = "/settings/connections" };

    private static EmailMessage Render(InAppNotification notification, BrandSummary? workspace = null, string publicOrigin = "https://kairo.test") =>
        EmailTemplate.Render(notification, NotificationKinds.Find(notification.Kind)!, workspace ?? Workspace, Priya, publicOrigin);
}
