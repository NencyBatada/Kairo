using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class NotifyTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_audience_gets_one_notification_and_one_queued_email_each()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var admin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Roles(BrandRoles.Admin)));

        var notifications = await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId);
        Assert.Equal(new[] { owner.UserId, admin.UserId }.Order(), notifications.Select(row => row.UserId).Order());
        Assert.All(notifications, row =>
        {
            Assert.Equal(
                ("connection.problem", "Google Ads needs reconnecting", "/settings/connections", (DateTimeOffset?)null),
                (row.Kind, row.Title, row.Link, row.ReadAt));
            Assert.Equal(_api.Clock.GetUtcNow(), row.CreatedAt);
        });

        var emails = await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId);
        Assert.Equal(
            notifications.Select(row => (row.Id, row.UserId)).Order(),
            emails.Select(row => (row.NotificationId, row.UserId)).Order());
        Assert.All(emails, email =>
        {
            Assert.Equal(("Pending", 0, (DateTimeOffset?)null), (email.Status, email.Attempts, email.SentAt));
            Assert.Equal(_api.Clock.GetUtcNow(), email.NextAttemptAt);
        });
    }

    [Fact]
    public async Task The_live_event_goes_to_the_recipients_only()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(marketer.UserId)));

        var created = Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notification.created");
        Assert.Equal("""{"kind":"connection.problem"}""", created.Payload);
        Assert.Equal("Users", created.AudienceKind);
        Assert.Equal([marketer.UserId], JsonSerializer.Deserialize<Guid[]>(created.AudienceUsers!)!);
    }

    [Fact]
    public async Task Everyone_means_every_current_member()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Everyone));

        var notified = (await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId)).Select(row => row.UserId);
        Assert.Equal(new[] { owner.UserId, marketer.UserId, approver.UserId }.Order(), notified.Order());
    }

    [Fact]
    public async Task A_named_person_who_is_not_a_member_is_skipped()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var outsider = await Workspaces.SignInAsync(_api);

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId, outsider.UserId)));

        Assert.Equal(new[] { owner.UserId }, (await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId)).Select(row => row.UserId));
    }

    [Fact]
    public async Task A_person_named_twice_is_notified_once()
    {
        var (owner, brandId) = await WorkspaceAsync();

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId, owner.UserId)));

        Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task Nobody_to_notify_writes_nothing()
    {
        var (_, brandId) = await WorkspaceAsync();

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(Guid.NewGuid())));

        Assert.Empty(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.DoesNotContain(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notification.created");
    }

    [Fact]
    public async Task An_in_app_only_kind_queues_no_email()
    {
        var (_, brandId) = await WorkspaceAsync();

        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Everyone, kind: "impact_lab.ready"));

        Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Empty(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task A_rolled_back_transaction_leaves_no_notification_email_or_live_event()
    {
        var (_, brandId) = await WorkspaceAsync();
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            await scope.ServiceProvider.GetRequiredService<INotifications>().NotifyAsync(Notifying.Sample(LiveAudience.Everyone), Token);
            await database.RollbackAsync(Token);
        }

        Assert.Empty(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Empty(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.DoesNotContain(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notification.created");
    }

    [Fact]
    public async Task Notifying_needs_a_workspace()
    {
        await using var scope = _api.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<INotifications>().NotifyAsync(Notifying.Sample(LiveAudience.Everyone), Token));
    }

    public static TheoryData<string, string, string, string> BadNotifications => new()
    {
        { "connection.unknown", "Title", "Body", "/settings" },
        { "connection.problem", "", "Body", "/settings" },
        { "connection.problem", new string('t', 201), "Body", "/settings" },
        { "connection.problem", "Title", new string('b', 1001), "/settings" },
        { "connection.problem", "Title", "Body", "settings/connections" },
        { "connection.problem", "Title", "Body", "https://elsewhere.example/settings" },
        { "connection.problem", "Title", "Body", "/" + new string('l', 400) },
    };

    public static TheoryData<string, string?> BadSubjects => new()
    {
        { "approval.needed", null },
        { "approval.needed", " " },
        { "approval.needed", new string('s', 201) },
        { "stock.pause_changed", "stock:1" },
    };

    [Theory]
    [MemberData(nameof(BadSubjects))]
    public async Task Approvals_need_a_subject_and_grouped_kinds_cannot_have_one(string kind, string? subject)
    {
        var (_, brandId) = await WorkspaceAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Notifying.NotifyAsync(_api, brandId, new Notification(kind, LiveAudience.Everyone, "Title", "Body", "/campaigns", subject)));
    }

    [Fact]
    public async Task Resolving_needs_a_workspace_and_a_subject()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotifications>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => notifications.ResolveAsync("approval:1", Token));
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        await Assert.ThrowsAsync<ArgumentException>(() => notifications.ResolveAsync(" ", Token));
    }

    [Theory]
    [MemberData(nameof(BadNotifications))]
    public async Task A_bad_kind_title_body_or_link_is_refused(string kind, string title, string body, string link)
    {
        var (_, brandId) = await WorkspaceAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Notifying.NotifyAsync(_api, brandId, new Notification(kind, LiveAudience.Everyone, title, body, link)));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser));
    }
}
