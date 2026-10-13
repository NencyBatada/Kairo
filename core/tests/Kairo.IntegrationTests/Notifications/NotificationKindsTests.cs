using System.Text.RegularExpressions;
using Kairo.Modules.Notifications.Kinds;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Notifications;

public sealed class NotificationKindsTests
{
    [Fact]
    public void There_is_one_kind_per_event_in_the_PRD_with_a_unique_dotted_key()
    {
        Assert.Equal(13, NotificationKinds.All.Length);
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
    public void Stock_pauses_and_Impact_Lab_results_stay_in_the_app()
    {
        var inAppOnly = NotificationKinds.All.Where(kind => !kind.Email).ToList();

        Assert.Equal(new[] { "impact_lab.ready", "stock.pause_changed" }, inAppOnly.Select(kind => kind.Key).Order());
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
        Assert.Null(NotificationKinds.Find("connection.unknown"));
    }
}
