# Kairo project structure — design

*12 October 2026 · Approved in conversation; this is the written record*

## Goal

Turn the empty repository into a runnable skeleton of the three parts described in the Kairo SAD: the Next.js web app, the ASP.NET Core core (API and worker), and the Python agent service. Each part builds, runs locally, answers a health check and has a passing test. Folders exist for every SAD module, agent and PRD screen, so each later feature has an obvious home. No business logic, database tables or authentication are added.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Scope | Runnable skeleton | Proves the toolchains and wiring work without committing to feature design |
| Repository | One repository, one top-level folder per deployable part | The parts change together and share docs |
| Core layout | One project per module, plus `Kairo.SharedKernel` and `Kairo.Contracts` | The compiler enforces module boundaries, and spend safety (only Launch writes to Google Ads, SAD AD-4) can later be enforced at compile time |
| Local development | Aspire AppHost, for local development only | One command starts and wires every part, with one dashboard for logs and traces |
| Deployment script | `deploy.bat` deferred until a Google Cloud project exists | An untested deploy script gives false confidence |
| CI | GitHub Actions running the three test suites | Not in the docs; catches boundary violations when local tests are skipped |
| Comments | None in code: no comments, XML doc comments or docstrings | `CLAUDE.md` §3; explanations live in the README, this spec and the SAD |

## Notes on the documentation

1. **Operational database.** BRD §8 lists Firestore for Brand Kits, campaign rooms and trigger rules, and lists Cloud Run as hosting only the web app and agent service. SAD AD-5 chooses Cloud SQL for SQL Server and rejects Firestore, and SAD §9 also runs the core on Cloud Run. This design follows the SAD; BRD §8 should be updated to match.
2. **Lift measurement library.** The SAD names `tfp-causalimpact`, whose last release was in May 2023; its base library `tensorflow-probability` was last released in November 2024 and supports Python up to 3.12. Maintained alternatives include `tfcausalimpact` (September 2026, Python below 3.14), CausalPy (July 2026) and statsmodels. The skeleton installs no measurement library; the choice is made when Prove and Impact Lab are built. Python 3.13 keeps every option open.
3. **Database migrations in `deploy.bat`.** The SAD has `deploy.bat` apply EF Core migrations, but Cloud SQL is reached only through a private IP, which a developer's machine cannot reach. When `deploy.bat` is written, migrations should ship as an EF Core migration bundle that runs as a Cloud Run job with Direct VPC egress; `deploy.bat` starts the job and waits for it before updating the services.

## Versions

Latest stable releases, checked on 12 October 2026. The build uses the latest stable release available when it runs.

| Part | Toolchain and main packages |
| --- | --- |
| Core | .NET SDK 10.0.400, ASP.NET Core 10, Aspire 13.6.1, xUnit v3 |
| Agent service | Python 3.13 through uv 0.12, FastAPI 0.143, Uvicorn 0.54, ADK (`google-adk`) 2.11, Gen AI SDK (`google-genai`) 2.29, pytest 9.1, Ruff 0.17 |
| Web app | Node 24 LTS with npm, Next.js 16.4, React 19.3, Tailwind CSS 4.3, Vitest 5 |

## Repository layout

```
Kairo/
├─ core/            .NET solution: API, worker, 14 modules, Aspire AppHost
├─ agents/          Python agent service
├─ web/             Next.js web app
├─ docs/            BRD, PRD, SAD, and specs and plans under docs/superpowers/
├─ .github/workflows/ci.yml   build and test on every push and pull request
├─ global.json      pins the .NET 10 SDK and makes dotnet test use Microsoft Testing Platform
├─ .editorconfig    indentation and charset rules for C#, TypeScript, Python and Markdown
├─ .gitattributes   text=auto with LF endings; CRLF for .bat and .cmd
├─ .gitignore       .NET, Node, Python, editor and OS artefacts
└─ README.md        what Kairo is, prerequisites, how to run and test
```

`CLAUDE.md` and `.claude/` stay untracked, as they are today.

`global.json` sits at the root rather than in `core/`. The .NET SDK looks for it starting from the current directory, and the README runs every command from the repository root.

## Core

### Solution layout

```
core/
├─ Kairo.slnx
├─ Directory.Build.props       net10.0, nullable, implicit usings, warnings as errors
├─ Directory.Packages.props    central package versions
├─ .dockerignore
├─ src/
│  ├─ Kairo.AppHost/           Aspire, local development only
│  ├─ Kairo.ServiceDefaults/   OpenTelemetry, health checks, service discovery, resilience
│  ├─ Kairo.Api/               host for the web app and the agent service
│  ├─ Kairo.Worker/            host for Cloud Scheduler, Cloud Tasks and Pub/Sub jobs
│  ├─ Kairo.SharedKernel/      IModule
│  ├─ Kairo.Contracts/         cross-module interfaces and events (empty for now)
│  └─ Modules/
│     ├─ Directory.Build.props gives every module the same framework and project references
│     └─ Kairo.Modules.<Name>/ one project per module (14)
└─ tests/
   ├─ Directory.Build.props    links the shared test helper into every test project
   ├─ Shared/                  helper that lists the module folders under src/Modules
   ├─ Kairo.Api.Tests/
   ├─ Kairo.Worker.Tests/
   └─ Kairo.ArchitectureTests/
```

NuGet audit warnings stay warnings rather than errors, so a newly published advisory does not break unrelated builds.

### Modules

Each module project holds one public class implementing `IModule`; every other type in the module is `internal`. Following `CLAUDE.md` §3, the code has no comments, including XML doc comments. The table below and SAD §3 record what each module owns.

```csharp
namespace Kairo.SharedKernel;

public interface IModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);
    void MapApi(IEndpointRouteBuilder api);
    void MapJobs(IEndpointRouteBuilder jobs);
}
```

- `AddServices` registers the module's services.
- `MapApi` adds the module's endpoints to the API host, under the `/api` route group.
- `MapJobs` adds the module's job endpoints to the worker host, under the `/jobs` route group.

In the skeleton all three method bodies are empty. `Kairo.SharedKernel` and the modules use the ASP.NET Core shared framework, because modules map their own endpoints.

Modules reference `Kairo.SharedKernel` and `Kairo.Contracts`, never each other. They communicate through interfaces and events defined in `Kairo.Contracts`.

| SAD module | Project | Class | Owns |
| --- | --- | --- | --- |
| Identity and access | `Kairo.Modules.Identity` | `IdentityModule` | Users, roles, approver assignments, budget limits |
| Brands and connections | `Kairo.Modules.Brands` | `BrandsModule` | Brand workspace, connected accounts, encrypted OAuth tokens |
| Brand Kit | `Kairo.Modules.BrandKit` | `BrandKitModule` | Brand Kit versions, rules, guideline files, best ads |
| Catalogue | `Kairo.Modules.Catalogue` | `CatalogueModule` | Products, stock levels, feed imports |
| Opportunities | `Kairo.Modules.Opportunities` | `OpportunitiesModule` | Opportunity cards, scores, evidence, watched and ignored topics |
| Triggers | `Kairo.Modules.Triggers` | `TriggersModule` | Trigger rules, firings, cooldowns |
| Campaign rooms | `Kairo.Modules.CampaignRooms` | `CampaignRoomsModule` | Rooms, states, stage runs, briefs, proof plans, assets, checks |
| Approvals | `Kairo.Modules.Approvals` | `ApprovalsModule` | Gate requests, decisions, approval tokens |
| Launch | `Kairo.Modules.Launch` | `LaunchModule` | Launch records, Google Ads and YouTube IDs, product-to-ad mapping |
| Care | `Kairo.Modules.Care` | `CareModule` | Pacing checks, health checks, the stock pause log, recommendations |
| Measurement | `Kairo.Modules.Measurement` | `MeasurementModule` | Test windows, impact reports, Impact Lab analyses |
| Brand Memory | `Kairo.Modules.BrandMemory` | `BrandMemoryModule` | Memory entries and where each was used |
| Notifications | `Kairo.Modules.Notifications` | `NotificationsModule` | In-app notifications, email digests, preferences |
| Audit | `Kairo.Modules.Audit` | `AuditModule` | Append-only audit log |

### Hosts

`Kairo.Api` and `Kairo.Worker` are ASP.NET Core apps. Each one:

1. Calls `AddServiceDefaults()` from `Kairo.ServiceDefaults`.
2. Lists all 14 modules explicitly, calls `AddServices` on each and registers each instance in dependency injection as `IModule`.
3. After building the app, calls `MapApi` (API) or `MapJobs` (worker) on each module.
4. Maps the health endpoints.

The API registers the SQL Server database `kairo` and Redis `redis` through Aspire's client integrations, which add their health checks. The worker registers only SQL Server. Nothing else uses these connections yet.

### Health endpoints

Both hosts map two endpoints in every environment, so Cloud Run probes can use them:

- `/alive`: liveness checks only (the process is running).
- `/health`: every check, including SQL Server, and Redis for the API.

Responses contain only the status word (`Healthy`, `Degraded` or `Unhealthy`), so they reveal nothing about the system. This changes Aspire's template, which maps these endpoints in development only.

### Tests

All test projects use xUnit v3. The expected set of modules is the list of project folders under `core/src/Modules`, read by a shared helper, so adding a module folder without wiring it up fails the tests.

- **`Kairo.Api.Tests`** and **`Kairo.Worker.Tests`** start the host with `WebApplicationFactory`. The connection strings point at a closed local port, so SQL Server and Redis are unreachable. The tests check that:
  - `/alive` returns 200 `Healthy` even though the dependencies are down, so a database outage never makes Cloud Run restart healthy instances
  - `/health` returns 503 with only `Unhealthy` in the body, with no exception text or connection details
  - the `IModule` instances registered in dependency injection match the module folders exactly
- **`Kairo.ArchitectureTests`** reference SharedKernel, Contracts and every module, and use plain reflection to check that:
  - each module assembly has exactly one public type, `Kairo.Modules.<Name>.<Name>Module`, which implements `IModule`
  - module assemblies reference no Kairo assembly except `Kairo.SharedKernel` and `Kairo.Contracts`
  - `Kairo.Contracts` references no Kairo assembly except `Kairo.SharedKernel`, and `Kairo.SharedKernel` references no Kairo assembly

## Agent service

```
agents/
├─ pyproject.toml           project kairo-agents; Python >=3.13,<3.14
├─ uv.lock
├─ .python-version          3.13
├─ .dockerignore
├─ Dockerfile
├─ src/kairo_agents/
│  ├─ __init__.py
│  ├─ main.py               FastAPI app with GET /health
│  ├─ agents/
│  │  ├─ sense/  plan/  create/  check/  prove/  learn/  ask/
│  ├─ tools/                typed clients for the core's internal API
│  ├─ measurement/          holdout matching, smallest detectable lift, lift estimates
│  └─ media/                FFmpeg helpers for voiceover, captions and logo
└─ tests/
   ├─ test_health.py
   └─ test_layout.py
```

- `GET /health` returns `{"status": "ok"}`.
- Every package contains only an empty `__init__.py`. Following `CLAUDE.md` §3, there are no comments or docstrings. The table below and SAD §4 record each agent's role, ADK pattern, models and tools.

  | Package | SAD agent | ADK pattern |
  | --- | --- | --- |
  | `sense` | Sense | Sequential: score, match to the catalogue, explain |
  | `plan` | Plan | Sequential: draft the brief, then the proof plan |
  | `create` | Create | Parallel: copy, images, video, shopping titles and translations |
  | `check` | Check (Brand Guardian) | Loop: check, fix and re-check, up to 3 times |
  | `prove` | Prove | Sequential: early readings, lift, then recommendations |
  | `learn` | Learn | Single agent |
  | `ask` | Ask Kairo | Conversational agent with tools |

- When an agent is built, its package gains an `agent.py` exposing `root_agent`, following ADK's convention, so `adk web src/kairo_agents/agents` runs it in ADK's development UI.
- Runtime dependencies are `fastapi`, `uvicorn[standard]`, `google-adk` and `google-genai`. ADK caps the FastAPI version, so uv resolves FastAPI 0.141 rather than 0.143.
- Development dependencies are `pytest`, `httpx2` and `ruff`. Starlette 1.7's test client deprecates `httpx` in favour of `httpx2`.
- Ruff handles both linting and formatting.
- `test_health.py` checks the health endpoint. `test_layout.py` checks that the seven agent packages import.
- The Dockerfile uses `python:3.13-slim`, installs FFmpeg, installs dependencies with uv from the lock file, runs as a non-root user, and starts Uvicorn on `$PORT` (default 8080).

## Web app

```
web/
├─ package.json             scripts: dev, build, start, lint, test
├─ next.config.ts           output: "standalone", plus create-next-app's defaults
├─ eslint.config.mjs
├─ vitest.config.mts
├─ vitest.setup.ts          cleans up rendered components after each test
├─ .dockerignore
├─ Dockerfile
├─ public/robots.txt        keeps the private app out of search engines
└─ src/
   ├─ app/                  one route per PRD §4 screen (table below)
   └─ components/           app shell, navigation menu, page placeholder
```

The app shell in `app/layout.tsx` shows a left-hand menu of the seven areas: Today, Opportunities, Campaigns, Triggers, Impact Lab, Brand Kit and Settings.

Each screen is a placeholder page showing its title and the "What it shows" text from PRD §4. The Evidence tab and the room overview have no PRD row of their own, so their text comes from the Campaign room row and US-4.1.

| Route | Screen |
| --- | --- |
| `/` | Redirects to `/today` |
| `/today` | Today |
| `/opportunities` | Opportunities |
| `/opportunities/[id]` | Opportunity detail |
| `/campaigns` | Campaigns |
| `/campaigns/[id]` | Campaign room overview: stage tracker and activity log |
| `/campaigns/[id]/evidence` | Evidence tab |
| `/campaigns/[id]/brief` | Brief tab |
| `/campaigns/[id]/assets` | Assets tab |
| `/campaigns/[id]/launch` | Launch tab |
| `/campaigns/[id]/performance` | Performance tab |
| `/campaigns/[id]/results` | Results tab |
| `/triggers` | Triggers |
| `/impact-lab` | Impact Lab |
| `/brand-kit` | Brand Kit |
| `/settings` | Settings |
| `/health` | Route handler returning `{"status": "ok"}` |

- `campaigns/[id]/layout.tsx` shows links to the six tabs above the tab content, so notifications can deep-link to a tab.
  - The links come from `campaigns/[id]/_components/campaign-room-tabs.tsx`, which takes the room ID as a prop and URL-encodes it.
  - Next.js 16.4 passes the `id` route parameter still percent-encoded. `_components/room-tabs.tsx` therefore decodes it before handing it over, falling back to the raw value if it isn't valid percent-encoding. Without this, an ID such as `a%2Fb` would be encoded twice.
  - Under Cache Components the room ID is request-time data, so the layout reads it inside `<Suspense>`.
- create-next-app 16.4 has some new defaults, which the app keeps:
  - `cacheComponents` and `partialPrefetching` are on.
  - Tailwind runs through Turbopack's `@tailwindcss/turbopack` loader, so there is no PostCSS config.
- `agentRules: false` stops `next dev` from writing an `AGENTS.md` whenever it detects an AI coding agent. This matches scaffolding with `--no-agents-md` and keeps agent instruction files out of the repository.
- Two dev-tool versions differ from the template:
  - `@types/node` moves to `^24`, which Vitest 5 requires.
  - ESLint stays on 9, because `eslint-config-next`'s plugins don't yet support ESLint 10.
- Components used by a single area will sit next to its routes in private `_components/` folders.
- Shared clients (core API, Google sign-in, SignalR) will go in `src/lib/`.
- Both kinds of folder are created when the first feature needs them.
- Tests use Vitest with Testing Library. They check that:
  - the menu lists the seven areas with the right links
  - the campaign room links to its six tabs and encodes unusual room IDs
  - `/health` returns 200
- ESLint is configured as `create-next-app` sets it up.
- The Dockerfile is a multi-stage build on Node 24 that packages the standalone output and serves it on `$PORT`.

## Local development

`dotnet run --project core/src/Kairo.AppHost` starts everything. Docker must be running.

| Resource | Definition | Connected to |
| --- | --- | --- |
| `sql` | SQL Server container with a data volume; database `kairo` | |
| `redis` | Redis container | |
| `agents` | `AddUvicornApp` on `agents/`, `kairo_agents.main:app`, using uv, health check on `/health` | |
| `api` | `Kairo.Api` | `kairo` and `redis` (waits for both), `agents` |
| `worker` | `Kairo.Worker` | `kairo` (waits for it), `agents` |
| `web` | `AddNextJsApp` on `web/`, running `next dev` | `api` |

`AddNextJsApp` is marked experimental (diagnostic `ASPIREJAVASCRIPT001`, suppressed in the AppHost). If it misbehaves, the fallback is `AddJavaScriptApp` running the `dev` script.

The Aspire dashboard shows logs and resource health for all six resources. It shows traces and metrics for the .NET hosts. The agent service and web app send OpenTelemetry later, with the foundations work.

## Delivery

- **Images.** There are four Dockerfiles:
  - `web/Dockerfile`
  - `agents/Dockerfile`
  - `core/src/Kairo.Api/Dockerfile`
  - `core/src/Kairo.Worker/Dockerfile`

  The two core Dockerfiles use `core/` as their build context. Every image serves HTTP on port 8080, Cloud Run's default:
  - the .NET images through `ASPNETCORE_HTTP_PORTS`
  - the Python and Node images through `$PORT`, which defaults to 8080
- **CI.** `.github/workflows/ci.yml` runs on pushes and pull requests to `main` and `develop`, with three parallel jobs:
  - core: `dotnet build` and `dotnet test`
  - agents: `uv sync`, `ruff check`, `ruff format --check` and `pytest`
  - web: `npm ci`, `lint`, `test` and `build`

  The workflow is first verified when it is pushed to GitHub.
- **`deploy.bat`.** Deferred. See note 3 under "Notes on the documentation".

## Out of scope

- Business logic, EF Core DbContexts, entities, migrations and SQL Server row-level security.
- Google sign-in, token validation and authorization policies.
- The SignalR hub and Redis backplane. Redis is only health-checked.
- Calls between the core and the agent service.
- Cloud Tasks, Cloud Scheduler and Pub/Sub integration.
- OpenTelemetry in the agent service and web app.
- All Google API integrations: Google Ads, YouTube, Merchant, GA4, BigQuery, Weather, Gmail and Model Armor.
- The choice of measurement library.
- `deploy.bat`, Cloud Build configuration and Google Cloud resources.
- The Ask Kairo panel, sign-in screens and the web app's API client.

## Done criteria

1. `dotnet build core/Kairo.slnx` and `dotnet test --solution core/Kairo.slnx` pass when run from the repository root.
2. `uv run ruff check`, `uv run ruff format --check` and `uv run pytest` pass in `agents/`.
3. `npm run lint`, `npm test` and `npm run build` pass in `web/`.
4. `dotnet run --project core/src/Kairo.AppHost` starts all six resources, and each reports healthy.
5. `docker build` succeeds for all four images.
6. The README explains prerequisites, how to run the stack and how to run the tests.
