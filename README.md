# Kairo

Kairo takes a marketer from a rising demand signal to a live, on-brand Google Ads campaign in under an hour, then proves the extra sales it caused. The [BRD](docs/Kairo-BRD.md), [PRD](docs/Kairo-PRD.md) and [SAD](docs/Kairo-SAD.md) describe the product and its architecture.

## Repository layout

| Folder | What it holds |
| --- | --- |
| `core/` | The ASP.NET Core API and worker, the 14 modules, the Migrator, and the Aspire AppHost for local development |
| `agents/` | The Python agent service, built with Google's Agent Development Kit and FastAPI |
| `web/` | The Next.js web app |
| `deploy/` | `deploy.bat`'s scripts, the per-environment settings and their tests |
| `docs/` | Requirements, architecture, and design specs and plans |

## Prerequisites

- .NET SDK 10.0.400 or later
- Node.js 24 with npm
- [uv](https://docs.astral.sh/uv/) 0.12, which installs Python 3.13 for you
- Docker Desktop, running. The local stack runs SQL Server and Redis in containers, and the core's integration tests start their own SQL Server container.
- A trusted HTTPS development certificate: `dotnet dev-certs https --trust`
- A Google OAuth client for sign-in, described next

## Set up Google sign-in

Kairo signs people in with a Google OAuth client from the Google Cloud project `kairo2`. Google offers no command-line tool for this, so do it once in the Cloud Console:

1. **Branding.** Open Google Auth Platform → Branding. Set the app name to "Kairo", and fill in the support and developer contact emails.
2. **Audience.** Choose External.
   - While the app is in Testing, only the test users you list can sign in.
   - Publish the app when anyone should be able to sign up. Asking only for `openid`, `email` and `profile` needs no verification review.
3. **Clients.** Create a Web application client with the authorized redirect URI `http://localhost:8000/api/auth/callback/google`.
4. **Store the client ID and secret** for the AppHost:

   ```bash
   dotnet user-secrets set "Parameters:google-client-id" "<client id>" --project core/src/Kairo.AppHost
   dotnet user-secrets set "Parameters:google-client-secret" "<client secret>" --project core/src/Kairo.AppHost
   ```

## Set up account connections

Admins connect the brand's Google Ads, YouTube, Merchant Center, Google Analytics 4 and BigQuery accounts in Settings → Connections. Each product gets its own Google consent through the same OAuth client used for sign-in.

Since 9 September 2026 the Google Ads API needs no developer token. Access levels belong to the Google Cloud project behind the OAuth client, and `kairo2` has Basic access.

Do this once in the Cloud Console for `kairo2`:

1. **APIs.** `deploy/setup.ps1` enables the Google Ads, YouTube Data, Merchant, Analytics Admin, Analytics Data, BigQuery, Cloud Scheduler and Cloud KMS APIs. Run it once before connecting accounts locally, too.
2. **Data access.** In Google Auth Platform → Data Access, add these scopes:
   - `https://www.googleapis.com/auth/adwords`
   - `https://www.googleapis.com/auth/youtube.readonly`
   - `https://www.googleapis.com/auth/youtube.upload`
   - `https://www.googleapis.com/auth/content`
   - `https://www.googleapis.com/auth/analytics.readonly`
   - `https://www.googleapis.com/auth/bigquery`
3. **Redirect URIs.** Add `http://localhost:8000/api/connections/callback/google` to the web OAuth client, plus the connections redirect URI that `deploy.bat` prints for each environment.
4. **Verification.**
   - These scopes are sensitive. Until Google verifies the app for them, people see an "unverified app" warning, and at most 100 users can give consent. That is enough for the pilot.
   - To verify, open Google Auth Platform → Verification Center and submit:
     - a public homepage and privacy policy on a verified domain
     - a demo video showing each scope in use
     - a short reason for each scope

Locally, refresh tokens are encrypted with ASP.NET Data Protection. In Google Cloud, `deploy.bat` points the API and worker at the Cloud KMS key `connection-tokens`.

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

## Run the analytics feeds locally

Every night at 05:00 India time, Cloud Scheduler calls the worker. It loads each brand's Google Ads reports, GA4 totals and sales table into a BigQuery dataset of its own, `brand_<brand ID>`. Ads and GA4 results are loaded by state and by city, which the proof plan uses to match holdout regions. Admins and marketers can also upload sales as a CSV file with the columns `date`, `region`, `orders` and `revenue`; name cities in `region` where you can.

A feed loads the last 13 months the first time it runs, then the last 7 days. A table added to a feed later, such as the city tables, gets its own 13-month backfill on the next run.

Locally the feeds are off until you point them at a project:

1. Sign in for Application Default Credentials, which Kairo uses to write to BigQuery:

   ```bash
   gcloud auth application-default login
   ```

2. Tell the AppHost which project holds the datasets:

   ```bash
   dotnet user-secrets set "Parameters:analytics-project" "kairo2" --project core/src/Kairo.AppHost
   ```

3. Start Kairo and connect Google Ads, GA4 or BigQuery in Settings → Connections. Then run the job for every brand, or add `?brandId=<id>` for one:

   ```bash
   curl -X POST http://localhost:5200/jobs/analytics/daily
   ```

   The worker listens on http://localhost:5200, as the Aspire dashboard shows. The response lists each brand's feeds and how many rows each loaded.

Your Google account needs BigQuery Data Owner and Job User on the project. Local runs write real datasets in `kairo2`, so delete a test brand's dataset in the BigQuery console when you're done with it.

## Draft a Brand Kit locally

Brand Kit drafting reads the website and Google Ads in the core, then asks Gemini on Vertex AI for the draft. Locally the agent service needs a project to call Gemini in:

1. Sign in for Application Default Credentials, if you haven't already:

   ```bash
   gcloud auth application-default login
   ```

2. Tell the AppHost which project to use:

   ```bash
   dotnet user-secrets set "Parameters:ai-project" "kairo2" --project core/src/Kairo.AppHost
   ```

3. Start Kairo, open Brand Kit, and draft from the brand's website.

Your Google account needs Vertex AI User on the project. Without the parameter, drafting runs fail with "Kairo couldn't draft the Brand Kit this time." Set `KAIRO_BRAND_KIT_MODEL` on the agent service to try another Gemini model; the default is `gemini-3.5-flash`.

## Try the catalogue locally

The catalogue takes product data in Google's Merchant Center format: CSV, TSV, or RSS and Atom XML, optionally gzipped, up to 50 MB and 50,000 products.

- **From a file.** Post it as the `file` field to `/api/brands/{brandId}/catalogue/imports`. Locally, the raw files are kept in the `kairo-files` folder in your temp folder.
- **From a feed link.** Kairo reads only public `http` and `https` addresses on the usual ports, so a file served from `localhost` is refused. To try a feed, use a file hosted somewhere public.
- **The hourly refresh.** In Cloud Run, Cloud Scheduler calls the worker every hour. Locally, call it yourself, for every brand with a feed or with `?brandId=<id>` for one:

  ```bash
  curl -X POST http://localhost:5200/jobs/catalogue/refresh
  ```

Three failed refreshes in a row send admins a "Your product feed isn't refreshing" notification.

## Try Opportunity Radar locally

Opportunity Radar turns rising Google searches into ranked opportunities the brand can sell into. A brand needs Google Ads connected and at least one categorised product in stock.

- **The daily refresh.** Cloud Scheduler calls the worker at half past every hour, and each brand refreshes once a day from 05:00 in its own time zone, or straight away if it has never refreshed. Locally, call it yourself. With `?brandId=<id>` it refreshes that brand even if it already ran today:

  ```bash
  curl -X POST "http://localhost:5200/jobs/opportunities/refresh?brandId=<id>"
  ```

- **What it does.** The worker seeds Google Ads Keyword Planner with watched topics, the approved Brand Kit's product categories and the in-stock catalogue categories. It keeps searches that grew at least 20% and had 100 searches last month. The Sense agent groups them into topics and rates product fit, and the core scores each topic: 40% growth, 30% volume, 30% fit, lowered for topics like recently dismissed ones. Sense then explains the top topics with Google Search grounding, and only reasons with a source are kept.
- **Market.** Keyword data and rising regions come from one country and language, set with `Kairo:Opportunities:CountryCode` (default `IN`) and `Kairo:Opportunities:Language` (a Google Ads language constant, default `1000` for English).
- **Accepting** an opportunity opens a campaign room with the evidence copied in.

## Try triggers locally

Trigger rules turn a weather forecast, a festival date or a stock level into an opportunity card with a short brief already drafted. Admins manage rules and the festival calendar; nothing a trigger creates spends money until someone accepts it and the usual approvals pass.

- **The check.** Cloud Scheduler calls the worker every 15 minutes. Weather rules are checked hourly, festival rules once a day in the brand's time zone, and stock rules after every catalogue import. Locally, call it yourself. `force=true` checks every active rule straight away:

  ```bash
  curl -X POST "http://localhost:5200/jobs/triggers/check?brandId=<id>&force=true"
  ```

- **Weather.** Forecasts come from the Google Maps Platform Weather API, signed in with Application Default Credentials. Enable `weather.googleapis.com` in the Google Cloud project, run `gcloud auth application-default login`, and set `Parameters:ai-project` for the AppHost so the worker bills the forecast calls to that project. Each city's forecast is fetched at most once an hour and shared by every brand.
- **Festivals.** Each brand's calendar starts with Kairo's list of Indian festivals and shopping days for 2026 and 2027. Lunar festival dates move each year, so check them and edit or add events for your market.
- **Stock.** Stock rules need unit counts. Add a `quantity` (or `stock_quantity` or `sell_on_google_quantity`) column to the catalogue file or feed.
- **When a rule fires.** Kairo matches in-stock products in the rule's category. If an open opportunity already sells them, the trigger is added to it; otherwise a new card appears, marked with what triggered it. The Plan agent drafts a short brief, marketers get a notification, and the rule waits out its cooldown before firing again in the same region. A card nobody accepts expires when its condition ends.

## Try a campaign room locally

Accepting an opportunity opens a campaign room. The room moves through brief draft, creating, asset review, ready to launch, launching, live, results ready and closed, and every move is in the room's activity log.

- **Agent work.** Each stage queues its agent work as a stage run. The core API picks runs up within a few seconds, and the worker sweeps any left behind every 5 minutes. Locally the API does it, or call the sweep yourself:

  ```bash
  curl -X POST "http://localhost:5200/jobs/campaigns/stage-runs"
  ```

- **The brief.** Accepting queues the Plan agent (Gemini Pro, `KAIRO_PLAN_BRIEF_MODEL`), which drafts the full brief: objective, KPI, campaign types, budget, dates, regions, audiences, key messages, products, landing pages and languages, each with its reason and evidence. A trigger's short brief is the starting point when there is one. The budget stays inside a range the core works out from keyword volumes and Keyword Planner bid ranges, or from product prices when there are no bids.
- **Changing the brief.** Marketers edit fields directly, or type an instruction such as "add Hindi and lead with quick-dry" and apply the previewed changes. Every change saves a new brief version, and the brief re-checks the budget limit, dates, regions, stock and connections after each one. The brief can't change while approval is pending.
- **The proof plan.** Once the brief is drafted, and again whenever a change moves its regions or dates, the Plan agent designs the test over the brief's dates. It reads a year of daily history by city from the analytics dataset, plus search demand by city from Keyword Planner when the room has keywords. It picks holdout cities inside the brief's regions that move like the rest, works out the test window and the smallest lift the test can detect from past periods, and the cost of holding the cities back. With too little city history it compares whole states, and it warns when the data is thin. Marketers can swap holdouts from the ranked list while the brief is a draft (`GET /campaigns/{id}/proof-plan` and `POST /campaigns/{id}/proof-plan/holdouts`).
- **Gates.** A marketer asks for brief and launch approval, and asset approval is asked for when the checked assets arrive. Only the approvers chosen for that gate in Settings can approve or send work back, with a comment. A marketer can discard a room before launch.
- **Brief approval.** Gate 1 can be asked for once the brief has no blocking problems and the test plan was designed for its current regions and dates. A brief above the budget limit, or in a different currency from it, goes only to the budget owners chosen in Settings; with no budget owner, it can't be sent. The route is checked again when someone approves, so a lowered limit needs a budget owner. Approving a test plan that has data warnings is recorded, so the report can say the test was weaker.
- **The lock.** Approving the launch locks the test plan. After that, holdouts or the end date change only through `POST /campaigns/{id}/proof-plan/changes` with a reason; each change is a new flagged version in the activity log, and `IProofPlans` gives later stages the plan and its changes.
- **The ads.** Approving the brief queues the Create agent (Gemini Flash, `KAIRO_CREATE_MODEL`). For each language in the brief, in parallel, it writes keywords and a search ad for each of up to five ad groups and Demand Gen text with the brand's name and a call to action, written natively in that language and its script rather than translated; Hindi keywords also come in Hinglish (Hindi in Latin letters). Text in the wrong script is left out and the agent is asked once more. Shopping titles are written once, in the language of the products' current titles (the feed language), and shown next to the current titles. It works in the Brand Kit's voice, leaves out banned words, competitors and any price, offer or claim the catalogue and brief don't support, and keeps every text within the Google Ads limits; the core checks the limits again before saving. Every asset keeps its versions, with the model, prompt version and instruction that made each one.
- **Images and video.** After the copy, the Create agent makes images from the catalogue photos (the main photo and up to two more per product, read through the safe web client, 10 MB at most). Each of up to four products gets a product shot and the first two a lifestyle shot, cropped exactly to the Google Ads sizes the campaign needs: landscape 1200×628 and square 1200×1200 for Search and Demand Gen, plus portrait 960×1200 for Demand Gen. Images have no language (`zxx`) and serve every language. When the brief has Demand Gen and YouTube is connected, the lifestyle shots also come in 1080×1920, and the agent animates them with Veo into two 8-second clips once, then for each brief language with a voice writes a voiceover script in that language, reads it with Cloud Text-to-Speech and puts together a 15-second 9:16 video with captions and an end card with the Brand Kit's logo (PNG, JPEG or WebP) on its primary colour. Videos are made for English and Hindi today; other brief languages get their ads but no video, and the brief warns about it. The voiceover is kept as its own asset, and the clips as hidden footage, so regenerating the video reuses them unless the instruction is about the visuals; a locked voiceover keeps its words when its video is regenerated. Without YouTube, Demand Gen runs image ads only. A video, or a video regeneration, that fails is tried twice; after that the marketer can retry it or continue without it (`POST /campaigns/{id}/assets/skip-video`). Media goes to the `generated-media` bucket under `drafts/`, which deletes files after 30 days, and is served by `GET /campaigns/{id}/assets/{assetId}/file?version=n` to members of the workspace only.
- **Changing the assets.** While the room is creating and no agent is working, marketers list the assets (`GET /campaigns/{id}/assets`, grouped by campaign type and language, with `?language=hi` to show one language and the images), edit text inline (`PATCH /campaigns/{id}/assets/{assetId}`), lock or unlock an asset, or ask Kairo to regenerate one with an instruction (`POST .../regenerate`). Locked assets never change, and an edit or rewrite marks the asset as unchecked. Assets sent back from asset review are written again, except the locked ones, and the unlocked ads of a language taken out of the brief are removed. Brand Guardian (section 8) moves checked assets on to asset review; until it lands the room waits in creating.
- **Asset history.** Every asset version records how it was made: the model and prompt version, a SHA-256 digest of each request sent to the Create agent, the products, the catalogue photo URLs, the images or footage it was made from, the Brand Kit and brief versions and the stage run. An edit records who made it, the fields changed and the version it replaced; a regeneration records who asked and the instruction. `GET /campaigns/{id}/assets/{assetId}/history` lists the versions, newest first. A Brand Memory list is kept empty until section 11. Approving the assets records the approver on each current version and copies approved media out of `drafts/` to a kept key in the same bucket before anything else changes, so a missing or unreadable file stops the approval and a retry copies again safely; earlier versions stay in `drafts/` and expire.
- **AI labels.** Every asset says it was made with AI, and a person's edit doesn't change that. Images and videos carry Google's SynthID watermark and need Google Ads' AI label, which shows on the ad in India, the EU and New York ([Google Ads AI transparency](https://support.google.com/My-Ad-Center-Help/answer/17196133)). Videos also set YouTube's `status.containsSyntheticMedia` when uploaded ([videos.insert](https://developers.google.com/youtube/v3/docs/videos/insert)). Text with election or political words is flagged as blocked, because election ads need advertiser verification and a synthetic content disclosure Kairo doesn't support ([Google's election ads policy](https://support.google.com/adspolicy/answer/15142358)). The rules are in `DisclosureRules`, versioned as `disclosure/2026-10`, and each asset view and the `IAssets` contract carry the result for Brand Guardian and Launch.
- **Failures.** A failed agent run is retried twice, after 30 seconds and 2 minutes. After that the room shows "needs attention" and a marketer can try again.

## Run everything locally

From the repository root:

```bash
dotnet run --project core/src/Kairo.AppHost
```

This starts SQL Server, Redis, the Migrator, the API, the worker, the agent service, the web app and a gateway. It also prints a link to the Aspire dashboard, where you can see each one's health, logs and traces. The Migrator brings the database up to date before the API and worker start.

Open Kairo at **http://localhost:8000**.
- The gateway sends `/api/*` to the core and everything else to the web app, so one session cookie works for both.
- The cookie is Secure. Chromium-based browsers accept Secure cookies on `http://localhost`.

The first run takes several minutes. It fetches the Aspire CLI bundle, installs the web and Python dependencies, and pulls the SQL Server and Redis images. If Docker isn't running, the API and worker stay in "Waiting" in the dashboard.

## Launch approval contract

Each gate stores an immutable approval snapshot in the `approvals` schema. Brief approval binds the brief and proof-plan versions, asset approval binds the current visible asset versions and Guardian decisions, and launch approval binds the complete launch plan. Sending work back appends invalidations. Old rooms without approval evidence must repeat the relevant gates before launching.

After asset approval, a marketer reads `GET /api/brands/{brandId}/campaigns/{campaignId}/launch-plan` and saves settings with `PUT` to the same route. Send the current campaign `version` and `settings`: `channels` contains a `campaignType`, total `budget`, `biddingStrategy`, and an optional positive `target` for each approved channel. Supported strategies are `maximize_clicks`, `target_cpa` and `target_roas`; Shopping excludes target CPA. Target CPA uses the account currency; target ROAS is a ratio (2 means 200%). Allocations must equal the brief budget. `salesCountry` is required for Shopping. Optional `audienceIds` are Google Ads user-list IDs, and `geoTargetIds` maps proof-plan region IDs to Google Ads geographic IDs. Unresolved regions block approval.

The response contains the complete plan, its hash, approval blockers, and each channel's derived average daily budget in micros. Daily figures are equivalents derived from the inclusive campaign dates. The worker uses fixed campaign total budgets (`CUSTOM` period) to enforce each approved channel allocation. The existing `approval-requests` and `approve` endpoints handle gate 3. Settings cannot change during a pending approval or after launch approval. The launch worker must verify the current plan against the approval evidence before executing it.

## Campaign build

Approving gate 3 queues a durable launch in the same transaction as the approval. The core worker checks the account currency and time zone, resolved locations, languages, stock, conversion tracking when required, audience lists and Merchant Center linkage. Demand Gen also needs a retained approved logo; its file digest and Brand Kit version are part of the launch snapshot. Shopping uses the selected sales country as its Merchant Center feed label and includes only the approved offer IDs. Generated Shopping titles remain proposals for feed export; launch does not change Merchant Center titles.

The worker uploads approved videos as unlisted to the approved YouTube channel using saved resumable sessions, retains Google resource IDs and product-to-ad mappings, and creates every campaign paused. It enables the campaigns in one atomic Google Ads request only after the whole build succeeds. Google Ads media assets carry synthetic-content attestations, and uploaded videos carry YouTube's synthetic-media disclosure. Only the Launch module sends these write requests, through a dedicated HTTP client with automatic retries disabled. A lost response retains the attempted step for reconciliation instead of issuing another create request.

A worker loop processes launches promptly, and `POST /jobs/launches/run` provides a durable scheduler fallback every minute. Set `Kairo:Launch:Loop=off` for manual local job control. Progress uses `launch.changed` and `campaign.changed` events; launch completion and failures also create notifications and audit entries. No live-account verification or deployment has been performed for this feature.

## Launch retries and next-day health

`GET /api/brands/{brandId}/campaigns/{campaignId}/launch` returns launch progress, attempt history, external resource and video IDs, a plain-language cause and suggested fix, retry availability, and health-check results. Upload session URLs and OAuth credentials stay private. Marketers can submit `POST` to the same path followed by `/retry`, with the current launch `version`. Concurrent or stale retries cannot enqueue a second execution. Every retry rechecks the exact approved snapshot.

Known transient failures receive at most three automatic retries, after 30 seconds, two minutes and ten minutes. Permission, policy, configuration and approval failures wait for correction. Saved write intents survive process restarts and lease expiry. Uncertain media, campaign-build and activation results are reconciled through Google before further writes; missing or ambiguous results require attention. Resumable YouTube uploads recover the completed video ID or continue after the confirmed byte offset. No create request is repeated merely because its response was lost.

Approvers can send a confirmed failed launch back through the existing send-back endpoint for fresh approval. Previously built campaigns must still be paused or removed. The previous execution, its approved snapshot and resource IDs remain in history, and a new approval queues a separate execution. Uncertain launches must be reconciled first.

Activation durably schedules a health check for 24 hours later. The worker checks the approved account's ad disapprovals and campaign delivery, publishes actionable `health.problem` notifications, and deduplicates them transactionally. Read failures use bounded retries. `POST /jobs/launches/health` and a fifteen-minute scheduler fallback process due checks. Proof-plan amendments retain their history and do not replace the launch snapshot or trigger Google changes. Development-account validation, real-account activation and launch-time performance verification remain pending; this implementation has only been verified locally with simulated Google APIs.

## Run the tests

From the repository root:

| Part | Commands |
| --- | --- |
| Core | `dotnet test --solution core/Kairo.slnx` |
| Agent service | `uv run --directory agents ruff check`, `uv run --directory agents ruff format --check`, `uv run --directory agents pytest` |
| Web app | `npm --prefix web run lint`, `npm --prefix web test`, `npm --prefix web run build` |
| Deployment scripts | `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"` |

The core tests need Docker running, because `Kairo.IntegrationTests` starts SQL Server in a container. To run one test class, use `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`.

The deployment script tests need Pester 5.9.1: `Install-Module Pester -RequiredVersion 5.9.1 -Scope CurrentUser -SkipPublisherCheck`.

GitHub Actions runs all of these on every push and pull request to `main` and `develop`.

## Change the database

Each module that stores data owns a schema named after it in snake case, and has its own EF Core migrations.

1. Restore the tools once: `dotnet tool restore`.
2. Change the module's DbContext, then add a migration and tidy it:

   ```bash
   dotnet ef migrations add <Name> --project core/src/Modules/Kairo.Modules.<Module> --startup-project core/src/Kairo.Migrator --context <Module>DbContext --output-dir Data/Migrations
   bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.<Module>/Data/Migrations/*.cs
   ```

   The tidy script strips the comments EF Core writes, and makes the migration classes `internal`.
3. If the migration adds a table that belongs to a workspace, add it to the schema's security policy:
   - At the end of `Up`, call `migrationBuilder.AddBrandSeparation("<schema>", "<Table>")`.
   - At the start of `Down`, call `migrationBuilder.RemoveBrandSeparation("<schema>", "<Table>")`.
   - For the schema's first such table, call `migrationBuilder.CreateBrandAccessPredicate("<schema>")` first in `Up`, and `migrationBuilder.DropBrandSeparation("<schema>")` in `Down` instead.

   A test fails if any table with a `BrandId` column is left out.

## Build the container images

Each deployable has a Dockerfile. Every image listens on port 8080, or on `$PORT` for the agent service and web app.

```bash
docker build -f core/src/Kairo.Api/Dockerfile -t kairo-api core
docker build -f core/src/Kairo.Worker/Dockerfile -t kairo-worker core
docker build -f core/src/Kairo.Migrator/Dockerfile -t kairo-migrator core
docker build -t kairo-agents agents
docker build -t kairo-web web
```

## Deploy

Kairo deploys to Google Cloud with `deploy.bat`. Each environment (development, staging, production) maps to a Google Cloud project in `deploy/environments.psd1`. Only development, the `kairo2` project, is set up.

You need the gcloud CLI, signed in with an account that owns the project.

1. **Set up the project, once.** `powershell -ExecutionPolicy Bypass -File deploy/setup.ps1 development` enables the APIs and creates the network, Cloud SQL, secrets and service accounts. It takes about fifteen minutes the first time and is safe to run again.
2. **Deploy.** Commit your changes, then run `deploy.bat development`. It runs the tests, builds every image with Cloud Build, applies database migrations as a Cloud Run job, deploys the four services and prints the web app's address. Add `-SkipTests` to skip the tests.
3. **Turn on Google sign-in, once per project.** In the Cloud Console, add the redirect URI that `deploy.bat` prints to the project's web OAuth client. Then run `powershell -ExecutionPolicy Bypass -File deploy/set-google-sign-in.ps1 development`, paste the client ID and secret when it asks, and deploy again. The script stores them in Secret Manager exactly as typed; piping a value into `gcloud` from PowerShell would add a line break and break sign-in.

In development there is no load balancer: the web app forwards `/api/*` to the core itself, sending a Google identity token. Only the web app is public; the API, worker and agent service accept calls only from Kairo's own service accounts.

## Conventions

**Core modules.**
- Each module in `core/src/Modules/` is its own project.
- A module exposes exactly one public class, its `IModule` implementation:
  - `AddServices` registers services.
  - `MapApi` adds API endpoints.
  - `MapJobs` adds job endpoints.
  - `MigrateAsync` applies the module's migrations.

  Everything else in the module is `internal`, including its migrations.
- Modules reference only `Kairo.SharedKernel` and `Kairo.Contracts`, never each other. They talk through the interfaces and events in `Kairo.Contracts`.
- A module that stores data has a `ModuleDbContext` and a design-time factory, and maps tables only into its own schema.
- Architecture tests enforce these rules.

To add a module:
1. Copy an existing module folder to `core/src/Modules/Kairo.Modules.<Name>/`.
2. Rename the project file and class to match, so you have `Kairo.Modules.<Name>.csproj` and `<Name>Module`.
3. Add the project to `core/Kairo.slnx`.
4. Register the module in `core/src/Kairo.Api/Program.cs`, `core/src/Kairo.Worker/Program.cs` and `core/src/Kairo.Migrator/MigratorHost.cs`. The tests fail until all three register it.

**Workspace data.**
- Every table that belongs to a workspace has a `BrandId`, its entity implements `IBrandOwned`, and its schema's security policy covers it.
- EF Core filters every query by the request's workspace, and SQL Server's row-level security backs that up.
- Code that has to read across workspaces opens `KairoDatabase.EnterSystemScopeAsync`.
- All modules share one database connection per request, so never run database calls in parallel inside a request.

**API.**
- **Writes.** Endpoints live under `/api`. Every write must send the header `X-Kairo-Request: 1`. It runs in one transaction shared by every module, which commits on a success status and rolls back on an error.
- **Workspace endpoints** sit under `/api/brands/{brandId}`, through `MapBrandApi`. Non-members get 404. Members who lack the role set by `RequireBrandRole` get 403.
- **Audit log.** Changes that matter to an admin or approver are recorded with `IAuditLog`.
- **Contract.** After changing endpoints, update the committed OpenAPI document and the web types:

  ```bash
  KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
  npm --prefix web run generate:api
  ```

**Health endpoints.**
- The API and worker answer `/alive` (the process is up) and `/health` (which also checks SQL Server, and Redis for the API).
- The agent service and web app answer `/health`.

**Agents.** Each agent has its own package in `agents/src/kairo_agents/agents/`. See the [agent service README](agents/README.md).

**Web screens.** Each screen from PRD section 4 is a route under `web/src/app/[workspace]/`. See the [web app README](web/README.md).

**Commits.** Commit messages never carry AI attribution.
