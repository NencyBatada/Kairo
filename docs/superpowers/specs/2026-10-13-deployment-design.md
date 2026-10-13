# Kairo Deployment — design

*13 October 2026 · Approved in conversation; this is the written record*

## Goal

One command, `deploy.bat development`, tests Kairo, builds every service and deploys it to Google Cloud, applying database migrations first. After it finishes, the web app answers on its Cloud Run address and Google sign-in works there.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Projects | `deploy.bat` takes `development`, `staging` or `production` and maps each to a project in `deploy/environments.psd1`. Only development (`kairo2`) is filled in. | Only `kairo2` exists. Staging and production are added by filling in their entries and running setup. |
| Footprint in development | Lean: Cloud SQL for SQL Server Express on the smallest tier, every Cloud Run service scales to zero, no Memorystore, no load balancer, no Cloud Armor | About $55–60 a month, almost all Cloud SQL. Chosen by the product owner. |
| One address for web and core | The web app forwards `/api/*` to the core through a route handler | No load balancer in development, chosen by the product owner. The session cookie stays first-party. |
| Script language | `deploy.bat` is a thin wrapper around `deploy/deploy.ps1` | The SAD names `deploy.bat`. Batch is hard to read and can't be unit-tested; PowerShell runs on every Windows machine and in CI. |
| Image builds | One Cloud Build run builds all five images in parallel, tagged with the git commit | One upload of the source, and builds don't depend on the developer's machine |
| Migrations | The Migrator runs as a Cloud Run job with Direct VPC egress; the script waits for it before updating the services | Cloud SQL has only a private IP, which a developer's machine can't reach |
| Service access | Every service except the web app requires a Google-signed identity token. The web app sends one in `X-Serverless-Authorization` when it calls the core. | SAD §8. The header leaves `Authorization` free and keeps the session cookie as the user's credential. |
| Redis | The API registers Redis only when a `redis` connection string is set | Nothing uses Redis yet. Memorystore arrives with the Live updates task, when SignalR needs a backplane. |
| Tests for the scripts | Pester 5, with `gcloud` mocked | Checks the commands each step builds without touching Google Cloud |

## Notes on the documentation

1. **Agent service and worker access.** SAD §9 gives the agent service "internal access only". Internal ingress would need every caller to send all traffic through the VPC, plus Cloud NAT for their internet access. This design keeps public ingress but requires a Google-signed identity token from an allowed service account, which gives the same protection for less cost. Revisit when production is set up.
2. **Data Protection keys.** The foundations spec put Cloud KMS encryption of the Data Protection keys in this sub-project. It moves to the Account connections task, which needs Cloud KMS anyway for brand OAuth tokens, so KMS is set up once.
3. **Database login.** In development every service connects as the `sqlserver` login. Production should give the API and worker a login without schema rights, leaving those to the Migrator.
4. **Load balancer.** SAD §8 and §9 put an HTTPS load balancer with Cloud Armor in front of Kairo. Development leaves it out. When production needs it, the route handler stays harmless: the load balancer sends `/api/*` to the core before the web app sees it, as the Aspire gateway does locally.

## Files

| Path | Purpose |
| --- | --- |
| `deploy.bat` | Runs `deploy/deploy.ps1` with the same arguments |
| `deploy/environments.psd1` | Per-environment project, region and sizing |
| `deploy/Kairo.Deploy.psm1` | Shared functions: load an environment, derive service URLs, build `gcloud` argument lists, run `gcloud` and stop on failure |
| `deploy/setup.ps1` | Creates the shared resources in a project, once. Safe to re-run. |
| `deploy/deploy.ps1` | Tests, builds and deploys |
| `deploy/cloudbuild.yaml` | Builds and pushes the five images |
| `deploy/tests/*.Tests.ps1` | Pester tests |
| `.gcloudignore` | Keeps `node_modules`, `bin`, `obj`, `.venv` and test results out of the Cloud Build upload |
| `core/src/Kairo.Migrator/Dockerfile` | The Migrator image |
| `web/src/app/api/[...path]/route.ts` | Forwards `/api/*` to the core |
| `web/src/lib/core-api/identity-token.ts` | Fetches and caches the identity token from the metadata server |

## Environment file

```powershell
@{
    development = @{
        Project       = 'kairo2'
        Region        = 'asia-south1'
        SqlTier       = 'db-custom-1-3840'
        SqlEdition    = 'SQLSERVER_2022_EXPRESS'
        SqlHighAvailability = $false
        MinInstances  = @{ web = 0; api = 0 }
    }
    staging    = @{ Project = '' }
    production = @{ Project = '' }
}
```

An environment with an empty `Project` stops the scripts with a message saying to fill it in.

## Shared resources (`setup.ps1 <environment>`)

Each step checks whether its resource exists and skips it if so.

1. Enable the APIs: Cloud Run, Cloud SQL Admin, Compute Engine, Service Networking, Artifact Registry, Cloud Build, Secret Manager, IAM.
2. Artifact Registry Docker repository `kairo` in the region.
3. VPC `kairo` with subnet `kairo-run` (`10.10.0.0/24`) in the region, for Direct VPC egress.
4. Private services access: an allocated range `kairo-google-services` (`/20`) peered with `servicenetworking.googleapis.com`.
5. Cloud SQL instance `kairo-sql`: SQL Server Express, private IP only on the `kairo` VPC, the environment's tier, 10 GB SSD with automatic growth, daily backups. Database `kairo`. A random 32-character `sqlserver` password.
6. Secrets: `kairo-sql-connection` holds the connection string with the instance's private IP. `google-client-id` and `google-client-secret` are created without a value.
7. Service accounts, one per service: `kairo-web`, `kairo-api`, `kairo-worker`, `kairo-agents`, `kairo-migrator` and `kairo-build`.
   - `kairo-api`, `kairo-worker` and `kairo-migrator` can read `kairo-sql-connection`.
   - `kairo-api` can read the two Google secrets.
   - `kairo-build` can write to the Artifact Registry repository and write logs.

Cloud Run invoker grants are made by `deploy.ps1`, because a service must exist before it can be granted.

## Deploying (`deploy.ps1 <environment> [-SkipTests]`)

1. **Check.** The environment exists and has a project; `gcloud` and `git` are on the path; the working tree is clean, so the image tag names exactly what is deployed.
2. **Test.** Unless `-SkipTests` is given, run the README's suites except `npm run build`, which Cloud Build runs anyway. Any failure stops the deploy.
3. **Build.** `gcloud builds submit --config deploy/cloudbuild.yaml` as `kairo-build`, with the region, project and commit as substitutions. It builds `api`, `worker`, `migrator`, `agents` and `web` in parallel into `<region>-docker.pkg.dev/<project>/kairo/<name>:<commit>`.
4. **Migrate.** Deploy the Cloud Run job `kairo-migrator` with the new image, Direct VPC egress on `kairo-run` and the connection string secret. Then run it with `--wait`. A failed run stops the deploy before any service changes.
5. **Deploy services**, each with its own service account:

   | Service | Settings |
   | --- | --- |
   | `kairo-agents` | Requires authentication. Request timeout 3600 s. |
   | `kairo-api` | Requires authentication. Direct VPC egress (private ranges). Session affinity. `ConnectionStrings__kairo`, `Authentication__Google__ClientId` and `Authentication__Google__ClientSecret` from secrets. `Kairo__PublicOrigin` is the web app's URL. |
   | `kairo-worker` | Requires authentication. Direct VPC egress (private ranges). Connection string from its secret. |
   | `kairo-web` | Public. `KAIRO_API_URL` is the API's URL. |

   Minimum instances come from the environment file. Every service scales to zero in development.

   The Google secrets are passed to the API only when they have a value. Until then the API runs without Google sign-in, as it does locally without a client.
6. **Grant access.** `kairo-web` may invoke `kairo-api`. `kairo-api` and `kairo-worker` may invoke `kairo-agents`.
7. **Report.** Print the web app's URL, and the redirect URI to register on the OAuth client.

Service URLs follow Cloud Run's fixed form, `https://<service>-<project number>.<region>.run.app`, so the API can be given the web app's URL before the web app exists.

## Web app changes

**Route handler.** `web/src/app/api/[...path]/route.ts` handles every HTTP method. It forwards the request to `KAIRO_API_URL` with the same path, query, body and headers, except hop-by-hop headers and `host`. Redirects are passed back, not followed. Every `Set-Cookie` header is returned unchanged. Locally the Aspire gateway sends `/api/*` to the core first, so the handler is never reached.

**Identity token.** `identityToken()` returns `undefined` unless `KAIRO_API_AUDIENCE` is set. When it is set, the function fetches a token for that audience from the metadata server and caches it until five minutes before it expires. Both the route handler and `coreApi()` send it as `X-Serverless-Authorization: Bearer <token>`.

## Core change

`Kairo.Api/Program.cs` calls `AddRedisClient("redis")` only when `ConnectionStrings:redis` is set.

## Signing in on the deployed site

Google lets OAuth clients be created only in the Cloud Console. After the first deploy:

1. The product owner adds `<web URL>/api/auth/callback/google` as an authorized redirect URI on the web client in `kairo2`.
2. They store the client ID and secret:

   ```bash
   printf '%s' '<client id>' | gcloud secrets versions add google-client-id --data-file=- --project kairo2
   printf '%s' '<client secret>' | gcloud secrets versions add google-client-secret --data-file=- --project kairo2
   ```

3. `deploy.bat development` runs again, and passes the secrets to the API.

## Testing

- **Pester.** Loading environments, including the empty-project refusal; service URLs; the `gcloud` arguments for each step; secrets skipped when they have no value; the deploy stopping on a failed test, build or migration. `gcloud` is replaced by a mock that records its calls.
- **Vitest.** The route handler forwards method, path, query, body and headers, drops `host`, returns redirects unfollowed and keeps every `Set-Cookie`. The token helper skips when no audience is set, caches, and refetches near expiry. `coreApi()` sends the token.
- **xUnit.** The API starts and answers `/alive` without a Redis connection string.
- **CI.** A `deploy` job installs Pester 5 and runs the Pester tests under `pwsh`.
- **Real run.** Run `setup.ps1 development` and `deploy.bat development` against `kairo2`. Check that the web URL answers `/health` from the web app and `/api/me` with 401 from the core, and that the API's own `/health` reports healthy when called with an identity token. After the OAuth client step, sign in with Google on the deployed site.

## Out of scope

- Staging and production projects, the load balancer, Cloud Armor, Memorystore and minimum instances in production.
- Cloud KMS, which moves to the Account connections task.
- Cloud Scheduler, Cloud Tasks and Pub/Sub, which arrive with the jobs that need them.
- Sending OpenTelemetry to Cloud Trace, which belongs to the Monitoring and alerts task.
