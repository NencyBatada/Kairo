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

1. **APIs.** `deploy/setup.ps1` enables the Google Ads, YouTube Data, Merchant, Analytics Admin, BigQuery and Cloud KMS APIs. Run it once before connecting accounts locally, too.
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
