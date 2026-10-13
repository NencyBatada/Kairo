# Kairo Live Updates — design

*13 October 2026 · Approved in conversation; this is the written record*

## Goal

Push status changes from the core and the worker to every open browser as they happen, so screens update without a refresh. This is the pipe that agent progress, campaign room changes and in-app notifications will use. Agents don't exist yet, so this task builds the pipe, its contract and the events for changes Kairo already makes.

Task: `docs/task.md` §1, Talha, **Live updates**. The screens that show updates are Nency's **Live status** task.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Publishing | A transactional outbox: `ILiveUpdates` writes a row to `live.Outbox` in the same transaction as the change | A rolled-back change publishes nothing, a crash after commit loses nothing, and the worker publishes the same way as the API |
| Fan-out | Each API instance polls the outbox about once a second | Several API instances work without a backplane, and there is no new infrastructure |
| Transport | Server-Sent Events from `GET /api/brands/{brandId}/live` | Works through YARP, the development project's Next.js proxy and the load balancer, needs no sticky sessions, and the browser's `EventSource` reconnects and resumes on its own |
| Event content | Signals, not data: a type, IDs and at most a few display fields | The browser refetches through the normal API, so permissions stay in one place and nothing leaks through the stream |
| Audience | Every member, members with a role, or named users | Notifications needs per-person delivery |
| Comments | None in code | `CLAUDE.md` §3 |

## Notes on the documentation

1. **AD-9 and §2 of the SAD** choose SignalR with a Memorystore backplane. This design replaces both with Server-Sent Events fed by a SQL Server outbox, for the reasons above. Memorystore is no longer needed for live updates; SAD §2 keeps it only as a possible later cache. This task updates SAD §2, §4, §5 and AD-9.
2. **Latency.** Polling adds up to about a second. Agent stages take minutes, so this is acceptable.

## Publishing

`Kairo.Contracts` gains:

```csharp
public interface ILiveUpdates
{
    Task PublishAsync(LiveEvent liveEvent, CancellationToken cancellationToken);
}

public sealed record LiveEvent(string Type, object Payload, LiveAudience Audience);
```

- `LiveAudience` is one of `Everyone`, `Roles(BrandRoles)` or `Users(Guid[])`.
- The brand is the current brand context. Publishing with no brand context throws.
- `PublishAsync` adds a row through the Live module's DbContext on the shared connection, so it commits or rolls back with the request or job.
- Event types are lowercase, dotted and past tense, such as `team.changed`, matching audit actions.

## Data

| Schema | Table | Belongs to a workspace | Holds |
| --- | --- | --- | --- |
| `live` | `Outbox` | Yes | `Id` (`bigint` identity, the event ID), brand, type, payload JSON, audience kind, audience roles, audience user IDs as JSON, created time |

- The table is covered by the `live` schema's row-level security policy, like every workspace-owned table.
- The poller reads across brands in a system scope.
- Event IDs come from one identity column shared by all brands. A brand's stream skips the gaps that other brands leave.

## Delivering

### The stream

`GET /api/brands/{brandId}/live` is open to every member, with the usual membership check (401, then 404).

- It responds with `text/event-stream` and `Cache-Control: no-cache`, using ASP.NET Core's `TypedResults.ServerSentEvents`.
- Each event is written with `id:` set to its event ID, `event:` set to its type, and `data:` set to its payload as camel-case JSON.
- A comment line is sent every 20 seconds when nothing else has been, so proxies don't time out an idle stream.

### Resuming

- The browser's `EventSource` sends `Last-Event-ID` when it reconnects.
- If it does, the stream first replays every event for that brand and listener after that ID, then carries on live.
- If that ID is older than the oldest event kept, the stream sends one `reset` event first, telling the client to refetch the screen.
- A stream with no `Last-Event-ID` starts from the newest event.

### The poller

- One background service per API instance reads `live.Outbox` for rows after the last ID it saw, about once a second, and hands them to the streams open on that instance.
- When there are no open streams, it doesn't query.
- Each stream checks the audience before sending: everyone, a role the member holds, or their user ID.

### Losing access

- Every 30 seconds each stream re-checks that its session still exists and that its user is still a member. If not, the stream ends.
- Roles are refreshed in the same check, so role audiences follow role changes.
- A `team.changed` event that removes the stream's user ends the stream straight away.
- The server ends every stream after 15 minutes. The browser reconnects through full authentication and resumes from its last event ID.

### Clean-up

- Rows older than 24 hours are deleted.
- Worker jobs have no authentication yet, so the API runs the clean-up as a background service every hour, with a SQL application lock so only one instance runs it at a time. It moves to a worker job when worker job authentication arrives.

## Events in this task

| Type | Published when | Audience | Payload |
| --- | --- | --- | --- |
| `workspace.changed` | The workspace's name or time zone changes | Everyone | `{}` |
| `team.changed` | A member's roles change, a member is removed or leaves, or an invitation is accepted | Everyone | `{ userId, removed }` |
| `invitations.changed` | An invitation is sent, revoked, accepted or declined | Admins | `{}` |
| `approval_settings.changed` | Duties or the budget limit change | Everyone | `{}` |

**Connections.** The Account connections task is being built in parallel and will publish `connection.changed`. Whichever of the two branches merges second wires that event up.

## Contract for the web app

This section is what Nency's **Live status** task builds against.

- **Opening the stream.** Open `new EventSource("/api/brands/{brandId}/live")` from the browser. The session cookie goes with it, because the stream is on the same origin. No `X-Kairo-Request` header is needed, because it is a GET.
- **Listening.** Listen with `addEventListener(type, …)` for each type above. `data` is JSON.
- **On an event.** Refetch the affected data through the normal API, or call `router.refresh()` for server-rendered screens. Don't render from the payload alone. The same event can arrive twice, and an event can arrive with a lower ID than the one before it, so handle every event as "something changed, refetch".
- **On `reset`.** Refetch everything the screen shows.
- **Errors and reconnects.**
  - `EventSource` reconnects on its own after a dropped connection or the 15-minute limit.
  - A 401 or 404 ends it without reconnecting. In that case the screen sends the user to sign in.
- **One stream per workspace per tab.** It is opened in the workspace layout and shared through React context.

## Code layout

| Project | Adds |
| --- | --- |
| `Kairo.Contracts` | `ILiveUpdates`, `LiveEvent`, `LiveAudience` |
| `Kairo.Modules.Live` | New module: the outbox DbContext and migration, `ILiveUpdates`, the stream endpoint, the poller and the clean-up service |
| `Kairo.Modules.Identity` and `Kairo.Modules.Brands` | Publish the events above |
| `Kairo.Api`, `Kairo.Worker` and `Kairo.Migrator` | Register the new module |

The module follows the existing rules: one public type, references only to `Kairo.SharedKernel` and `Kairo.Contracts`, tables only in its own schema, and internal migrations tidied by `core/scripts/tidy-migrations.sh`.

## Deployment

- The core API's Cloud Run request timeout goes to 60 minutes, and so does the web app's, because in the development project the web app proxies the stream.
- Nothing else changes, and no Memorystore is needed.

## Testing

**Integration tests**, on real SQL Server through Testcontainers:

- **Commit and rollback.**
  - A write that commits publishes its event.
  - A write that rolls back publishes nothing.
- **The worker.** An event written outside a request, as a worker job would write it, reaches an open API stream.
- **Resume.**
  - `Last-Event-ID` replays the missed events in order.
  - An ID older than the oldest kept event gets `reset`.
- **Separation.** One workspace's events never reach another workspace's stream.
- **Audiences.** Role and user audiences reach only their listeners.
- **Access.**
  - The stream needs a session (401) and membership (404).
  - A removed member's stream ends.
  - A signed-out session's stream ends at the next check.
- **The four events** above are published by their endpoints.
- **Security policy.** The new table is covered, through the existing coverage test.

**Unit tests** cover the poller's hand-off and audience filter, and the clean-up's cutoff.

**Architecture tests** pick up the new module automatically.

Tests use `FakeTimeProvider` for the 20-second, 30-second and 15-minute timers.

## Out of scope

- The web client and screens (Nency's **Live status**).
- Agent progress and campaign room events, which come with the campaign room engine.
- In-app notifications, which come with the Notifications task.
- Memorystore and any Redis use.
- Moving clean-up to a worker job.

## Done criteria

1. `dotnet build core/Kairo.slnx` and `dotnet test --solution core/Kairo.slnx` pass with Docker running, with 0 warnings.
2. The web checks still pass, and `npm --prefix web run generate:api` leaves no changes beyond the new endpoint.
3. With `dotnet run --project core/src/Kairo.AppHost`, `curl -N` on `http://localhost:8000/api/brands/{id}/live` with a session cookie shows an event within about a second of changing a member's roles in another window.
4. SAD §2, §4, §5 and AD-9 are updated.
