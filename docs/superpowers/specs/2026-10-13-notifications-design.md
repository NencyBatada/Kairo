# Kairo Notifications — design

*13 October 2026 · Approved in conversation; this is the written record*

## Goal

Tell people when something needs them, in the app and by email, with a link to the screen where they act. This task builds the notification module, its contract for other modules, the inbox and email-settings API, email delivery through Gmail, and the one notification Kairo can already raise: a connection that needs reconnecting. Approvals, triggers, launches, pacing and reports don't exist yet; each calls this module when it is built.

Task: `docs/task.md` §1, Talha, **Notifications**. PRD §9, PRD US-13.3, BRD FR-13.5. The screens are Nency's **Notifications** task: the in-app list, email settings per user and the email templates.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Contract | One call, `INotifications.NotifyAsync`, that runs inside the caller's transaction | A notification exists only if the change it reports commits |
| Audience | `LiveAudience` from the live updates contract: everyone, members with a role, or named users | One audience type across live updates and notifications; resolved to current members when the call is made |
| Kinds | A fixed catalogue in code, from PRD §9 | Each kind's email policy and whether it can be turned off are product rules, not data |
| In-app delivery | Rows in `notifications.Notifications`, plus a `notification.created` live event to the recipients only | The list survives reloads; the live event makes it appear without a refresh |
| Email delivery | An email outbox in SQL, `notifications.OutboundEmails`, sent by a loop in the API | Retries and backoff without new infrastructure; reminders and grouping can later become rows due later. Chosen by the product owner. |
| Sender | One mailbox, signed in once through Kairo's existing OAuth client with `gmail.send`; its refresh token in Secret Manager | Works for a Workspace or a plain Gmail address and needs no Workspace admin. Chosen by the product owner. |
| Email format | One HTML layout and a plain-text version, built in the core | Every kind shares it; Nency's templates task can restyle it later |
| Comments | None in code | `CLAUDE.md` §3 |

## Notes on the documentation

1. **Cloud Tasks queue.** SAD §9 lists a Cloud Tasks queue for notifications. This design sends email from a SQL outbox in the API instead, as the product owner chose, so there is no notifications queue. This task updates SAD §9, and §6 gains the two new tables.
2. **Sender account.** SAD §2 and §7 name a Google Workspace sender, and SAD §13 has the open question "Which Google Workspace account sends notification emails?". The answer is one dedicated mailbox, Workspace or Gmail, that consents once to `gmail.send`. This task updates §2 and §7 and ticks the open question. Gmail allows about 500 messages a day from a Gmail account and about 2,000 from a Workspace account, which is enough for the pilot.
3. **Testing-mode refresh tokens.** If the `kairo2` consent screen's publishing status is **Testing**, Google expires every refresh token after 7 days. That would break the sender mailbox weekly, and every brand connection too. The status must be **In production**, which works unverified for fewer than 100 users. The README says so, and the done criteria check it.
4. **Grouping and reminders.** PRD §9 groups automatic pauses hourly and reminds approvers after 4 working hours. Both belong to the modules that raise them (Care and Approvals). The email rows already carry a due time, so either can be added without changing this design.

## Contract for other modules

`Kairo.Contracts` gains a `Notifications` folder:

```csharp
namespace Kairo.Contracts.Notifications;

public interface INotifications
{
    Task NotifyAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record Notification(string Kind, LiveAudience Audience, string Title, string Body, string Link);
```

- **Brand.** The brand is the current brand context. Notifying with no brand context throws `InvalidOperationException`.
- **Validation.** `NotifyAsync` throws `ArgumentException` when:
  - `Kind` isn't in the catalogue
  - `Title` is empty or longer than 200 characters
  - `Body` is longer than 1,000 characters
  - `Link` doesn't start with `/`
- **Links.** `Link` is a path inside the workspace, without the handle, such as `/settings/connections`. The web app puts `/{handle}` in front, and emails put `{PublicOrigin}/{handle}` in front. A changed handle therefore never breaks a stored link.
- **Recipients.** The audience is resolved to the brand's members at the time of the call:
  - `Everyone` means every member
  - `Roles(r)` means members holding any role in `r`
  - `Users(ids)` means those users who are still members
  - with no recipients, nothing is written
- **One write.** All rows are saved in one `SaveChanges` on the shared connection. They commit or roll back with the caller's transaction when there is one.
- **What it writes.**
  - one `Notifications` row per recipient
  - one `OutboundEmails` row per recipient whose email for that kind is on
  - one live event, `notification.created`, to `LiveAudience.Users(recipients)`

`Kairo.Contracts.Identity` gains a member directory, which Notifications needs for both resolving and sending:

```csharp
public interface IBrandMembers
{
    Task<IReadOnlyList<BrandMember>> ListAsync(CancellationToken cancellationToken);
    Task<BrandMember?> FindAsync(Guid brandId, Guid userId, CancellationToken cancellationToken);
}

public sealed record BrandMember(Guid UserId, string Email, string Name, BrandRoles Roles);
```

- `ListAsync` lists the current brand's members.
- `FindAsync` looks up one member of any brand in a system scope. It is used by the sending loop.
- Identity implements both from `Memberships` joined to `Users`.
- Identity saves a membership change before it calls `IConnections.ReleaseAsync`. A departing admin is therefore never among the admins told about their own connections.

## Kinds

`NotificationKinds` in the module holds one entry per kind: the key, a display name, a one-line description for the settings screen, the roles that usually receive it, its email policy, and whether email can be turned off.

| Kind | Name | Usual recipients | Email | Can turn off email |
| --- | --- | --- | --- | --- |
| `opportunities.ready` | New top opportunities | Marketer | Yes | Yes |
| `trigger.fired` | Trigger fired | Marketer | Yes | Yes |
| `approval.needed` | Approval needed | Approver | Yes | **No** |
| `work.returned` | Work sent back | Marketer | Yes | Yes |
| `launch.finished` | Launch finished | Marketer | Yes | Yes |
| `launch.failed` | Launch failed | Marketer | Yes | **No** |
| `health.problem` | Health-check problem | Marketer | Yes | Yes |
| `stock.pause_changed` | Ad paused or resumed for stock | Marketer | No (in app only) | — |
| `mass_pause.held` | Mass pause held for confirmation | Admin, Marketer | Yes | Yes |
| `pacing.alert` | Pacing alert | Marketer, Approver | Yes | Yes |
| `report.ready` | Recommendation or impact report ready | Marketer, Approver | Yes | Yes |
| `impact_lab.ready` | Impact Lab result ready | Marketer, Admin | No (in app only) | — |
| `connection.problem` | Connection or data problem | Admin | Yes | Yes |

- "Usual recipients" only decides which kinds a user sees in email settings. The audience on each call decides who is actually notified.
- PRD §9 lets users turn off email for any event except approval requests and launch problems. Those are `approval.needed` and `launch.failed`.
- Kinds are lowercase and dotted, like live event types.

## Data

| Schema | Table | Belongs to a workspace | Holds |
| --- | --- | --- | --- |
| `notifications` | `Notifications` | Yes | `Id` (`bigint` identity), `BrandId`, `UserId`, `Kind` (`nvarchar(64)`), `Title` (`nvarchar(200)`), `Body` (`nvarchar(1000)`), `Link` (`nvarchar(400)`), `CreatedAt`, `ReadAt` (nullable) |
| `notifications` | `EmailOptOuts` | Yes | `BrandId`, `UserId`, `Kind`, `CreatedAt`; primary key on all three IDs |
| `notifications` | `OutboundEmails` | Yes | `Id` (`bigint` identity), `BrandId`, `UserId`, `NotificationId` (foreign key), `Status` (`Pending`, `Sent`, `Logged`, `Skipped`, `Failed`), `Attempts`, `NextAttemptAt`, `ClaimedUntil` (nullable), `SentAt` (nullable), `LastError` (`nvarchar(500)`, nullable), `CreatedAt` |

- **Indexes.**
  - `Notifications (BrandId, UserId, Id DESC)` for the list.
  - A filtered index on `Notifications (BrandId, UserId) WHERE ReadAt IS NULL` for the unread count.
  - `OutboundEmails (Status, NextAttemptAt)` for the sending loop.
- **Separation.** All three tables are covered by the `notifications` schema's row-level security policy, through `AddBrandSeparation`, like every workspace-owned table.
- **Emails.** Addresses are never stored. The sending loop looks up the recipient's current address when it sends.
- **Entity names.** The `Notifications` row is the entity `InAppNotification`, so it doesn't clash with the contract record `Notification`.

## Email delivery

### The sending loop

`EmailDelivery` is a `BackgroundService` registered by the module.

- **Hosts.** `NotificationsModule.MapApi` switches it on, through a singleton switch it sets before the host starts. The worker never maps the API, so the worker never sends.
- **Polling.** Every 5 seconds it calls `SendDueAsync`, which tests call directly.
- **Claiming.** In a system scope, `SendDueAsync` claims up to 10 due rows:

  ```sql
  UPDATE TOP (10) notifications.OutboundEmails WITH (READPAST, UPDLOCK, ROWLOCK)
  SET ClaimedUntil = @leaseEnd, Attempts = Attempts + 1
  OUTPUT inserted.Id, inserted.BrandId, inserted.UserId, inserted.NotificationId, inserted.Attempts
  WHERE Status = 'Pending' AND NextAttemptAt <= @now AND (ClaimedUntil IS NULL OR ClaimedUntil < @now)
  ```

  The lease is 2 minutes, so two API instances never send the same email, and a crashed instance's rows are picked up after the lease ends.
- **For each claimed row:**
  1. Look up the recipient with `IBrandMembers.FindAsync`. If they are no longer a member, the row becomes `Skipped`.
  2. Look up the workspace's name and handle with `IBrandDirectory`.
  3. Render the email and send it through `IEmailSender`.
  4. On success, the row becomes `Sent` (or `Logged`, see below) with `SentAt`.
- **Retries.** A failure that may pass sets `NextAttemptAt` to now plus the next step of 1 minute, 5 minutes, 30 minutes, 2 hours and 6 hours. It clears `ClaimedUntil` and records `LastError`. The sixth failed attempt makes the row `Failed`, logged as an error.
- **What may pass.** Network errors, timeouts, 429, 5xx, and a sender token that can't be refreshed. Any other 4xx from Gmail, such as a rejected recipient address, makes the row `Failed` straight away.
- **Time.** The loop uses `TimeProvider`, so tests drive it with `FakeTimeProvider`.

### Senders

`IEmailSender.SendAsync(EmailMessage message, CancellationToken)` has two versions:

- **`GmailSender`**, used when `Kairo:Notifications:Sender` and `Kairo:Notifications:SenderRefreshToken` are both set:
  - It refreshes an access token at `https://oauth2.googleapis.com/token` with the sender's refresh token and Kairo's OAuth client (`Authentication:Google:ClientId` and `ClientSecret`). It caches the token until a minute before it expires.
  - It posts `{ "raw": base64url(MIME) }` to `https://gmail.googleapis.com/gmail/v1/users/me/messages/send`.
  - Both calls go through the `kairo-google` named `HttpClient`, so the tests' fake Google handler sees them.
  - `invalid_grant` on refresh is logged as a critical error, "The notification sender mailbox needs signing in again", and counts as a failure that may pass.
  - The MIME message is built with MimeKit, at its latest stable version, checked when planning. It is `multipart/alternative` with text and HTML, `From: Kairo <sender>`, the recipient as `To`, and a UTF-8 subject.
- **`LogEmailSender`**, used when either setting is missing, as in Aspire and in tests:
  - It logs the recipient's user ID, the kind and the subject at Information.
  - The row becomes `Logged`, not `Sent`.

### The email

`EmailTemplate.Render(notification, workspace, member)` returns the subject, HTML and text.

- **Subject.** The notification's title.
- **HTML.**
  - **Layout:** one centred card, table-based and inline-styled for email clients, with colours from `docs/design.md`:
    - page `#F7F3ED`
    - card `#FFFDF9` with border `#E6DDD0`
    - text `#1E1A15`, secondary text `#574F45`
  - **Content:** the workspace name as an eyebrow, the title, the body, and an "Open in Kairo" button. The button is `#A8461F` with `#FFFDF9` text and goes to `{PublicOrigin}/{handle}{link}`.
- **Footer.**
  - For kinds that can be turned off: "You're getting this because you're a member of {workspace}." and a "Change email settings" link to `{PublicOrigin}/{handle}/settings/notifications`.
  - For kinds that can't be turned off, it says this email can't be turned off.
- **Text version.** The same content as plain lines.
- **Encoding.** Every value is HTML-encoded.

## API

These are added to the committed OpenAPI document. Nency's screens build on them. Every route is in the brand route group, open to any member, and works only on the signed-in user's own notifications and settings, with the usual 401 and 404 rules. Writes need the `X-Kairo-Request` header, like every write.

| Method and path | What it does |
| --- | --- |
| `GET /api/brands/{brandId}/notifications?before={id}&limit={n}` | The user's notifications, newest first. `limit` is 1 to 100, 30 by default. `before` returns those with a lower ID. |
| `POST /api/brands/{brandId}/notifications/{id}/read` | Marks one read. Returns 204, and does nothing if it was already read. Another user's ID returns 404. |
| `POST /api/brands/{brandId}/notifications/read-all` | Marks all the user's notifications in this workspace read. Returns 204. |
| `GET /api/brands/{brandId}/notifications/email-settings` | The kinds that email, filtered to those whose usual recipients share a role with the user, with each one's on/off state |
| `PUT /api/brands/{brandId}/notifications/email-settings` | Sets on/off for one or more kinds and returns the same shape as `GET` |

Shapes, as camel-case JSON:

```json
GET …/notifications
{
  "items": [
    {
      "id": 123,
      "kind": "connection.problem",
      "title": "Google Ads needs reconnecting",
      "body": "Google stopped accepting Kairo's access to Google Ads. Reconnect it so refreshes and changes can continue.",
      "link": "/settings/connections",
      "createdAt": "2026-10-13T09:30:00Z",
      "readAt": null
    }
  ],
  "unreadCount": 4,
  "nextBefore": 101
}
```

`nextBefore` is the last item's ID when there may be more, otherwise `null`.

```json
GET …/notifications/email-settings
{
  "kinds": [
    { "kind": "connection.problem", "name": "Connection or data problem", "description": "A connected account needs reconnecting, or a data feed failed.", "email": true, "canTurnOff": true },
    { "kind": "approval.needed", "name": "Approval needed", "description": "Something is waiting for your approval.", "email": true, "canTurnOff": false }
  ]
}

PUT …/notifications/email-settings
{ "kinds": [ { "kind": "connection.problem", "email": false } ] }
```

- **Rejected writes.** The `PUT` returns a 400 problem when it names an unknown kind, an in-app-only kind, or a kind that can't be turned off with `email: false`.
- **What it changes.** Turning a kind off adds an `EmailOptOuts` row, and turning it back on removes it.
- **Live events.** Marking read publishes `notifications.read` with `{}` to `LiveAudience.Users(userId)`, so the user's other tabs refresh their count.
- **Audit.** Changing email settings records `notifications.email_settings_changed` in the audit log, with before and after. Reading is not recorded.

## Live events

| Type | Published when | Audience | Payload |
| --- | --- | --- | --- |
| `notification.created` | `NotifyAsync` writes notifications | The recipients only | `{ kind }` |
| `notifications.read` | One or all notifications are marked read | The user who read them | `{}` |

As with every live event, the browser refetches the list and the count through the API, and doesn't render from the payload.

## First producer: connections that need reconnecting

`ConnectionTokens.LoseAsync` in the Brands module calls `NotifyAsync` after it publishes `connection.changed`, with:

- **Kind:** `connection.problem`.
- **Audience:** `LiveAudience.Roles(BrandRoles.Admin)`.
- **Title:** "{Product name} needs reconnecting", using the display name from `Products`, such as "Google Ads".
- **Body,** by reason:
  - `token_rejected`: "Google stopped accepting Kairo's access to {Product name}. Reconnect it so refreshes and changes can continue."
  - `token_unreadable`: "Kairo can no longer read its stored access to {Product name}. Reconnect it so refreshes and changes can continue."
  - `admin_left`: "{Product name} was connected by an admin who has left or is no longer an admin. Reconnect it so refreshes and changes can continue."
- **Link:** `/settings/connections`.

This covers every path into Needs reconnect:

- a rejected token found on use or by Check connection, through `LoseOnItsOwnAsync`
- an unreadable token
- a departing admin, through `ReleaseAsync`

## Sender setup

**`deploy/setup.ps1`** gains these steps, all without prompting:

- enable `gmail.googleapis.com`
- create the empty secret `mail-sender-refresh-token`
- grant `roles/secretmanager.secretAccessor` on that secret to `kairo-api`

**`deploy/environments.psd1`** gains `MailSender`, the sender's address, for each environment. It is empty until the product owner names the mailbox.

**`deploy/set-mail-sender.ps1 <environment>`** is a new interactive script. It follows `set-google-sign-in.ps1` and calls `Set-KairoMailSender` in `Setup.ps1`. It:

1. Stops with a clear message if `MailSender` is empty.
2. Reads the OAuth client ID and secret from Secret Manager (`google-client-id`, `google-client-secret`).
3. Listens on `http://localhost:8765/` and opens the browser at Google's authorization endpoint with:
   - `scope=openid email https://www.googleapis.com/auth/gmail.send`
   - `access_type=offline` and `prompt=consent`
   - PKCE with S256, a random `state`, and `login_hint` set to the sender address
4. On the redirect, checks `state` and exchanges the code.
5. Checks that `gmail.send` was granted, and that the ID token's verified email equals `MailSender`.
6. Stores the refresh token in `mail-sender-refresh-token` with `Set-KairoSecretValue`, then prints a short confirmation without the token.

**`Release.ps1`**, when `MailSender` is set and the secret has a value:

- passes `Kairo__Notifications__Sender` to the API as an environment variable
- passes `Kairo__Notifications__SenderRefreshToken=mail-sender-refresh-token:latest` as a secret

Otherwise it warns that notification emails are only logged until `set-mail-sender.ps1` has been run.

**Manual steps** for the product owner, which the README lists:

1. Add `http://localhost:8765/` as an authorized redirect URI on the OAuth client in the Cloud Console. This is Console-only.
2. Add the `gmail.send` scope under Google Auth Platform → Data Access.
3. Make sure the publishing status is **In production** (see note 3).
4. Put the sender address in `environments.psd1`, then run `deploy/set-mail-sender.ps1 development`.

**Locally,** `Kairo:Notifications:Sender` and `Kairo:Notifications:SenderRefreshToken` can be set as user secrets on `Kairo.Api` to send real email from Aspire. Without them, emails are logged.

## Code layout

| Project | Adds |
| --- | --- |
| `Kairo.Contracts` | `Notifications/INotifications.cs` (`INotifications`, `Notification`), and `Identity/IBrandMembers.cs` (`IBrandMembers`, `BrandMember`) |
| `Kairo.Modules.Notifications` | `Data` (DbContext, the three entities, the migration with brand separation), `Kinds` (`NotificationKinds`), `Publishing` (`Notifier : INotifications`), `Inbox` (list and read endpoints), `EmailSettings` (settings endpoints), `Delivery` (`EmailDelivery`, the claim query, `IEmailSender`, `GmailSender`, `LogEmailSender`, `SenderToken`, `EmailTemplate`) |
| `Kairo.Modules.Identity` | `BrandMembers : IBrandMembers` |
| `Kairo.Modules.Brands` | `ConnectionTokens.LoseAsync` notifies admins |
| `deploy` | `set-mail-sender.ps1`, `Set-KairoMailSender`, the setup and release steps above, and `MailSender` in `environments.psd1` |

The module follows the existing rules:
- one public type
- references only to `Kairo.SharedKernel` and `Kairo.Contracts`
- tables only in its own schema
- internal migrations tidied by `core/scripts/tidy-migrations.sh`

It is already registered in the API, worker and Migrator.

## Testing

**Integration tests**, on real SQL Server through Testcontainers. The fake Google handler gains a Gmail send endpoint that records decoded messages and can be set to fail with a chosen status, plus a way to register the sender's refresh token.

- **Notify.**
  - It writes one notification per recipient, queues emails, and publishes `notification.created` to the recipients only.
  - Each of the role, user and everyone audiences reaches only the right members. A named user who isn't a member is skipped.
  - A rollback of the caller's transaction leaves no notification, email or live event.
  - An unknown kind, a bad link and no brand context throw.
- **Email settings.**
  - An opted-out kind writes the notification but no email.
  - `approval.needed` and `launch.failed` can't be turned off (400), and the `PUT` rejects unknown and in-app-only kinds.
  - The `GET` lists only kinds relevant to the user's roles.
  - A change is audited.
- **Inbox API.**
  - Paging with `before` and `nextBefore`, newest first.
  - The unread count.
  - Read one, which is idempotent, and read all, each publishing `notifications.read`.
  - Another user's notification returns 404.
  - Another workspace's notifications never appear.
  - The 401 and 404 rules apply.
- **Sending.**
  - A due email is sent through the fake Gmail with the right recipient, subject, link and footer.
  - A 500 is retried on the 1-minute, 5-minute, 30-minute, 2-hour and 6-hour schedule, and the sixth failure marks it failed.
  - A 400 fails it straight away.
  - A recipient who left is skipped.
  - With no sender configured, the email is logged.
  - Two loops running at once send each email once.
  - An expired lease is claimed again.
- **Connections.**
  - A rejected token, an unreadable token and a departing admin each notify the remaining admins with `connection.problem`.
  - The departing admin isn't notified.
- **Security policy.** The three tables are covered, through the existing coverage test.

**Unit tests:**
- the kind catalogue, including that the two required kinds can't be turned off and that in-app-only kinds don't email
- the retry schedule
- the template's encoding, links and footers
- the MIME message the Gmail sender builds

**Deployment tests** (Pester):
- setup enables the Gmail API and creates and grants the secret
- `Set-KairoMailSender` builds the consent URL, exchanges the code, and rejects a missing scope or a different email
- it stores the token
- release passes the sender only when both values exist, and warns otherwise

**Architecture tests** pick up the module's dependencies automatically.

## Out of scope

- The web screens and email restyling (Nency's **Notifications**).
- Producers other than connection problems: approvals, triggers, launches, health checks, stock pauses, mass pauses, pacing, reports and Impact Lab.
- Hourly grouping of stock pauses, approval reminders and the daily opportunity digest (see note 4). Opportunities sends the digest as one notification.
- Notifying admins about failed data feeds. This follows once **Analytics data feeds** merges.
- Deleting old notifications and email rows.
- Workspace-wide notification settings, and channels other than email and in app.

## Done criteria

1. `dotnet build core/Kairo.slnx` and `dotnet test --solution core/Kairo.slnx` pass with Docker running, with 0 warnings.
2. `npm --prefix web run generate:api` regenerates the types with the new endpoints, and the web checks still pass.
3. The deployment tests pass with the new setup, sender and release steps.
4. With Aspire running, demoting an admin who connected Google Ads leaves the other admins with a `connection.problem` notification from `GET …/notifications`, and a logged email.
5. With the sender's user secrets set, and `kairo2`'s publishing status **In production**, the same check delivers a real email.
6. SAD §2, §6 (the Operations row gains EmailOptOut and OutboundEmail), §7, §9 and the §13 open question, and the README, are updated.
