# Kairo Foundations — design

*13 October 2026 · Approved in conversation; this is the written record*

## Goal

Build the base every later feature stands on: Google sign-in, workspaces, team roles, approval settings, brand-separated data and the audit log.

Users see it as steps 1 and 4 of PRD journey 3.1, without the Brand Kit. They sign in, create a workspace, invite the team, assign roles, choose approvers and set the budget limit. Later modules get a ready pattern for tables, migrations, permissions and audit entries.

## Context

Kairo is being built for a pilot with a real brand, so it is production-grade and built foundations first. The product is split into sub-projects, and each one gets its own spec, plan and build:

| # | Sub-project | Builds on |
| --- | --- | --- |
| 1 | Foundations (this spec) | — |
| 2 | Campaign room engine: lifecycle, stage runs, agent jobs, approval gates, activity log, live updates | 1 |
| 3 | Brand Kit and catalogue | 1 |
| 4 | Opportunity Radar (Sense) | 2, 3 |
| 5 | Smart Brief and proof plan (Plan) | 4 |
| 6 | Creative Studio and Brand Guardian (Create and Check) | 5 |
| 7 | Launch | 6 |
| 8 | Post-launch care | 7 |
| 9 | Prove and Learn | 8 |
| 10+ | Triggers, Impact Lab, Ask Kairo, email notifications and Cloud deployment | Various |

Every Google integration sits behind an interface with a fake implementation, so work never waits on Google approvals. Foundations uses only Google sign-in, which needs no review for the scopes it asks for.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Sign-up | Open self-serve: any Google user can sign in and create a workspace | Chosen by the product owner |
| Sign-in | The core runs Google OpenID Connect itself, with an OAuth client from the Google Cloud project `kairo2`. No Firebase or Identity Platform. | Fewest moving parts, and the same client later asks for Google Ads and YouTube consent |
| Sessions | Stored in SQL Server and identified by an httpOnly cookie | No tokens in the browser, sign-out takes effect immediately, and it works with server components and SignalR |
| One address for web and core | The load balancer in Google Cloud, and a YARP gateway in Aspire locally | Keeps the cookie first-party |
| Several workspaces per person | Yes, with the workspace handle in every web URL | Invitations must work for people who already belong to a workspace |
| Data layout | One database, with one schema and one DbContext per module | Module boundaries hold in the database too |
| Writes across modules | One connection and one transaction per request or job, shared by every module | A change, its effects in other modules and its audit entries commit together or not at all |
| Brand separation | EF Core query filters, backed by SQL Server row-level security | A forgotten filter returns nothing rather than another brand's data |
| Audit log | An append-only table guarded by a trigger | SQL Server ledger tables can never be purged, which would block the per-brand data retention in BRD §7 |
| API contract | The core's OpenAPI document is committed, and the web app generates its types from it | A renamed field breaks the web build rather than a screen at runtime |
| Database tests | A real SQL Server in Docker, through Testcontainers | Row-level security, triggers and migrations need the real engine |
| Comments | None in code | `CLAUDE.md` §3 |

## Notes on the documentation

1. **Sign-in.** SAD §2 and §8 use Identity Platform, with the web app sending an ID token on every request. This design has the core sign users in through a Google OAuth client and keep server-side sessions behind an httpOnly cookie. This sub-project updates SAD §2 and §8 to match, and adds `Session` and `Invitation` to the Access row of SAD §6.
2. **Several approvers per gate.** PRD US-4.2 says a pending approval names "the approver". Here each gate can have several approvers, any one of whom can approve, so a pending approval names all of them.
3. **No budget limit set.** The PRD doesn't say what happens before an admin sets the per-campaign budget limit. Here, an unset limit means every brief and launch counts as above it and goes to a budget owner, which is the safe default. Sub-project 2 enforces this when it builds the gates.

## What users can do

### Sign in and out

- Anyone with a Google account can sign in. Kairo identifies users by their Google account ID (`sub`), not their email, because emails can change.
- An account whose email Google hasn't verified can't sign in.
- Each sign-in refreshes the user's email, name and picture from Google.
- Signing out ends the session on the server.

### Create a workspace

- A signed-in user creates a workspace by giving its name, handle, time zone and currency.
- The creator gets all three roles: admin, marketer and approver. They're also put on every approval duty: brief, assets, launch and budget owner. A one-person workspace therefore works straight away, and the creator can hand these to others later.
- The budget limit starts unset.
- Rules:
  - **Handle.** It is 3 to 40 characters of lowercase letters, digits and single hyphens. It starts with a letter, doesn't end with a hyphen, and is unique across Kairo.
    - It can't be a reserved word: `api`, `health`, `sign-in`, `sign-out`, `welcome`, `settings`, `new`, `admin`, `app` or `help`.
    - It can't be changed after creation.
  - **Time zone.** It must be an IANA time zone ID. Admins can change it later.
  - **Currency.** It must be an ISO 4217 code. It can't be changed after creation, because budgets and reports are kept in it.

### Invite the team

- An admin invites someone by email and picks at least one role.
- When that person signs in with a Google account whose verified email matches, `/welcome` shows the invitation. They can accept it, which makes them a member with those roles, or decline it.
- Rules:
  - Emails are compared trimmed and in lowercase.
  - You can't invite someone who is already a member.
  - Inviting an email that already has a pending invitation replaces that invitation, with the new roles and a new expiry.
  - An invitation expires 14 days after it's sent, and an admin can revoke it before then.
  - No email is sent. Sending email comes with the Notifications sub-project, so for now the admin tells the person to sign in.

### Manage the team

- Admins change members' roles and remove members, and any member can leave.
- Rules:
  - Every member keeps at least one role.
  - The last admin can't be removed or demoted, and can't leave.
  - Removing a member (including a member leaving), or taking away their approver role, also takes them off their approval duties. This is rejected if they're the only person on a duty.

### Approval settings

- Admins choose who holds each approval duty and set the per-campaign budget limit.
- There are four duties:
  - brief approver (gate 1)
  - asset approver (gate 2)
  - launch approver (gate 3)
  - budget owner, who approves briefs and launches above the budget limit
- Rules:
  - Each duty always has at least one person, and any one of them can approve.
  - Everyone on a duty must hold the approver role.
  - The budget limit is either empty or an amount of zero or more, in the workspace currency.
- Approvers for recommendations come later, with the Care sub-project.

### Audit log

Every change above is recorded. Admins see the log in Settings, newest first, with paging. Filters and export (US-13.2) come later.

### Several workspaces

One person can belong to several workspaces. The workspace handle appears in every web URL (`/acme/today`), and a switcher in the menu moves between workspaces.

- Opening `/` takes a signed-in user to the workspace they used last, if they still belong to it. Otherwise it takes them to their first workspace, or to `/welcome` if they have none.
- The switcher also links to `/welcome`, for creating another workspace. It shows how many invitations are waiting, so people who already belong to a workspace still see new ones.

## Sign-in and sessions

### One address

The browser reaches the web app and the core API through a single origin, so the session cookie is first-party:
- In Google Cloud, the load balancer sends `/api/*` to the core and everything else to the web app. This is set up in the deployment sub-project.
- Locally, a YARP gateway in Aspire does the same at `http://localhost:8000`.

The API knows this public origin from configuration and uses it to build the Google redirect URI, `{origin}/api/auth/callback/google`.

### Signing in

1. The "Sign in with Google" link goes to `GET /api/auth/sign-in?returnUrl=…`.
2. The core redirects to Google using ASP.NET Core's OpenID Connect handler. It uses the authorization code flow with PKCE, asks for the `openid email profile` scopes, and prompts for account selection.
3. Google returns to `/api/auth/callback/google`, and the handler validates the ID token.
4. The core rejects an unverified email. Otherwise it creates or updates the user and starts a session.
5. The core redirects to the return URL. That URL must be a local path, so it must start with `/` and not with `//` or `/\`. Anything else falls back to `/`.

User sign-in is registered in the API host only. The worker will authenticate Google-signed job calls instead, in a later sub-project.

### Sessions

- The cookie authentication handler keeps each session in SQL Server through a session store (`ITicketStore`). The cookie holds only the encrypted session key.
- The cookie is `__Host-kairo-session`, set httpOnly, Secure, SameSite=Lax, with path `/`.
- A session ends after 3 days without use, or 14 days after sign-in, whichever comes first. Both limits are configurable.
- `POST /api/auth/sign-out` deletes the session and the cookie, so a copied cookie stops working.
- Data Protection keys, which encrypt the cookie and the sign-in state, are stored in the database, so every API instance can read them. In Google Cloud they'll be encrypted with Cloud KMS, in the deployment sub-project.
- Secure cookies on `http://localhost` depend on the browser treating localhost as secure, which Chromium browsers do. If that fails in practice, the local gateway moves to HTTPS with the .NET development certificate.

### CSRF protection

- SameSite=Lax keeps the cookie off POST requests that come from other sites.
- As a second layer, the core rejects any POST, PUT, PATCH or DELETE request that lacks the `X-Kairo-Request` header.
- A page on another origin can't add that header without a CORS preflight, and the core allows no cross-origin requests.
- Sign-in and the Google callback are exempt, because they are GET requests.

## Access control

- Endpoints that belong to a workspace are grouped under `/api/brands/{brandId}`.
- For each request in that group, the core loads the caller's membership in the workspace:
  - with no session, it returns 401
  - if the caller isn't a member, it returns 404, so nobody can probe which workspace IDs exist
  - if the caller is a member but lacks the role the endpoint needs, it returns 403
- Each endpoint declares the roles it needs, following PRD §8.
- Memberships are read from the database on every request rather than stored in the session. Role changes and removals therefore take effect immediately.
- The membership check sets the request's brand context, which the data layer uses to keep brands apart.

## Data

### Layout and migrations

- There is one SQL Server database. Each module that stores data has its own schema, named after the module in snake case (`identity`, `brands`, `audit`, `brand_kit` and so on), and its own EF Core DbContext.
- A DbContext maps only tables in its own schema, and keeps its migration history in that schema too.
- Each module has its own migrations. `IModule` gains `MigrateAsync(IServiceProvider, CancellationToken)`, and modules without a database return a completed task.
- A new `Kairo.Migrator` console app registers every module and calls `MigrateAsync` on each, then exits.
  - Aspire runs it before the API and worker start.
  - In Google Cloud it becomes the Cloud Run job that the project-structure spec planned for migrations.

### One connection and transaction per request

- Every module DbContext in a request or job uses the same SQL connection, which opens on first use.
- Requests that change data (POST, PUT, PATCH, DELETE) run in a single transaction shared by all modules:
  - it commits if the endpoint returns a success status
  - it rolls back on an error status or an exception
- Modules call `SaveChanges` as usual. Nothing is committed until the request ends.
- No database work runs in parallel within one request, because a SQL connection allows one command at a time.

### Keeping brands apart

Every table that belongs to a workspace has a `BrandId` column. The `brands.Brands` table uses its `Id` for this. Two layers keep brands apart:

1. **EF Core.** Each module DbContext adds a query filter for the current brand to every workspace-owned entity, and stamps the current brand on every new row.
2. **SQL Server row-level security.**
   - Each module schema has a predicate function and a security policy covering its workspace-owned tables.
   - A filter predicate hides other brands' rows from reads, updates and deletes.
   - Block predicates stop rows being inserted for, or moved to, another brand.
   - The predicate compares the row's brand with the connection's session context. With nothing set, no rows are visible.

**Session context.**
- The connection carries two session context keys, `kairo.brand_id` and `kairo.all_brands`.
- Both are written whenever the connection opens and whenever the brand context changes. A pooled connection therefore never keeps an earlier request's brand.

**System scope.** Reads that legitimately span workspaces open an explicit system scope. This sets `kairo.all_brands` and skips the EF Core filter. The only such reads in this sub-project are:
- the membership check itself
- the "my workspaces" list
- the "my invitations" list

Later, worker jobs that loop over brands will use the same scope.

**Changing the brand within a request.**
- Creating a workspace sets the brand context to the new workspace's ID before writing anything.
- Accepting an invitation sets it to the invitation's workspace after checking the email match.

**What sits outside both layers.** Users, sessions and Data Protection keys belong to no workspace.

### Tables

| Schema | Table | Belongs to a workspace | Holds |
| --- | --- | --- | --- |
| `identity` | `Users` | No | Google account ID (unique), email, name, picture URL, created and last sign-in times |
| `identity` | `Sessions` | No | Session key, user, serialized ticket, and created, last-seen and expiry times |
| `identity` | `DataProtectionKeys` | No | Keys for the session cookie and sign-in state |
| `identity` | `Memberships` | Yes | Brand, user, roles (admin, marketer, approver), joined time |
| `identity` | `Invitations` | Yes | Brand, email, roles, who invited, sent and expiry times, status (pending, accepted, declined or revoked) |
| `identity` | `ApprovalDuties` | Yes | Brand, duty (brief, assets, launch or budget owner), user |
| `identity` | `BudgetLimits` | Yes | Brand, per-campaign limit (optional), currency |
| `brands` | `Brands` | Yes, by `Id` | Handle (unique), name, time zone, currency, creator, created time |
| `audit` | `Entries` | Yes | See below |

### Audit log

- The Audit module owns `audit.Entries`. Other modules record entries through `IAuditLog` in `Kairo.Contracts`, inside the same transaction as the change.
- Each entry holds:
  - the brand and the time
  - the actor: its type (user, agent or system), ID and display name at the time
  - the action, such as `member.roles_changed`
  - the target's type and ID
  - the before and after values as JSON
  - the trace ID
- An `INSTEAD OF UPDATE, DELETE` trigger rejects every change to existing entries.
  - A future retention job can purge a brand's entries under controlled conditions by disabling the trigger.
  - Ledger tables would make that purge impossible, which is why this design doesn't use them.
- Actions recorded in this sub-project:
  - `brand.created` and `brand.updated`
  - `invitation.sent`, `invitation.revoked`, `invitation.accepted` and `invitation.declined`
  - `member.roles_changed`, `member.removed` and `member.left`
  - `approval_settings.changed`
- Sign-ins aren't audit entries. They go to the logs.

### Conventions

- Keys are GUIDs generated by EF Core in SQL Server's sequential order.
- Times are stored in UTC as `datetimeoffset`, and shown in the workspace's time zone.
- Money is a `decimal(19,4)` amount stored with its ISO 4217 currency code, never a floating-point number.
- Errors use RFC 9457 problem details. Validation errors list the fields they apply to.

## Modules and contracts

| Project | Adds |
| --- | --- |
| `Kairo.SharedKernel` | Brand context (user, brand, roles, system scope); the `IBrandAccess` membership check, implemented by Identity; filters for the whole `/api` group (the CSRF header check, and one transaction per write request); the `/api/brands/{brandId}` route group with its membership and role checks; the shared connection and transaction; a module DbContext base class (schema, history table, brand filter and stamping); session context handling; migration helpers that add a table to the module's security policy |
| `Kairo.Contracts` | `IAuditLog` (Audit implements); `IMembershipDirectory`, the workspaces a user belongs to with their roles (Identity implements); `IWorkspaceFounding`, which sets up a new workspace's founder, duties and budget limit currency (Identity implements); `IBrandDirectory`, names and handles for a set of brand IDs (Brands implements) |
| `Kairo.Modules.Identity` | Users, sessions, sign-in and sign-out, memberships, invitations, approval duties and the budget limit |
| `Kairo.Modules.Brands` | Workspaces: create, read and update |
| `Kairo.Modules.Audit` | The audit log and its read endpoint |
| `Kairo.Migrator` | New host that applies every module's migrations |

The modules keep the existing rules. Each one exposes exactly one public type, and references only `Kairo.SharedKernel` and `Kairo.Contracts`. `Kairo.SharedKernel` gains the EF Core SQL Server package.

## Core API

| Method and path | Who | What it does |
| --- | --- | --- |
| `GET /api/auth/sign-in?returnUrl=` | Anyone | Starts Google sign-in |
| `GET /api/auth/callback/google` | Google | Finishes sign-in |
| `POST /api/auth/sign-out` | Signed in | Ends the session and clears the cookie |
| `GET /api/me` | Signed in | Returns the current user |
| `GET /api/me/invitations` | Signed in | Lists pending invitations for the user's verified email, with workspace names |
| `POST /api/me/invitations/{id}/accept` | Signed in | Joins the workspace with the invited roles |
| `POST /api/me/invitations/{id}/decline` | Signed in | Declines the invitation |
| `GET /api/brands` | Signed in | Lists the user's workspaces, with handle, name and the user's roles |
| `POST /api/brands` | Signed in | Creates a workspace |
| `GET /api/brands/{brandId}` | Member | Returns workspace details |
| `PATCH /api/brands/{brandId}` | Admin | Changes the name or time zone |
| `GET /api/brands/{brandId}/members` | Member | Lists members and their roles |
| `PUT /api/brands/{brandId}/members/{userId}/roles` | Admin | Changes a member's roles |
| `DELETE /api/brands/{brandId}/members/{userId}` | Admin, or the member themself | Removes a member, or leaves |
| `GET /api/brands/{brandId}/invitations` | Admin | Lists pending invitations |
| `POST /api/brands/{brandId}/invitations` | Admin | Invites an email with roles |
| `POST /api/brands/{brandId}/invitations/{id}/revoke` | Admin | Revokes an invitation |
| `GET /api/brands/{brandId}/approval-settings` | Member | Returns the people on each duty and the budget limit |
| `PUT /api/brands/{brandId}/approval-settings` | Admin | Replaces the duties and the budget limit |
| `GET /api/brands/{brandId}/audit-log?before=&limit=` | Admin | Lists entries, newest first |

Settings pages are readable by every member, as PRD §8 lets every role view the workspace. Only admins can change them.

The audit log is paged by a cursor. Each page returns an opaque `before` value for the next page, and `limit` defaults to 50, up to a maximum of 200.

The OpenAPI document comes from ASP.NET Core's built-in generator, mapped in development only. It is committed as `core/src/Kairo.Api/openapi.json`.

## Web app

### Routes

| Route | Screen |
| --- | --- |
| `/` | Sends signed-in users to their most recent workspace, or to `/welcome` |
| `/sign-in` | "Sign in with Google" |
| `/welcome` | Pending invitations, and a form to create a workspace |
| `/[workspace]/today`, `/[workspace]/opportunities`, `/[workspace]/campaigns/…` and the other existing screens | The current placeholder screens, moved under the workspace segment |
| `/[workspace]/settings` | Workspace name, handle, time zone and currency |
| `/[workspace]/settings/team` | Members, roles and invitations |
| `/[workspace]/settings/approvals` | Approval duties and the budget limit |
| `/[workspace]/settings/audit-log` | The audit log, for admins |
| `/health` | Unchanged |

- The shell adds a workspace switcher above the menu, plus a user menu with the user's name and sign-out. The menu links include the current workspace.
- `proxy.ts`, the Next.js 16 replacement for middleware, sends requests that have no session cookie to `/sign-in?returnUrl=…`. It skips `/sign-in`, `/health` and static assets.
  - This is only a fast path. The core still validates every session.
  - The proxy also remembers the last workspace the user opened, which `/` uses.

### Data flow

- Pages read data in server components, through a server-only client in `src/lib/core-api/`.
  - The client is built on `openapi-fetch`, using types generated by `openapi-typescript` from the committed OpenAPI document.
  - It forwards the user's session cookie, and reads the core's address from configuration.
  - A 401 sends the user to `/sign-in`, a 403 renders "you don't have access", and a 404 renders "not found".
- Forms submit through Server Actions.
  - These call the same client, which adds the `X-Kairo-Request` header.
  - Validation errors from the core's problem details are shown next to their fields.
- The browser talks to the core directly only for sign-in and sign-out (and, later, SignalR).

### Look and feel

- Forms use plain HTML controls styled with Tailwind, which are accessible by default.
- A component library will be chosen when the first data-heavy screen arrives.
- Settings pages show editing controls to admins only. The core enforces this either way.

### Generated types

- `npm run generate:api` regenerates `src/lib/core-api/schema.ts` from `core/src/Kairo.Api/openapi.json`, and strips the generator's header comment to follow `CLAUDE.md` §3.
- CI regenerates it and fails if the result differs from the committed file.

## Local development

### AppHost changes

| Resource | Change |
| --- | --- |
| `gateway` | New YARP resource on host port 8000. It routes `/api/{**catch-all}` to `api` and everything else to `web`. |
| `migrator` | New project resource. It waits for SQL Server and runs to completion. |
| `api` | Waits for `migrator` to complete. It receives the Google client ID and secret, and the public origin `http://localhost:8000`. |
| `worker` | Waits for `migrator` to complete |
| Parameters | `google-client-id`, and `google-client-secret` (marked secret), read from the AppHost's user secrets |

Developers open `http://localhost:8000`.

### One-time Google setup

This is done in the Cloud Console for project `kairo2`, because Google offers no CLI for it. The README lists the steps:

1. **Google Auth Platform → Branding.** Set the app name "Kairo" and the support and developer contact emails.
2. **Audience.** Choose External. For open sign-up, publish the app, which needs no verification review for `openid email profile`. While the app is in Testing, only the listed test users can sign in.
3. **Clients.** Create a "Web application" client with the redirect URI `http://localhost:8000/api/auth/callback/google`.
4. **Store the credentials.** Save the client ID and secret with `dotnet user-secrets` in `core/src/Kairo.AppHost`, under `Parameters:google-client-id` and `Parameters:google-client-secret`.

## Testing

### Core integration tests

- These live in `Kairo.Api.Tests`. They run against a real SQL Server through Testcontainers, with one container per test run, migrated by the Migrator's code.
- Each test creates its own workspaces and users, so tests don't interfere with each other.
- The existing health tests keep their unreachable-database setup.
- A test-only endpoint, added by the test host, signs in as any user through the real cookie handler and session store. Tests therefore exercise real sessions without calling Google.
- The Google-specific step is a separate class that takes the validated ID token's claims, so it's unit-tested directly. It covers rejecting unverified emails and creating or updating users.

**What the integration tests cover:**
- **Permissions.** The PRD §8 permission matrix on every endpoint, and the 401, 403 and 404 rules.
- **Request rules.** The CSRF header rule, and the return-URL check (including `//evil.example` and `/\evil.example`).
- **Sessions.** Sign-out invalidating a copied cookie, and the idle and absolute timeouts.
- **Invitations.** Email matching, expiry, revoking, replacing a pending invitation, and refusing to invite existing members.
- **Team and approval rules.** The last admin, at least one role per member, and duties tied to the approver role.
- **Atomic workspace creation.** A workspace is created with its membership, duties, budget limit and audit entry, all in one transaction. A forced failure in the last step leaves nothing behind.
- **Brand separation, each layer on its own:**
  - with the EF Core filter switched off, row-level security still hides and blocks other brands' rows
  - raw SQL with no brand set sees nothing
  - a pooled connection never keeps an earlier brand
- **Audit log.** It rejects updates and deletes.
- **OpenAPI document.** It matches the committed `openapi.json`. The test can rewrite the file locally when it's run with an environment variable set.

### Architecture tests

- Existing rules stay.
- New rules:
  - Every module DbContext maps tables only in its own schema. This is checked from the EF Core model, with no database needed.
  - The Migrator registers every module.
- An integration test also fails if any table with a `BrandId` column, or `brands.Brands`, isn't covered by a security policy. It checks this in `sys.security_predicates`.

### Other suites

- **Web.** Vitest tests cover the shell, the workspace switcher, the proxy's redirect, the settings forms (including validation errors), and the API client's handling of 401 and 404.
- **Agent service.** Unchanged.
- **Browser end-to-end tests.** None yet, because Google sign-in can't be automated. Real sign-in is checked by hand (see the done criteria). Browser tests come with the first full journey, using a fake OpenID Connect server in Aspire.

### CI

The core job already runs on `ubuntu-latest`, which has Docker for Testcontainers. The web job adds a check that the generated API types are current.

## Versions

These are the latest stable releases, checked on 13 October 2026.

| Package | Version |
| --- | --- |
| `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` and the `dotnet-ef` tool | 10.0.12 |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` and `Microsoft.AspNetCore.OpenApi` | 10.0.12 |
| `Aspire.Hosting.Yarp` | 13.6.1 |
| `Testcontainers.MsSql` | 4.16.0 |
| `openapi-typescript` | 7.13.0 |
| `openapi-fetch` | 0.17.0 |

## Out of scope

- Account connections: Google Ads, YouTube, Merchant Center, GA4 and BigQuery.
- The Brand Kit, the catalogue and every later module's features.
- Approval gates themselves, which belong to sub-project 2. This sub-project stores who approves, not the approvals.
- Approvers for recommendations.
- Email, in-app notifications, the SignalR hub and the Redis backplane.
- Deleting a workspace, data-retention purges, and changing a workspace's handle or currency.
- Listing or revoking other sessions, and signing out everywhere.
- Limits on how many workspaces one person can create.
- Audit log filters and export.
- Deployment: `deploy.bat`, Cloud Run, the load balancer, Cloud SQL, Secret Manager and Cloud KMS.
- Browser end-to-end tests.
- Any change to the agent service.

## Done criteria

1. `dotnet build core/Kairo.slnx` and `dotnet test --solution core/Kairo.slnx` pass from the repository root with Docker running.
2. `npm --prefix web run lint`, `npm --prefix web test` and `npm --prefix web run build` pass, and `npm --prefix web run generate:api` leaves no changes.
3. The agent service checks still pass.
4. `dotnet run --project core/src/Kairo.AppHost` starts every resource, the migrator completes, and the API and worker report healthy.
5. At `http://localhost:8000`, with real Google accounts, a manual check succeeds:
   - sign in and create a workspace
   - invite a second account, and accept the invitation as that account
   - change roles, approval duties and the budget limit, and see each change in the audit log
   - switch between two workspaces
   - sign out
6. CI runs the new tests and the API types check.
7. SAD §2, §6 and §8 and the README are updated.
