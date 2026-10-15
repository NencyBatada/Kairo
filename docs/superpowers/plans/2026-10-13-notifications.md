# Kairo Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tell people when something needs them, in the app and by email through Gmail, with a link to the screen where they act, starting with connections that need reconnecting.

**Architecture:**
- **Contract.** `INotifications.NotifyAsync` in `Kairo.Contracts` runs inside the caller's transaction. It resolves a `LiveAudience` to the workspace's current members through a new Identity contract, `IBrandMembers`.
- **What a notify call writes.**
  - one `notifications.Notifications` row per recipient
  - one `notifications.OutboundEmails` row per recipient whose email for that kind is on
  - one `notification.created` live event to the recipients
- **Sending.** A background loop in the API claims due email rows with `UPDATE TOP (10) … OUTPUT` and a 2-minute lease. It renders one HTML and text layout and sends it through Gmail as one sender mailbox, retrying on a fixed schedule.
- **API.** The web app gets an inbox API and an email-settings API.
- **First producer.** Brands' `ConnectionTokens.LoseAsync` notifies admins.

**Tech Stack:** .NET 10, ASP.NET Core 10 minimal APIs, EF Core 10 on SQL Server 2022, `BackgroundService`, MimeKit 4.18.1, the Gmail API v1 REST endpoint, xUnit v3 with Testcontainers and `FakeTimeProvider`, Pester 5.9.1 for the deploy scripts.

**Spec:** `docs/superpowers/specs/2026-10-13-notifications-design.md`

## Global Constraints

- **Worktree and branch.** Work only in `C:\Kairo-worktrees\notifications` (Git Bash path `/c/Kairo-worktrees/notifications`), on `feature/notifications`. Every command in this plan runs from that folder unless a step says otherwise. Never touch `C:\Kairo` or any other worktree. Don't push.
- **Task tracker.** Don't edit `docs/task.md`. Status commits are made separately on `develop`.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user.
  - Stage files by explicit path, never with `git add -A` or `git add .`.
  - Never stage `CLAUDE.md` or `.claude/`.
- **No comments in code** (`CLAUDE.md` §3): no `//`, `#`, `/* */` or `<!-- -->` comments, no XML doc comments. EF Core migrations go through `bash core/scripts/tidy-migrations.sh`.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build must finish with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container. Run `docker info` first.
- **Running tests.**
  - Whole core: `dotnet test --solution core/Kairo.slnx`.
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`.
  - Deploy scripts: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`.
- **Module rules stay true.**
  - `Kairo.Modules.Notifications` exposes exactly one public type, `NotificationsModule`, and adds no `ProjectReference`.
  - Its tables live only in the `notifications` schema.
  - Its migration classes are `internal`.
- **Names, copied from the spec.**
  - Contract: `Kairo.Contracts.Notifications.INotifications.NotifyAsync(Notification notification, CancellationToken cancellationToken)`, with `Notification(string Kind, LiveAudience Audience, string Title, string Body, string Link)`.
  - Identity contract: `IBrandMembers` with `ListAsync` and `FindAsync`, and `BrandMember(Guid UserId, string Email, string Name, BrandRoles Roles)`.
  - Tables: `notifications.Notifications` (entity `InAppNotification`), `notifications.EmailOptOuts`, `notifications.OutboundEmails`.
  - Email statuses: `Pending`, `Sent`, `Logged`, `Skipped`, `Failed`.
  - Live events: `notification.created` with `{ kind }`, and `notifications.read` with `{}`.
  - Routes, all under `/api/brands/{brandId}`:
    - `GET /notifications?before=&limit=`
    - `POST /notifications/{id}/read`
    - `POST /notifications/read-all`
    - `GET` and `PUT /notifications/email-settings`
  - Configuration: `Kairo:Notifications:Sender` and `Kairo:Notifications:SenderRefreshToken` (environment variables `Kairo__Notifications__Sender` and `Kairo__Notifications__SenderRefreshToken`).
  - Secret `mail-sender-refresh-token`, `MailSender` in `deploy/environments.psd1`, and the script `deploy/set-mail-sender.ps1`.
  - Kinds that can't be turned off: `approval.needed` and `launch.failed`. In-app-only kinds: `stock.pause_changed` and `impact_lab.ready`.
- **Timings.**
  - Sending loop every 5 seconds, claiming up to 10 rows with a 2-minute lease.
  - Retries after 1 minute, 5 minutes, 30 minutes, 2 hours and 6 hours. The sixth failed attempt marks the row `Failed`.
- **Versions.** One new NuGet package, `MimeKit` 4.18.1, the latest stable version on nuget.org when this plan was written. It restores with no audit warnings and brings `BouncyCastle.Cryptography` 2.7.0. No new npm packages.

## Review Focus

These are the inputs most likely to hurt a real user. Each one is pinned by a test in the task named in brackets.

1. **The same person is named twice**, for example `LiveAudience.Users(priya, priya)`, or a producer that merges two lists. They must get one notification and one email, not two. (Task 2: `A_person_named_twice_is_notified_once`.)
2. **A user belongs to two workspaces.** Turning an email off in one must leave it on in the other, and neither inbox may show the other's notifications. (Task 4: `Email_settings_belong_to_one_workspace`; Task 3: `Another_workspace_s_notifications_never_appear`.)
3. **Titles and bodies carry markup or non-English text**, such as `<b>` from a product name, `₹`, or Hindi. The email must show them exactly as text, never as HTML, and the subject must arrive intact in UTF-8. (Task 5: `Markup_and_non_english_text_arrive_as_plain_text` for the subject and text, and `Markup_in_any_value_is_shown_as_text` for the HTML.)
4. **The sender's access token expires between emails**, after an hour. The next email must refresh it and still send. (Task 5: `An_expired_access_token_is_refreshed_before_sending`.)
5. **The recipient changes email address, or the workspace changes handle, before the email goes out.** The email must go to the current address with a link that uses the current handle. (Task 6: `The_email_uses_the_current_address_and_handle`.)

## Where this plan differs from the spec

1. **The loop is off in the integration tests.**
   - Every test host shares one database, and each has its own fake clock. A loop in one host would claim another test's rows whenever its own clock ran ahead.
   - `KairoApiFactory` therefore sets `Kairo:Notifications:DeliveryLoop` to `off`, and the loop runs only when that setting isn't `off` and `MapApi` has switched it on.
   - Tests call `EmailDispatcher.SendDueAsync` directly. One test, on its own database, turns the loop on.
2. **`SendDueAsync` lives on a scoped `EmailDispatcher`**, which the `EmailDelivery` background service calls in a new scope every 5 seconds. The spec puts both on `EmailDelivery`.
3. **Sending tests use their own database.** The claim spans every workspace, so a shared database would let one test claim another's rows.
4. **The template takes more inputs:** `EmailTemplate.Render(notification, kind, workspace, member, publicOrigin)`. The kind decides the footer, and the origin builds the links.
5. **Links are also limited to 400 characters**, the size of the `Link` column.
6. **The sender is chosen when it is first resolved**, from the host's configuration, rather than when services are registered. Settings applied by the test factory are then always seen.
7. **A 401 from Gmail may pass.** The cached access token is dropped and the email retried, because an access token Google ended early recovers on the next refresh. Every other 4xx except 429 still fails at once.
8. **`Release.ps1` passes the sender only when Google sign-in is also configured.** `GmailSender` refreshes its token with Kairo's OAuth client, so without the client's secrets it could never send.
9. **A new `Get-KairoSecretValue` helper** reads the OAuth client ID and secret for `Set-KairoMailSender`.
10. **`IEmailSender.SendAsync` returns a `SendResult`**, `Sent` or `Logged`, so the dispatcher can mark a row `Logged` without knowing which sender it has.
11. **A local sender goes through the AppHost.** The spec suggests user secrets on `Kairo.Api`. That project has no user secrets, and giving it some would let a developer's real sender reach the integration tests' hosts. The AppHost passes `Kairo:MailSender` and `Kairo:MailSenderRefreshToken` from its own user secrets instead.

## File Structure

**Contracts**

| File | Responsibility |
| --- | --- |
| `core/src/Kairo.Contracts/Notifications/INotifications.cs` (new) | `INotifications` and `Notification` |
| `core/src/Kairo.Contracts/Identity/IBrandMembers.cs` (new) | `IBrandMembers` and `BrandMember` |

**Identity**

| File | Responsibility |
| --- | --- |
| `core/src/Modules/Kairo.Modules.Identity/Members/BrandMembers.cs` (new) | `IBrandMembers` from `Memberships` joined to `Users` |
| `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs` (modify) | Register it |

**The Notifications module, `core/src/Modules/Kairo.Modules.Notifications/`**

| File | Responsibility |
| --- | --- |
| `Kairo.Modules.Notifications.csproj` (modify) | MimeKit, and `InternalsVisibleTo` for the integration tests |
| `NotificationsModule.cs` (modify) | Registrations, endpoints, the loop switch and migration |
| `Kinds/NotificationKinds.cs` | `NotificationKind` and the 13 kinds |
| `Data/InAppNotification.cs`, `Data/EmailOptOut.cs`, `Data/OutboundEmail.cs` | The three tables |
| `Data/NotificationsDbContext.cs` and `Data/NotificationsDbContextFactory.cs` | Schema `notifications` |
| `Data/Migrations/*_Notifications.cs` | Tables, indexes and row-level security |
| `Publishing/Notifier.cs` | `INotifications` |
| `Inbox/InboxEndpoints.cs` | List, read one, read all |
| `EmailSettings/EmailSettingsEndpoints.cs` | Get and save email settings |
| `Delivery/EmailMessage.cs` | `EmailMessage`, `IEmailSender` and `EmailSendException` |
| `Delivery/EmailTemplate.cs` | The HTML and text email |
| `Delivery/LogEmailSender.cs` | Logs instead of sending |
| `Delivery/SenderToken.cs` | The sender mailbox's cached access token |
| `Delivery/GmailSender.cs` | Builds the MIME message and posts it to Gmail |
| `Delivery/EmailRetries.cs` | The retry schedule |
| `Delivery/EmailDispatcher.cs` | Claims, sends and records due emails |
| `Delivery/EmailDelivery.cs` | The 5-second loop, and the switch `MapApi` turns on |

**Brands**

| File | Change |
| --- | --- |
| `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionNotices.cs` (new) | Title and body for each reconnect reason |
| `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs` (modify) | `LoseAsync` notifies admins |

**Other core files**

| File | Change |
| --- | --- |
| `core/Directory.Packages.props` | `MimeKit` 4.18.1 |
| `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts` | Regenerated |

**Tests, `core/tests/Kairo.IntegrationTests/`**

| File | Covers |
| --- | --- |
| `Api/KairoApiFactory.cs` (modify) | Extra settings, and the loop off by default |
| `Api/FakeGoogleApis.cs` (modify) | The Gmail send endpoint and the sender's refresh token |
| `Notifications/BrandMembersTests.cs` | The member directory |
| `Notifications/NotificationKindsTests.cs` | The catalogue |
| `Notifications/Notifying.cs` | Calls `NotifyAsync` as a module would |
| `Notifications/NotificationRows.cs` | Raw SQL reads of the three tables |
| `Notifications/NotifyTests.cs` | `NotifyAsync` |
| `Notifications/InboxTests.cs` | The inbox API |
| `Notifications/EmailSettingsTests.cs` | The settings API |
| `Notifications/EmailTemplateTests.cs` | The email layout |
| `Notifications/GmailSenderTests.cs` | The Gmail sender and its token |
| `Notifications/EmailRetriesTests.cs` | The retry schedule |
| `Notifications/EmailDeliveryTests.cs` | Claiming, sending, retries and the loop |
| `Notifications/ConnectionNotificationTests.cs` | Connection problems notify admins |

**Deployment and docs**

| File | Change |
| --- | --- |
| `deploy/Kairo.Deploy/Common.ps1` | `$script:MailSecret` |
| `deploy/Kairo.Deploy/Setup.ps1` | The Gmail API, the secret and its grant, `Get-KairoSecretValue`, `Set-KairoMailSender` and its helpers |
| `deploy/Kairo.Deploy/Release.ps1` | Pass the sender to the API |
| `deploy/environments.psd1` | `MailSender` |
| `deploy/set-mail-sender.ps1` (new) | Signs the sender mailbox in once |
| `deploy/tests/Common.Tests.ps1`, `Setup.Tests.ps1`, `Release.Tests.ps1` | Their tests |
| `core/src/Kairo.AppHost/AppHost.cs` | Passes a local sender to the API when both user secrets are set |
| `docs/Kairo-SAD.md` and `README.md` | Sender, outbox and setup |

---

### Task 1: The contracts and the member directory

**Files:**
- Create: `core/src/Kairo.Contracts/Notifications/INotifications.cs`
- Create: `core/src/Kairo.Contracts/Identity/IBrandMembers.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/BrandMembers.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/BrandMembersTests.cs`

**Interfaces:**
- Consumes: existing `IdentityDbContext.Memberships` and `Users`, `KairoDatabase.BrandId`, `KairoDatabase.UseBrandAsync`, `KairoDatabase.EnterSystemScopeAsync`, and `LiveAudience` from `Kairo.Contracts.Live`.
- Produces:
  - `Kairo.Contracts.Notifications.INotifications.NotifyAsync(Notification notification, CancellationToken cancellationToken) : Task`
  - `Notification(string Kind, LiveAudience Audience, string Title, string Body, string Link)`
  - `Kairo.Contracts.Identity.IBrandMembers`, with:
    - `ListAsync(CancellationToken) : Task<IReadOnlyList<BrandMember>>`, the current workspace's members ordered by name. It throws `InvalidOperationException` with no workspace.
    - `FindAsync(Guid brandId, Guid userId, CancellationToken) : Task<BrandMember?>`, any workspace's member, or `null`
  - `BrandMember(Guid UserId, string Email, string Name, BrandRoles Roles)`

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/BrandMembersTests.cs`:

```csharp
using Kairo.Contracts.Identity;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class BrandMembersTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listing_returns_the_current_workspace_s_members_with_their_email_and_roles()
    {
        var owner = await Workspaces.SignInAsync(_api, "Priya Owner");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await Workspaces.CreateAsync((await Workspaces.SignInAsync(_api)).Browser);

        var members = await ListAsync(brandId);

        Assert.Equal(2, members.Count);
        var listedOwner = Assert.Single(members, member => member.UserId == owner.UserId);
        Assert.Equal((owner.Account.Email, "Priya Owner", BrandRoles.All), (listedOwner.Email, listedOwner.Name, listedOwner.Roles));
        Assert.Equal(BrandRoles.Marketer, Assert.Single(members, member => member.UserId == marketer.UserId).Roles);
    }

    [Fact]
    public async Task Listing_needs_a_workspace()
    {
        await using var scope = _api.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IBrandMembers>().ListAsync(Token));
    }

    [Fact]
    public async Task Finding_works_for_any_workspace_without_a_workspace_set()
    {
        var owner = await Workspaces.SignInAsync(_api, "Arjun Owner");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var scope = _api.Services.CreateAsyncScope();
        var members = scope.ServiceProvider.GetRequiredService<IBrandMembers>();

        var found = await members.FindAsync(brandId, owner.UserId, Token);

        Assert.Equal(new BrandMember(owner.UserId, owner.Account.Email, "Arjun Owner", BrandRoles.All), found);
        Assert.Null(await members.FindAsync(brandId, Guid.NewGuid(), Token));
        Assert.Null(await members.FindAsync(Guid.NewGuid(), owner.UserId, Token));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<IReadOnlyList<BrandMember>> ListAsync(Guid brandId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IBrandMembers>().ListAsync(Token);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.BrandMembersTests"`
Expected: the build fails with `CS0234: The type or namespace name 'IBrandMembers' does not exist in the namespace 'Kairo.Contracts.Identity'`.

- [ ] **Step 3: Add the contracts**

Create `core/src/Kairo.Contracts/Notifications/INotifications.cs`:

```csharp
using Kairo.Contracts.Live;

namespace Kairo.Contracts.Notifications;

public interface INotifications
{
    Task NotifyAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record Notification(string Kind, LiveAudience Audience, string Title, string Body, string Link);
```

Create `core/src/Kairo.Contracts/Identity/IBrandMembers.cs`:

```csharp
using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Identity;

public interface IBrandMembers
{
    Task<IReadOnlyList<BrandMember>> ListAsync(CancellationToken cancellationToken);

    Task<BrandMember?> FindAsync(Guid brandId, Guid userId, CancellationToken cancellationToken);
}

public sealed record BrandMember(Guid UserId, string Email, string Name, BrandRoles Roles);
```

- [ ] **Step 4: Implement the directory in Identity**

Create `core/src/Modules/Kairo.Modules.Identity/Members/BrandMembers.cs`:

```csharp
using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class BrandMembers(IdentityDbContext db, KairoDatabase database) : IBrandMembers
{
    public async Task<IReadOnlyList<BrandMember>> ListAsync(CancellationToken cancellationToken)
    {
        if (database.BrandId is null)
        {
            throw new InvalidOperationException("Listing members needs a workspace.");
        }

        return await (
                from membership in db.Memberships
                join user in db.Users on membership.UserId equals user.Id
                orderby user.Name
                select new BrandMember(user.Id, user.Email, user.Name, membership.Roles))
            .ToListAsync(cancellationToken);
    }

    public async Task<BrandMember?> FindAsync(Guid brandId, Guid userId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await (
                    from membership in db.Memberships
                    where membership.BrandId == brandId && membership.UserId == userId
                    join user in db.Users on membership.UserId equals user.Id
                    select new BrandMember(user.Id, user.Email, user.Name, membership.Roles))
                .SingleOrDefaultAsync(cancellationToken);
        }
    }
}
```

In `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`, add the registration after `ISessionDirectory`:

```csharp
        services.AddScoped<ISessionDirectory, SessionDirectory>();
        services.AddScoped<IBrandMembers, BrandMembers>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.BrandMembersTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add core/src/Kairo.Contracts/Notifications/INotifications.cs core/src/Kairo.Contracts/Identity/IBrandMembers.cs core/src/Modules/Kairo.Modules.Identity/Members/BrandMembers.cs core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs core/tests/Kairo.IntegrationTests/Notifications/BrandMembersTests.cs
git commit -m "Add the notifications contract and a directory of a workspace's members"
```

---

### Task 2: The kind catalogue, the tables and `NotifyAsync`

**Files:**
- Modify: `core/src/Modules/Kairo.Modules.Notifications/Kairo.Modules.Notifications.csproj`
- Modify: `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Kinds/NotificationKinds.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Data/InAppNotification.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Data/EmailOptOut.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Data/OutboundEmail.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Data/NotificationsDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Data/NotificationsDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Data/Migrations/<timestamp>_Notifications.cs`, plus its `.Designer.cs` and `NotificationsDbContextModelSnapshot.cs` (generated)
- Create: `core/src/Modules/Kairo.Modules.Notifications/Publishing/Notifier.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/NotificationKindsTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/Notifying.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/NotificationRows.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/NotifyTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `INotifications`, `Notification`, `IBrandMembers.ListAsync`, `BrandMember`
  - existing: `ILiveUpdates`, `LiveEvent`, `LiveAudience` (`Includes`, `Users`), `ModuleDbContext`, `IBrandOwned`, `KairoDbContextOptions.CreateForDesignTime`, and the test helper `OutboxRows.ReadAsync` in `Kairo.IntegrationTests.Live`
- Produces:
  - `NotificationKind(string Key, string Name, string Description, BrandRoles UsualRecipients, bool Email, bool CanTurnOff)`
  - `NotificationKinds.All : NotificationKind[]` and `NotificationKinds.Find(string key) : NotificationKind?`
  - `InAppNotification`, with `long Id`, `Guid BrandId`, `Guid UserId`, `string Kind`, `string Title`, `string Body`, `string Link`, `DateTimeOffset CreatedAt` and `DateTimeOffset? ReadAt`
  - `EmailOptOut`, with `Guid BrandId`, `Guid UserId`, `string Kind` and `DateTimeOffset CreatedAt`
  - `EmailStatus { Pending, Sent, Logged, Skipped, Failed }`
  - `OutboundEmail`, with:
    - `long Id`, `Guid BrandId`, `Guid UserId`, `long NotificationId` and `InAppNotification? Notification`
    - `EmailStatus Status`, `int Attempts`, `DateTimeOffset NextAttemptAt`, `DateTimeOffset? ClaimedUntil`
    - `DateTimeOffset? SentAt`, `string? LastError` and `DateTimeOffset CreatedAt`
  - `NotificationsDbContext`, with `SchemaName = "notifications"` and the sets `Notifications`, `EmailOptOuts` and `OutboundEmails`
  - `Notifier : INotifications`
  - the test helpers:
    - `Notifying.NotifyAsync(KairoApiFactory api, Guid brandId, Notification notification)`
    - `Notifying.Sample(LiveAudience audience, string kind = "connection.problem") : Notification`
    - `NotificationRows.NotificationsAsync(string connectionString, Guid brandId) : Task<IReadOnlyList<NotificationRow>>`
    - `NotificationRows.EmailsAsync(string connectionString, Guid brandId) : Task<IReadOnlyList<EmailRow>>`
    - `NotificationRows.ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters) : Task<int>`, run with every workspace visible

- [ ] **Step 1: Write the failing catalogue tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/NotificationKindsTests.cs`:

```csharp
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
```

- [ ] **Step 2: Write the test helpers**

Create `core/tests/Kairo.IntegrationTests/Notifications/Notifying.cs`:

```csharp
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

internal static class Notifying
{
    public static Notification Sample(LiveAudience audience, string kind = "connection.problem") =>
        new(kind, audience, "Google Ads needs reconnecting", "Reconnect it so refreshes and changes can continue.", "/settings/connections");

    public static async Task NotifyAsync(KairoApiFactory api, Guid brandId, Notification notification)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<INotifications>().NotifyAsync(notification, TestContext.Current.CancellationToken);
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Notifications/NotificationRows.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Notifications;

internal sealed record NotificationRow(long Id, Guid UserId, string Kind, string Title, string Body, string Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal sealed record EmailRow(
    long Id,
    Guid UserId,
    long NotificationId,
    string Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? ClaimedUntil,
    DateTimeOffset? SentAt,
    string? LastError);

internal static class NotificationRows
{
    public static async Task<IReadOnlyList<NotificationRow>> NotificationsAsync(string connectionString, Guid brandId)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, UserId, Kind, Title, Body, Link, CreatedAt, ReadAt
            FROM notifications.Notifications
            WHERE BrandId = @brand
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var rows = new List<NotificationRow>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new NotificationRow(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<EmailRow>> EmailsAsync(string connectionString, Guid brandId)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, UserId, NotificationId, Status, Attempts, NextAttemptAt, ClaimedUntil, SentAt, LastError
            FROM notifications.OutboundEmails
            WHERE BrandId = @brand
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var rows = new List<EmailRow>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new EmailRow(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    public static async Task<int> ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SqlConnection> OpenAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = connection.CreateCommand();
        context.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;";
        await context.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
```

- [ ] **Step 3: Write the failing notify tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/NotifyTests.cs`:

```csharp
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
        Assert.Equal([marketer.UserId], JsonSerializer.Deserialize<Guid[]>(created.AudienceUsers!));
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
```

- [ ] **Step 4: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.NotifyTests"`
Expected: the build fails with `CS0234: The type or namespace name 'Kinds' does not exist in the namespace 'Kairo.Modules.Notifications'`, from `NotificationKindsTests.cs`.

- [ ] **Step 5: Let the tests see the module's internals**

Replace `core/src/Modules/Kairo.Modules.Notifications/Kairo.Modules.Notifications.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 6: Add the kind catalogue**

Create `core/src/Modules/Kairo.Modules.Notifications/Kinds/NotificationKinds.cs`:

```csharp
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
```

- [ ] **Step 7: Add the tables**

Create `core/src/Modules/Kairo.Modules.Notifications/Data/InAppNotification.cs`:

```csharp
namespace Kairo.Modules.Notifications.Data;

internal sealed class InAppNotification : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public string Link { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Data/EmailOptOut.cs`:

```csharp
namespace Kairo.Modules.Notifications.Data;

internal sealed class EmailOptOut : IBrandOwned
{
    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Data/OutboundEmail.cs`:

```csharp
namespace Kairo.Modules.Notifications.Data;

internal enum EmailStatus
{
    Pending,
    Sent,
    Logged,
    Skipped,
    Failed,
}

internal sealed class OutboundEmail : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public long NotificationId { get; set; }

    public InAppNotification? Notification { get; set; }

    public EmailStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ClaimedUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Data/NotificationsDbContext.cs`:

```csharp
namespace Kairo.Modules.Notifications.Data;

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "notifications";

    public DbSet<InAppNotification> Notifications => Set<InAppNotification>();

    public DbSet<EmailOptOut> EmailOptOuts => Set<EmailOptOut>();

    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InAppNotification>(notification =>
        {
            notification.ToTable("Notifications");
            notification.HasIndex(n => new { n.BrandId, n.UserId, n.Id }).IsDescending(false, false, true);
            notification.HasIndex(n => new { n.BrandId, n.UserId })
                .HasFilter("[ReadAt] IS NULL")
                .HasDatabaseName("IX_Notifications_Unread");
            notification.Property(n => n.Kind).HasMaxLength(64);
            notification.Property(n => n.Title).HasMaxLength(200);
            notification.Property(n => n.Body).HasMaxLength(1000);
            notification.Property(n => n.Link).HasMaxLength(400);
        });

        modelBuilder.Entity<EmailOptOut>(optOut =>
        {
            optOut.ToTable("EmailOptOuts");
            optOut.HasKey(o => new { o.BrandId, o.UserId, o.Kind });
            optOut.Property(o => o.Kind).HasMaxLength(64);
        });

        modelBuilder.Entity<OutboundEmail>(email =>
        {
            email.ToTable("OutboundEmails");
            email.HasIndex(e => new { e.Status, e.NextAttemptAt });
            email.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            email.Property(e => e.LastError).HasMaxLength(500);
            email.HasOne(e => e.Notification).WithMany()
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Data/NotificationsDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Notifications.Data;

internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<NotificationsDbContext>(
            NotificationsDbContext.SchemaName,
            (options, database) => new NotificationsDbContext(options, database));
}
```

- [ ] **Step 8: Add `NotifyAsync`**

Create `core/src/Modules/Kairo.Modules.Notifications/Publishing/Notifier.cs`:

```csharp
using Kairo.Contracts.Identity;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;

namespace Kairo.Modules.Notifications.Publishing;

internal sealed class Notifier(
    NotificationsDbContext db,
    KairoDatabase database,
    IBrandMembers members,
    ILiveUpdates live,
    TimeProvider time) : INotifications
{
    public async Task NotifyAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (database.BrandId is null)
        {
            throw new InvalidOperationException("Notifying needs a workspace.");
        }

        var kind = Validate(notification);
        var recipients = (await members.ListAsync(cancellationToken))
            .Where(member => notification.Audience.Includes(member.UserId, member.Roles))
            .Select(member => member.UserId)
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        List<Guid> optedOut = kind.CanTurnOff
            ? await db.EmailOptOuts
                .Where(optOut => optOut.Kind == kind.Key && recipients.Contains(optOut.UserId))
                .Select(optOut => optOut.UserId)
                .ToListAsync(cancellationToken)
            : [];

        var now = time.GetUtcNow();
        foreach (var userId in recipients)
        {
            var row = new InAppNotification
            {
                UserId = userId,
                Kind = kind.Key,
                Title = notification.Title,
                Body = notification.Body,
                Link = notification.Link,
                CreatedAt = now,
            };
            db.Notifications.Add(row);
            if (kind.Email && !optedOut.Contains(userId))
            {
                db.OutboundEmails.Add(new OutboundEmail
                {
                    UserId = userId,
                    Notification = row,
                    Status = EmailStatus.Pending,
                    NextAttemptAt = now,
                    CreatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(
            new LiveEvent("notification.created", new { kind = kind.Key }, LiveAudience.Users([.. recipients])),
            cancellationToken);
    }

    private static NotificationKind Validate(Notification notification)
    {
        var kind = NotificationKinds.Find(notification.Kind)
            ?? throw new ArgumentException($"'{notification.Kind}' isn't a notification kind.", nameof(notification));
        if (string.IsNullOrWhiteSpace(notification.Title) || notification.Title.Length > 200)
        {
            throw new ArgumentException("A notification needs a title of up to 200 characters.", nameof(notification));
        }

        if (notification.Body.Length > 1000)
        {
            throw new ArgumentException("A notification's body can be up to 1,000 characters.", nameof(notification));
        }

        if (!notification.Link.StartsWith('/') || notification.Link.Length > 400)
        {
            throw new ArgumentException("A notification's link is a path inside the workspace, starting with '/', of up to 400 characters.", nameof(notification));
        }

        return kind;
    }
}
```

Replace `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs` with:

```csharp
using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Publishing;

namespace Kairo.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, NotificationsDbContext.SchemaName));
        services.AddScoped<INotifications, Notifier>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 9: Generate the migration and add row-level security**

Run:

```bash
dotnet tool restore
dotnet ef migrations add Notifications --project core/src/Modules/Kairo.Modules.Notifications --startup-project core/src/Kairo.Migrator --context NotificationsDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Notifications/Data/Migrations/*.cs
```

Open the generated `core/src/Modules/Kairo.Modules.Notifications/Data/Migrations/*_Notifications.cs`.

1. **Check the tables.** `Up` must create, in schema `notifications`:
   - `Notifications`:
     - `Id`: `bigint`, identity
     - `BrandId` and `UserId`: `uniqueidentifier`
     - `Kind`: `nvarchar(64)`
     - `Title`: `nvarchar(200)`
     - `Body`: `nvarchar(1000)`
     - `Link`: `nvarchar(400)`
     - `CreatedAt`: `datetimeoffset`
     - `ReadAt`: `datetimeoffset`, nullable
   - `EmailOptOuts`, with primary key `(BrandId, UserId, Kind)` and `Kind` as `nvarchar(64)`.
   - `OutboundEmails`:
     - `Id`: `bigint`, identity
     - `Status`: `nvarchar(20)`
     - `LastError`: `nvarchar(500)`, nullable
     - `ClaimedUntil` and `SentAt`: nullable
     - a foreign key from `NotificationId` to `Notifications` with cascade delete
   - The indexes:
     - `IX_Notifications_BrandId_UserId_Id`, with `descending: new[] { false, false, true }`
     - `IX_Notifications_Unread`, with `filter: "[ReadAt] IS NULL"`
     - `IX_OutboundEmails_Status_NextAttemptAt`
     - `IX_OutboundEmails_NotificationId`
2. **Add row-level security.** Add these as the last four lines of `Up`:

   ```csharp
               migrationBuilder.CreateBrandAccessPredicate("notifications");
               migrationBuilder.AddBrandSeparation("notifications", "Notifications");
               migrationBuilder.AddBrandSeparation("notifications", "EmailOptOuts");
               migrationBuilder.AddBrandSeparation("notifications", "OutboundEmails");
   ```
3. **Undo it in `Down`.** Add this as the first line of `Down`:

   ```csharp
               migrationBuilder.DropBrandSeparation("notifications");
   ```

- [ ] **Step 10: Run the tests**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.NotificationKindsTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.NotifyTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.SecurityPolicyCoverageTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: every test passes. That includes 4 catalogue tests and 16 notify tests, 7 of them the bad-input theory. `Every_table_with_a_brand_column_has_row_level_security` passes with the three new tables, and `Module_tables_live_in_the_module_schema` passes for `Kairo.Modules.Notifications`.

- [ ] **Step 11: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Notifications core/tests/Kairo.IntegrationTests/Notifications/NotificationKindsTests.cs core/tests/Kairo.IntegrationTests/Notifications/Notifying.cs core/tests/Kairo.IntegrationTests/Notifications/NotificationRows.cs core/tests/Kairo.IntegrationTests/Notifications/NotifyTests.cs
git commit -m "Store notifications for each recipient and queue their emails in the caller's transaction"
```

---

### Task 3: The inbox API

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Notifications/Inbox/InboxEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`
- Modify: `core/src/Kairo.Api/openapi.json` (regenerated)
- Modify: `web/src/lib/core-api/schema.ts` (regenerated)
- Test: `core/tests/Kairo.IntegrationTests/Notifications/InboxTests.cs`

**Interfaces:**
- Consumes:
  - from Task 2: `NotificationsDbContext.Notifications`, `InAppNotification`, and the test helpers `Notifying` and `NotificationRows`
  - existing: `MapBrandApi`, `CurrentMember`, `ILiveUpdates`, and the test helper `OutboxRows.ReadAsync`
- Produces:
  - `GET /api/brands/{brandId}/notifications?before={id}&limit={n}`, returning `NotificationPage(IReadOnlyList<NotificationItem> Items, int UnreadCount, long? NextBefore)`, with `NotificationItem(long Id, string Kind, string Title, string Body, string Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt)`
  - `POST /api/brands/{brandId}/notifications/{id}/read`, returning 204 or 404
  - `POST /api/brands/{brandId}/notifications/read-all`, returning 204
  - the live event `notifications.read`, with `{}`, to `LiveAudience.Users(userId)`

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/InboxTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.Notifications;

public sealed class InboxTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_list_is_newest_first_and_pages_with_before()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 3);

        var first = await ListAsync(owner.Browser, brandId, "?limit=2");
        var ids = Ids(first);
        Assert.Equal(2, ids.Length);
        Assert.True(ids[0] > ids[1]);
        Assert.Equal(ids[1], first.GetProperty("nextBefore").GetInt64());
        Assert.Equal(3, first.GetProperty("unreadCount").GetInt32());

        var second = await ListAsync(owner.Browser, brandId, $"?limit=2&before={ids[1]}");
        var rest = Assert.Single(Ids(second));
        Assert.True(rest < ids[1]);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task Each_item_carries_what_the_screen_shows()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 1);

        var item = (await ListAsync(owner.Browser, brandId)).GetProperty("items")[0];

        Assert.Equal("connection.problem", item.GetProperty("kind").GetString());
        Assert.Equal("Google Ads needs reconnecting", item.GetProperty("title").GetString());
        Assert.Equal("Reconnect it so refreshes and changes can continue.", item.GetProperty("body").GetString());
        Assert.Equal("/settings/connections", item.GetProperty("link").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), item.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
    }

    [Fact]
    public async Task A_limit_outside_1_to_100_is_brought_into_range()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 2);

        Assert.Single(Ids(await ListAsync(owner.Browser, brandId, "?limit=0")));
        Assert.Equal(2, Ids(await ListAsync(owner.Browser, brandId, "?limit=500")).Length);
    }

    [Fact]
    public async Task Reading_one_marks_it_read_once_and_tells_the_reader_s_other_tabs()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 2);
        var id = Ids(await ListAsync(owner.Browser, brandId))[0];

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Browser.PostAsync($"/api/brands/{brandId}/notifications/{id}/read", null, Token)).StatusCode);
        var readAt = _api.Clock.GetUtcNow();
        _api.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Browser.PostAsync($"/api/brands/{brandId}/notifications/{id}/read", null, Token)).StatusCode);

        var list = await ListAsync(owner.Browser, brandId);
        Assert.Equal(1, list.GetProperty("unreadCount").GetInt32());
        Assert.Equal(readAt, list.GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset());
        var read = Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notifications.read");
        Assert.Equal("{}", read.Payload);
        Assert.Equal([owner.UserId], JsonSerializer.Deserialize<Guid[]>(read.AudienceUsers!));
    }

    [Fact]
    public async Task Reading_all_marks_every_one_read_and_tells_the_reader_s_other_tabs()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 3);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Browser.PostAsync($"/api/brands/{brandId}/notifications/read-all", null, Token)).StatusCode);

        var list = await ListAsync(owner.Browser, brandId);
        Assert.Equal(0, list.GetProperty("unreadCount").GetInt32());
        Assert.All(list.GetProperty("items").EnumerateArray(), item => Assert.Equal(JsonValueKind.String, item.GetProperty("readAt").ValueKind));
        Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId), row => row.Type == "notifications.read");
    }

    [Fact]
    public async Task Another_member_s_notification_is_not_found_and_stays_unread()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await NotifyOwnerAsync(owner, brandId, 1);
        var id = Ids(await ListAsync(owner.Browser, brandId))[0];

        var response = await marketer.Browser.PostAsync($"/api/brands/{brandId}/notifications/{id}/read", null, Token);
        (await marketer.Browser.PostAsync($"/api/brands/{brandId}/notifications/read-all", null, Token)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Ids(await ListAsync(marketer.Browser, brandId)));
        Assert.Equal(1, (await ListAsync(owner.Browser, brandId)).GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Another_workspace_s_notifications_never_appear()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var otherBrandId = await Workspaces.CreateAsync(owner.Browser);
        await NotifyOwnerAsync(owner, brandId, 1);
        var id = Ids(await ListAsync(owner.Browser, brandId))[0];

        var other = await ListAsync(owner.Browser, otherBrandId);
        var response = await owner.Browser.PostAsync($"/api/brands/{otherBrandId}/notifications/{id}/read", null, Token);

        Assert.Empty(Ids(other));
        Assert.Equal(0, other.GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Only_signed_in_members_reach_the_inbox()
    {
        var (_, brandId) = await WorkspaceAsync();
        var outsider = await Workspaces.SignInAsync(_api);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateBrowser().GetAsync($"/api/brands/{brandId}/notifications", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync($"/api/brands/{brandId}/notifications", Token)).StatusCode);
    }

    [Fact]
    public async Task Marking_read_needs_the_request_header()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await NotifyOwnerAsync(owner, brandId, 1);
        var bare = _api.CreateBrowser(withRequestHeader: false);
        await _api.SignInAsync(bare, owner.Account, "/");

        var response = await bare.PostAsync($"/api/brands/{brandId}/notifications/read-all", null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, (await ListAsync(owner.Browser, brandId)).GetProperty("unreadCount").GetInt32());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser));
    }

    private async Task NotifyOwnerAsync(Member owner, Guid brandId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        }
    }

    private static Task<JsonElement> ListAsync(HttpClient browser, Guid brandId, string query = "") =>
        browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/notifications{query}", Token);

    private static long[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToArray();
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.InboxTests"`
Expected: FAIL. Most tests fail with `System.Net.Http.HttpRequestException: Response status code does not indicate success: 404 (Not Found)`, because the routes don't exist yet.

- [ ] **Step 3: Add the endpoints**

Create `core/src/Modules/Kairo.Modules.Notifications/Inbox/InboxEndpoints.cs`:

```csharp
using Kairo.Contracts.Live;
using Kairo.Modules.Notifications.Data;

namespace Kairo.Modules.Notifications.Inbox;

internal static class InboxEndpoints
{
    public static void MapInbox(this IEndpointRouteBuilder api)
    {
        var inbox = api.MapBrandApi().MapGroup("/notifications");
        inbox.MapGet("", ListAsync);
        inbox.MapPost("/{id:long}/read", ReadAsync);
        inbox.MapPost("/read-all", ReadAllAsync);
    }

    private static async Task<Ok<NotificationPage>> ListAsync(
        long? before,
        int? limit,
        CurrentMember member,
        NotificationsDbContext db,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 30, 1, 100);
        var mine = db.Notifications.AsNoTracking().Where(notification => notification.UserId == member.UserId);
        var page = before is { } cursor ? mine.Where(notification => notification.Id < cursor) : mine;
        var rows = await page
            .OrderByDescending(notification => notification.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var items = rows.Take(take)
            .Select(row => new NotificationItem(row.Id, row.Kind, row.Title, row.Body, row.Link, row.CreatedAt, row.ReadAt))
            .ToList();
        var unread = await mine.CountAsync(notification => notification.ReadAt == null, cancellationToken);
        return TypedResults.Ok(new NotificationPage(items, unread, rows.Count > take ? items[^1].Id : null));
    }

    private static async Task<Results<NoContent, NotFound>> ReadAsync(
        long id,
        CurrentMember member,
        NotificationsDbContext db,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(
            row => row.Id == id && row.UserId == member.UserId,
            cancellationToken);
        if (notification is null)
        {
            return TypedResults.NotFound();
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(ReadEvent(member.UserId), cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> ReadAllAsync(
        CurrentMember member,
        NotificationsDbContext db,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? now = time.GetUtcNow();
        var changed = await db.Notifications
            .Where(notification => notification.UserId == member.UserId && notification.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAt, now), cancellationToken);
        if (changed > 0)
        {
            await live.PublishAsync(ReadEvent(member.UserId), cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static LiveEvent ReadEvent(Guid userId) => new("notifications.read", new { }, LiveAudience.Users(userId));
}

internal sealed record NotificationItem(
    long Id,
    string Kind,
    string Title,
    string Body,
    string Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

internal sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, int UnreadCount, long? NextBefore);
```

In `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`, add `using Kairo.Modules.Notifications.Inbox;` to the usings, and map the endpoints:

```csharp
    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapInbox();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.InboxTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Regenerate the OpenAPI document and the web types**

Run:

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web ci
npm --prefix web run generate:api
```

Expected:
- Both test runs pass.
- `git diff --stat` shows `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts`.
- `openapi.json` has the three new paths, `/api/brands/{brandId}/notifications`, `/api/brands/{brandId}/notifications/{id}/read` and `/api/brands/{brandId}/notifications/read-all`, and the schemas `NotificationPage` and `NotificationItem`.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Notifications/Inbox/InboxEndpoints.cs core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts core/tests/Kairo.IntegrationTests/Notifications/InboxTests.cs
git commit -m "Let each member list their notifications and mark them read"
```

---

### Task 4: The email settings API

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Notifications/EmailSettings/EmailSettingsEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`
- Modify: `core/src/Kairo.Api/openapi.json` (regenerated)
- Modify: `web/src/lib/core-api/schema.ts` (regenerated)
- Test: `core/tests/Kairo.IntegrationTests/Notifications/EmailSettingsTests.cs`

**Interfaces:**
- Consumes:
  - from Task 2: `NotificationKinds`, `NotificationsDbContext.EmailOptOuts`, `EmailOptOut`, and the test helpers
  - existing: `IAuditLog`, `AuditRecord`, `CurrentMember`, and the test helper `Workspaces.AuditActionsAsync` and `Workspaces.LatestAuditEntryAsync`
- Produces:
  - `GET /api/brands/{brandId}/notifications/email-settings`, returning `EmailSettings(IReadOnlyList<EmailSetting> Kinds)`, with `EmailSetting(string Kind, string Name, string Description, bool Email, bool CanTurnOff)`
  - `PUT /api/brands/{brandId}/notifications/email-settings`, taking `SaveEmailSettingsRequest(IReadOnlyList<EmailSettingChange>? Kinds)`, with `EmailSettingChange(string Kind, bool Email)`. It returns `EmailSettings`, or a 400 validation problem.
  - the audit action `notifications.email_settings_changed`, with target type `user` and the user's ID

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/EmailSettingsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailSettingsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Settings_list_only_the_email_kinds_a_member_s_roles_usually_receive()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        var forMarketer = Kinds(await GetAsync(marketer.Browser, brandId));
        var forApprover = Kinds(await GetAsync(approver.Browser, brandId));
        var forOwner = Kinds(await GetAsync(owner.Browser, brandId));

        Assert.Contains("trigger.fired", forMarketer);
        Assert.DoesNotContain("connection.problem", forMarketer);
        Assert.DoesNotContain("approval.needed", forMarketer);
        Assert.Equal(new[] { "approval.needed", "pacing.alert", "report.ready" }, forApprover.Order());
        Assert.Contains("connection.problem", forOwner);
        Assert.DoesNotContain("stock.pause_changed", forOwner);
        Assert.DoesNotContain("impact_lab.ready", forOwner);
        Assert.Equal(11, forOwner.Length);
    }

    [Fact]
    public async Task Each_kind_says_whether_it_emails_and_whether_it_can_be_turned_off()
    {
        var (owner, brandId) = await WorkspaceAsync();

        var settings = (await GetAsync(owner.Browser, brandId)).GetProperty("kinds").EnumerateArray().ToList();

        var connection = settings.Single(kind => kind.GetProperty("kind").GetString() == "connection.problem");
        Assert.Equal("Connection or data problem", connection.GetProperty("name").GetString());
        Assert.Equal("A connected account needs reconnecting, or a data feed failed.", connection.GetProperty("description").GetString());
        Assert.True(connection.GetProperty("email").GetBoolean());
        Assert.True(connection.GetProperty("canTurnOff").GetBoolean());
        var approval = settings.Single(kind => kind.GetProperty("kind").GetString() == "approval.needed");
        Assert.False(approval.GetProperty("canTurnOff").GetBoolean());
    }

    [Fact]
    public async Task Turning_a_kind_off_stops_its_emails_but_not_its_notifications()
    {
        var (owner, brandId) = await WorkspaceAsync();

        var saved = await PutAsync(owner.Browser, brandId, ("connection.problem", false));
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.False(EmailOn(await saved.Content.ReadFromJsonAsync<JsonElement>(Token), "connection.problem"));
        Assert.False(EmailOn(await GetAsync(owner.Browser, brandId), "connection.problem"));
        Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Empty(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task Turning_a_kind_back_on_resumes_its_emails()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await PutAsync(owner.Browser, brandId, ("connection.problem", false));

        (await PutAsync(owner.Browser, brandId, ("connection.problem", true))).EnsureSuccessStatusCode();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.True(EmailOn(await GetAsync(owner.Browser, brandId), "connection.problem"));
        Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task One_member_turning_a_kind_off_leaves_the_others_emails_on()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var admin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");

        await PutAsync(admin.Browser, brandId, ("connection.problem", false));
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Roles(BrandRoles.Admin)));

        var email = Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal(owner.UserId, email.UserId);
    }

    [Fact]
    public async Task Email_settings_belong_to_one_workspace()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var otherBrandId = await Workspaces.CreateAsync(owner.Browser);

        await PutAsync(owner.Browser, brandId, ("connection.problem", false));
        await Notifying.NotifyAsync(_api, otherBrandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.True(EmailOn(await GetAsync(owner.Browser, otherBrandId), "connection.problem"));
        Assert.Single(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, otherBrandId));
    }

    [Theory]
    [InlineData("approval.needed", false)]
    [InlineData("launch.failed", false)]
    [InlineData("stock.pause_changed", false)]
    [InlineData("impact_lab.ready", true)]
    [InlineData("connection.unknown", false)]
    public async Task Required_in_app_only_and_unknown_kinds_are_refused(string kind, bool email)
    {
        var (owner, brandId) = await WorkspaceAsync();

        var response = await PutAsync(owner.Browser, brandId, ("connection.problem", false), (kind, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(EmailOn(await GetAsync(owner.Browser, brandId), "connection.problem"));
    }

    [Fact]
    public async Task A_required_kind_can_be_saved_as_on()
    {
        var (owner, brandId) = await WorkspaceAsync();

        var response = await PutAsync(owner.Browser, brandId, ("approval.needed", true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_change_is_audited_with_before_and_after()
    {
        var (owner, brandId) = await WorkspaceAsync();

        await PutAsync(owner.Browser, brandId, ("connection.problem", false));

        var entry = await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId);
        Assert.Equal("notifications.email_settings_changed", entry.GetProperty("action").GetString());
        Assert.Equal("user", entry.GetProperty("targetType").GetString());
        Assert.Equal(owner.UserId.ToString(), entry.GetProperty("targetId").GetString());
        Assert.True(EmailOn(entry.GetProperty("before"), "connection.problem"));
        Assert.False(EmailOn(entry.GetProperty("after"), "connection.problem"));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser));
    }

    private static Task<JsonElement> GetAsync(HttpClient browser, Guid brandId) =>
        browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/notifications/email-settings", Token);

    private static Task<HttpResponseMessage> PutAsync(HttpClient browser, Guid brandId, params (string Kind, bool Email)[] changes) =>
        browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/notifications/email-settings",
            new { kinds = changes.Select(change => new { kind = change.Kind, email = change.Email }) },
            Token);

    private static string[] Kinds(JsonElement settings) =>
        settings.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetProperty("kind").GetString()!).ToArray();

    private static bool EmailOn(JsonElement settings, string kind) =>
        settings.GetProperty("kinds").EnumerateArray().Single(row => row.GetProperty("kind").GetString() == kind).GetProperty("email").GetBoolean();
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailSettingsTests"`
Expected: FAIL. The `GET` tests fail with `System.Net.Http.HttpRequestException: Response status code does not indicate success: 404 (Not Found)`.

- [ ] **Step 3: Add the endpoints**

Create `core/src/Modules/Kairo.Modules.Notifications/EmailSettings/EmailSettingsEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;

namespace Kairo.Modules.Notifications.EmailSettings;

internal static class EmailSettingsEndpoints
{
    public static void MapEmailSettings(this IEndpointRouteBuilder api)
    {
        var settings = api.MapBrandApi().MapGroup("/notifications/email-settings");
        settings.MapGet("", GetAsync);
        settings.MapPut("", SaveAsync);
    }

    private static async Task<Ok<EmailSettings>> GetAsync(
        CurrentMember member,
        NotificationsDbContext db,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await LoadAsync(member, db, cancellationToken));

    private static async Task<Results<Ok<EmailSettings>, ValidationProblem>> SaveAsync(
        SaveEmailSettingsRequest request,
        CurrentMember member,
        NotificationsDbContext db,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var changes = request.Kinds ?? [];
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < changes.Count; i++)
        {
            var kind = NotificationKinds.Find(changes[i].Kind);
            if (kind is null)
            {
                errors[$"kinds[{i}].kind"] = [$"'{changes[i].Kind}' isn't a notification kind."];
            }
            else if (!kind.Email)
            {
                errors[$"kinds[{i}].kind"] = [$"{kind.Name} is shown only in the app, so it has no email to turn on or off."];
            }
            else if (!kind.CanTurnOff && !changes[i].Email)
            {
                errors[$"kinds[{i}].email"] = [$"{kind.Name} emails can't be turned off."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var before = await LoadAsync(member, db, cancellationToken);
        var optOuts = await db.EmailOptOuts
            .Where(optOut => optOut.UserId == member.UserId)
            .ToListAsync(cancellationToken);
        foreach (var change in changes.GroupBy(change => change.Kind).Select(group => group.Last()))
        {
            var existing = optOuts.SingleOrDefault(optOut => optOut.Kind == change.Kind);
            if (change.Email && existing is not null)
            {
                db.EmailOptOuts.Remove(existing);
            }
            else if (!change.Email && existing is null)
            {
                db.EmailOptOuts.Add(new EmailOptOut { UserId = member.UserId, Kind = change.Kind, CreatedAt = time.GetUtcNow() });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var after = await LoadAsync(member, db, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("notifications.email_settings_changed", "user", member.UserId.ToString(), before, after),
            cancellationToken);
        return TypedResults.Ok(after);
    }

    private static async Task<EmailSettings> LoadAsync(CurrentMember member, NotificationsDbContext db, CancellationToken cancellationToken)
    {
        var off = (await db.EmailOptOuts
                .AsNoTracking()
                .Where(optOut => optOut.UserId == member.UserId)
                .Select(optOut => optOut.Kind)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var kinds = NotificationKinds.All
            .Where(kind => kind.Email && member.Has(kind.UsualRecipients))
            .Select(kind => new EmailSetting(kind.Key, kind.Name, kind.Description, !kind.CanTurnOff || !off.Contains(kind.Key), kind.CanTurnOff))
            .ToList();
        return new EmailSettings(kinds);
    }
}

internal sealed record EmailSetting(string Kind, string Name, string Description, bool Email, bool CanTurnOff);

internal sealed record EmailSettings(IReadOnlyList<EmailSetting> Kinds);

internal sealed record EmailSettingChange(string Kind, bool Email);

internal sealed record SaveEmailSettingsRequest(IReadOnlyList<EmailSettingChange>? Kinds);
```

In `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`, add `using Kairo.Modules.Notifications.EmailSettings;` to the usings, and map the endpoints:

```csharp
    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapInbox();
        api.MapEmailSettings();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailSettingsTests"`
Expected: PASS, 13 tests, 5 of them the refused-kinds theory.

- [ ] **Step 5: Run the inbox tests again**

The settings group, `/notifications/email-settings`, sits beside the inbox's `/notifications/{id:long}/read`. Make sure neither shadows the other.

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.InboxTests"`
Expected: PASS, 9 tests.

- [ ] **Step 6: Regenerate the OpenAPI document and the web types**

Run:

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
```

Expected: both test runs pass. `openapi.json` gains `/api/brands/{brandId}/notifications/email-settings` with `get` and `put`, and the schemas `EmailSettings`, `EmailSetting`, `SaveEmailSettingsRequest` and `EmailSettingChange`.

- [ ] **Step 7: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Notifications/EmailSettings/EmailSettingsEndpoints.cs core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts core/tests/Kairo.IntegrationTests/Notifications/EmailSettingsTests.cs
git commit -m "Let each member turn notification emails on and off for a workspace"
```

---

### Task 5: The email layout and the senders

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/src/Modules/Kairo.Modules.Notifications/Kairo.Modules.Notifications.csproj`
- Modify: `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/NotificationSettings.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailMessage.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailTemplate.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/LogEmailSender.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/SenderToken.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/GmailSender.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/EmailTemplateTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/GmailSenderTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `BrandMember`
  - from Task 2: `InAppNotification`, `NotificationKind`, `NotificationKinds.Find`
  - existing: `BrandSummary(Guid Id, string Handle, string Name)` from `Kairo.Contracts.Brands`, the `kairo-google` named `HttpClient` that the tests route to `FakeGoogleApis`, and `Authentication:Google:ClientId` and `ClientSecret`
- Produces:
  - `NotificationSettings`, with:
    - the keys `Sender = "Kairo:Notifications:Sender"`, `SenderRefreshToken = "Kairo:Notifications:SenderRefreshToken"`, `DeliveryLoop = "Kairo:Notifications:DeliveryLoop"` and `PublicOrigin = "Kairo:PublicOrigin"`
    - `HasSender(IConfiguration) : bool`
  - `EmailMessage(Guid UserId, string Kind, string To, string ToName, string Subject, string Html, string Text)`
  - `SendResult { Sent, Logged }`
  - `IEmailSender.SendAsync(EmailMessage message, CancellationToken cancellationToken) : Task<SendResult>`
  - `EmailSendException(string message, bool mayPass)`, with `bool MayPass`
  - `EmailTemplate.Render(InAppNotification notification, NotificationKind kind, BrandSummary workspace, BrandMember member, string publicOrigin) : EmailMessage`
  - `GmailSender.Build(EmailMessage message, string sender) : string`, the base64url MIME message
  - `SenderToken`, with `GetAsync(CancellationToken) : Task<string>` and `Forget()`
  - `IEmailSender` registered as a singleton: `GmailSender` when both sender settings are set, otherwise `LogEmailSender`
  - for the tests:
    - `KairoApiFactory(string connectionString, Action<IServiceCollection>? configureServices = null, IReadOnlyDictionary<string, string>? settings = null)`
    - `FakeGoogleApis.AllowRefreshToken(string refreshToken)`
    - `FakeGoogleApis.GmailSend` (`HttpStatusCode`, `OK` by default)
    - `FakeGoogleApis.SentMail : IReadOnlyList<MimeMessage>`

- [ ] **Step 1: Add MimeKit**

In `core/Directory.Packages.props`, add a group after the `Google` group:

```xml
  <ItemGroup Label="Email">
    <PackageVersion Include="MimeKit" Version="4.18.1" />
  </ItemGroup>
```

Replace `core/src/Modules/Kairo.Modules.Notifications/Kairo.Modules.Notifications.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="MimeKit" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

Run: `dotnet restore core/Kairo.slnx`
Expected: it restores with no `NU1902`, `NU1903` or `NU1904` warning for MimeKit or `BouncyCastle.Cryptography`.

- [ ] **Step 2: Teach the fake Google APIs to send mail**

In `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`:

1. Add `using MimeKit;` to the usings.
2. Add these members after the `BigQueryJobs` property:

   ```csharp
       public HttpStatusCode GmailSend { get; set; } = HttpStatusCode.OK;

       public ConcurrentQueue<string> GmailMessages { get; } = new();

       public IReadOnlyList<MimeMessage> SentMail =>
           GmailMessages.Select(raw => MimeMessage.Load(new MemoryStream(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/'))))).ToList();
   ```

3. Add this method after `Reject`:

   ```csharp
       public void AllowRefreshToken(string refreshToken) => _refreshTokens[refreshToken] = "sender";
   ```

4. In `Product`, add this arm before the final `_ =>` arm:

   ```csharp
               ("gmail.googleapis.com", "/gmail/v1/users/me/messages/send") => SendMail(body),
   ```

5. Add this method after `Product`:

   ```csharp
       private HttpResponseMessage SendMail(string body)
       {
           if (GmailSend != HttpStatusCode.OK)
           {
               return Json(GmailSend, new { error = new { code = (int)GmailSend, message = "Refused by the fake." } });
           }

           using var request = JsonDocument.Parse(body);
           GmailMessages.Enqueue(request.RootElement.GetProperty("raw").GetString()!);
           return Json(HttpStatusCode.OK, new { id = $"message-{GmailMessages.Count}", threadId = "thread" });
       }
   ```

- [ ] **Step 3: Let a test host take extra settings**

In `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`, change the class declaration to:

```csharp
public sealed class KairoApiFactory(
    string connectionString,
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
```

and, in `ConfigureWebHost`, add this loop right after `builder.UseSetting("Logging:LogLevel:Default", "Warning");`:

```csharp
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
```

- [ ] **Step 4: Write the failing layout tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/EmailTemplateTests.cs`:

```csharp
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
```

- [ ] **Step 5: Write the failing sender tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/GmailSenderTests.cs`:

```csharp
using System.Net;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Notifications.Delivery;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Kairo.IntegrationTests.Notifications;

public sealed class GmailSenderTests(SqlServer sqlServer) : IAsyncDisposable
{
    public const string Sender = "khanmohdtalha3250@gmail.com";
    public const string SenderRefreshToken = "sender-refresh-token";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString, settings: SenderSettings);

    public static IReadOnlyDictionary<string, string> SenderSettings { get; } = new Dictionary<string, string>
    {
        ["Kairo:Notifications:Sender"] = Sender,
        ["Kairo:Notifications:SenderRefreshToken"] = SenderRefreshToken,
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_email_goes_through_Gmail_from_the_sender_mailbox()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        var result = await SendAsync(Message());

        Assert.Equal(SendResult.Sent, result);
        var mail = Assert.Single(_api.GoogleApis.SentMail);
        var from = Assert.Single(mail.From.Mailboxes);
        var to = Assert.Single(mail.To.Mailboxes);
        Assert.Equal(("Kairo", Sender), (from.Name, from.Address));
        Assert.Equal(("Priya Shah", "priya@acme.example"), (to.Name, to.Address));
        Assert.Equal("Google Ads needs reconnecting", mail.Subject);
        Assert.Equal("Reconnect Google Ads.", mail.TextBody.Trim());
        Assert.Equal("<p>Reconnect Google Ads.</p>", mail.HtmlBody.Trim());
    }

    [Fact]
    public async Task Markup_and_non_english_text_arrive_as_plain_text()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        await SendAsync(Message() with { Subject = "<b>Pacing</b> ₹ अलर्ट", Text = "बजट ₹ <b>" });

        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal("<b>Pacing</b> ₹ अलर्ट", mail.Subject);
        Assert.Equal("बजट ₹ <b>", mail.TextBody.Trim());
    }

    [Fact]
    public async Task The_access_token_is_reused_until_shortly_before_it_expires()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        await SendAsync(Message());
        _api.Clock.Advance(TimeSpan.FromMinutes(58));
        await SendAsync(Message());

        Assert.Equal(1, _api.GoogleApis.Refreshes);
        Assert.Equal(2, _api.GoogleApis.SentMail.Count);
    }

    [Fact]
    public async Task An_expired_access_token_is_refreshed_before_sending()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);

        await SendAsync(Message());
        _api.Clock.Advance(TimeSpan.FromMinutes(60));
        var result = await SendAsync(Message());

        Assert.Equal(SendResult.Sent, result);
        Assert.Equal(2, _api.GoogleApis.Refreshes);
        Assert.Equal(2, _api.GoogleApis.SentMail.Count);
    }

    [Fact]
    public async Task A_rejected_message_fails_for_good()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);
        _api.GoogleApis.GmailSend = HttpStatusCode.BadRequest;

        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));

        Assert.False(failure.MayPass);
        Assert.Equal("Gmail answered 400.", failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Rate_limits_and_outages_may_pass(HttpStatusCode status)
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);
        _api.GoogleApis.GmailSend = status;

        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));

        Assert.True(failure.MayPass);
    }

    [Fact]
    public async Task A_refused_access_token_is_dropped_and_may_pass()
    {
        _api.GoogleApis.AllowRefreshToken(SenderRefreshToken);
        _api.GoogleApis.GmailSend = HttpStatusCode.Unauthorized;

        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));
        _api.GoogleApis.GmailSend = HttpStatusCode.OK;
        await SendAsync(Message());

        Assert.True(failure.MayPass);
        Assert.Equal(2, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_sender_that_needs_signing_in_again_may_pass()
    {
        var failure = await Assert.ThrowsAsync<EmailSendException>(() => SendAsync(Message()));

        Assert.True(failure.MayPass);
        Assert.Equal("The notification sender mailbox needs signing in again.", failure.Message);
        Assert.Empty(_api.GoogleApis.SentMail);
    }

    [Fact]
    public async Task Without_a_sender_emails_are_only_logged()
    {
        await using var api = new KairoApiFactory(sqlServer.KairoConnectionString);
        await using var scope = api.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(Message(), Token);

        Assert.Equal(SendResult.Logged, result);
        Assert.Empty(api.GoogleApis.Requests);
    }

    [Fact]
    public void The_raw_message_is_base64url_without_unsafe_characters()
    {
        var raw = GmailSender.Build(Message() with { Text = new string('?', 300) }, Sender);

        Assert.DoesNotContain('+', raw);
        Assert.DoesNotContain('/', raw);
        Assert.Equal("Google Ads needs reconnecting", MimeMessage.Load(new MemoryStream(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/')))).Subject);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static EmailMessage Message() => new(
        Guid.NewGuid(),
        "connection.problem",
        "priya@acme.example",
        "Priya Shah",
        "Google Ads needs reconnecting",
        "<p>Reconnect Google Ads.</p>",
        "Reconnect Google Ads.");

    private async Task<SendResult> SendAsync(EmailMessage message)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, Token);
    }
}
```

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailTemplateTests"`
Expected: the build fails with `CS0234: The type or namespace name 'Delivery' does not exist in the namespace 'Kairo.Modules.Notifications'`.

- [ ] **Step 7: Add the message types and settings**

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/NotificationSettings.cs`:

```csharp
namespace Kairo.Modules.Notifications.Delivery;

internal static class NotificationSettings
{
    public const string Sender = "Kairo:Notifications:Sender";
    public const string SenderRefreshToken = "Kairo:Notifications:SenderRefreshToken";
    public const string DeliveryLoop = "Kairo:Notifications:DeliveryLoop";
    public const string PublicOrigin = "Kairo:PublicOrigin";

    public static bool HasSender(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[Sender]) && !string.IsNullOrWhiteSpace(configuration[SenderRefreshToken]);
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailMessage.cs`:

```csharp
namespace Kairo.Modules.Notifications.Delivery;

internal sealed record EmailMessage(Guid UserId, string Kind, string To, string ToName, string Subject, string Html, string Text);

internal enum SendResult
{
    Sent,
    Logged,
}

internal interface IEmailSender
{
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class EmailSendException(string message, bool mayPass) : Exception(message)
{
    public bool MayPass { get; } = mayPass;
}
```

- [ ] **Step 8: Add the layout**

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailTemplate.cs`:

```csharp
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
```

- [ ] **Step 9: Add the senders**

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/LogEmailSender.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "No notification sender is set, so an email was only logged: {Kind} to user {UserId}, \"{Subject}\".",
            message.Kind,
            message.UserId,
            message.Subject);
        return Task.FromResult(SendResult.Logged);
    }
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/SenderToken.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class SenderToken(IHttpClientFactory http, IConfiguration configuration, TimeProvider time, ILogger<SenderToken> logger)
{
    public const string ClientName = "kairo-google";

    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions SnakeCase = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _expiresAt - Margin > time.GetUtcNow())
            {
                return _accessToken;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = configuration[NotificationSettings.SenderRefreshToken] ?? "",
                ["client_id"] = configuration["Authentication:Google:ClientId"] ?? "",
                ["client_secret"] = configuration["Authentication:Google:ClientSecret"] ?? "",
            });
            using var response = await http.CreateClient(ClientName).PostAsync(TokenEndpoint, content, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest
                && (await response.Content.ReadFromJsonAsync<TokenError>(SnakeCase, cancellationToken))?.Error == "invalid_grant")
            {
                logger.LogCritical("The notification sender mailbox needs signing in again. Run deploy/set-mail-sender.ps1.");
                throw new EmailSendException("The notification sender mailbox needs signing in again.", mayPass: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new EmailSendException($"Google's token endpoint answered {(int)response.StatusCode}.", mayPass: true);
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(SnakeCase, cancellationToken)
                ?? throw new EmailSendException("Google returned an empty token response.", mayPass: true);
            _accessToken = token.AccessToken;
            _expiresAt = time.GetUtcNow().AddSeconds(token.ExpiresIn);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Forget() => _accessToken = null;

    private sealed record TokenResponse(string AccessToken, int ExpiresIn);

    private sealed record TokenError(string? Error);
}
```

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/GmailSender.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MimeKit;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class GmailSender(IHttpClientFactory http, SenderToken token, IConfiguration configuration) : IEmailSender
{
    private const string SendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var raw = Build(message, configuration[NotificationSettings.Sender]!);
        var accessToken = await token.GetAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint) { Content = JsonContent.Create(new { raw }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.CreateClient(SenderToken.ClientName).SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return SendResult.Sent;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            token.Forget();
            throw new EmailSendException("Gmail refused the sender's access token.", mayPass: true);
        }

        var status = (int)response.StatusCode;
        throw new EmailSendException($"Gmail answered {status}.", mayPass: response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500);
    }

    public static string Build(EmailMessage message, string sender)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Kairo", sender));
        mime.To.Add(new MailboxAddress(message.ToName, message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();
        using var stream = new MemoryStream();
        mime.WriteTo(stream);
        return Convert.ToBase64String(stream.ToArray()).Replace('+', '-').Replace('/', '_');
    }
}
```

In `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`, add `using Kairo.Modules.Notifications.Delivery;` to the usings, and add these registrations at the end of `AddServices`:

```csharp
        services.AddHttpClient(SenderToken.ClientName);
        services.AddSingleton<SenderToken>();
        services.AddSingleton<GmailSender>();
        services.AddSingleton<LogEmailSender>();
        services.AddSingleton<IEmailSender>(provider =>
            NotificationSettings.HasSender(provider.GetRequiredService<IConfiguration>())
                ? provider.GetRequiredService<GmailSender>()
                : provider.GetRequiredService<LogEmailSender>());
```

- [ ] **Step 10: Run the tests to verify they pass**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailTemplateTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.GmailSenderTests"
```

Expected: PASS, 7 layout tests and 12 sender tests, 3 of them the rate-limit theory.

- [ ] **Step 11: Run the existing connection tests**

The fake Google handler and the test factory changed. Make sure nothing that used them broke.

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionConsentTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionAccessTests"
```

Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add core/Directory.Packages.props core/src/Modules/Kairo.Modules.Notifications/Kairo.Modules.Notifications.csproj core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs core/src/Modules/Kairo.Modules.Notifications/Delivery core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs core/tests/Kairo.IntegrationTests/Notifications/EmailTemplateTests.cs core/tests/Kairo.IntegrationTests/Notifications/GmailSenderTests.cs
git commit -m "Build notification emails and send them through Gmail as the sender mailbox"
```

---

### Task 6: The sending loop

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailRetries.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailDispatcher.cs`
- Create: `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailDelivery.cs`
- Modify: `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/EmailRetriesTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/EmailDeliveryTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `IBrandMembers.FindAsync`
  - from Task 2: `NotificationsDbContext.OutboundEmails`, `OutboundEmail`, `EmailStatus`, `NotificationKinds.Find`, and the test helpers `Notifying` and `NotificationRows`
  - from Task 5: `EmailTemplate.Render`, `IEmailSender`, `SendResult`, `EmailSendException`, `NotificationSettings`, `GmailSenderTests.SenderSettings`, `GmailSenderTests.SenderRefreshToken`, and the fake's `AllowRefreshToken`, `GmailSend` and `SentMail`
  - existing: `IBrandDirectory.GetAsync`, `KairoDatabase.EnterSystemScopeAsync`, and the test helper `IsolatedDatabase.CreateAsync` in `Kairo.IntegrationTests.Live`
- Produces:
  - `EmailRetries.MaxAttempts = 6` and `EmailRetries.After(int failedAttempts) : TimeSpan?`, which gives 1 minute, 5 minutes, 30 minutes, 2 hours and 6 hours for attempts 1 to 5, and `null` from attempt 6
  - `EmailDispatcher.SendDueAsync(CancellationToken) : Task<int>`, which returns the number of rows it claimed
  - `EmailDispatcher.BatchSize = 10` and `EmailDispatcher.Lease` (2 minutes)
  - `EmailDeliverySwitch.TurnOn()`, and `EmailDelivery : BackgroundService` with `EmailDelivery.Interval` (5 seconds)
  - `KairoApiFactory` sets `Kairo:Notifications:DeliveryLoop` to `off` unless a test's settings override it

- [ ] **Step 1: Write the failing retry schedule test**

Create `core/tests/Kairo.IntegrationTests/Notifications/EmailRetriesTests.cs`:

```csharp
using Kairo.Modules.Notifications.Delivery;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailRetriesTests
{
    [Fact]
    public void Failed_attempts_wait_longer_each_time_and_the_sixth_gives_up()
    {
        var waits = Enumerable.Range(1, 6).Select(EmailRetries.After).ToArray();

        Assert.Equal(
            new TimeSpan?[]
            {
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(2),
                TimeSpan.FromHours(6),
                null,
            },
            waits);
        Assert.Equal(6, EmailRetries.MaxAttempts);
    }
}
```

- [ ] **Step 2: Write the failing delivery tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/EmailDeliveryTests.cs`:

```csharp
using System.Net;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Notifications.Delivery;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailDeliveryTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString, settings: GmailSenderTests.SenderSettings);
        _api.GoogleApis.AllowRefreshToken(GmailSenderTests.SenderRefreshToken);
    }

    [Fact]
    public async Task A_due_email_is_sent_to_the_recipient_with_a_link_to_act()
    {
        var handle = Workspaces.NewHandle();
        var (owner, brandId) = await WorkspaceAsync(handle);
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        Assert.Equal(1, await SendDueAsync(_api));

        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal(owner.Account.Email, Assert.Single(mail.To.Mailboxes).Address);
        Assert.Equal("Google Ads needs reconnecting", mail.Subject);
        Assert.Contains($"{KairoApiFactory.PublicOrigin}/{handle}/settings/connections", mail.HtmlBody);
        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Sent", 1, (DateTimeOffset?)null, (string?)null), (email.Status, email.Attempts, email.ClaimedUntil, email.LastError));
        Assert.Equal(_api.Clock.GetUtcNow(), email.SentAt);
    }

    [Fact]
    public async Task A_sent_email_is_never_sent_again()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await SendDueAsync(_api);

        _api.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, await SendDueAsync(_api));
        Assert.Single(_api.GoogleApis.SentMail);
    }

    [Fact]
    public async Task Without_a_sender_the_email_is_logged_instead()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await using var unconfigured = new KairoApiFactory(_connectionString);

        Assert.Equal(1, await SendDueAsync(unconfigured));

        Assert.Equal("Logged", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
        Assert.Empty(_api.GoogleApis.SentMail);
        Assert.Empty(unconfigured.GoogleApis.Requests);
    }

    [Fact]
    public async Task A_failure_that_may_pass_is_retried_on_the_schedule_and_the_sixth_gives_up()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        _api.GoogleApis.GmailSend = HttpStatusCode.InternalServerError;

        foreach (var wait in new[] { 1, 5, 30, 120, 360 }.Select(minutes => TimeSpan.FromMinutes(minutes)))
        {
            Assert.Equal(1, await SendDueAsync(_api));
            var pending = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
            Assert.Equal(("Pending", "Gmail answered 500."), (pending.Status, pending.LastError));
            Assert.Equal(_api.Clock.GetUtcNow() + wait, pending.NextAttemptAt);

            _api.Clock.Advance(wait - TimeSpan.FromSeconds(1));
            Assert.Equal(0, await SendDueAsync(_api));
            _api.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, await SendDueAsync(_api));
        var failed = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Failed", 6, (DateTimeOffset?)null), (failed.Status, failed.Attempts, failed.SentAt));
        _api.GoogleApis.GmailSend = HttpStatusCode.OK;
        _api.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await SendDueAsync(_api));
    }

    [Fact]
    public async Task A_rejected_message_fails_at_once()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        _api.GoogleApis.GmailSend = HttpStatusCode.BadRequest;

        await SendDueAsync(_api);

        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Failed", 1, "Gmail answered 400."), (email.Status, email.Attempts, email.LastError));
    }

    [Fact]
    public async Task A_recipient_who_left_the_workspace_is_skipped()
    {
        var (owner, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(marketer.UserId)));
        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Equal(1, await SendDueAsync(_api));

        Assert.Equal("Skipped", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
        Assert.Empty(_api.GoogleApis.SentMail);
    }

    [Fact]
    public async Task The_email_uses_the_current_address_and_handle()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        var newHandle = Workspaces.NewHandle();
        await NotificationRows.ExecuteAsync(_connectionString, "UPDATE identity.Users SET Email = @email WHERE Id = @id", ("@email", "priya.new@acme.example"), ("@id", owner.UserId));
        await NotificationRows.ExecuteAsync(_connectionString, "UPDATE brands.Brands SET Handle = @handle WHERE Id = @id", ("@handle", newHandle), ("@id", brandId));

        await SendDueAsync(_api);

        var mail = Assert.Single(_api.GoogleApis.SentMail);
        Assert.Equal("priya.new@acme.example", Assert.Single(mail.To.Mailboxes).Address);
        Assert.Contains($"{KairoApiFactory.PublicOrigin}/{newHandle}/settings/connections", mail.HtmlBody);
    }

    [Fact]
    public async Task Two_senders_at_once_send_each_email_once()
    {
        var (owner, brandId) = await WorkspaceAsync();
        for (var i = 0; i < 15; i++)
        {
            await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        }

        var claimed = 0;
        int[] round;
        do
        {
            round = await Task.WhenAll(SendDueAsync(_api), SendDueAsync(_api));
            claimed += round.Sum();
        }
        while (round.Sum() > 0);

        Assert.Equal(15, claimed);
        Assert.Equal(15, _api.GoogleApis.SentMail.Count);
        Assert.All(await NotificationRows.EmailsAsync(_connectionString, brandId), email => Assert.Equal(("Sent", 1), (email.Status, email.Attempts)));
    }

    [Fact]
    public async Task A_claim_whose_lease_ran_out_is_claimed_again()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        await NotificationRows.ExecuteAsync(
            _connectionString,
            "UPDATE notifications.OutboundEmails SET Attempts = 1, ClaimedUntil = @until WHERE BrandId = @brand",
            ("@until", _api.Clock.GetUtcNow() + EmailDispatcher.Lease),
            ("@brand", brandId));

        Assert.Equal(0, await SendDueAsync(_api));
        _api.Clock.Advance(EmailDispatcher.Lease + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await SendDueAsync(_api));

        var email = Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId));
        Assert.Equal(("Sent", 2), (email.Status, email.Attempts));
    }

    [Fact]
    public async Task At_most_ten_emails_are_claimed_at_a_time()
    {
        var (owner, brandId) = await WorkspaceAsync();
        for (var i = 0; i < 12; i++)
        {
            await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        }

        Assert.Equal(EmailDispatcher.BatchSize, await SendDueAsync(_api));
        Assert.Equal(2, await SendDueAsync(_api));
    }

    [Fact]
    public async Task The_loop_in_the_API_sends_due_emails_every_five_seconds()
    {
        await using var api = new KairoApiFactory(
            _connectionString,
            settings: new Dictionary<string, string>(GmailSenderTests.SenderSettings) { ["Kairo:Notifications:DeliveryLoop"] = "on" });
        api.GoogleApis.AllowRefreshToken(GmailSenderTests.SenderRefreshToken);
        var owner = await Workspaces.SignInAsync(api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await Notifying.NotifyAsync(api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));
        for (var step = 0; step < 100 && api.GoogleApis.GmailMessages.IsEmpty; step++)
        {
            api.Clock.Advance(EmailDelivery.Interval);
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        Assert.Single(api.GoogleApis.SentMail);
        Assert.Equal("Sent", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
    }

    [Fact]
    public async Task The_test_hosts_leave_the_loop_off()
    {
        var (owner, brandId) = await WorkspaceAsync();
        await Notifying.NotifyAsync(_api, brandId, Notifying.Sample(LiveAudience.Users(owner.UserId)));

        for (var step = 0; step < 10; step++)
        {
            _api.Clock.Advance(EmailDelivery.Interval);
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        Assert.Equal("Pending", Assert.Single(await NotificationRows.EmailsAsync(_connectionString, brandId)).Status);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<(Member Owner, Guid BrandId)> WorkspaceAsync(string? handle = null)
    {
        var owner = await Workspaces.SignInAsync(_api);
        return (owner, await Workspaces.CreateAsync(owner.Browser, handle));
    }

    private static async Task<int> SendDueAsync(KairoApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Token);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailDeliveryTests"`
Expected: the build fails with `CS0103: The name 'EmailRetries' does not exist in the current context` from `EmailRetriesTests.cs`, and `CS0246` for `EmailDispatcher` and `EmailDelivery` from `EmailDeliveryTests.cs`.

- [ ] **Step 4: Add the retry schedule**

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailRetries.cs`:

```csharp
namespace Kairo.Modules.Notifications.Delivery;

internal static class EmailRetries
{
    public const int MaxAttempts = 6;

    private static readonly TimeSpan[] Waits =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    public static TimeSpan? After(int failedAttempts) =>
        failedAttempts is >= 1 and < MaxAttempts ? Waits[failedAttempts - 1] : null;
}
```

- [ ] **Step 5: Add the dispatcher**

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailDispatcher.cs`:

```csharp
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class EmailDispatcher(
    NotificationsDbContext db,
    KairoDatabase database,
    IBrandMembers members,
    IBrandDirectory brands,
    IEmailSender sender,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<EmailDispatcher> logger)
{
    public const int BatchSize = 10;

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var claimed = await ClaimAsync(cancellationToken);
            foreach (var id in claimed)
            {
                await SendAsync(id, cancellationToken);
            }

            return claimed.Count;
        }
    }

    private Task<List<long>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var leaseEnd = now + Lease;
        return db.Database.SqlQuery<long>($"""
            UPDATE TOP (10) notifications.OutboundEmails WITH (READPAST, UPDLOCK, ROWLOCK)
            SET ClaimedUntil = {leaseEnd}, Attempts = Attempts + 1
            OUTPUT inserted.Id AS Value
            WHERE Status = N'Pending' AND NextAttemptAt <= {now} AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
            """).ToListAsync(cancellationToken);
    }

    private async Task SendAsync(long id, CancellationToken cancellationToken)
    {
        var email = await db.OutboundEmails.Include(row => row.Notification).SingleAsync(row => row.Id == id, cancellationToken);
        var notification = email.Notification!;
        var kind = NotificationKinds.Find(notification.Kind);
        var member = await members.FindAsync(email.BrandId, email.UserId, cancellationToken);
        var workspace = (await brands.GetAsync([email.BrandId], cancellationToken)).SingleOrDefault();

        if (kind is null || member is null || workspace is null)
        {
            email.Status = EmailStatus.Skipped;
        }
        else
        {
            try
            {
                var message = EmailTemplate.Render(notification, kind, workspace, member, configuration[NotificationSettings.PublicOrigin] ?? "");
                var result = await sender.SendAsync(message, cancellationToken);
                email.Status = result == SendResult.Sent ? EmailStatus.Sent : EmailStatus.Logged;
                email.SentAt = time.GetUtcNow();
                email.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Fail(email, exception);
            }
        }

        email.ClaimedUntil = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Fail(OutboundEmail email, Exception exception)
    {
        var wait = exception is EmailSendException { MayPass: false } ? null : EmailRetries.After(email.Attempts);
        email.LastError = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
        if (wait is { } delay)
        {
            email.NextAttemptAt = time.GetUtcNow() + delay;
            logger.LogWarning(exception, "Notification email {EmailId} failed on attempt {Attempts}. Trying again in {Delay}.", email.Id, email.Attempts, delay);
            return;
        }

        email.Status = EmailStatus.Failed;
        logger.LogError(exception, "Gave up on notification email {EmailId} after {Attempts} attempts.", email.Id, email.Attempts);
    }
}
```

- [ ] **Step 6: Add the loop and its switch**

Create `core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailDelivery.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class EmailDeliverySwitch
{
    public bool IsOn { get; private set; }

    public void TurnOn() => IsOn = true;
}

internal sealed class EmailDelivery(
    EmailDeliverySwitch deliverySwitch,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<EmailDelivery> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!deliverySwitch.IsOn || string.Equals(configuration[NotificationSettings.DeliveryLoop], "off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Sending notification emails failed. Trying again shortly.");
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
```

In `core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs`:

1. Add these registrations at the end of `AddServices`:

   ```csharp
           services.AddScoped<EmailDispatcher>();
           services.AddSingleton<EmailDeliverySwitch>();
           services.AddHostedService<EmailDelivery>();
   ```

2. Switch the loop on in `MapApi`, which only the API calls:

   ```csharp
       public void MapApi(IEndpointRouteBuilder api)
       {
           api.ServiceProvider.GetRequiredService<EmailDeliverySwitch>().TurnOn();
           api.MapInbox();
           api.MapEmailSettings();
       }
   ```

- [ ] **Step 7: Keep the loop off in the test hosts**

In `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`, in `ConfigureWebHost`, add this line right before the `foreach` over `settings`, so a test can still turn the loop on:

```csharp
        builder.UseSetting("Kairo:Notifications:DeliveryLoop", "off");
```

- [ ] **Step 8: Run the tests to verify they pass**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailRetriesTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.EmailDeliveryTests"
dotnet test --project core/tests/Kairo.Worker.Tests
```

Expected: PASS, 1 retry test and 12 delivery tests. The worker host tests still pass: the worker registers the loop but never switches it on.

- [ ] **Step 9: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailRetries.cs core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailDispatcher.cs core/src/Modules/Kairo.Modules.Notifications/Delivery/EmailDelivery.cs core/src/Modules/Kairo.Modules.Notifications/NotificationsModule.cs core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs core/tests/Kairo.IntegrationTests/Notifications/EmailRetriesTests.cs core/tests/Kairo.IntegrationTests/Notifications/EmailDeliveryTests.cs
git commit -m "Send queued notification emails from the API, with leases, retries and skipped leavers"
```

---

### Task 7: Connections that need reconnecting notify admins

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionNotices.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Live/ConnectionLiveEventsTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Notifications/ConnectionNotificationTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `INotifications`, `Notification`
  - from Task 2: the test helper `NotificationRows`
  - existing: `ReconnectReasons`, `Products.Get(product).Name`, `ConnectionTokens.LoseAsync`, and the test helpers `Connections.ConnectAsync`, `Connections.CopyStoredTokenAsync` and `FakeGoogleApis.Reject`
- Produces: `ConnectionNotices.NeedsReconnect(ConnectedProduct product, string reason) : Notification`, with kind `connection.problem`, audience `LiveAudience.Roles(BrandRoles.Admin)` and link `/settings/connections`

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Notifications/ConnectionNotificationTests.cs`:

```csharp
using System.Net.Http.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Notifications;

public sealed class ConnectionNotificationTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rejected_token_tells_every_admin_and_no_one_else()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var admin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var issued = await Connections.ConnectAsync(_api, owner.Browser, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);

        (await owner.Browser.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        var notifications = await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId);
        Assert.Equal(new[] { owner.UserId, admin.UserId }.Order(), notifications.Select(row => row.UserId).Order());
        Assert.All(notifications, row =>
        {
            Assert.Equal(("connection.problem", "Google Ads needs reconnecting", "/settings/connections"), (row.Kind, row.Title, row.Link));
            Assert.Equal("Google stopped accepting Kairo's access to Google Ads. Reconnect it so refreshes and changes can continue.", row.Body);
        });
        Assert.Equal(2, (await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId)).Count);
    }

    [Fact]
    public async Task An_unreadable_token_tells_the_admins_Kairo_can_no_longer_read_it()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await Connections.ConnectAsync(_api, owner.Browser, first, "google-ads");
        await Connections.ConnectAsync(_api, owner.Browser, second, "google-ads");
        await Connections.CopyStoredTokenAsync(sqlServer.KairoConnectionString, first, second, "google-ads");

        (await owner.Browser.PostAsync($"/api/brands/{second}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, second));
        Assert.Equal("Kairo can no longer read its stored access to Google Ads. Reconnect it so refreshes and changes can continue.", notification.Body);
        Assert.Empty(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, first));
    }

    [Fact]
    public async Task An_admin_who_loses_the_role_leaves_the_other_admins_a_notice_and_gets_none()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "ga4");

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal(owner.UserId, notification.UserId);
        Assert.Equal("Google Analytics needs reconnecting", notification.Title);
        Assert.Equal(
            "Google Analytics was connected by an admin who has left or is no longer an admin. Reconnect it so refreshes and changes can continue.",
            notification.Body);
    }

    [Fact]
    public async Task An_admin_who_is_removed_leaves_the_other_admins_a_notice_and_gets_none()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "youtube");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        var notification = Assert.Single(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal((owner.UserId, "YouTube needs reconnecting"), (notification.UserId, notification.Title));
    }

    [Fact]
    public async Task Disconnecting_on_purpose_sends_no_notice()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "merchant-center");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/connections/merchant-center", Token)).EnsureSuccessStatusCode();

        Assert.Empty(await NotificationRows.NotificationsAsync(sqlServer.KairoConnectionString, brandId));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.ConnectionNotificationTests"`
Expected: FAIL. The first four tests fail on `Assert.Single` or `Assert.Equal` with an empty collection, because nothing notifies yet. `Disconnecting_on_purpose_sends_no_notice` passes already, which is expected: it guards against notifying too much.

- [ ] **Step 3: Add the notices**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionNotices.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionNotices
{
    private const string Continue = "Reconnect it so refreshes and changes can continue.";

    public static Notification NeedsReconnect(ConnectedProduct product, string reason)
    {
        var name = Products.Get(product).Name;
        var cause = reason switch
        {
            ReconnectReasons.TokenRejected => $"Google stopped accepting Kairo's access to {name}.",
            ReconnectReasons.TokenUnreadable => $"Kairo can no longer read its stored access to {name}.",
            ReconnectReasons.AdminLeft => $"{name} was connected by an admin who has left or is no longer an admin.",
            _ => $"Kairo lost its access to {name}.",
        };

        return new Notification(
            "connection.problem",
            LiveAudience.Roles(BrandRoles.Admin),
            $"{name} needs reconnecting",
            $"{cause} {Continue}",
            "/settings/connections");
    }
}
```

- [ ] **Step 4: Notify when a connection is lost**

In `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs`:

1. Add `using Kairo.Contracts.Notifications;` to the usings.
2. Add `INotifications notifications,` to the constructor, right after `ILiveUpdates live,`.
3. Make the last lines of `LoseAsync`:

   ```csharp
           await live.PublishAsync(ConnectionEvents.Changed(connection.Product), cancellationToken);
           await notifications.NotifyAsync(ConnectionNotices.NeedsReconnect(connection.Product, reason), cancellationToken);
       }
   ```

- [ ] **Step 5: Expect the extra live event in the departing-admin test**

A departing admin's connections now publish three events to the owner's stream, in this order: `connection.changed`, `notification.created` (the owner is the only admin left) and `team.changed`. `A_departing_admins_connections_publish_connection_changed` reads only two, so it would miss `team.changed`.

In `core/tests/Kairo.IntegrationTests/Live/ConnectionLiveEventsTests.cs`, replace the last three lines of that test with:

```csharp
        var types = new[] { await listener.NextAsync(), await listener.NextAsync(), await listener.NextAsync() };
        Assert.Contains(types, message => message.Type == "team.changed");
        Assert.Contains(types, message => message.Type == "connection.changed" && message.Data.GetProperty("product").GetString() == "ga4");
        Assert.Contains(types, message => message.Type == "notification.created" && message.Data.GetProperty("kind").GetString() == "connection.problem");
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Notifications.ConnectionNotificationTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.ConnectionLiveEventsTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionLifecycleTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionReleaseTests"
```

Expected: PASS, with the 5 new tests and the existing connection tests.

- [ ] **Step 7: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionNotices.cs core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs core/tests/Kairo.IntegrationTests/Live/ConnectionLiveEventsTests.cs core/tests/Kairo.IntegrationTests/Notifications/ConnectionNotificationTests.cs
git commit -m "Tell admins when a connection needs reconnecting"
```

---

### Task 8: Setting up and deploying the sender mailbox

**Files:**
- Modify: `deploy/Kairo.Deploy/Common.ps1`
- Modify: `deploy/Kairo.Deploy/Setup.ps1`
- Modify: `deploy/Kairo.Deploy/Release.ps1`
- Modify: `deploy/environments.psd1`
- Create: `deploy/set-mail-sender.ps1`
- Test: `deploy/tests/Common.Tests.ps1`
- Test: `deploy/tests/Setup.Tests.ps1`
- Test: `deploy/tests/Release.Tests.ps1`

**Interfaces:**
- Consumes: existing `Invoke-Gcloud`, `New-KairoSecret`, `Set-KairoSecretValue`, `Test-KairoSecretHasValue`, `Add-KairoBinding`, `Get-KairoServiceAccount`, `$script:GoogleSecrets`, and the configuration keys from Task 5 (`Kairo__Notifications__Sender` and `Kairo__Notifications__SenderRefreshToken`)
- Produces:
  - `$script:MailSecret = 'mail-sender-refresh-token'`
  - `Get-KairoSecretValue -Environment -Name`, which returns the latest version's value
  - `Set-KairoMailSender -Environment`
  - the helpers `New-KairoOAuthVerifier`, `ConvertTo-KairoBase64Url -Bytes`, `New-KairoMailConsentUrl -ClientId -Sender -Verifier -State`, `Get-KairoIdTokenEmail -IdToken` and `Wait-KairoOAuthRedirect -Url -RedirectUri`
  - `MailSender` in each environment in `deploy/environments.psd1`

- [ ] **Step 1: Write the failing setup tests**

In `deploy/tests/Setup.Tests.ps1`:

1. In `It 'enables the APIs Kairo needs'`, replace the expected line's ending `'bigquery.googleapis.com --project kairo-test')` with `'bigquery.googleapis.com gmail.googleapis.com --project kairo-test')`.
2. In `It 'creates the Google secrets without a value'`, add:

   ```powershell
               $calls | Should -Contain 'secrets create mail-sender-refresh-token --replication-policy automatic --project kairo-test'
   ```

3. After `It 'lets each service read only the secrets it needs'`, add:

   ```powershell
           It 'lets only the API read the sender mailbox token' {
               $calls | Should -Contain 'secrets add-iam-policy-binding mail-sender-refresh-token --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test'
               ($calls | Where-Object { $_ -like 'secrets add-iam-policy-binding mail-sender-refresh-token *' }) | Should -HaveCount 1
           }
   ```

4. At the end of the file, add:

   ```powershell
   Describe 'Get-KairoSecretValue' {
       It 'reads the latest version without a trailing newline' {
           Mock -ModuleName Kairo.Deploy Invoke-Gcloud { $script:read = $ArgumentList -join ' '; "client-id.apps.googleusercontent.com`r`n" }

           Get-KairoSecretValue -Environment $environment -Name 'google-client-id' | Should -BeExactly 'client-id.apps.googleusercontent.com'
           $read | Should -Be 'secrets versions access latest --secret=google-client-id --project kairo-test'
       }
   }

   Describe 'Set-KairoMailSender' {
       BeforeAll {
           function New-TestIdToken([string]$Email, [bool]$Verified) {
               $json = [Text.Encoding]::UTF8.GetBytes((@{ email = $Email; email_verified = $Verified; sub = '123' } | ConvertTo-Json -Compress))
               "header.$([Convert]::ToBase64String($json).TrimEnd('=').Replace('+', '-').Replace('/', '_')).signature"
           }
       }

       BeforeEach {
           $script:sender = $environment.Clone()
           $script:sender.MailSender = 'khanmohdtalha3250@gmail.com'
           $script:consentUrl = $null
           $script:tokenRequest = $null
           $script:returnedState = $null
           $script:grant = [pscustomobject]@{
               refresh_token = 'sender-refresh-1'
               scope         = 'openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/gmail.send'
               id_token      = New-TestIdToken 'khanmohdtalha3250@gmail.com' $true
           }
           Mock -ModuleName Kairo.Deploy Get-KairoSecretValue {
               if ($Name -eq 'google-client-id') { 'client-id' } else { 'client-secret' }
           }
           Mock -ModuleName Kairo.Deploy Wait-KairoOAuthRedirect {
               $script:consentUrl = $Url
               $state = [Uri]::UnescapeDataString([regex]::Match($Url, '[?&]state=([^&]+)').Groups[1].Value)
               @{ code = 'the-code'; state = $(if ($script:returnedState) { $script:returnedState } else { $state }); error = $null }
           }
           Mock -ModuleName Kairo.Deploy Invoke-RestMethod { $script:tokenRequest = $Body; $script:grant }
           Mock -ModuleName Kairo.Deploy Set-KairoSecretValue { }
       }

       It 'refuses to start until the environment names a sender' {
           { Set-KairoMailSender -Environment $environment } | Should -Throw '*MailSender*'
           Should -Invoke -ModuleName Kairo.Deploy Wait-KairoOAuthRedirect -Times 0
       }

       It 'asks the sender to let Kairo send email, offline, with PKCE' {
           Set-KairoMailSender -Environment $sender

           $consentUrl | Should -BeLike 'https://accounts.google.com/o/oauth2/v2/auth?*'
           $consentUrl | Should -BeLike '*client_id=client-id*'
           $consentUrl | Should -BeLike '*redirect_uri=http%3A%2F%2Flocalhost%3A8765%2F*'
           $consentUrl | Should -BeLike '*scope=openid%20email%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgmail.send*'
           $consentUrl | Should -BeLike '*access_type=offline*'
           $consentUrl | Should -BeLike '*prompt=consent*'
           $consentUrl | Should -BeLike '*login_hint=Kairo-Alerts%40acme.example*'
           $consentUrl | Should -BeLike '*code_challenge_method=S256*'
           $sha = [Security.Cryptography.SHA256]::Create()
           $challenge = [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($tokenRequest.code_verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_')
           $consentUrl | Should -BeLike "*code_challenge=$challenge*"
       }

       It 'exchanges the code with the client secret and the same redirect address' {
           Set-KairoMailSender -Environment $sender

           $tokenRequest.grant_type | Should -Be 'authorization_code'
           $tokenRequest.code | Should -Be 'the-code'
           $tokenRequest.client_id | Should -Be 'client-id'
           $tokenRequest.client_secret | Should -Be 'client-secret'
           $tokenRequest.redirect_uri | Should -Be 'http://localhost:8765/'
       }

       It 'stores the refresh token in its secret' {
           Set-KairoMailSender -Environment $sender

           Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -Exactly -ParameterFilter {
               $Name -eq 'mail-sender-refresh-token' -and $Value -eq 'sender-refresh-1'
           }
       }

       It 'refuses a response meant for another sign-in' {
           $script:returnedState = 'someone-elses-state'

           { Set-KairoMailSender -Environment $sender } | Should -Throw '*did not match*'
           Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
       }

       It 'refuses a sign-in that did not allow sending email' {
           $script:grant.scope = 'openid https://www.googleapis.com/auth/userinfo.email'

           { Set-KairoMailSender -Environment $sender } | Should -Throw '*send email*'
           Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
       }

       It 'refuses a different account' {
           $script:grant.id_token = New-TestIdToken 'someone@acme.example' $true

           { Set-KairoMailSender -Environment $sender } | Should -Throw '*signed in as someone@acme.example*'
           Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
       }

       It 'refuses an address Google has not verified' {
           $script:grant.id_token = New-TestIdToken 'khanmohdtalha3250@gmail.com' $false

           { Set-KairoMailSender -Environment $sender } | Should -Throw '*verified*'
           Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
       }

       It 'refuses a grant without a refresh token' {
           $script:grant.refresh_token = $null

           { Set-KairoMailSender -Environment $sender } | Should -Throw '*no refresh token*'
           Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
       }
   }
   ```

- [ ] **Step 2: Write the failing release tests**

In `deploy/tests/Release.Tests.ps1`, inside `Describe 'Invoke-KairoDeploy'`, add these tests after `It 'tells the API and the worker which key encrypts connection tokens'`:

```powershell
    It 'passes the sender mailbox to the API once it is signed in' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'khanmohdtalha3250@gmail.com'

        Invoke-KairoDeploy -Environment $withSender -Root $root | Out-Null

        $api = $calls | Where-Object { $_ -like 'run deploy kairo-api *' }
        $api | Should -BeLike '*,Kairo__Notifications__Sender=khanmohdtalha3250@gmail.com*'
        $api | Should -BeLike '*--set-secrets=*,Kairo__Notifications__SenderRefreshToken=mail-sender-refresh-token:latest*'
        ($calls | Where-Object { $_ -like 'run deploy kairo-worker *' }) | Should -Not -BeLike '*Notifications*'
    }

    It 'only logs notification emails until the sender is signed in' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'khanmohdtalha3250@gmail.com'
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $Name -ne 'mail-sender-refresh-token' }

        Invoke-KairoDeploy -Environment $withSender -Root $root -WarningAction SilentlyContinue -WarningVariable warnings | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -Not -BeLike '*Notifications*'
        ($warnings -join ' ') | Should -BeLike '*set-mail-sender.ps1*'
    }

    It 'only logs notification emails while the environment names no sender' {
        Invoke-KairoDeploy -Environment $environment -Root $root -WarningAction SilentlyContinue -WarningVariable warnings | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -Not -BeLike '*Notifications*'
        ($warnings -join ' ') | Should -BeLike '*set-mail-sender.ps1*'
    }

    It 'leaves the sender out while Google sign-in is off, since sending needs the OAuth client' {
        $withSender = $environment.Clone()
        $withSender.MailSender = 'khanmohdtalha3250@gmail.com'
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $Name -eq 'mail-sender-refresh-token' }

        Invoke-KairoDeploy -Environment $withSender -Root $root 3>$null | Out-Null

        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) | Should -Not -BeLike '*Notifications*'
    }
```

In `deploy/tests/Common.Tests.ps1`, in `It 'loads development from the environment file'`, add:

```powershell
        $environment.ContainsKey('MailSender') | Should -BeTrue
```

- [ ] **Step 3: Run them to verify they fail**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: FAIL. The API list test fails because `gmail.googleapis.com` is missing, the `Set-KairoMailSender` and `Get-KairoSecretValue` tests fail with `CommandNotFoundException`, the release tests fail because the API gets no sender, and the environment test fails because `MailSender` is missing.

- [ ] **Step 4: Add the secret name and the environment setting**

In `deploy/Kairo.Deploy/Common.ps1`, add after `$script:TokenKey = 'connection-tokens'`:

```powershell
$script:MailSecret = 'mail-sender-refresh-token'
```

In `deploy/environments.psd1`, add `MailSender = ''` to `development`, after `MinInstances`:

```powershell
        MinInstances        = @{ web = 0; api = 0 }
        MailSender          = ''
```

- [ ] **Step 5: Add the setup steps and the sign-in**

In `deploy/Kairo.Deploy/Setup.ps1`:

1. Add `'gmail.googleapis.com'` to the end of `$script:Apis`, so its last line reads:

   ```powershell
       'bigquery.googleapis.com', 'gmail.googleapis.com'
   ```

2. Add these two script variables after `$script:PasswordCharacters`:

   ```powershell
   $script:MailRedirectUri = 'http://localhost:8765/'
   $script:GmailSendScope = 'https://www.googleapis.com/auth/gmail.send'
   ```

3. Make `Initialize-KairoSecrets` create the mailbox secret too:

   ```powershell
   function Initialize-KairoSecrets {
       param([Parameter(Mandatory)][hashtable]$Environment)

       foreach ($secret in @($script:GoogleSecrets.Values) + $script:MailSecret) {
           New-KairoSecret -Environment $Environment -Name $secret
       }
   }
   ```

4. In `Initialize-KairoServiceAccounts`, after the loop that grants the Google secrets to `kairo-api`, add:

   ```powershell
       Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $script:MailSecret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-api')") + $accessor + $scope)
   ```

5. Add these functions after `Test-KairoSecretHasValue`:

   ```powershell
   function Get-KairoSecretValue {
       param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

       ((Invoke-Gcloud -ArgumentList @('secrets', 'versions', 'access', 'latest', "--secret=$Name", '--project', $Environment.Project) -Capture) -join "`n").Trim()
   }
   ```

6. Add these functions at the end of the file:

   ```powershell
   function Set-KairoMailSender {
       param([Parameter(Mandatory)][hashtable]$Environment)

       $sender = "$($Environment.MailSender)".Trim()
       if (-not $sender) {
           throw "Set MailSender for the $($Environment.Name) environment in deploy/environments.psd1 first."
       }

       $clientId = Get-KairoSecretValue -Environment $Environment -Name $script:GoogleSecrets['Authentication__Google__ClientId']
       $clientSecret = Get-KairoSecretValue -Environment $Environment -Name $script:GoogleSecrets['Authentication__Google__ClientSecret']
       $verifier = New-KairoOAuthVerifier
       $state = New-KairoOAuthVerifier
       $url = New-KairoMailConsentUrl -ClientId $clientId -Sender $sender -Verifier $verifier -State $state
       $response = Wait-KairoOAuthRedirect -Url $url -RedirectUri $script:MailRedirectUri
       if ($response.state -ne $state) {
           throw 'The sign-in response did not match this request. Run the script again.'
       }

       if ($response.error) {
           throw "Google sign-in failed: $($response.error)."
       }

       $grant = Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -Body @{
           grant_type    = 'authorization_code'
           code          = $response.code
           code_verifier = $verifier
           redirect_uri  = $script:MailRedirectUri
           client_id     = $clientId
           client_secret = $clientSecret
       }
       if (-not $grant.refresh_token) {
           throw 'Google returned no refresh token. Remove Kairo from the account''s third-party access, then run the script again.'
       }

       if (-not (("$($grant.scope)" -split ' ') -contains $script:GmailSendScope)) {
           throw 'Google did not grant permission to send email. Run the script again and allow Kairo to send email.'
       }

       $email = Get-KairoIdTokenEmail -IdToken $grant.id_token
       if ($email -ne $sender.ToLowerInvariant()) {
           throw "You signed in as $email, but MailSender is $sender. Run the script again and choose $sender."
       }

       Set-KairoSecretValue -Environment $Environment -Name $script:MailSecret -Value $grant.refresh_token
   }

   function New-KairoOAuthVerifier {
       $bytes = New-Object byte[] 32
       $random = [Security.Cryptography.RandomNumberGenerator]::Create()
       try {
           $random.GetBytes($bytes)
       }
       finally {
           $random.Dispose()
       }

       ConvertTo-KairoBase64Url -Bytes $bytes
   }

   function ConvertTo-KairoBase64Url {
       param([Parameter(Mandatory)][byte[]]$Bytes)

       [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
   }

   function New-KairoMailConsentUrl {
       param(
           [Parameter(Mandatory)][string]$ClientId,
           [Parameter(Mandatory)][string]$Sender,
           [Parameter(Mandatory)][string]$Verifier,
           [Parameter(Mandatory)][string]$State
       )

       $sha = [Security.Cryptography.SHA256]::Create()
       try {
           $challenge = ConvertTo-KairoBase64Url -Bytes $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($Verifier))
       }
       finally {
           $sha.Dispose()
       }

       $query = [ordered]@{
           client_id             = $ClientId
           redirect_uri          = $script:MailRedirectUri
           response_type         = 'code'
           scope                 = "openid email $script:GmailSendScope"
           access_type           = 'offline'
           prompt                = 'consent'
           login_hint            = $Sender
           code_challenge        = $challenge
           code_challenge_method = 'S256'
           state                 = $State
       }
       'https://accounts.google.com/o/oauth2/v2/auth?' + (($query.GetEnumerator() | ForEach-Object { "$($_.Key)=$([Uri]::EscapeDataString($_.Value))" }) -join '&')
   }

   function Get-KairoIdTokenEmail {
       param([Parameter(Mandatory)][string]$IdToken)

       $parts = $IdToken.Split('.')
       if ($parts.Length -ne 3) {
           throw 'Google returned an ID token Kairo cannot read.'
       }

       $payload = $parts[1].Replace('-', '+').Replace('_', '/')
       $payload += '=' * ((4 - $payload.Length % 4) % 4)
       $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
       if (-not $claims.email_verified) {
           throw "Google has not verified the address $($claims.email)."
       }

       "$($claims.email)".ToLowerInvariant()
   }

   function Wait-KairoOAuthRedirect {
       param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$RedirectUri)

       $listener = New-Object System.Net.HttpListener
       $listener.Prefixes.Add($RedirectUri)
       $listener.Start()
       try {
           Write-Host "Opening your browser to sign in the sender mailbox. If it does not open, go to:`n$Url"
           Start-Process $Url
           $context = $listener.GetContext()
           $query = $context.Request.QueryString
           $result = @{ code = $query['code']; state = $query['state']; error = $query['error'] }
           $page = [Text.Encoding]::UTF8.GetBytes('<!DOCTYPE html><html><body style="font-family:sans-serif"><p>Kairo has what it needs. You can close this tab.</p></body></html>')
           $context.Response.ContentType = 'text/html; charset=utf-8'
           $context.Response.OutputStream.Write($page, 0, $page.Length)
           $context.Response.Close()
           $result
       }
       finally {
           $listener.Stop()
           $listener.Close()
       }
   }
   ```

- [ ] **Step 6: Pass the sender to the API**

In `deploy/Kairo.Deploy/Release.ps1`, in `Invoke-KairoDeploy`:

1. After the `if ($googleConfigured) { … }` block that adds the Google secrets, add:

   ```powershell
       $apiSettings = "Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName"
       $mailSender = "$($Environment.MailSender)".Trim()
       $mailConfigured = $googleConfigured -and $mailSender -and (Test-KairoSecretHasValue -Environment $Environment -Name $script:MailSecret)
       if ($mailConfigured) {
           $apiSettings += ",Kairo__Notifications__Sender=$mailSender"
           $apiSecrets += "Kairo__Notifications__SenderRefreshToken=$($script:MailSecret):latest"
       }
   ```

2. In the `kairo-api` settings, replace the line `"--set-env-vars=Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName",` with:

   ```powershell
               "--set-env-vars=$apiSettings",
   ```

3. After the existing `if (-not $googleConfigured) { Write-Warning … }` block at the end, add:

   ```powershell
       if (-not $mailConfigured) {
           Write-Warning 'Notification emails are only logged until deploy/set-mail-sender.ps1 has been run. See "Set up notification emails" in README.md.'
       }
   ```

- [ ] **Step 7: Add the script**

Create `deploy/set-mail-sender.ps1`:

```powershell
param([Parameter(Mandatory, Position = 0)][string]$Environment)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
Set-KairoMailSender -Environment (Get-KairoEnvironment -Name $Environment)
Write-Host "The notification sender mailbox is signed in for $Environment. Run deploy.bat $Environment to start sending email."
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: every test passes, including the 15 new ones: 1 setup grant test, 1 `Get-KairoSecretValue` test, 9 `Set-KairoMailSender` tests and 4 release tests. The changed API list, secret and environment tests also pass.

- [ ] **Step 9: Commit**

```bash
git add deploy/Kairo.Deploy/Common.ps1 deploy/Kairo.Deploy/Setup.ps1 deploy/Kairo.Deploy/Release.ps1 deploy/environments.psd1 deploy/set-mail-sender.ps1 deploy/tests/Common.Tests.ps1 deploy/tests/Setup.Tests.ps1 deploy/tests/Release.Tests.ps1
git commit -m "Sign the notification sender mailbox in once and pass it to the API on deploy"
```

---

### Task 9: The architecture document and the README

**Files:**
- Modify: `docs/Kairo-SAD.md`
- Modify: `README.md`
- Modify: `core/src/Kairo.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: the names and decisions from Tasks 1 to 8
- Produces: the AppHost user secrets `Kairo:MailSender` and `Kairo:MailSenderRefreshToken`, passed to the API only when both are set

- [ ] **Step 1: Update the architecture document**

In `docs/Kairo-SAD.md`, make these replacements. Each "from" line appears exactly once.

1. §2, the Email row:
   - from: `| Email | Gmail API with a Google Workspace sender | Notification emails |`
   - to: `| Email | Gmail API, sending as one sender mailbox | Notification emails, sent from an outbox in SQL Server |`
2. §6, the Operations row:
   - from: `| Operations | Notification, AuditLog, LiveOutbox | AuditLog is append-only; LiveOutbox keeps live updates for 24 hours |`
   - to: `| Operations | Notification, EmailOptOut, OutboundEmail, AuditLog, LiveOutbox | AuditLog is append-only; LiveOutbox keeps live updates for 24 hours; OutboundEmail is the email outbox, retried on a fixed schedule |`
3. §7, the Gmail API row:
   - from: `| Gmail API | Notification emails | A Google Workspace sender account, from the core | The Workspace account's daily sending limit |`
   - to: `| Gmail API | Notification emails | One sender mailbox, a Workspace or Gmail address, signed in once with the gmail.send scope through Kairo's OAuth client. Its refresh token sits in Secret Manager, and the core API sends from an email outbox in SQL Server. | About 500 messages a day from a Gmail account, or 2,000 from a Workspace account |`
4. §9, the Queues row:
   - from: `| Queues | Cloud Tasks | Separate queues for agent runs, Google Ads changes and notifications, each with its own rate limit |`
   - to: `| Queues | Cloud Tasks | Separate queues for agent runs and Google Ads changes, each with its own rate limit. Notification emails go through an outbox in SQL Server, sent by the core API, so they need no queue. |`
5. §13, the open question:
   - from: `- [ ] Which Google Workspace account sends notification emails?`
   - to: `- [x] Which Google Workspace account sends notification emails? One dedicated sender mailbox, a Workspace or Gmail address, that signs in once to gmail.send with deploy/set-mail-sender.ps1.`

- [ ] **Step 2: Add the README section**

In `README.md`, add this section after the "Set up account connections" section and before "## Run everything locally":

````markdown
## Set up notification emails

Kairo sends notification emails through the Gmail API as one sender mailbox. It can be a Google Workspace or a plain Gmail address. Gmail allows about 500 messages a day from a Gmail account and about 2,000 from a Workspace account.

Do this once for each environment:

1. **Publishing status.** In Google Auth Platform → Audience, make sure the app is **In production**. While it is in Testing, Google ends every refresh token after 7 days, which would stop the sender and every brand connection each week. An unverified app in production works for up to 100 users.
2. **Data access.** In Google Auth Platform → Data Access, add `https://www.googleapis.com/auth/gmail.send`.
3. **Redirect URI.** Add `http://localhost:8765/` to the web OAuth client's authorized redirect URIs.
4. **Sender address.** Put the mailbox's address in `MailSender` for the environment in `deploy/environments.psd1`, and commit it.
5. **Sign in.** Run `powershell -ExecutionPolicy Bypass -File deploy/set-mail-sender.ps1 development`. Your browser opens; sign in as the sender mailbox and allow Kairo to send email. The script checks the account and the permission, then stores the refresh token in the secret `mail-sender-refresh-token`.
6. **Deploy.** Run `deploy.bat development`. Until the sender is signed in, the API only logs each email.

Locally, emails are logged unless you give the AppHost a sender, for example the refresh token that `set-mail-sender.ps1` stored for development:

```bash
dotnet user-secrets set "Kairo:MailSender" "<sender address>" --project core/src/Kairo.AppHost
dotnet user-secrets set "Kairo:MailSenderRefreshToken" "<refresh token>" --project core/src/Kairo.AppHost
```
````

- [ ] **Step 3: Let the AppHost pass a local sender to the API**

The API project has no user secrets of its own, and giving it some would let a developer's real sender leak into the integration tests' hosts. The AppHost passes the sender instead, and only when both values are set.

In `core/src/Kairo.AppHost/AppHost.cs`, add this block right after the statement that defines `api` (the one ending `.WithHttpHealthCheck("/health");`):

```csharp
var mailSender = builder.Configuration["Kairo:MailSender"];
var mailSenderRefreshToken = builder.Configuration["Kairo:MailSenderRefreshToken"];
if (!string.IsNullOrWhiteSpace(mailSender) && !string.IsNullOrWhiteSpace(mailSenderRefreshToken))
{
    api.WithEnvironment("Kairo__Notifications__Sender", mailSender)
        .WithEnvironment("Kairo__Notifications__SenderRefreshToken", mailSenderRefreshToken);
}
```

Run: `dotnet build core/src/Kairo.AppHost`
Expected: `Build succeeded.` with 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add docs/Kairo-SAD.md README.md core/src/Kairo.AppHost/AppHost.cs
git commit -m "Record the notification sender and email outbox in the docs, and let the AppHost pass a local sender"
```

---

### Task 10: Full verification

**Files:** none changed, unless a check below fails.

- [ ] **Step 1: Build the core with no warnings**

Run: `dotnet build core/Kairo.slnx`
Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: Run every core test**

Run:

```bash
docker info
dotnet test --solution core/Kairo.slnx
```

Expected: every test passes, including the new `Kairo.IntegrationTests.Notifications` classes, `OpenApiDocumentTests`, `SecurityPolicyCoverageTests` and the architecture tests.

- [ ] **Step 3: Check the web types and the web app**

Run:

```bash
npm --prefix web run generate:api
git status --short web/src/lib/core-api/schema.ts
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
```

Expected: `git status` prints nothing for `schema.ts`, so the committed types match the committed OpenAPI document. Lint, tests and build pass.

- [ ] **Step 4: Run the deployment tests**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: every test passes.

- [ ] **Step 5: Check for comments and attribution**

Run:

```bash
base=$(git merge-base HEAD develop)
git diff --name-only "$base" -- core deploy | grep -E '\.(cs|ps1|psd1)$' | xargs grep -nE '^\s*(//|# |<#)|/\*' || true
git log "$base"..HEAD --format='%an <%ae>%n%b' | grep -i 'co-authored' || true
```

Expected: both print nothing. The `#nullable disable` line in generated migrations is a directive, not a comment, so the pattern skips it.

- [ ] **Step 6: Check it end to end in Aspire**

With Docker running, start the AppHost: `dotnet run --project core/src/Kairo.AppHost`.

1. Sign in at `http://localhost:8000` as an admin of a workspace that has Google Ads connected. Invite a second admin.
2. As the second admin, connect a product. As the first admin, change the second admin's roles to marketer.
3. Call `GET http://localhost:8000/api/brands/{brandId}/notifications` as the first admin. It returns a `connection.problem` notification with the title "{Product} needs reconnecting".
4. The API's log in the Aspire dashboard shows "No notification sender is set, so an email was only logged: connection.problem …" within about 5 seconds.

Record what you saw in the ledger. With the sender's user secrets set and the `kairo2` publishing status In production, the same check sends a real email; that step needs the sender mailbox, so it waits for the product owner.

- [ ] **Step 7: Commit any fixes**

If any step needed a fix, commit it with a message that says what it fixed, staging only the files you changed.
