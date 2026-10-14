using System.Text.RegularExpressions;
using Kairo.Modules.Notifications.Kinds;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Notifications;

public sealed class NotificationKindsTests
{
    [Fact]
    public void There_is_one_kind_per_event_in_the_PRD_with_a_unique_dotted_key()
    {
        Assert.Equal(14, NotificationKinds.All.Length);
        Assert.Equal(NotificationKinds.All.Length, NotificationKinds.All.Select(kind => kind.Key).Distinct().Count());
        Assert.All(NotificationKinds.All, kind => Assert.Matches(new Regex("^[a-z_]+(\\.[a-z_]+)+$"), kind.Key));
    }

    [Fact]
    public void Only_approval_requests_and_failed_launches_always_email()
    {
        var required = NotificationKinds.All.Where(kind => kind.Email && !kind.CanTurnOff).Select(kind => kind.Key);

        Assert.Equal(new[] { "approval.needed", "launch.failed" }, required.Order());
    }

    [Fact]
    public void Stock_pauses_Impact_Lab_results_and_Brand_Kit_drafts_stay_in_the_app()
    {
        var inAppOnly = NotificationKinds.All.Where(kind => !kind.Email).ToList();

        Assert.Equal(new[] { "brand_kit.drafted", "impact_lab.ready", "stock.pause_changed" }, inAppOnly.Select(kind => kind.Key).Order());
        Assert.All(inAppOnly, kind => Assert.False(kind.CanTurnOff));
    }

    [Fact]
    public void Every_kind_has_a_name_a_description_and_usual_recipients()
    {
        Assert.All(NotificationKinds.All, kind =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kind.Name));
            Assert.False(string.IsNullOrWhiteSpace(kind.Description));
            Assert.NotEqual(BrandRoles.None, kind.UsualRecipients);
        });
        Assert.Equal(BrandRoles.Admin, NotificationKinds.Find("connection.problem")!.UsualRecipients);
        Assert.Equal(BrandRoles.Admin, NotificationKinds.Find("brand_kit.drafted")!.UsualRecipients);
        Assert.Null(NotificationKinds.Find("connection.unknown"));
    }

    [Fact]
    public void Only_top_opportunities_wait_for_the_daily_digest_and_only_stock_pauses_are_grouped_hourly()
    {
        Assert.Equal(["opportunities.ready"], NotificationKinds.All.Where(kind => kind.Delivery == NotificationDelivery.DailyDigest).Select(kind => kind.Key));
        var grouped = Assert.Single(NotificationKinds.All, kind => kind.Delivery == NotificationDelivery.GroupedHourly);
        Assert.Equal(("stock.pause_changed", "/campaigns"), (grouped.Key, grouped.GroupLink));
    }

    [Fact]
    public void Only_approval_requests_are_reminded_after_four_working_hours()
    {
        var reminded = Assert.Single(NotificationKinds.All, kind => kind.RemindAfter is not null);

        Assert.Equal(("approval.needed", (TimeSpan?)TimeSpan.FromHours(4)), (reminded.Key, reminded.RemindAfter));
    }
}
