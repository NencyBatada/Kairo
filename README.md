# Kairo

Kairo takes a marketer from a rising demand signal to a live, on-brand Google Ads campaign in under an hour, then proves the extra sales it caused. The [BRD](docs/Kairo-BRD.md), [PRD](docs/Kairo-PRD.md) and [SAD](docs/Kairo-SAD.md) describe the product and its architecture.

## Repository layout

| Folder | What it holds |
| --- | --- |
| `core/` | The ASP.NET Core API and worker, the 14 modules, and the Aspire AppHost for local development |
| `agents/` | The Python agent service, built with Google's Agent Development Kit and FastAPI |
| `web/` | The Next.js web app |
| `docs/` | Requirements, architecture, and design specs and plans |

## Prerequisites

- .NET SDK 10.0.400 or later
- Node.js 24 with npm
- [uv](https://docs.astral.sh/uv/) 0.12, which installs Python 3.13 for you
- Docker Desktop, running, for the SQL Server and Redis containers
- A trusted HTTPS development certificate: `dotnet dev-certs https --trust`

## Run everything locally

From the repository root:

```bash
dotnet run --project core/src/Kairo.AppHost
```

This starts SQL Server, Redis, the API, the worker, the agent service and the web app, and prints a link to the Aspire dashboard, where you can see each one's health, logs and traces.

The first run takes several minutes. It fetches the Aspire CLI bundle, installs the web and Python dependencies, and pulls the SQL Server and Redis images. If Docker isn't running, the API and worker stay in "Waiting" in the dashboard.

## Run the tests

From the repository root:

| Part | Commands |
| --- | --- |
| Core | `dotnet test --solution core/Kairo.slnx` |
| Agent service | `uv run --directory agents ruff check`, `uv run --directory agents ruff format --check`, `uv run --directory agents pytest` |
| Web app | `npm --prefix web run lint`, `npm --prefix web test`, `npm --prefix web run build` |

GitHub Actions runs all of these on every push and pull request to `main` and `develop`.

## Build the container images

Each deployable has a Dockerfile. Every image listens on port 8080, or on `$PORT` for the agent service and web app.

```bash
docker build -f core/src/Kairo.Api/Dockerfile -t kairo-api core
docker build -f core/src/Kairo.Worker/Dockerfile -t kairo-worker core
docker build -t kairo-agents agents
docker build -t kairo-web web
```

## Conventions

**Core modules.**
- Each module in `core/src/Modules/` is its own project.
- A module exposes exactly one public class, its `IModule` implementation, which registers services (`AddServices`), API endpoints (`MapApi`) and job endpoints (`MapJobs`). Everything else in the module is `internal`.
- Modules reference only `Kairo.SharedKernel` and `Kairo.Contracts`, never each other. They talk through the interfaces and events in `Kairo.Contracts`.
- Architecture tests enforce these rules.

To add a module:
1. Copy an existing module folder to `core/src/Modules/Kairo.Modules.<Name>/`.
2. Rename the project file and class to match, so you have `Kairo.Modules.<Name>.csproj` and `<Name>Module`.
3. Add the project to `core/Kairo.slnx`.
4. Register the module in both `core/src/Kairo.Api/Program.cs` and `core/src/Kairo.Worker/Program.cs`. The tests fail until both hosts register it.

**Health endpoints.**
- The API and worker answer `/alive` (the process is up) and `/health` (also checks SQL Server, and Redis for the API).
- The agent service and web app answer `/health`.

**Agents.** Each agent has its own package in `agents/src/kairo_agents/agents/`. See the [agent service README](agents/README.md).

**Web screens.** Each screen from PRD section 4 is a route under `web/src/app/`. See the [web app README](web/README.md).

**Commits.** Commit messages never carry AI attribution.
