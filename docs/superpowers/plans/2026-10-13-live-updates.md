# Kairo Live Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Push status changes from the core and the worker to every open browser within about a second, through a SQL Server outbox read by each API instance and streamed to the browser as Server-Sent Events.

**Architecture:**
- **Publishing.** `ILiveUpdates` in `Kairo.Contracts` writes a row to `live.Outbox` in the same transaction as the change, so the API and the worker publish the same way, and a rollback publishes nothing.
- **Fan-out.** A new `Kairo.Modules.Live` module owns the outbox. One `LiveFeed` per API instance starts with the first stream, reads new rows about once a second with `READPAST`, and hands them to the open streams on that instance. An `OutboxCursor` keeps waiting for a minute for event IDs that committed out of order.
- **Transport.** `GET /api/brands/{brandId}/live` returns `TypedResults.ServerSentEvents`. It sends a heartbeat at once and every 20 seconds, replays from `Last-Event-ID`, re-checks the session and membership every 30 seconds, and closes after 15 minutes.

**Tech Stack:** .NET 10, ASP.NET Core 10 minimal APIs with `TypedResults.ServerSentEvents` and `System.Net.ServerSentEvents.SseItem<T>`, EF Core 10 on SQL Server 2022, `System.Threading.Channels`, xUnit v3 with Testcontainers and `FakeTimeProvider`, Pester 5.9.1 for the deploy scripts.

**Spec:** `docs/superpowers/specs/2026-10-13-live-updates-design.md`

## Global Constraints

- **Worktree and branch.** Work only in `C:\Kairo-worktrees\live-updates` (Git Bash path `/c/Kairo-worktrees/live-updates`), on `feature/live-updates`. Every command in this plan runs from that folder unless a step says otherwise. Never touch `C:\Kairo` or any other worktree. Don't push.
- **Task tracker.** Don't edit `docs/task.md`. Status commits are made separately on `develop`.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user.
  - Stage files by explicit path, never with `git add -A` or `git add .`.
  - Never stage `CLAUDE.md` or `.claude/`.
- **No comments in code** (`CLAUDE.md` §3): no `//`, `#`, `/* */` or `<!-- -->` comments, no XML doc comments, no JSDoc. EF Core migrations go through `bash core/scripts/tidy-migrations.sh`.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build must finish with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container. Run `docker info` first.
- **Running tests.**
  - Whole core: `dotnet test --solution core/Kairo.slnx`.
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`.
- **Module rules stay true.**
  - `Kairo.Modules.Live` exposes exactly one public type, `LiveModule`, and adds no `ProjectReference` of its own.
  - Its tables live only in the `live` schema.
  - Its migration classes are `internal`, which the tidy script handles.
- **Names, copied from the spec.**
  - Stream: `GET /api/brands/{brandId}/live`, `text/event-stream`, with `id:` set to the event ID and `event:` set to the type.
  - Event types are lowercase, dotted and past tense: `workspace.changed`, `team.changed`, `invitations.changed`, `approval_settings.changed`.
  - Timings: poll about every 1 second; heartbeat every 20 seconds; session and membership re-check every 30 seconds; stream lifetime 15 minutes; outbox rows kept 24 hours; clean-up hourly.
- **Versions.** No new NuGet or npm packages. `SseItem<T>`, `TypedResults.ServerSentEvents` and `System.Threading.Channels` ship with the ASP.NET Core 10 shared framework.

## Review Focus

These are the inputs most likely to hurt a real user. Each one is pinned by a test in the task named in brackets.

1. **An event commits after a later event.** A long request takes outbox ID 10, and a quick one takes ID 11 and commits first. The quick event must arrive at once, without waiting for the slow transaction, and the slow event must still arrive when it commits. (Task 4: `An_event_that_commits_after_a_later_one_still_arrives_without_holding_the_later_one_back`.)
2. **A browser reconnects just after that.** The browser saw ID 11, dropped, and ID 10 then committed. The reconnect with `Last-Event-ID: 11` must replay ID 10. (Task 5: `Reconnecting_also_replays_an_event_that_committed_after_the_last_one_seen`.)
3. **A `Last-Event-ID` that belongs to another workspace** is sent, whether forged or from a stale tab. It must get a `reset` and nothing from the other workspace. (Task 5: `Another_workspace_s_event_id_gets_a_reset_and_nothing_from_that_workspace`.)
4. **An admin is demoted to marketer while their stream is open.** They must stop receiving admin-only events straight away, without reconnecting. (Task 8: `A_demoted_admin_stops_seeing_admin_events_without_reconnecting`.)
5. **A removed member's tab stays open.** The stream must end straight away, not at the next 30-second check, and their reconnect must get 404. (Task 8: `Removing_a_member_ends_their_stream_straight_away`; Task 6 covers the 30-second check.)

## Where this plan differs from the spec

1. **Heartbeats are `heartbeat` events, not comment lines.** `SseItem<T>` can't write SSE comments. A browser's `EventSource` ignores named events that nobody listens for, so the effect is the same. One heartbeat is also sent the moment a stream opens, so proxies and the test host forward the response headers straight away.
2. **Events can repeat.** Outbox IDs come from an identity column, and transactions can commit out of order. Two mechanisms cover that:
   - The feed keeps waiting for a missing ID for up to a minute (`OutboxCursor`).
   - A `Last-Event-ID` replay also resends events created in the minute before that ID.

   The web contract therefore says to treat events as "something changed, refetch", which is safe to do twice. The spec's contract already says to refetch rather than render from the payload, so the client code doesn't change.
3. **Reads skip locked rows.** The feed, the replay and the clean-up read the outbox `WITH (READCOMMITTEDLOCK, READPAST)`, so one slow transaction can't stall every workspace's stream.
4. **The feed's lifetime.**
   - The feed starts with the first stream on an instance and then polls every second for the life of that instance, even when no streams are open. If it stopped while idle, its position would go stale, and the next stream would be flooded with old events.
   - The worker and the Migrator never open a stream, so they never start it.
5. **Clean-up runs inside the feed's loop**, once when it starts and then hourly, under a non-blocking application lock. A separate background service would also run in the worker and the Migrator, because modules can't tell which host they're in.
6. **More reset cases.** A `reset` is also sent when the `Last-Event-ID` isn't one of this workspace's events, or when more than 1,000 events were missed.
7. **How the session re-check works.**
   - The session store adds a `kairo_session_key` claim to each new session's ticket.
   - `ISessionDirectory` in `Kairo.Contracts` lets the Live module check that session.
   - Sessions created before this change have no such claim. They get only the membership re-check until the user signs in again, which happens within 14 days.
8. **Removal ends the stream at once.** A `team.changed` event about the stream's own user triggers an immediate re-check. That re-check ends the stream for a removed member and refreshes roles for a demoted one.
9. **SAD sections.** The spec lists §2, §4, §5 and AD-9. SignalR and Memorystore are actually mentioned in §1, §2, §3, §5, §8, §9 and AD-9 (§4 has no mention), so Task 10 updates those.
10. **Unit tests reach internals.** `Kairo.Modules.Live` gets `InternalsVisibleTo` for `Kairo.IntegrationTests`, so `OutboxCursor` can be unit-tested. The clean-up's cutoff is checked by an integration test on its own database, because it moves the clock forward a day.
11. **Tests that move the clock past 24 hours use their own database.** Clean-up deletes rows by the API's clock. A test whose fake clock is a day ahead would otherwise delete other test classes' rows in the shared database.

## File Structure

**Contracts and shared kernel**

| File | Responsibility |
| --- | --- |
| `core/src/Kairo.Contracts/Live/ILiveUpdates.cs` (new) | `ILiveUpdates` and `LiveEvent` |
| `core/src/Kairo.Contracts/Live/LiveAudience.cs` (new) | `LiveAudienceKind` and `LiveAudience` with `Includes` |
| `core/src/Kairo.Contracts/Identity/ISessionDirectory.cs` (new) | Whether a session key still names a live session |
| `core/src/Kairo.SharedKernel/Access/KairoClaims.cs` (modify) | `SessionKey` claim name |

**The Live module, `core/src/Modules/Kairo.Modules.Live/` (new)**

| File | Responsibility |
| --- | --- |
| `Kairo.Modules.Live.csproj` | Project, with `InternalsVisibleTo` for the integration tests |
| `LiveModule.cs` | Registrations, endpoint mapping and migration |
| `Data/OutboxEvent.cs` | Outbox row, and its audience |
| `Data/LiveDbContext.cs` and `Data/LiveDbContextFactory.cs` | Schema `live`, the `Outbox` table, and lock-skipping reads |
| `Data/Migrations/*_Outbox.cs` | Table, indexes and row-level security |
| `Publishing/LiveUpdates.cs` | `ILiveUpdates`: validates the type and writes the row |
| `Delivery/LiveTimings.cs` | Every interval in one place |
| `Delivery/OutboxCursor.cs` | Which outbox IDs are new, including late commits |
| `Delivery/LiveFeed.cs` | Per-instance poller, subscriptions and the clean-up schedule |
| `Delivery/LiveSubscription.cs` | One stream's queue of rows |
| `Delivery/Viewer.cs` | Who a stream is for, and what they may see |
| `Delivery/LiveItems.cs` | The `SseItem<string>` values: event, heartbeat and reset |
| `Delivery/LiveStream.cs` | One stream: replay, heartbeats, re-checks and lifetime |
| `Delivery/LiveEndpoints.cs` | `GET /live` |
| `Delivery/OutboxCleanup.cs` | Deletes rows older than 24 hours |

**Changes elsewhere in the core**

| File | Change |
| --- | --- |
| `core/src/Kairo.Api/Program.cs`, `core/src/Kairo.Worker/Program.cs` and `core/src/Kairo.Migrator/MigratorHost.cs` | Register `LiveModule` |
| `core/Kairo.slnx` | Add the project |
| `core/src/Modules/Kairo.Modules.Identity/Sessions/DatabaseTicketStore.cs` | Add the session key claim |
| `core/src/Modules/Kairo.Modules.Identity/Sessions/SessionDirectory.cs` (new) | `ISessionDirectory` |
| `core/src/Modules/Kairo.Modules.Identity/IdentityLiveEvents.cs` (new) | The Identity module's event factories |
| `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs` | Register `ISessionDirectory` |
| `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`, `Invitations/InvitationEndpoints.cs` and `Approvals/ApprovalSettingsEndpoints.cs` | Publish events |
| `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceEndpoints.cs` | Publish `workspace.changed` |
| `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts` | Regenerated |

**Tests, `core/tests/Kairo.IntegrationTests/Live/` (new)**

| File | Covers |
| --- | --- |
| `LiveListener.cs` | Test client: opens a stream, parses SSE and moves the fake clock |
| `LivePublishing.cs` | Publishes as a worker job would |
| `OutboxRows.cs` | Raw SQL reads and bulk inserts on the outbox |
| `IsolatedDatabase.cs` | A fresh, migrated database |
| `LiveAudienceTests.cs` | Audiences |
| `OutboxStorageTests.cs` | Publishing |
| `OutboxCursorTests.cs` | The cursor |
| `LiveDeliveryTests.cs` | Delivery |
| `LiveResumeTests.cs` | Resume |
| `LiveRetentionTests.cs` | Retention and clean-up |
| `LiveAccessTests.cs` | Access |
| `LiveEventsTests.cs` | The four events |

**Deployment and docs**

| File | Change |
| --- | --- |
| `deploy/Kairo.Deploy/Release.ps1` and `deploy/tests/Release.Tests.ps1` | 60-minute request timeout for the API and the web app |
| `docs/Kairo-SAD.md` | SSE and the outbox replace SignalR and Memorystore |

---

### Task 1: Contracts and an empty Live module

**Files:**
- Create: `core/src/Kairo.Contracts/Live/ILiveUpdates.cs`
- Create: `core/src/Kairo.Contracts/Live/LiveAudience.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Kairo.Modules.Live.csproj`
- Create: `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`
- Modify: `core/Kairo.slnx`
- Modify: `core/src/Kairo.Api/Program.cs`
- Modify: `core/src/Kairo.Worker/Program.cs`
- Modify: `core/src/Kairo.Migrator/MigratorHost.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveAudienceTests.cs`

**Interfaces:**
- Produces:
  - `Kairo.Contracts.Live.ILiveUpdates.PublishAsync(LiveEvent liveEvent, CancellationToken cancellationToken) : Task`
  - `LiveEvent(string Type, object Payload, LiveAudience Audience)`
  - `LiveAudienceKind { Everyone, Roles, Users }`
  - `LiveAudience` with:
    - `LiveAudience.Everyone`
    - `LiveAudience.Roles(BrandRoles roles)`
    - `LiveAudience.Users(params Guid[] users)`
    - the properties `Kind`, `RoleFilter` and `UserFilter`
    - `Includes(Guid userId, BrandRoles roles) : bool`
  - `Kairo.Modules.Live.LiveModule : IModule`

- [ ] **Step 1: Write the failing audience tests**

Create `core/tests/Kairo.IntegrationTests/Live/LiveAudienceTests.cs`:

```csharp
using Kairo.Contracts.Live;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveAudienceTests
{
    private static readonly Guid Priya = Guid.NewGuid();
    private static readonly Guid Arjun = Guid.NewGuid();

    [Fact]
    public void Everyone_includes_every_member()
    {
        Assert.True(LiveAudience.Everyone.Includes(Priya, BrandRoles.Marketer));
        Assert.True(LiveAudience.Everyone.Includes(Arjun, BrandRoles.Approver));
    }

    [Fact]
    public void A_role_audience_includes_members_holding_any_of_its_roles()
    {
        var audience = LiveAudience.Roles(BrandRoles.Admin | BrandRoles.Approver);

        Assert.True(audience.Includes(Priya, BrandRoles.Approver));
        Assert.True(audience.Includes(Priya, BrandRoles.Admin | BrandRoles.Marketer));
        Assert.False(audience.Includes(Priya, BrandRoles.Marketer));
    }

    [Fact]
    public void A_user_audience_includes_only_the_people_it_names()
    {
        var audience = LiveAudience.Users(Priya);

        Assert.True(audience.Includes(Priya, BrandRoles.Marketer));
        Assert.False(audience.Includes(Arjun, BrandRoles.All));
    }

    [Fact]
    public void An_audience_says_what_kind_it_is()
    {
        Assert.Equal(LiveAudienceKind.Everyone, LiveAudience.Everyone.Kind);
        Assert.Equal((LiveAudienceKind.Roles, BrandRoles.Admin), (LiveAudience.Roles(BrandRoles.Admin).Kind, LiveAudience.Roles(BrandRoles.Admin).RoleFilter));
        Assert.Equal([Priya, Arjun], LiveAudience.Users(Priya, Arjun).UserFilter);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build core/tests/Kairo.IntegrationTests`
Expected: FAIL, with `error CS0234: The type or namespace name 'Live' does not exist in the namespace 'Kairo.Contracts'`.

- [ ] **Step 3: Add the contracts**

Create `core/src/Kairo.Contracts/Live/ILiveUpdates.cs`:

```csharp
namespace Kairo.Contracts.Live;

public interface ILiveUpdates
{
    Task PublishAsync(LiveEvent liveEvent, CancellationToken cancellationToken);
}

public sealed record LiveEvent(string Type, object Payload, LiveAudience Audience);
```

Create `core/src/Kairo.Contracts/Live/LiveAudience.cs`:

```csharp
using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Live;

public enum LiveAudienceKind
{
    Everyone,
    Roles,
    Users,
}

public sealed class LiveAudience
{
    private LiveAudience(LiveAudienceKind kind, BrandRoles roleFilter, IReadOnlyList<Guid> userFilter)
    {
        Kind = kind;
        RoleFilter = roleFilter;
        UserFilter = userFilter;
    }

    public static LiveAudience Everyone { get; } = new(LiveAudienceKind.Everyone, BrandRoles.None, []);

    public LiveAudienceKind Kind { get; }

    public BrandRoles RoleFilter { get; }

    public IReadOnlyList<Guid> UserFilter { get; }

    public static LiveAudience Roles(BrandRoles roles) => new(LiveAudienceKind.Roles, roles, []);

    public static LiveAudience Users(params Guid[] users) => new(LiveAudienceKind.Users, BrandRoles.None, users);

    public bool Includes(Guid userId, BrandRoles roles) => Kind switch
    {
        LiveAudienceKind.Everyone => true,
        LiveAudienceKind.Roles => (roles & RoleFilter) != BrandRoles.None,
        LiveAudienceKind.Users => UserFilter.Contains(userId),
        _ => false,
    };
}
```

- [ ] **Step 4: Run the audience tests to verify they pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveAudienceTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Add the empty module and watch the host tests fail**

Create `core/src/Modules/Kairo.Modules.Live/Kairo.Modules.Live.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

Create `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`:

```csharp
namespace Kairo.Modules.Live;

public sealed class LiveModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

In `core/Kairo.slnx`, add this line after the `Kairo.Modules.Launch` line:

```xml
    <Project Path="src/Modules/Kairo.Modules.Live/Kairo.Modules.Live.csproj" />
```

Run: `dotnet test --project core/tests/Kairo.Api.Tests`
Expected: FAIL in `Registers_every_module`. The expected list contains `Kairo.Modules.Live`, because `ModuleFolders` finds the new folder, but the API doesn't register it yet.

- [ ] **Step 6: Register the module in every host**

In `core/src/Kairo.Api/Program.cs` and `core/src/Kairo.Worker/Program.cs`:
- add `using Kairo.Modules.Live;` after `using Kairo.Modules.Launch;`
- add `new LiveModule(),` after `new NotificationsModule(),` in the `modules` array

In `core/src/Kairo.Migrator/MigratorHost.cs`, make the same two changes.

- [ ] **Step 7: Run the host tests to verify they pass**

Run:

```bash
dotnet build core/Kairo.slnx
dotnet test --project core/tests/Kairo.Api.Tests
dotnet test --project core/tests/Kairo.Worker.Tests
dotnet test --project core/tests/Kairo.ArchitectureTests
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
```

Expected: the build succeeds with 0 warnings, and every test passes, including `Module_exposes_only_its_module_class` for `Kairo.Modules.Live`.

- [ ] **Step 8: Commit**

```bash
git add core/src/Kairo.Contracts/Live core/src/Modules/Kairo.Modules.Live core/Kairo.slnx core/src/Kairo.Api/Program.cs core/src/Kairo.Worker/Program.cs core/src/Kairo.Migrator/MigratorHost.cs core/tests/Kairo.IntegrationTests/Live/LiveAudienceTests.cs
git commit -m "Add the live updates contract and an empty Live module"
```

---

### Task 2: The outbox and publishing

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Live/Data/OutboxEvent.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Data/LiveDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Data/LiveDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Data/Migrations/<timestamp>_Outbox.cs`, plus its `.Designer.cs` and `LiveDbContextModelSnapshot.cs` (generated)
- Create: `core/src/Modules/Kairo.Modules.Live/Publishing/LiveUpdates.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LivePublishing.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/OutboxRows.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/OutboxStorageTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `ILiveUpdates`, `LiveEvent`, `LiveAudience`, `LiveAudienceKind`
  - existing: `ModuleDbContext`, `IBrandOwned`, `KairoDatabase.UseBrandAsync(Guid, CancellationToken)`, `BeginTransactionAsync`, `RollbackAsync`, `CommitAsync`
- Produces:
  - `OutboxEvent`, with:
    - `long Id`, `Guid BrandId`, `string Type`, `string Payload`
    - `LiveAudienceKind AudienceKind`, `BrandRoles AudienceRoles`, `string? AudienceUsers`
    - `DateTimeOffset CreatedAt`
    - `LiveAudience Audience()`
  - `LiveDbContext`, with:
    - `SchemaName = "live"`
    - `DbSet<OutboxEvent> Outbox`
    - `IQueryable<OutboxEvent> Unlocked()`, untracked and skipping locked rows
  - the test helpers `LivePublishing.PublishAsync(KairoApiFactory api, Guid brandId, string type, object? payload = null, LiveAudience? audience = null)` and `OutboxRows.ReadAsync(string connectionString, Guid brandId)`, which returns `IReadOnlyList<OutboxRow>`

- [ ] **Step 1: Write the test helpers**

Create `core/tests/Kairo.IntegrationTests/Live/LivePublishing.cs`:

```csharp
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

internal static class LivePublishing
{
    public static async Task PublishAsync(KairoApiFactory api, Guid brandId, string type, object? payload = null, LiveAudience? audience = null)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<ILiveUpdates>().PublishAsync(
            new LiveEvent(type, payload ?? new { }, audience ?? LiveAudience.Everyone),
            TestContext.Current.CancellationToken);
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Live/OutboxRows.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Live;

internal sealed record OutboxRow(long Id, string Type, string Payload, string AudienceKind, int AudienceRoles, string? AudienceUsers);

internal static class OutboxRows
{
    public static async Task<IReadOnlyList<OutboxRow>> ReadAsync(string connectionString, Guid brandId)
    {
        await using var connection = await OpenForBrandAsync(connectionString, brandId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Type, Payload, AudienceKind, AudienceRoles, AudienceUsers
            FROM live.Outbox
            WHERE BrandId = @brand
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var rows = new List<OutboxRow>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new OutboxRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    public static async Task InsertAsync(string connectionString, Guid brandId, string type, int count, DateTimeOffset createdAt)
    {
        await using var connection = await OpenForBrandAsync(connectionString, brandId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO live.Outbox (BrandId, Type, Payload, AudienceKind, AudienceRoles, AudienceUsers, CreatedAt)
            SELECT TOP (@count) @brand, @type, N'{}', N'Everyone', 0, NULL, @createdAt
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
            """;
        command.Parameters.AddWithValue("@count", count);
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SqlConnection> OpenForBrandAsync(string connectionString, Guid brandId)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = connection.CreateCommand();
        context.CommandText = "EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @brand;";
        context.Parameters.AddWithValue("@brand", brandId);
        await context.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
```

- [ ] **Step 2: Write the failing storage tests**

Create `core/tests/Kairo.IntegrationTests/Live/OutboxStorageTests.cs`:

```csharp
using System.Text.Json;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class OutboxStorageTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_published_event_is_stored_for_its_workspace_with_a_camel_case_payload()
    {
        var brandId = Guid.NewGuid();

        await LivePublishing.PublishAsync(_api, brandId, "test.happened", new { UserId = brandId, Removed = true });

        var row = Assert.Single(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId));
        Assert.Equal("test.happened", row.Type);
        Assert.Equal($$"""{"userId":"{{brandId}}","removed":true}""", row.Payload);
        Assert.Equal("Everyone", row.AudienceKind);
    }

    [Fact]
    public async Task The_audience_is_stored_with_the_event()
    {
        var brandId = Guid.NewGuid();
        var priya = Guid.NewGuid();
        var arjun = Guid.NewGuid();

        await LivePublishing.PublishAsync(_api, brandId, "test.for_admins", audience: LiveAudience.Roles(BrandRoles.Admin | BrandRoles.Approver));
        await LivePublishing.PublishAsync(_api, brandId, "test.for_people", audience: LiveAudience.Users(priya, arjun));

        var rows = await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId);
        Assert.Equal(("Roles", 5), (rows[0].AudienceKind, rows[0].AudienceRoles));
        Assert.Equal("Users", rows[1].AudienceKind);
        Assert.Equal([priya, arjun], JsonSerializer.Deserialize<Guid[]>(rows[1].AudienceUsers!));
    }

    [Fact]
    public async Task An_event_published_in_a_rolled_back_transaction_is_not_stored()
    {
        var brandId = Guid.NewGuid();
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var live = scope.ServiceProvider.GetRequiredService<ILiveUpdates>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            await live.PublishAsync(new LiveEvent("test.discarded", new { }, LiveAudience.Everyone), Token);
            await database.RollbackAsync(Token);
        }

        Assert.Empty(await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId));
    }

    [Fact]
    public async Task Publishing_needs_a_workspace()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var live = scope.ServiceProvider.GetRequiredService<ILiveUpdates>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            live.PublishAsync(new LiveEvent("test.happened", new { }, LiveAudience.Everyone), Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("team")]
    [InlineData("Team.Changed")]
    [InlineData("team changed")]
    [InlineData("team.")]
    public async Task Event_types_are_lowercase_and_dotted(string type)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => LivePublishing.PublishAsync(_api, Guid.NewGuid(), type));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.OutboxStorageTests"`
Expected: FAIL, with `System.InvalidOperationException: No service for type 'Kairo.Contracts.Live.ILiveUpdates' has been registered.`

- [ ] **Step 4: Add the outbox entity and DbContext**

Create `core/src/Modules/Kairo.Modules.Live/Data/OutboxEvent.cs`:

```csharp
using System.Text.Json;
using Kairo.Contracts.Live;

namespace Kairo.Modules.Live.Data;

internal sealed class OutboxEvent : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "{}";

    public LiveAudienceKind AudienceKind { get; set; }

    public BrandRoles AudienceRoles { get; set; }

    public string? AudienceUsers { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public LiveAudience Audience() => AudienceKind switch
    {
        LiveAudienceKind.Roles => LiveAudience.Roles(AudienceRoles),
        LiveAudienceKind.Users => LiveAudience.Users(JsonSerializer.Deserialize<Guid[]>(AudienceUsers ?? "[]") ?? []),
        _ => LiveAudience.Everyone,
    };
}
```

Create `core/src/Modules/Kairo.Modules.Live/Data/LiveDbContext.cs`:

```csharp
namespace Kairo.Modules.Live.Data;

internal sealed class LiveDbContext(DbContextOptions<LiveDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "live";

    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();

    protected override string Schema => SchemaName;

    public IQueryable<OutboxEvent> Unlocked() =>
        Outbox.FromSql($"SELECT * FROM [live].[Outbox] WITH (READCOMMITTEDLOCK, READPAST)").AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxEvent>(row =>
        {
            row.ToTable("Outbox");
            row.HasIndex(e => new { e.BrandId, e.Id });
            row.HasIndex(e => e.CreatedAt);
            row.Property(e => e.Type).HasMaxLength(100);
            row.Property(e => e.AudienceKind).HasConversion<string>().HasMaxLength(20);
        });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Live/Data/LiveDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Live.Data;

internal sealed class LiveDbContextFactory : IDesignTimeDbContextFactory<LiveDbContext>
{
    public LiveDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<LiveDbContext>(
            LiveDbContext.SchemaName,
            (options, database) => new LiveDbContext(options, database));
}
```

- [ ] **Step 5: Add the publisher and register both**

Create `core/src/Modules/Kairo.Modules.Live/Publishing/LiveUpdates.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Contracts.Live;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Publishing;

internal sealed partial class LiveUpdates(LiveDbContext db, TimeProvider time) : ILiveUpdates
{
    public async Task PublishAsync(LiveEvent liveEvent, CancellationToken cancellationToken)
    {
        if (!EventType().IsMatch(liveEvent.Type))
        {
            throw new ArgumentException($"'{liveEvent.Type}' isn't a lowercase, dotted event type.", nameof(liveEvent));
        }

        var audience = liveEvent.Audience;
        db.Outbox.Add(new OutboxEvent
        {
            Type = liveEvent.Type,
            Payload = JsonSerializer.Serialize(liveEvent.Payload, JsonSerializerOptions.Web),
            AudienceKind = audience.Kind,
            AudienceRoles = audience.RoleFilter,
            AudienceUsers = audience.Kind == LiveAudienceKind.Users ? JsonSerializer.Serialize(audience.UserFilter) : null,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    [GeneratedRegex("^[a-z_]+(\\.[a-z_]+)+$")]
    private static partial Regex EventType();
}
```

Replace `core/src/Modules/Kairo.Modules.Live/LiveModule.cs` with:

```csharp
using Kairo.Contracts.Live;
using Kairo.Modules.Live.Data;
using Kairo.Modules.Live.Publishing;

namespace Kairo.Modules.Live;

public sealed class LiveModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LiveDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, LiveDbContext.SchemaName));
        services.AddScoped<ILiveUpdates, LiveUpdates>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<LiveDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 6: Generate the migration and add row-level security**

Run:

```bash
dotnet tool restore
dotnet ef migrations add Outbox --project core/src/Modules/Kairo.Modules.Live --startup-project core/src/Kairo.Migrator --context LiveDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Live/Data/Migrations/*.cs
```

Open the generated `core/src/Modules/Kairo.Modules.Live/Data/Migrations/*_Outbox.cs`.

1. **Check the columns.** Its `CreateTable` for `live.Outbox` must have these columns:
   - `Id`: `bigint`, identity
   - `BrandId`: `uniqueidentifier`
   - `Type`: `nvarchar(100)`
   - `Payload`: `nvarchar(max)`
   - `AudienceKind`: `nvarchar(20)`
   - `AudienceRoles`: `int`
   - `AudienceUsers`: `nvarchar(max)`, nullable
   - `CreatedAt`: `datetimeoffset`

   It must also create the indexes `IX_Outbox_BrandId_Id` and `IX_Outbox_CreatedAt`.
2. **Add row-level security.** Add these as the last two lines of `Up`:

   ```csharp
               migrationBuilder.CreateBrandAccessPredicate("live");
               migrationBuilder.AddBrandSeparation("live", "Outbox");
   ```
3. **Undo it in `Down`.** Add this as the first line of `Down`:

   ```csharp
               migrationBuilder.DropBrandSeparation("live");
   ```

- [ ] **Step 7: Run the storage tests and the policy coverage test**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.OutboxStorageTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.SecurityPolicyCoverageTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: every test passes. In particular, `Every_table_with_a_brand_column_has_row_level_security` passes with `live.Outbox`, and `Module_tables_live_in_the_module_schema` passes for `Kairo.Modules.Live`.

- [ ] **Step 8: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Live core/tests/Kairo.IntegrationTests/Live/LivePublishing.cs core/tests/Kairo.IntegrationTests/Live/OutboxRows.cs core/tests/Kairo.IntegrationTests/Live/OutboxStorageTests.cs
git commit -m "Store live events in an outbox in the same transaction as the change"
```

---

### Task 3: The outbox cursor

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveTimings.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/OutboxCursor.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/OutboxCursorTests.cs`

**Interfaces:**
- Produces:
  - `LiveTimings`, with:
    - `PollInterval` (1 s), `HeartbeatInterval` (20 s), `AccessCheckInterval` (30 s)
    - `Lifetime` (15 min), `GapPatience` (1 min), `Retention` (24 h), `CleanupInterval` (1 h)
  - `OutboxCursor`, with:
    - `static OutboxCursor Start(long highest, IReadOnlyList<long> recent, DateTimeOffset now)`
    - `long Floor`
    - `IReadOnlyList<long> Advance(IReadOnlyList<long> ids, DateTimeOffset now)`, which returns the IDs that are new

- [ ] **Step 1: Write the failing cursor tests**

Create `core/tests/Kairo.IntegrationTests/Live/OutboxCursorTests.cs`:

```csharp
using Kairo.Modules.Live.Delivery;

namespace Kairo.IntegrationTests.Live;

public sealed class OutboxCursorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_cursor_starts_after_the_newest_event()
    {
        var cursor = OutboxCursor.Start(41, [], Now);

        Assert.Equal(41, cursor.Floor);
    }

    [Fact]
    public void New_events_are_accepted_once_in_order()
    {
        var cursor = OutboxCursor.Start(10, [], Now);

        Assert.Equal([11L, 12L], cursor.Advance([11, 12], Now));
        Assert.Empty(cursor.Advance([11, 12], Now));
        Assert.Equal(12, cursor.Floor);
    }

    [Fact]
    public void An_event_that_commits_after_a_later_one_is_still_accepted()
    {
        var cursor = OutboxCursor.Start(10, [], Now);

        Assert.Equal([12L], cursor.Advance([12], Now));
        Assert.Equal(10, cursor.Floor);
        Assert.Equal([11L], cursor.Advance([11, 12], Now.AddSeconds(5)));
        Assert.Equal(12, cursor.Floor);
    }

    [Fact]
    public void Filling_part_of_a_gap_keeps_waiting_for_the_rest()
    {
        var cursor = OutboxCursor.Start(10, [], Now);
        cursor.Advance([14], Now);

        Assert.Equal([12L], cursor.Advance([12, 14], Now));
        Assert.Equal(10, cursor.Floor);
        Assert.Equal([11L, 13L], cursor.Advance([11, 12, 13, 14], Now));
        Assert.Equal(14, cursor.Floor);
    }

    [Fact]
    public void A_gap_that_never_fills_is_given_up_after_a_minute()
    {
        var cursor = OutboxCursor.Start(10, [], Now);
        cursor.Advance([13], Now);

        cursor.Advance([13], Now + LiveTimings.GapPatience - TimeSpan.FromSeconds(1));
        Assert.Equal(10, cursor.Floor);
        cursor.Advance([13], Now + LiveTimings.GapPatience);
        Assert.Equal(13, cursor.Floor);
        Assert.Empty(cursor.Advance([11], Now + LiveTimings.GapPatience));
    }

    [Fact]
    public void Starting_treats_recent_events_as_seen_and_waits_for_their_gaps()
    {
        var cursor = OutboxCursor.Start(20, [18, 20], Now);

        Assert.Equal(18, cursor.Floor);
        Assert.Equal([19L], cursor.Advance([18, 19, 20], Now));
        Assert.Equal(20, cursor.Floor);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build core/tests/Kairo.IntegrationTests`
Expected: FAIL, with `error CS0234: The type or namespace name 'Delivery' does not exist in the namespace 'Kairo.Modules.Live'`.

- [ ] **Step 3: Add the timings and the cursor**

Create `core/src/Modules/Kairo.Modules.Live/Delivery/LiveTimings.cs`:

```csharp
namespace Kairo.Modules.Live.Delivery;

internal static class LiveTimings
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan AccessCheckInterval = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan GapPatience = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
}
```

Create `core/src/Modules/Kairo.Modules.Live/Delivery/OutboxCursor.cs`:

```csharp
namespace Kairo.Modules.Live.Delivery;

internal sealed class OutboxCursor
{
    private readonly List<Gap> _gaps = [];
    private long _highest;

    private OutboxCursor(long highest) => _highest = highest;

    public long Floor => _gaps.Count == 0 ? _highest : _gaps.Min(gap => gap.From) - 1;

    public static OutboxCursor Start(long highest, IReadOnlyList<long> recent, DateTimeOffset now)
    {
        var cursor = new OutboxCursor(recent.Count == 0 ? highest : recent.Min() - 1);
        cursor.Advance(recent, now);
        return cursor;
    }

    public IReadOnlyList<long> Advance(IReadOnlyList<long> ids, DateTimeOffset now)
    {
        var accepted = new List<long>();
        foreach (var id in ids.Order())
        {
            if (id > _highest)
            {
                if (id > _highest + 1)
                {
                    _gaps.Add(new Gap(_highest + 1, id - 1, now));
                }

                _highest = id;
                accepted.Add(id);
            }
            else if (FillGap(id))
            {
                accepted.Add(id);
            }
        }

        _gaps.RemoveAll(gap => now - gap.OpenedAt >= LiveTimings.GapPatience);
        return accepted;
    }

    private bool FillGap(long id)
    {
        var index = _gaps.FindIndex(gap => gap.From <= id && id <= gap.To);
        if (index < 0)
        {
            return false;
        }

        var gap = _gaps[index];
        _gaps.RemoveAt(index);
        if (gap.From < id)
        {
            _gaps.Add(gap with { To = id - 1 });
        }

        if (id < gap.To)
        {
            _gaps.Add(gap with { From = id + 1 });
        }

        return true;
    }

    private sealed record Gap(long From, long To, DateTimeOffset OpenedAt);
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.OutboxCursorTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Live/Delivery/LiveTimings.cs core/src/Modules/Kairo.Modules.Live/Delivery/OutboxCursor.cs core/tests/Kairo.IntegrationTests/Live/OutboxCursorTests.cs
git commit -m "Track which outbox events are new, including ones that commit out of order"
```

---

### Task 4: The feed and the stream endpoint

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveSubscription.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveFeed.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/Viewer.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveItems.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs`
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`
- Modify (regenerated): `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveListener.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveDeliveryTests.cs`

**Interfaces:**
- Consumes:
  - from Task 2: `LiveDbContext.Unlocked()`, `OutboxEvent` and `OutboxEvent.Audience()`
  - from Task 3: `OutboxCursor` and `LiveTimings`
- Produces:
  - `LiveFeed.SubscribeAsync(Guid brandId, CancellationToken) : Task<LiveSubscription>`
  - `LiveSubscription.NextAsync(TimeSpan wait, CancellationToken) : Task<OutboxEvent?>` and `Dispose()`
  - `Viewer(Guid brandId, Guid userId, BrandRoles roles)`, with `BrandId`, `UserId`, `Roles { get; set; }` and `CanSee(OutboxEvent)`
  - `LiveItems.Heartbeat()`, `LiveItems.Reset()` and `LiveItems.From(OutboxEvent)`, each returning `SseItem<string>`
  - `LiveStream.ReadAsync(Viewer viewer, CancellationToken) : IAsyncEnumerable<SseItem<string>>`
  - `LiveEndpoints.MapLive(IEndpointRouteBuilder)`
  - the test helper `LiveListener`, with:
    - `OpenAsync(api, browser, brandId, lastEventId?)`
    - `Status` and `ContentType`
    - `NextAsync(maxSteps = 100)`, which skips heartbeats
    - `NextOrEndAsync(TimeSpan step, int maxSteps = 100)`, which returns `null` when the stream ends
    - the record `LiveMessage(string Type, string? Id, JsonElement Data)`

- [ ] **Step 1: Write the test listener**

Create `core/tests/Kairo.IntegrationTests/Live/LiveListener.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

internal sealed record LiveMessage(string Type, string? Id, JsonElement Data);

internal sealed class LiveListener : IAsyncDisposable
{
    private static readonly TimeSpan RealWait = TimeSpan.FromMilliseconds(50);

    private readonly KairoApiFactory _api;
    private readonly HttpResponseMessage _response;
    private readonly StreamReader? _reader;
    private Task<LiveMessage?>? _pending;

    private LiveListener(KairoApiFactory api, HttpResponseMessage response, StreamReader? reader)
    {
        _api = api;
        _response = response;
        _reader = reader;
    }

    public HttpStatusCode Status => _response.StatusCode;

    public string? ContentType => _response.Content.Headers.ContentType?.MediaType;

    public static async Task<LiveListener> OpenAsync(KairoApiFactory api, HttpClient browser, Guid brandId, string? lastEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/brands/{brandId}/live");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        var response = await browser.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var reader = response.IsSuccessStatusCode
            ? new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), Encoding.UTF8)
            : null;
        return new LiveListener(api, response, reader);
    }

    public async Task<LiveMessage> NextAsync(int maxSteps = 100) =>
        await NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps) ?? throw new InvalidOperationException("The stream ended.");

    public async Task<LiveMessage?> NextOrEndAsync(TimeSpan step, int maxSteps = 100)
    {
        var steps = 0;
        while (true)
        {
            _pending ??= ReadMessageAsync();
            if (await Task.WhenAny(_pending, Task.Delay(RealWait, TestContext.Current.CancellationToken)) == _pending)
            {
                var message = await _pending;
                _pending = null;
                if (message is { Type: "heartbeat" })
                {
                    continue;
                }

                return message;
            }

            if (++steps > maxSteps)
            {
                throw new TimeoutException($"Nothing arrived within {maxSteps} steps of {step}.");
            }

            _api.Clock.Advance(step);
        }
    }

    public ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        _response.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<LiveMessage?> ReadMessageAsync()
    {
        if (_reader is null)
        {
            return null;
        }

        string? type = null;
        string? id = null;
        var data = new StringBuilder();
        while (true)
        {
            var line = await _reader.ReadLineAsync(TestContext.Current.CancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                if (type is null && id is null && data.Length == 0)
                {
                    continue;
                }

                using var parsed = JsonDocument.Parse(data.Length == 0 ? "{}" : data.ToString());
                return new LiveMessage(type ?? "message", id, parsed.RootElement.Clone());
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    type = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    data.Append(value);
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Write the failing delivery tests**

Create `core/tests/Kairo.IntegrationTests/Live/LiveDeliveryTests.cs`:

```csharp
using System.Net;
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveDeliveryTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_stream_is_an_event_stream()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        Assert.Equal(HttpStatusCode.OK, listener.Status);
        Assert.Equal("text/event-stream", listener.ContentType);
    }

    [Fact]
    public async Task An_event_published_outside_a_request_reaches_the_workspace_s_streams()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await LivePublishing.PublishAsync(_api, brandId, "test.happened", new { Name = "Acme" });

        var message = await listener.NextAsync();
        Assert.Equal("test.happened", message.Type);
        Assert.False(string.IsNullOrEmpty(message.Id));
        Assert.Equal("Acme", message.Data.GetProperty("name").GetString());
    }

    [Fact]
    public async Task One_workspace_never_sees_another_workspace_s_events()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, first);

        await LivePublishing.PublishAsync(_api, second, "test.elsewhere");
        await LivePublishing.PublishAsync(_api, first, "test.here");

        Assert.Equal("test.here", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Role_and_user_audiences_reach_only_their_listeners()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var ownerStream = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var marketerStream = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        await LivePublishing.PublishAsync(_api, brandId, "test.for_admins", audience: LiveAudience.Roles(BrandRoles.Admin));
        await LivePublishing.PublishAsync(_api, brandId, "test.for_marketer", audience: LiveAudience.Users(marketer.UserId));
        await LivePublishing.PublishAsync(_api, brandId, "test.for_everyone");

        var ownerFirst = await ownerStream.NextAsync();
        var ownerSecond = await ownerStream.NextAsync();
        var marketerFirst = await marketerStream.NextAsync();
        var marketerSecond = await marketerStream.NextAsync();
        Assert.Equal(("test.for_admins", "test.for_everyone"), (ownerFirst.Type, ownerSecond.Type));
        Assert.Equal(("test.for_marketer", "test.for_everyone"), (marketerFirst.Type, marketerSecond.Type));
    }

    [Fact]
    public async Task An_event_that_commits_after_a_later_one_still_arrives_without_holding_the_later_one_back()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await using var slow = _api.Services.CreateAsyncScope();
        var database = slow.ServiceProvider.GetRequiredService<KairoDatabase>();
        var live = slow.ServiceProvider.GetRequiredService<ILiveUpdates>();
        await database.UseBrandAsync(brandId, Token);
        await database.BeginTransactionAsync(Token);
        await live.PublishAsync(new LiveEvent("test.slow", new { }, LiveAudience.Everyone), Token);

        await LivePublishing.PublishAsync(_api, brandId, "test.quick");
        Assert.Equal("test.quick", (await listener.NextAsync()).Type);

        await database.CommitAsync(Token);
        Assert.Equal("test.slow", (await listener.NextAsync()).Type);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveDeliveryTests"`
Expected: FAIL. `The_stream_is_an_event_stream` gets `NotFound` instead of `OK`, because nothing is mapped at `/live` yet. The other tests throw `InvalidOperationException: The stream ended.`, because the 404 response has no stream to read.

- [ ] **Step 4: Add the subscription and the feed**

Create `core/src/Modules/Kairo.Modules.Live/Delivery/LiveSubscription.cs`:

```csharp
using System.Threading.Channels;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveSubscription(Guid brandId, TimeProvider time, LiveFeed feed) : IDisposable
{
    private readonly Channel<OutboxEvent> _events = Channel.CreateUnbounded<OutboxEvent>(new UnboundedChannelOptions { SingleReader = true });

    public Guid BrandId => brandId;

    public void Offer(OutboxEvent row) => _events.Writer.TryWrite(row);

    public async Task<OutboxEvent?> NextAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (_events.Reader.TryRead(out var row))
        {
            return row;
        }

        if (wait <= TimeSpan.Zero)
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(wait, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await _events.Reader.WaitToReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return _events.Reader.TryRead(out row) ? row : null;
    }

    public void Dispose()
    {
        _events.Writer.TryComplete();
        feed.Unsubscribe(this);
    }
}
```

Create `core/src/Modules/Kairo.Modules.Live/Delivery/LiveFeed.cs`:

```csharp
using System.Collections.Concurrent;
using Kairo.Modules.Live.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveFeed(IServiceScopeFactory scopes, TimeProvider time, ILogger<LiveFeed> logger) : IAsyncDisposable
{
    private const int BatchSize = 1000;

    private readonly ConcurrentDictionary<LiveSubscription, byte> _subscriptions = new();
    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private OutboxCursor? _cursor;
    private Task? _loop;

    public async Task<LiveSubscription> SubscribeAsync(Guid brandId, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        var subscription = new LiveSubscription(brandId, time, this);
        _subscriptions.TryAdd(subscription, 0);
        return subscription;
    }

    public void Unsubscribe(LiveSubscription subscription) => _subscriptions.TryRemove(subscription, out _);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        _stopping.Dispose();
        _starting.Dispose();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _loop) is not null)
        {
            return;
        }

        await _starting.WaitAsync(cancellationToken);
        try
        {
            if (_loop is null)
            {
                _cursor = await StartCursorAsync(cancellationToken);
                Volatile.Write(ref _loop, Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None));
            }
        }
        finally
        {
            _starting.Release();
        }
    }

    private async Task<OutboxCursor> StartCursorAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var db = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
        var now = time.GetUtcNow();
        var since = now - LiveTimings.GapPatience;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var highest = await db.Unlocked().MaxAsync(row => (long?)row.Id, cancellationToken) ?? 0;
            var recent = await db.Unlocked()
                .Where(row => row.CreatedAt >= since)
                .Select(row => row.Id)
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);
            return OutboxCursor.Start(highest, recent, now);
        }
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Reading live updates failed.");
            }

            try
            {
                await Task.Delay(LiveTimings.PollInterval, time, stopping);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var cursor = _cursor!;
        var floor = cursor.Floor;
        List<OutboxEvent> rows;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var db = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
            await using (await database.EnterSystemScopeAsync(cancellationToken))
            {
                rows = await db.Unlocked()
                    .Where(row => row.Id > floor)
                    .OrderBy(row => row.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);
            }
        }

        var fresh = cursor.Advance(rows.Select(row => row.Id).ToList(), time.GetUtcNow()).ToHashSet();
        foreach (var row in rows.Where(row => fresh.Contains(row.Id)))
        {
            foreach (var subscription in _subscriptions.Keys.Where(subscription => subscription.BrandId == row.BrandId))
            {
                subscription.Offer(row);
            }
        }
    }
}
```

- [ ] **Step 5: Add the viewer, the items, the stream and the endpoint**

Create `core/src/Modules/Kairo.Modules.Live/Delivery/Viewer.cs`:

```csharp
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class Viewer(Guid brandId, Guid userId, BrandRoles roles)
{
    public Guid BrandId => brandId;

    public Guid UserId => userId;

    public BrandRoles Roles { get; set; } = roles;

    public bool CanSee(OutboxEvent row) => row.Audience().Includes(UserId, Roles);
}
```

Create `core/src/Modules/Kairo.Modules.Live/Delivery/LiveItems.cs`:

```csharp
using System.Globalization;
using System.Net.ServerSentEvents;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal static class LiveItems
{
    public static SseItem<string> Heartbeat() => new("{}", "heartbeat");

    public static SseItem<string> Reset() => new("{}", "reset");

    public static SseItem<string> From(OutboxEvent row) =>
        new(row.Payload, row.Type) { EventId = row.Id.ToString(CultureInfo.InvariantCulture) };
}
```

Create `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs`:

```csharp
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveStream(LiveFeed feed, TimeProvider time)
{
    public async IAsyncEnumerable<SseItem<string>> ReadAsync(
        Viewer viewer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = await feed.SubscribeAsync(viewer.BrandId, cancellationToken);
        yield return LiveItems.Heartbeat();
        var lastWrite = time.GetUtcNow();
        var sent = new HashSet<long>();

        while (true)
        {
            var now = time.GetUtcNow();
            if (now - lastWrite >= LiveTimings.HeartbeatInterval)
            {
                yield return LiveItems.Heartbeat();
                lastWrite = now;
            }

            var row = await subscription.NextAsync(lastWrite + LiveTimings.HeartbeatInterval - now, cancellationToken);
            if (row is not null && viewer.CanSee(row) && sent.Add(row.Id))
            {
                yield return LiveItems.From(row);
                lastWrite = time.GetUtcNow();
            }
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs`:

```csharp
namespace Kairo.Modules.Live.Delivery;

internal static class LiveEndpoints
{
    public static void MapLive(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/live", Open);
    }

    private static ServerSentEventsResult<string> Open(HttpContext http, CurrentMember member, LiveStream stream) =>
        TypedResults.ServerSentEvents(stream.ReadAsync(new Viewer(member.BrandId, member.UserId, member.Roles), http.RequestAborted));
}
```

In `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`:
- add `using Kairo.Modules.Live.Delivery;` after `using Kairo.Modules.Live.Data;`
- add these two lines at the end of `AddServices`:

  ```csharp
          services.AddSingleton<LiveFeed>();
          services.AddScoped<LiveStream>();
  ```
- replace the empty `MapApi` body with `api.MapLive();`, so that it reads:

  ```csharp
      public void MapApi(IEndpointRouteBuilder api)
      {
          api.MapLive();
      }
  ```

- [ ] **Step 6: Run the delivery tests to verify they pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveDeliveryTests"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Update the committed OpenAPI document and the web types**

Run:

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web ci
npm --prefix web run generate:api
git diff --stat core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
```

Expected:
- the second test run passes
- the diff shows a new `/api/brands/{brandId}/live` path in both files, with a `200` response of `text/event-stream`, and nothing else

- [ ] **Step 8: Run the whole core suite**

Run: `dotnet test --solution core/Kairo.slnx`
Expected: every test passes, with 0 build warnings.

- [ ] **Step 9: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Live core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts core/tests/Kairo.IntegrationTests/Live/LiveListener.cs core/tests/Kairo.IntegrationTests/Live/LiveDeliveryTests.cs
git commit -m "Stream each workspace's live events to its members as Server-Sent Events"
```

---

### Task 5: Resuming from Last-Event-ID

**Files:**
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/IsolatedDatabase.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveResumeTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveRetentionTests.cs`

**Interfaces:**
- Consumes:
  - from Task 2: `LiveDbContext.Unlocked()`
  - from Task 3: `LiveTimings.GapPatience` and `LiveTimings.Retention`
  - from Task 4: `LiveItems.Reset()`, `LiveListener` and `OutboxRows.InsertAsync`
- Produces:
  - `LiveStream.ReadAsync(Viewer viewer, long? lastEventId, CancellationToken)`, which replaces the Task 4 signature
  - `LiveStream(LiveFeed feed, LiveDbContext db, TimeProvider time)`
  - the test helper `IsolatedDatabase.CreateAsync(SqlServer sqlServer) : Task<string>`

- [ ] **Step 1: Write the isolated database helper**

Create `core/tests/Kairo.IntegrationTests/Live/IsolatedDatabase.cs`:

```csharp
using Kairo.Migrator;
using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Live;

internal static class IsolatedDatabase
{
    public static async Task<string> CreateAsync(SqlServer sqlServer)
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("live");
        await using var migrator = MigratorHost.Build([$"--ConnectionStrings:kairo={connectionString}"]);
        await ModuleMigrations.ApplyAsync(migrator.Services, TestContext.Current.CancellationToken);
        return connectionString;
    }
}
```

- [ ] **Step 2: Write the failing resume tests**

Create `core/tests/Kairo.IntegrationTests/Live/LiveResumeTests.cs`:

```csharp
using Kairo.Contracts.Live;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveResumeTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reconnecting_replays_what_was_missed_in_order()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var lastSeen = await SeeOneEventAsync(owner.Browser, brandId, "test.first");
        await using var witness = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await LivePublishing.PublishAsync(_api, brandId, "test.second");
        await LivePublishing.PublishAsync(_api, brandId, "test.third");
        Assert.Equal("test.second", (await witness.NextAsync()).Type);
        Assert.Equal("test.third", (await witness.NextAsync()).Type);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("test.second", (await listener.NextAsync()).Type);
        Assert.Equal("test.third", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Reconnecting_also_replays_an_event_that_committed_after_the_last_one_seen()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var slow = _api.Services.CreateAsyncScope();
        var database = slow.ServiceProvider.GetRequiredService<KairoDatabase>();
        var live = slow.ServiceProvider.GetRequiredService<ILiveUpdates>();
        await database.UseBrandAsync(brandId, Token);
        await database.BeginTransactionAsync(Token);
        await live.PublishAsync(new LiveEvent("test.slow", new { }, LiveAudience.Everyone), Token);

        var lastSeen = await SeeOneEventAsync(owner.Browser, brandId, "test.quick");
        await using var witness = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await database.CommitAsync(Token);
        Assert.Equal("test.slow", (await witness.NextAsync()).Type);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("test.slow", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Another_workspace_s_event_id_gets_a_reset_and_nothing_from_that_workspace()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        var foreignId = await SeeOneEventAsync(owner.Browser, second, "test.elsewhere");

        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, first, foreignId);
        Assert.Equal("reset", (await listener.NextAsync()).Type);

        await LivePublishing.PublishAsync(_api, first, "test.here");
        Assert.Equal("test.here", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task An_unknown_event_id_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, long.MaxValue.ToString());

        Assert.Equal("reset", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Missing_more_than_a_thousand_events_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var lastSeen = await SeeOneEventAsync(owner.Browser, brandId, "test.first");

        await OutboxRows.InsertAsync(sqlServer.KairoConnectionString, brandId, "test.bulk", 1001, _api.Clock.GetUtcNow());
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("reset", (await listener.NextAsync()).Type);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<string> SeeOneEventAsync(HttpClient browser, Guid brandId, string type)
    {
        await using var listener = await LiveListener.OpenAsync(_api, browser, brandId);
        await LivePublishing.PublishAsync(_api, brandId, type);
        var message = await listener.NextAsync();
        Assert.Equal(type, message.Type);
        return message.Id!;
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Live/LiveRetentionTests.cs`:

```csharp
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveRetentionTests(SqlServer sqlServer) : IAsyncLifetime
{
    private string _connectionString = "";
    private KairoApiFactory _api = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_connectionString);
    }

    [Fact]
    public async Task An_event_id_older_than_a_day_gets_a_reset()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        string lastSeen;
        await using (var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId))
        {
            await LivePublishing.PublishAsync(_api, brandId, "test.old");
            lastSeen = (await listener.NextAsync()).Id!;
        }

        _api.Clock.Advance(TimeSpan.FromHours(25));
        await using var reconnected = await LiveListener.OpenAsync(_api, owner.Browser, brandId, lastSeen);

        Assert.Equal("reset", (await reconnected.NextAsync()).Type);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();
}
```

- [ ] **Step 3: Run them to verify they fail**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveResumeTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveRetentionTests"
```

Expected: FAIL. With `Last-Event-ID` still ignored:
- the replay tests time out with `TimeoutException`. The witness listener has already shown that the feed passed those events, so only a replay can deliver them.
- the reset tests time out the same way, or receive `test.here` instead of `reset`

- [ ] **Step 4: Add replay to the stream**

Replace `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs` with:

```csharp
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveStream(LiveFeed feed, LiveDbContext db, TimeProvider time)
{
    private const int ReplayLimit = 1000;

    public async IAsyncEnumerable<SseItem<string>> ReadAsync(
        Viewer viewer,
        long? lastEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = await feed.SubscribeAsync(viewer.BrandId, cancellationToken);
        yield return LiveItems.Heartbeat();
        var sent = new HashSet<long>();
        if (lastEventId is { } after)
        {
            var missed = await ReplayAsync(after, cancellationToken);
            if (missed is null)
            {
                yield return LiveItems.Reset();
            }
            else
            {
                foreach (var row in missed)
                {
                    if (viewer.CanSee(row) && sent.Add(row.Id))
                    {
                        yield return LiveItems.From(row);
                    }
                }
            }
        }

        var lastWrite = time.GetUtcNow();
        while (true)
        {
            var now = time.GetUtcNow();
            if (now - lastWrite >= LiveTimings.HeartbeatInterval)
            {
                yield return LiveItems.Heartbeat();
                lastWrite = now;
            }

            var row = await subscription.NextAsync(lastWrite + LiveTimings.HeartbeatInterval - now, cancellationToken);
            if (row is not null && viewer.CanSee(row) && sent.Add(row.Id))
            {
                yield return LiveItems.From(row);
                lastWrite = time.GetUtcNow();
            }
        }
    }

    private async Task<List<OutboxEvent>?> ReplayAsync(long after, CancellationToken cancellationToken)
    {
        var anchoredAt = await db.Unlocked()
            .Where(row => row.Id == after)
            .Select(row => (DateTimeOffset?)row.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (anchoredAt is not { } anchor || anchor < time.GetUtcNow() - LiveTimings.Retention)
        {
            return null;
        }

        var since = anchor - LiveTimings.GapPatience;
        var missed = await db.Unlocked()
            .Where(row => row.Id > after || (row.Id < after && row.CreatedAt >= since))
            .OrderBy(row => row.Id)
            .Take(ReplayLimit + 1)
            .ToListAsync(cancellationToken);
        return missed.Count > ReplayLimit ? null : missed;
    }
}
```

- [ ] **Step 5: Read Last-Event-ID in the endpoint**

Replace `core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs` with:

```csharp
using System.Globalization;

namespace Kairo.Modules.Live.Delivery;

internal static class LiveEndpoints
{
    private const string LastEventIdHeader = "Last-Event-ID";

    public static void MapLive(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/live", Open);
    }

    private static ServerSentEventsResult<string> Open(HttpContext http, CurrentMember member, LiveStream stream) =>
        TypedResults.ServerSentEvents(stream.ReadAsync(
            new Viewer(member.BrandId, member.UserId, member.Roles),
            LastEventId(http.Request),
            http.RequestAborted));

    private static long? LastEventId(HttpRequest request) =>
        long.TryParse(request.Headers[LastEventIdHeader].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
```

- [ ] **Step 6: Run the resume, retention and delivery tests to verify they pass**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveResumeTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveRetentionTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveDeliveryTests"
```

Expected: PASS, with 5, 1 and 5 tests.

- [ ] **Step 7: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs core/tests/Kairo.IntegrationTests/Live/IsolatedDatabase.cs core/tests/Kairo.IntegrationTests/Live/LiveResumeTests.cs core/tests/Kairo.IntegrationTests/Live/LiveRetentionTests.cs
git commit -m "Replay missed live events from Last-Event-ID, or tell the browser to refetch"
```

---

### Task 6: Re-checking access, and the 15-minute limit

**Files:**
- Create: `core/src/Kairo.Contracts/Identity/ISessionDirectory.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Sessions/SessionDirectory.cs`
- Modify: `core/src/Kairo.SharedKernel/Access/KairoClaims.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Sessions/DatabaseTicketStore.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/Viewer.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveAccessTests.cs`

**Interfaces:**
- Consumes:
  - existing: `IBrandAccess.FindRolesAsync(Guid brandId, Guid userId, CancellationToken)`
  - from Task 5: `LiveStream.ReadAsync(Viewer, long?, CancellationToken)`
- Produces:
  - `KairoClaims.SessionKey = "kairo_session_key"`
  - `Kairo.Contracts.Identity.ISessionDirectory.IsActiveAsync(string sessionKey, CancellationToken) : Task<bool>`
  - `Viewer(Guid brandId, Guid userId, BrandRoles roles, string? sessionKey)`, with `SessionKey`
  - `LiveStream(LiveFeed feed, LiveDbContext db, IBrandAccess access, ISessionDirectory sessions, TimeProvider time)`, with a private `StillAllowedAsync(Viewer, CancellationToken) : Task<bool>` that Task 8 calls

- [ ] **Step 1: Write the failing access tests**

Create `core/tests/Kairo.IntegrationTests/Live/LiveAccessTests.cs`:

```csharp
using System.Net;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveAccessTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_stream_needs_a_session()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        await using var listener = await LiveListener.OpenAsync(_api, _api.CreateBrowser(), brandId);

        Assert.Equal(HttpStatusCode.Unauthorized, listener.Status);
    }

    [Fact]
    public async Task Someone_outside_the_workspace_gets_not_found()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var outsider = await Workspaces.SignInAsync(_api);

        await using var listener = await LiveListener.OpenAsync(_api, outsider.Browser, brandId);

        Assert.Equal(HttpStatusCode.NotFound, listener.Status);
    }

    [Fact]
    public async Task A_removed_member_s_stream_ends()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var listener = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Null(await listener.NextOrEndAsync(TimeSpan.FromSeconds(5), maxSteps: 20));
    }

    [Fact]
    public async Task Signing_out_ends_the_stream()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PostAsync("/api/auth/sign-out", null, Token)).EnsureSuccessStatusCode();

        Assert.Null(await listener.NextOrEndAsync(TimeSpan.FromSeconds(5), maxSteps: 20));
    }

    [Fact]
    public async Task A_stream_closes_after_fifteen_minutes()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        await Assert.ThrowsAsync<TimeoutException>(() => listener.NextOrEndAsync(TimeSpan.FromSeconds(30), maxSteps: 28));
        Assert.Null(await listener.NextOrEndAsync(TimeSpan.FromSeconds(30), maxSteps: 4));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveAccessTests"`
Expected:
- `The_stream_needs_a_session` and `Someone_outside_the_workspace_gets_not_found` PASS. The membership filter already covers them, and they pin that behaviour.
- `A_removed_member_s_stream_ends`, `Signing_out_ends_the_stream` and `A_stream_closes_after_fifteen_minutes` FAIL with `TimeoutException`.

- [ ] **Step 3: Add the session key claim and the session directory**

In `core/src/Kairo.SharedKernel/Access/KairoClaims.cs`, add this line after `public const string Email = "email";`:

```csharp
    public const string SessionKey = "kairo_session_key";
```

Create `core/src/Kairo.Contracts/Identity/ISessionDirectory.cs`:

```csharp
namespace Kairo.Contracts.Identity;

public interface ISessionDirectory
{
    Task<bool> IsActiveAsync(string sessionKey, CancellationToken cancellationToken);
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Sessions/SessionDirectory.cs`:

```csharp
using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Sessions;

internal sealed class SessionDirectory(IdentityDbContext db, TimeProvider time) : ISessionDirectory
{
    public Task<bool> IsActiveAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Sessions.AnyAsync(session => session.Key == sessionKey && session.ExpiresAt > now, cancellationToken);
    }
}
```

In `core/src/Modules/Kairo.Modules.Identity/Sessions/DatabaseTicketStore.cs`:
- add `using System.Security.Claims;` as the first using
- in `StoreAsync`, insert this line directly after `var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));`:

  ```csharp
          ticket.Principal.Identities.First().AddClaim(new Claim(KairoClaims.SessionKey, key));
  ```

In `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`:
- add `using Kairo.Modules.Identity.Sessions;` after `using Kairo.Modules.Identity.Members;`
- add this line after `services.AddScoped<IWorkspaceFounding, WorkspaceFounding>();`:

  ```csharp
          services.AddScoped<ISessionDirectory, SessionDirectory>();
  ```

- [ ] **Step 4: Carry the session key on the viewer**

Replace `core/src/Modules/Kairo.Modules.Live/Delivery/Viewer.cs` with:

```csharp
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class Viewer(Guid brandId, Guid userId, BrandRoles roles, string? sessionKey)
{
    public Guid BrandId => brandId;

    public Guid UserId => userId;

    public string? SessionKey => sessionKey;

    public BrandRoles Roles { get; set; } = roles;

    public bool CanSee(OutboxEvent row) => row.Audience().Includes(UserId, Roles);
}
```

In `core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs`:
- add `using System.Security.Claims;` after `using System.Globalization;`
- replace `new Viewer(member.BrandId, member.UserId, member.Roles),` with:

  ```csharp
              new Viewer(member.BrandId, member.UserId, member.Roles, http.User.FindFirstValue(KairoClaims.SessionKey)),
  ```

- [ ] **Step 5: Re-check access and close after 15 minutes**

Replace `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs` with:

```csharp
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Kairo.Contracts.Identity;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class LiveStream(LiveFeed feed, LiveDbContext db, IBrandAccess access, ISessionDirectory sessions, TimeProvider time)
{
    private const int ReplayLimit = 1000;

    public async IAsyncEnumerable<SseItem<string>> ReadAsync(
        Viewer viewer,
        long? lastEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opened = time.GetUtcNow();
        using var subscription = await feed.SubscribeAsync(viewer.BrandId, cancellationToken);
        yield return LiveItems.Heartbeat();
        var sent = new HashSet<long>();
        if (lastEventId is { } after)
        {
            var missed = await ReplayAsync(after, cancellationToken);
            if (missed is null)
            {
                yield return LiveItems.Reset();
            }
            else
            {
                foreach (var row in missed)
                {
                    if (viewer.CanSee(row) && sent.Add(row.Id))
                    {
                        yield return LiveItems.From(row);
                    }
                }
            }
        }

        var lastWrite = time.GetUtcNow();
        var lastCheck = opened;
        while (true)
        {
            var now = time.GetUtcNow();
            if (now - opened >= LiveTimings.Lifetime)
            {
                yield break;
            }

            if (now - lastCheck >= LiveTimings.AccessCheckInterval)
            {
                if (!await StillAllowedAsync(viewer, cancellationToken))
                {
                    yield break;
                }

                lastCheck = now;
            }

            if (now - lastWrite >= LiveTimings.HeartbeatInterval)
            {
                yield return LiveItems.Heartbeat();
                lastWrite = now;
            }

            var wake = new[]
            {
                opened + LiveTimings.Lifetime,
                lastCheck + LiveTimings.AccessCheckInterval,
                lastWrite + LiveTimings.HeartbeatInterval,
            }.Min();
            var row = await subscription.NextAsync(wake - now, cancellationToken);
            if (row is null)
            {
                continue;
            }

            if (viewer.CanSee(row) && sent.Add(row.Id))
            {
                yield return LiveItems.From(row);
                lastWrite = time.GetUtcNow();
            }
        }
    }

    private async Task<bool> StillAllowedAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        if (viewer.SessionKey is { } key && !await sessions.IsActiveAsync(key, cancellationToken))
        {
            return false;
        }

        if (await access.FindRolesAsync(viewer.BrandId, viewer.UserId, cancellationToken) is not { } roles)
        {
            return false;
        }

        viewer.Roles = roles;
        return true;
    }

    private async Task<List<OutboxEvent>?> ReplayAsync(long after, CancellationToken cancellationToken)
    {
        var anchoredAt = await db.Unlocked()
            .Where(row => row.Id == after)
            .Select(row => (DateTimeOffset?)row.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        if (anchoredAt is not { } anchor || anchor < time.GetUtcNow() - LiveTimings.Retention)
        {
            return null;
        }

        var since = anchor - LiveTimings.GapPatience;
        var missed = await db.Unlocked()
            .Where(row => row.Id > after || (row.Id < after && row.CreatedAt >= since))
            .OrderBy(row => row.Id)
            .Take(ReplayLimit + 1)
            .ToListAsync(cancellationToken);
        return missed.Count > ReplayLimit ? null : missed;
    }
}
```

- [ ] **Step 6: Run the access, live and sign-in tests to verify they pass**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveAccessTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveDeliveryTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveResumeTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.SignInTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
```

Expected: every test passes. `Every_registration_resolves_in_development` resolves `LiveStream`, which now needs `ISessionDirectory`.

- [ ] **Step 7: Commit**

```bash
git add core/src/Kairo.SharedKernel/Access/KairoClaims.cs core/src/Kairo.Contracts/Identity/ISessionDirectory.cs core/src/Modules/Kairo.Modules.Identity/Sessions/SessionDirectory.cs core/src/Modules/Kairo.Modules.Identity/Sessions/DatabaseTicketStore.cs core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs core/src/Modules/Kairo.Modules.Live/Delivery/Viewer.cs core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs core/src/Modules/Kairo.Modules.Live/Delivery/LiveEndpoints.cs core/tests/Kairo.IntegrationTests/Live/LiveAccessTests.cs
git commit -m "End live streams when the session or membership goes, and after 15 minutes"
```

---

### Task 7: Cleaning up old outbox rows

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Live/Delivery/OutboxCleanup.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveFeed.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Live/LiveRetentionTests.cs`

**Interfaces:**
- Consumes:
  - from Task 3: `LiveTimings.Retention` and `LiveTimings.CleanupInterval`
  - existing: `KairoDatabase.EnterSystemScopeAsync`, `BeginTransactionAsync`, `CommitAsync` and `RollbackAsync`
- Produces: `OutboxCleanup.RunAsync(CancellationToken) : Task`, which is scoped

- [ ] **Step 1: Write the failing clean-up test**

In `core/tests/Kairo.IntegrationTests/Live/LiveRetentionTests.cs`, add this test before `DisposeAsync`:

```csharp
    [Fact]
    public async Task Events_older_than_a_day_are_deleted_and_newer_ones_kept()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await LivePublishing.PublishAsync(_api, brandId, "test.old");
        var old = long.Parse((await listener.NextAsync()).Id!);

        _api.Clock.Advance(TimeSpan.FromHours(25));
        await LivePublishing.PublishAsync(_api, brandId, "test.new");

        var ids = Array.Empty<long>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            ids = (await OutboxRows.ReadAsync(_connectionString, brandId)).Select(row => row.Id).ToArray();
            if (!ids.Contains(old))
            {
                break;
            }

            _api.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.DoesNotContain(old, ids);
        Assert.Single(ids);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveRetentionTests"`
Expected: `Events_older_than_a_day_are_deleted_and_newer_ones_kept` FAILS at `Assert.DoesNotContain`, because nothing deletes rows yet.

- [ ] **Step 3: Add the clean-up**

Create `core/src/Modules/Kairo.Modules.Live/Delivery/OutboxCleanup.cs`:

```csharp
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class OutboxCleanup(LiveDbContext db, KairoDatabase database, TimeProvider time)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - LiveTimings.Retention;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            await database.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                    DECLARE @lock int;
                    EXEC @lock = sys.sp_getapplock @Resource = N'live.outbox.cleanup', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
                    IF @lock >= 0
                        DELETE FROM [live].[Outbox] WITH (READCOMMITTEDLOCK, READPAST) WHERE [CreatedAt] < {cutoff};
                    """,
                    cancellationToken);
                await database.CommitAsync(cancellationToken);
            }
            catch
            {
                await database.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
```

In `core/src/Modules/Kairo.Modules.Live/LiveModule.cs`, add this line at the end of `AddServices`:

```csharp
        services.AddScoped<OutboxCleanup>();
```

In `core/src/Modules/Kairo.Modules.Live/Delivery/LiveFeed.cs`:

1. Replace the whole `RunAsync` method with:

   ```csharp
       private async Task RunAsync(CancellationToken stopping)
       {
           var lastCleanup = DateTimeOffset.MinValue;
           while (!stopping.IsCancellationRequested)
           {
               try
               {
                   await PollAsync(stopping);
                   if (time.GetUtcNow() - lastCleanup >= LiveTimings.CleanupInterval)
                   {
                       await CleanUpAsync(stopping);
                       lastCleanup = time.GetUtcNow();
                   }
               }
               catch (OperationCanceledException) when (stopping.IsCancellationRequested)
               {
                   return;
               }
               catch (Exception exception)
               {
                   logger.LogWarning(exception, "Reading or cleaning up live updates failed.");
               }

               try
               {
                   await Task.Delay(LiveTimings.PollInterval, time, stopping);
               }
               catch (OperationCanceledException)
               {
                   return;
               }
           }
       }
   ```

2. Add this method after `PollAsync`:

   ```csharp
       private async Task CleanUpAsync(CancellationToken cancellationToken)
       {
           await using var scope = scopes.CreateAsyncScope();
           await scope.ServiceProvider.GetRequiredService<OutboxCleanup>().RunAsync(cancellationToken);
       }
   ```

- [ ] **Step 4: Run the retention and delivery tests to verify they pass**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveRetentionTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveDeliveryTests"
```

Expected: PASS, with 2 and 5 tests.

- [ ] **Step 5: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Live/Delivery/OutboxCleanup.cs core/src/Modules/Kairo.Modules.Live/Delivery/LiveFeed.cs core/src/Modules/Kairo.Modules.Live/LiveModule.cs core/tests/Kairo.IntegrationTests/Live/LiveRetentionTests.cs
git commit -m "Delete live events after a day"
```

---

### Task 8: Publishing the workspace, team, invitation and approval events

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Identity/IdentityLiveEvents.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Invitations/InvitationEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Approvals/ApprovalSettingsEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs`
- Test: `core/tests/Kairo.IntegrationTests/Live/LiveEventsTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `ILiveUpdates`, `LiveEvent` and `LiveAudience`
  - from Task 6: `LiveStream.StillAllowedAsync`
- Produces:
  - `IdentityLiveEvents.TeamChanged(Guid userId, bool removed)`, `IdentityLiveEvents.InvitationsChanged()` and `IdentityLiveEvents.ApprovalSettingsChanged()`, each returning `LiveEvent`
  - these events on the wire:

| Type | Payload | Audience |
| --- | --- | --- |
| `workspace.changed` | `{}` | Everyone |
| `team.changed` | `{ userId, removed }` | Everyone |
| `invitations.changed` | `{}` | Admins |
| `approval_settings.changed` | `{}` | Everyone |

- [ ] **Step 1: Write the failing event tests**

Create `core/tests/Kairo.IntegrationTests/Live/LiveEventsTests.cs`:

```csharp
using System.Net.Http.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveEventsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Renaming_the_workspace_publishes_workspace_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token)).EnsureSuccessStatusCode();

        Assert.Equal("workspace.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Saving_approval_settings_publishes_approval_settings_changed()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        var duty = new[] { owner.UserId };

        (await owner.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/approval-settings",
            new { briefApprovers = duty, assetApprovers = duty, launchApprovers = duty, budgetOwners = duty, budgetLimit = 50000m },
            Token)).EnsureSuccessStatusCode();

        Assert.Equal("approval_settings.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Invitation_changes_reach_admins_only()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var ownerStream = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var marketerStream = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, $"new-{Guid.NewGuid():N}@example.com", "marketer");
        (await owner.Browser.PostAsync($"/api/brands/{brandId}/invitations/{invitationId}/revoke", null, Token)).EnsureSuccessStatusCode();
        (await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token)).EnsureSuccessStatusCode();

        Assert.Equal("invitations.changed", (await ownerStream.NextAsync()).Type);
        Assert.Equal("invitations.changed", (await ownerStream.NextAsync()).Type);
        Assert.Equal("workspace.changed", (await marketerStream.NextAsync()).Type);
    }

    [Fact]
    public async Task Accepting_an_invitation_publishes_the_new_member()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        var member = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        Assert.Equal("invitations.changed", (await listener.NextAsync()).Type);
        var joined = await listener.NextAsync();
        Assert.Equal("team.changed", joined.Type);
        Assert.Equal(member.UserId, joined.Data.GetProperty("userId").GetGuid());
        Assert.False(joined.Data.GetProperty("removed").GetBoolean());
        Assert.Equal("invitations.changed", (await listener.NextAsync()).Type);
    }

    [Fact]
    public async Task Changing_roles_publishes_team_changed_for_that_member()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var listener = await LiveListener.OpenAsync(_api, owner.Browser, brandId);

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{marketer.UserId}/roles", new { roles = new[] { "marketer", "approver" } }, Token)).EnsureSuccessStatusCode();

        var message = await listener.NextAsync();
        Assert.Equal("team.changed", message.Type);
        Assert.Equal(marketer.UserId, message.Data.GetProperty("userId").GetGuid());
        Assert.False(message.Data.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task Removing_a_member_ends_their_stream_straight_away()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await using var ownerStream = await LiveListener.OpenAsync(_api, owner.Browser, brandId);
        await using var marketerStream = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Null(await marketerStream.NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps: 10));
        var removed = await ownerStream.NextAsync();
        Assert.Equal("team.changed", removed.Type);
        Assert.True(removed.Data.GetProperty("removed").GetBoolean());
        await using var reconnect = await LiveListener.OpenAsync(_api, marketer.Browser, brandId);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, reconnect.Status);
    }

    [Fact]
    public async Task A_demoted_admin_stops_seeing_admin_events_without_reconnecting()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var deputy = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        await using var listener = await LiveListener.OpenAsync(_api, deputy.Browser, brandId);

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{deputy.UserId}/roles", new { roles = new[] { "marketer" } }, Token)).EnsureSuccessStatusCode();
        Assert.Equal("team.changed", (await listener.NextAsync()).Type);

        await Workspaces.InviteAsync(owner.Browser, brandId, $"new-{Guid.NewGuid():N}@example.com", "marketer");
        (await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token)).EnsureSuccessStatusCode();

        Assert.Equal("workspace.changed", (await listener.NextAsync(maxSteps: 10)).Type);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveEventsTests"`
Expected: FAIL. Every test except the removal test throws `TimeoutException`, because nothing publishes yet. The removal test fails because `NextOrEndAsync` with `maxSteps: 10` times out before the 30-second check.

- [ ] **Step 3: Add the Identity event factories**

Create `core/src/Modules/Kairo.Modules.Identity/IdentityLiveEvents.cs`:

```csharp
using Kairo.Contracts.Live;

namespace Kairo.Modules.Identity;

internal static class IdentityLiveEvents
{
    public static LiveEvent TeamChanged(Guid userId, bool removed) =>
        new("team.changed", new { userId, removed }, LiveAudience.Everyone);

    public static LiveEvent InvitationsChanged() =>
        new("invitations.changed", new { }, LiveAudience.Roles(BrandRoles.Admin));

    public static LiveEvent ApprovalSettingsChanged() =>
        new("approval_settings.changed", new { }, LiveAudience.Everyone);
}
```

- [ ] **Step 4: Publish from the member endpoints**

In `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`:

1. Add `using Kairo.Contracts.Live;` after `using Kairo.Contracts.Audit;`.
2. **`ChangeRolesAsync`.**
   - Add `ILiveUpdates live,` after `IAuditLog audit,` in its parameter list.
   - Directly after its `await audit.RecordAsync(...)` statement, add:

     ```csharp
             await live.PublishAsync(IdentityLiveEvents.TeamChanged(userId, removed: false), cancellationToken);
     ```
3. **`RemoveAsync`.**
   - Add `ILiveUpdates live,` after `IAuditLog audit,` in its parameter list.
   - Directly after its `await audit.RecordAsync(...)` statement and before `return TypedResults.NoContent();`, add:

     ```csharp
             await live.PublishAsync(IdentityLiveEvents.TeamChanged(userId, removed: true), cancellationToken);
     ```

- [ ] **Step 5: Publish from the invitation endpoints**

In `core/src/Modules/Kairo.Modules.Identity/Invitations/InvitationEndpoints.cs`:

1. Add `using Kairo.Contracts.Live;` after `using Kairo.Contracts.Brands;`.
2. **`InviteAsync`, `RevokeAsync` and `DeclineAsync`.** In each method:
   - add `ILiveUpdates live,` after `IAuditLog audit,` in its parameter list
   - add this line directly after its `await audit.RecordAsync(...)` statement:

     ```csharp
             await live.PublishAsync(IdentityLiveEvents.InvitationsChanged(), cancellationToken);
     ```
3. **`AcceptAsync`.**
   - Add `ILiveUpdates live,` after `IAuditLog audit,` in its parameter list.
   - Directly after its `await audit.RecordAsync(...)` statement, add:

     ```csharp
             await live.PublishAsync(IdentityLiveEvents.TeamChanged(userId, removed: false), cancellationToken);
             await live.PublishAsync(IdentityLiveEvents.InvitationsChanged(), cancellationToken);
     ```

- [ ] **Step 6: Publish from approval settings and the workspace**

In `core/src/Modules/Kairo.Modules.Identity/Approvals/ApprovalSettingsEndpoints.cs`:
- add `using Kairo.Contracts.Live;` after `using Kairo.Contracts.Audit;`
- in `SaveAsync`, add `ILiveUpdates live,` after `IAuditLog audit,`
- in `SaveAsync`, insert this line directly after its `await audit.RecordAsync(...)` statement:

  ```csharp
          await live.PublishAsync(IdentityLiveEvents.ApprovalSettingsChanged(), cancellationToken);
  ```

In `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceEndpoints.cs`:
- add `using Kairo.Contracts.Live;` after `using Kairo.Contracts.Identity;`
- in `UpdateAsync`, add `ILiveUpdates live,` after `IAuditLog audit,`
- in `UpdateAsync`, insert this line directly after its `await audit.RecordAsync(...)` statement:

  ```csharp
          await live.PublishAsync(new LiveEvent("workspace.changed", new { }, LiveAudience.Everyone), cancellationToken);
  ```

- [ ] **Step 7: Re-check at once when an event concerns the stream's own user**

In `core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs`:

1. Add `using System.Text.Json;` after `using System.Runtime.CompilerServices;`.
2. Replace:

   ```csharp
               if (row is null)
               {
                   continue;
               }

               if (viewer.CanSee(row) && sent.Add(row.Id))
   ```

   with:

   ```csharp
               if (row is null)
               {
                   continue;
               }

               if (ConcernsViewer(row, viewer))
               {
                   if (!await StillAllowedAsync(viewer, cancellationToken))
                   {
                       yield break;
                   }

                   lastCheck = time.GetUtcNow();
               }

               if (viewer.CanSee(row) && sent.Add(row.Id))
   ```

3. Add this method before `StillAllowedAsync`:

   ```csharp
       private static bool ConcernsViewer(OutboxEvent row, Viewer viewer)
       {
           if (row.Type != "team.changed")
           {
               return false;
           }

           using var payload = JsonDocument.Parse(row.Payload);
           return payload.RootElement.TryGetProperty("userId", out var userId)
               && userId.TryGetGuid(out var id)
               && id == viewer.UserId;
       }
   ```

- [ ] **Step 8: Run the event tests and the suites they touch**

Run:

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveEventsTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveAccessTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Live.LiveDeliveryTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.TeamTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.InvitationTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ApprovalSettingsTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.WorkspaceTests"
```

Expected: every test passes.

- [ ] **Step 9: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Identity/IdentityLiveEvents.cs core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs core/src/Modules/Kairo.Modules.Identity/Invitations/InvitationEndpoints.cs core/src/Modules/Kairo.Modules.Identity/Approvals/ApprovalSettingsEndpoints.cs core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceEndpoints.cs core/src/Modules/Kairo.Modules.Live/Delivery/LiveStream.cs core/tests/Kairo.IntegrationTests/Live/LiveEventsTests.cs
git commit -m "Publish workspace, team, invitation and approval setting changes as live events"
```

---

### Task 9: A 60-minute request timeout for the API and the web app

**Files:**
- Modify: `deploy/Kairo.Deploy/Release.ps1`
- Test: `deploy/tests/Release.Tests.ps1`

**Interfaces:**
- Consumes: `Publish-KairoService -Settings`
- Produces: `kairo-api` and `kairo-web` deployed with `--timeout 3600`

- [ ] **Step 1: Write the failing Pester test**

In `deploy/tests/Release.Tests.ps1`, add this test after the `It 'deploys the web app publicly, pointing at the API'` block:

```powershell
    It 'lets live update streams stay open for an hour through the web app and the API' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Where-Object { $_ -like 'run deploy kairo-api *' } | Should -BeLike '*--timeout 3600*'
        $calls | Where-Object { $_ -like 'run deploy kairo-web *' } | Should -BeLike '*--timeout 3600*'
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: FAIL in `lets live update streams stay open for an hour through the web app and the API`. The message is `Expected like wildcard '*--timeout 3600*'` for `kairo-api`.

- [ ] **Step 3: Add the timeouts**

In `deploy/Kairo.Deploy/Release.ps1`:

1. In the `kairo-api` `Publish-KairoService` call, replace `'--session-affinity',` with:

   ```powershell
               '--session-affinity',
               '--timeout', '3600',
   ```

2. In the `kairo-web` `Publish-KairoService` call, replace `'--no-invoker-iam-check',` with:

   ```powershell
               '--no-invoker-iam-check',
               '--timeout', '3600',
   ```

- [ ] **Step 4: Run the deploy tests to verify they pass**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: every test passes.

- [ ] **Step 5: Commit**

```bash
git add deploy/Kairo.Deploy/Release.ps1 deploy/tests/Release.Tests.ps1
git commit -m "Keep live update streams open for up to an hour on Cloud Run"
```

---

### Task 10: The architecture document

**Files:**
- Modify: `docs/Kairo-SAD.md`
- Modify: `docs/superpowers/specs/2026-10-13-live-updates-design.md`

**Interfaces:** None.

Don't change the `Identity and secrets` row of §2 or the `Secrets` row of §8. The account connections branch edits those rows.

- [ ] **Step 1: Replace each SignalR and Memorystore mention**

Make these exact replacements in `docs/Kairo-SAD.md`:

| Section | Find | Replace with |
| --- | --- | --- |
| §1 diagram | `-->\|HTTPS and SignalR\|` | `-->\|HTTPS and Server-Sent Events\|` |
| §2, Web app row | `shadcn/ui on Base UI, SignalR client \|` | `shadcn/ui on Base UI, the browser's EventSource \|` |
| §2, Core service row | `ASP.NET Core in C#, EF Core, SignalR, Google Ads API .NET client library` | `ASP.NET Core in C#, EF Core, Server-Sent Events, Google Ads API .NET client library` |
| §2, Cache row | `\| Cache \| Memorystore \| SignalR backplane and short-lived caches \|` | `\| Live updates \| A SQL Server outbox, streamed as Server-Sent Events \| Agent progress and status changes, pushed to the browser within about a second \|` |
| §3, Notifications row | `Send live updates over SignalR and emails through the Gmail API` | `Send in-app notifications as live updates and emails through the Gmail API` |
| §5 diagram | `C-->>W: Assets ready (SignalR)` | `C-->>W: Assets ready (live update)` |
| §6, Operations row | `\| Operations \| Notification, AuditLog \| AuditLog is append-only \|` | `\| Operations \| Notification, AuditLog, LiveOutbox \| AuditLog is append-only; LiveOutbox keeps live updates for 24 hours \|` |
| §8, Network row | `Cloud SQL and Memorystore use private IPs` | `Cloud SQL uses a private IP` |
| §9, Core API row | `At least 1 instance, with session affinity for SignalR` | `At least 1 instance; 60-minute request timeout for live update streams` |
| §9, Cache row | `\| Cache \| Memorystore \| Small instance on a private IP \|\n` | *(delete the row)* |
| AD-9 | `\| AD-9 Live updates \| SignalR with Memorystore \| Polling; Firestore listeners \| Native to .NET, with no second database \|` | `\| AD-9 Live updates \| Server-Sent Events fed by a SQL Server outbox \| SignalR with a Memorystore backplane; Firestore listeners \| Events commit with the change they describe, the worker publishes the same way as the API, several API instances need no backplane, and plain HTTP passes through the development project's web proxy and the load balancer \|` |

In the table above, `\|` stands for a literal `|` inside the find and replace text.

- [ ] **Step 2: Add how live updates flow to §5**

In `docs/Kairo-SAD.md`, under `### Other flows` in §5, add this item after item 6 (**Ask Kairo**), as the last entry of that list:

```markdown
7. **Live updates.** A change and its live event are written in one transaction: the event goes to a SQL Server outbox. Each core API instance reads new outbox rows about once a second and streams them as Server-Sent Events on `GET /api/brands/{brandId}/live` to that workspace's members, filtered by role or person. Events are signals that tell the screen to refetch. A reconnecting browser sends `Last-Event-ID` and gets what it missed, or a `reset` if that is more than a day old.
```

- [ ] **Step 3: Say in the spec's web contract that events can repeat**

In `docs/superpowers/specs/2026-10-13-live-updates-design.md`, under "Contract for the web app", replace:

```markdown
- **On an event.** Refetch the affected data through the normal API, or call `router.refresh()` for server-rendered screens. Don't render from the payload alone.
```

with:

```markdown
- **On an event.** Refetch the affected data through the normal API, or call `router.refresh()` for server-rendered screens. Don't render from the payload alone. The same event can arrive twice, and an event can arrive with a lower ID than the one before it, so handle every event as "something changed, refetch".
```

- [ ] **Step 4: Check that no SignalR or Memorystore mention is left**

Run: `grep -n -i "signalr\|memorystore" docs/Kairo-SAD.md`
Expected: matches only in the AD-9 "alternatives considered" cell.

- [ ] **Step 5: Commit**

```bash
git add docs/Kairo-SAD.md docs/superpowers/specs/2026-10-13-live-updates-design.md
git commit -m "Describe live updates as Server-Sent Events from a SQL Server outbox in the SAD"
```

---

### Task 11: Full verification

**Files:** None, unless a check fails.

- [ ] **Step 1: Build and test the core**

Run:

```bash
docker info > /dev/null && echo docker-ok
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: `docker-ok`, the build succeeds with 0 warnings, and every test passes.

- [ ] **Step 2: Check the web app and the generated types**

Run:

```bash
npm --prefix web run generate:api
git diff --exit-code -- web/src/lib/core-api/schema.ts
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
```

Expected: `git diff` exits 0, and lint, tests and build pass.

- [ ] **Step 3: Check the agent service and the deploy scripts**

Run:

```bash
uv run --directory agents ruff check
uv run --directory agents ruff format --check
uv run --directory agents pytest
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
```

Expected: every check passes.

- [ ] **Step 4: Check the stream by hand through Aspire**

Run: `dotnet run --project core/src/Kairo.AppHost`, and leave it running.

1. Open `http://localhost:8000` in Chrome, sign in, and open a workspace.
2. Copy the workspace ID from `GET http://localhost:8000/api/brands`.
3. Copy the `__Host-kairo-session` cookie value from DevTools → Application → Cookies.
4. In a second terminal, run:

   ```bash
   curl -N -H "Cookie: __Host-kairo-session=<value>" http://localhost:8000/api/brands/<id>/live
   ```

   Expected: `event: heartbeat` appears at once and then every 20 seconds.
5. In the browser, change a member's role or the workspace name in Settings.

   Expected: within about a second, curl prints `event: team.changed` or `event: workspace.changed`, with an `id:` line and a `data:` line.

Stop Aspire with Ctrl+C.

- [ ] **Step 5: Check the branch is clean and committed**

Run: `git status --short && git log --oneline develop..HEAD`
Expected: `git status` prints nothing. The log lists the spec commit, the plan commit and the ten task commits.
