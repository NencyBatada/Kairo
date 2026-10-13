# Kairo Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `deploy.bat development` tests Kairo, builds all five images with Cloud Build, runs the Migrator as a Cloud Run job, and deploys the web app, API, worker and agent service to Cloud Run in `kairo2`.

**Architecture:** A PowerShell module, `deploy/Kairo.Deploy`, holds every step as a small function that calls `gcloud` through one wrapper, so Pester tests can mock it. `deploy/setup.ps1` creates the shared resources once per project, and `deploy/deploy.ps1` (wrapped by `deploy.bat`) tests, builds, migrates and deploys. The web app gains a `/api/*` route handler that forwards to the core with a Google-signed identity token, because development has no load balancer.

**Tech Stack:** Windows PowerShell 5.1 and PowerShell 7 (both supported), Pester 5.9.1, gcloud CLI, Cloud Build, Cloud Run services and jobs, Cloud SQL for SQL Server, Secret Manager, Next.js 16 route handlers, Vitest 5, ASP.NET Core 10, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-10-13-deployment-design.md`

## Global Constraints

- No comments in code, including PowerShell, YAML, Dockerfiles and TypeScript (`CLAUDE.md` §3).
- Commits carry no AI attribution trailers (`CLAUDE.md` §1).
- PowerShell must run on Windows PowerShell 5.1 and PowerShell 7: no `??`, `?:`, `?.`, `&&`, `||`, `ConvertFrom-Json -AsHashtable`, or three-argument `Join-Path`.
- Only approved PowerShell verbs (`Get`, `Set`, `New`, `Test`, `Invoke`, `Initialize`, `Publish`, `Grant`, `Assert`, `Hide`, `Add`).
- Region `asia-south1`. Development project `kairo2`.
- Resource names: Artifact Registry repo `kairo`; VPC `kairo`; subnet `kairo-run` (`10.10.0.0/24`); range `kairo-google-services` (`/20`); Cloud SQL `kairo-sql`; database `kairo`; secrets `kairo-sql-connection`, `google-client-id`, `google-client-secret`; build bucket `gs://<project>-builds`; services `kairo-web`, `kairo-api`, `kairo-worker`, `kairo-agents`; job `kairo-migrator`; service accounts `kairo-web`, `kairo-api`, `kairo-worker`, `kairo-agents`, `kairo-migrator`, `kairo-build`.
- Images: `<region>-docker.pkg.dev/<project>/kairo/<api|worker|migrator|agents|web>:<12-char commit>`.
- Service URLs: `https://<service>-<project number>.<region>.run.app`.
- Identity token header: `X-Serverless-Authorization: Bearer <token>`.

## Review Focus

1. **Compressed core responses through the forwarder.** Node's `fetch` decompresses the body but keeps `content-encoding`, so the browser would fail to decode it. Expect the forwarder to drop `content-encoding` and `content-length`. Pinned in Task 3.
2. **Secrets in error messages.** A failed `gcloud sql instances create --root-password=…` must not print the password. Expect `***`. Pinned in Task 4.
3. **A secret written with a trailing newline.** Piping into `gcloud` from Windows PowerShell appends CRLF, which would corrupt the connection string. Expect the exact bytes. Pinned in Task 5.
4. **Re-running setup after a half-finished run.** If the instance exists but the connection secret doesn't, expect the password to be reset and the secret written, not a failure or an unusable secret. Pinned in Task 5.
5. **A failed migration.** Expect no service to be updated. Pinned in Task 6.

---

### Task 1: Start the API without Redis

**Files:**
- Modify: `core/src/Kairo.Api/Program.cs:24`
- Test: `core/tests/Kairo.Api.Tests/ApiHostTests.cs`

**Interfaces:**
- Produces: the API registers `IConnectionMultiplexer` only when `ConnectionStrings:redis` is set.

- [ ] **Step 1: Write the failing tests**

Add to `ApiHostTests` (it uses `ApiFactory`, which sets a Redis connection string):

```csharp
    [Fact]
    public void Registers_redis_when_a_connection_string_is_set()
    {
        Assert.NotNull(factory.Services.GetService<IConnectionMultiplexer>());
    }
```

Add `using StackExchange.Redis;` to the top of the file, and append to the end of the file:

```csharp
public sealed class ApiWithoutRedisTests(ApiWithoutRedisFactory factory) : IClassFixture<ApiWithoutRedisFactory>
{
    [Fact]
    public async Task Starts_without_redis()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(factory.Services.GetService<IConnectionMultiplexer>());
    }
}

public sealed class ApiWithoutRedisFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", "Server=127.0.0.1,1;Database=kairo;User Id=test;Password=test;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=true");
    }
}
```

- [ ] **Step 2: Run them and see `Starts_without_redis` fail**

Run: `dotnet test --project core/tests/Kairo.Api.Tests`
Expected: `Starts_without_redis` fails: resolving `IConnectionMultiplexer` throws because the connection string is missing.

- [ ] **Step 3: Make Redis optional**

In `core/src/Kairo.Api/Program.cs` replace `builder.AddRedisClient("redis");` with:

```csharp
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
{
    builder.AddRedisClient("redis");
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --project core/tests/Kairo.Api.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add core/src/Kairo.Api/Program.cs core/tests/Kairo.Api.Tests/ApiHostTests.cs
git commit -m "Start the API without Redis when no Redis connection string is set"
```

---

### Task 2: Send a Google identity token with the web app's core calls

**Files:**
- Create: `web/src/lib/core-api/identity-token.ts`
- Create: `web/src/lib/core-api/identity-token.test.ts`
- Modify: `web/src/lib/http-names.ts`
- Modify: `web/src/lib/core-api/client.ts`
- Modify: `web/src/lib/core-api/client.test.ts`

**Interfaces:**
- Produces: `identityToken(): Promise<string | undefined>` from `@/lib/core-api/identity-token`; `SERVERLESS_AUTHORIZATION_HEADER = "X-Serverless-Authorization"` from `@/lib/http-names`.

- [ ] **Step 1: Write the failing token tests**

`web/src/lib/core-api/identity-token.test.ts`:

```ts
import { afterEach, beforeEach, expect, test, vi } from "vitest";

vi.mock("server-only", () => ({}));

const AUDIENCE = "https://kairo-api-123.asia-south1.run.app";

function tokenExpiringAt(seconds: number) {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "RS256" })}.${encode({ exp: seconds })}.signature`;
}

async function load() {
  vi.resetModules();
  return (await import("./identity-token")).identityToken;
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-10-13T10:00:00Z"));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

test("returns nothing and calls nobody when no audience is set", async () => {
  const fetch = vi.fn();
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_AUDIENCE", "");

  expect(await (await load())()).toBeUndefined();
  expect(fetch).not.toHaveBeenCalled();
});

test("asks the metadata server for a token for the audience", async () => {
  const token = tokenExpiringAt(Date.parse("2026-10-13T11:00:00Z") / 1000);
  const fetch = vi.fn(async () => new Response(token));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_AUDIENCE", AUDIENCE);

  expect(await (await load())()).toBe(token);
  expect(fetch).toHaveBeenCalledWith(
    `http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity?audience=${encodeURIComponent(AUDIENCE)}`,
    expect.objectContaining({ headers: { "Metadata-Flavor": "Google" } }),
  );
});

test("reuses the token until five minutes before it expires", async () => {
  const first = tokenExpiringAt(Date.parse("2026-10-13T11:00:00Z") / 1000);
  const second = tokenExpiringAt(Date.parse("2026-10-13T12:00:00Z") / 1000);
  const fetch = vi.fn().mockResolvedValueOnce(new Response(first)).mockResolvedValueOnce(new Response(second));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_AUDIENCE", AUDIENCE);
  const identityToken = await load();

  expect(await identityToken()).toBe(first);
  vi.setSystemTime(new Date("2026-10-13T10:54:00Z"));
  expect(await identityToken()).toBe(first);
  vi.setSystemTime(new Date("2026-10-13T10:56:00Z"));
  expect(await identityToken()).toBe(second);
  expect(fetch).toHaveBeenCalledTimes(2);
});

test("fails loudly when the metadata server refuses", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => new Response("no", { status: 404 })));
  vi.stubEnv("KAIRO_API_AUDIENCE", AUDIENCE);

  await expect((await load())()).rejects.toThrow("The metadata server answered 404 for an identity token.");
});
```

- [ ] **Step 2: Run and see them fail**

Run: `npm --prefix web test -- identity-token`
Expected: FAIL, cannot find `./identity-token`.

- [ ] **Step 3: Write the helper**

Append to `web/src/lib/http-names.ts`:

```ts
export const SERVERLESS_AUTHORIZATION_HEADER = "X-Serverless-Authorization";
```

`web/src/lib/core-api/identity-token.ts`:

```ts
import "server-only";

const METADATA_IDENTITY_URL =
  "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity";
const REFRESH_MARGIN_MS = 5 * 60 * 1000;

let cached: { audience: string; token: string; expiresAt: number } | undefined;

export async function identityToken(): Promise<string | undefined> {
  const audience = process.env.KAIRO_API_AUDIENCE;
  if (!audience) {
    return undefined;
  }

  if (cached && cached.audience === audience && cached.expiresAt - REFRESH_MARGIN_MS > Date.now()) {
    return cached.token;
  }

  const response = await fetch(`${METADATA_IDENTITY_URL}?audience=${encodeURIComponent(audience)}`, {
    headers: { "Metadata-Flavor": "Google" },
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`The metadata server answered ${response.status} for an identity token.`);
  }

  const token = (await response.text()).trim();
  cached = { audience, token, expiresAt: expiryOf(token) };
  return token;
}

function expiryOf(token: string): number {
  const payload = JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString("utf8")) as { exp: number };
  return payload.exp * 1000;
}
```

- [ ] **Step 4: Run the token tests**

Run: `npm --prefix web test -- identity-token`
Expected: 4 pass.

- [ ] **Step 5: Write the failing client test**

Append to `web/src/lib/core-api/client.test.ts` (change its first import to `import { afterEach, expect, test, vi } from "vitest";` and its client import to `import { coreApi, unwrap } from "./client";`):

```ts
vi.mock("./identity-token", () => ({ identityToken: vi.fn() }));

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

test("sends the identity token to the core when one is available", async () => {
  const { identityToken } = await import("./identity-token");
  vi.mocked(identityToken).mockResolvedValue("signed-token");
  const fetch = vi.fn(async (_request: Request) => Response.json({}));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_URL", "https://core.test");

  await (await coreApi()).GET("/api/me");

  expect(fetch.mock.calls[0][0].headers.get("X-Serverless-Authorization")).toBe("Bearer signed-token");
});

test("sends no identity token locally", async () => {
  const { identityToken } = await import("./identity-token");
  vi.mocked(identityToken).mockResolvedValue(undefined);
  const fetch = vi.fn(async (_request: Request) => Response.json({}));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_URL", "https://core.test");

  await (await coreApi()).GET("/api/me");

  expect(fetch.mock.calls[0][0].headers.has("X-Serverless-Authorization")).toBe(false);
});
```

- [ ] **Step 6: Run and see the first new test fail**

Run: `npm --prefix web test -- client`
Expected: "sends the identity token…" fails, because the header is missing.

- [ ] **Step 7: Send the token**

In `web/src/lib/core-api/client.ts`, import `identityToken` from `./identity-token` and `SERVERLESS_AUTHORIZATION_HEADER` from `@/lib/http-names`, and make `coreApi` read:

```ts
export async function coreApi() {
  const session = (await cookies()).get(SESSION_COOKIE);
  const baseUrl = process.env.KAIRO_API_URL;
  if (!baseUrl) {
    throw new Error("KAIRO_API_URL is not set.");
  }

  const token = await identityToken();

  return createClient<paths>({
    baseUrl,
    headers: {
      [REQUEST_HEADER]: "1",
      ...(token ? { [SERVERLESS_AUTHORIZATION_HEADER]: `Bearer ${token}` } : {}),
      ...(session ? { cookie: `${SESSION_COOKIE}=${session.value}` } : {}),
    },
  });
}
```

`openapi-fetch` reads `globalThis.fetch` when the client is created, which is after the test stubs it.

- [ ] **Step 8: Run the web checks**

Run: `npm --prefix web test` and `npm --prefix web run lint`
Expected: all pass, no lint errors.

- [ ] **Step 9: Commit**

```bash
git add web/src/lib
git commit -m "Send a Google identity token with the web app's calls to the core when deployed"
```

---

### Task 3: Forward /api/* from the web app to the core

**Files:**
- Create: `web/src/app/api/[...path]/route.ts`
- Create: `web/src/app/api/[...path]/route.test.ts`

**Interfaces:**
- Consumes: `identityToken()` and `SERVERLESS_AUTHORIZATION_HEADER` from Task 2.
- Produces: route exports `GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`, each `(request: Request) => Promise<Response>`.

- [ ] **Step 1: Write the failing tests**

`web/src/app/api/[...path]/route.test.ts`:

```ts
import { afterEach, beforeEach, expect, test, vi } from "vitest";

import { GET, POST } from "./route";

vi.mock("server-only", () => ({}));
vi.mock("@/lib/core-api/identity-token", () => ({ identityToken: vi.fn(async () => undefined) }));

let fetch: ReturnType<typeof vi.fn<(url: URL, init: RequestInit) => Promise<Response>>>;

beforeEach(() => {
  fetch = vi.fn(async () => new Response("ok"));
  vi.stubGlobal("fetch", fetch);
  vi.stubEnv("KAIRO_API_URL", "https://core.test");
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

test("forwards the method, path, query, headers and body to the core", async () => {
  await POST(
    new Request("https://web.test/api/brands/7/invitations?x=1", {
      method: "POST",
      headers: { cookie: "__Host-kairo-session=abc", "content-type": "application/json", "X-Kairo-Request": "1", host: "web.test" },
      body: JSON.stringify({ email: "a@b.c" }),
    }),
  );

  const [url, init] = fetch.mock.calls[0];
  expect(url.toString()).toBe("https://core.test/api/brands/7/invitations?x=1");
  expect(init.method).toBe("POST");
  expect(init.redirect).toBe("manual");
  const headers = new Headers(init.headers);
  expect(headers.get("cookie")).toBe("__Host-kairo-session=abc");
  expect(headers.get("x-kairo-request")).toBe("1");
  expect(headers.has("host")).toBe(false);
  expect(new TextDecoder().decode(init.body as ArrayBuffer)).toBe('{"email":"a@b.c"}');
});

test("sends no body with a GET", async () => {
  await GET(new Request("https://web.test/api/me"));

  expect(fetch.mock.calls[0][1].body).toBeUndefined();
});

test("adds the identity token when deployed", async () => {
  const { identityToken } = await import("@/lib/core-api/identity-token");
  vi.mocked(identityToken).mockResolvedValueOnce("signed-token");

  await GET(new Request("https://web.test/api/me"));

  expect(new Headers(fetch.mock.calls[0][1].headers).get("X-Serverless-Authorization")).toBe("Bearer signed-token");
});

test("passes redirects back instead of following them", async () => {
  fetch.mockResolvedValueOnce(new Response(null, { status: 302, headers: { location: "https://accounts.google.com/o/oauth2/v2/auth?x=1" } }));

  const response = await GET(new Request("https://web.test/api/auth/sign-in"));

  expect(response.status).toBe(302);
  expect(response.headers.get("location")).toBe("https://accounts.google.com/o/oauth2/v2/auth?x=1");
});

test("keeps every Set-Cookie header", async () => {
  const headers = new Headers();
  headers.append("set-cookie", "__Host-kairo-session=abc; Path=/; Secure; HttpOnly");
  headers.append("set-cookie", ".AspNetCore.Correlation=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT");
  fetch.mockResolvedValueOnce(new Response(null, { status: 302, headers }));

  const response = await GET(new Request("https://web.test/api/auth/callback/google?code=1"));

  expect(response.headers.getSetCookie()).toEqual([
    "__Host-kairo-session=abc; Path=/; Secure; HttpOnly",
    ".AspNetCore.Correlation=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT",
  ]);
});

test("drops the encoding headers of a body fetch has already decoded", async () => {
  fetch.mockResolvedValueOnce(
    new Response("{}", { headers: { "content-encoding": "gzip", "content-length": "20", "content-type": "application/json" } }),
  );

  const response = await GET(new Request("https://web.test/api/me"));

  expect(response.headers.has("content-encoding")).toBe(false);
  expect(response.headers.has("content-length")).toBe(false);
  expect(response.headers.get("content-type")).toBe("application/json");
});
```

- [ ] **Step 2: Run and see them fail**

Run: `npm --prefix web test -- route`
Expected: FAIL, cannot find `./route`.

- [ ] **Step 3: Write the route handler**

`web/src/app/api/[...path]/route.ts`:

```ts
import { identityToken } from "@/lib/core-api/identity-token";
import { SERVERLESS_AUTHORIZATION_HEADER } from "@/lib/http-names";

const HOP_BY_HOP = new Set([
  "connection",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
  "host",
  "content-length",
]);

const DECODED_BODY = new Set(["content-encoding", "content-length"]);

async function forward(request: Request): Promise<Response> {
  const baseUrl = process.env.KAIRO_API_URL;
  if (!baseUrl) {
    throw new Error("KAIRO_API_URL is not set.");
  }

  const incoming = new URL(request.url);
  const target = new URL(incoming.pathname + incoming.search, baseUrl);

  const headers = new Headers();
  request.headers.forEach((value, name) => {
    if (!HOP_BY_HOP.has(name)) {
      headers.set(name, value);
    }
  });

  const token = await identityToken();
  if (token) {
    headers.set(SERVERLESS_AUTHORIZATION_HEADER, `Bearer ${token}`);
  }

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  const answer = await fetch(target, {
    method: request.method,
    headers,
    body: hasBody ? await request.arrayBuffer() : undefined,
    redirect: "manual",
    cache: "no-store",
  });

  const responseHeaders = new Headers();
  answer.headers.forEach((value, name) => {
    if (name !== "set-cookie" && !HOP_BY_HOP.has(name) && !DECODED_BODY.has(name)) {
      responseHeaders.set(name, value);
    }
  });
  for (const cookie of answer.headers.getSetCookie()) {
    responseHeaders.append("set-cookie", cookie);
  }

  return new Response(answer.body, { status: answer.status, statusText: answer.statusText, headers: responseHeaders });
}

export const GET = forward;
export const HEAD = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;
export const OPTIONS = forward;
```

- [ ] **Step 4: Run the web checks, including the build**

Run: `npm --prefix web test`, `npm --prefix web run lint`, `npm --prefix web run build`
Expected: all pass. The build lists `ƒ /api/[...path]`.

- [ ] **Step 5: Commit**

```bash
git add "web/src/app/api"
git commit -m "Forward /api requests from the web app to the core when there is no load balancer"
```

---

### Task 4: Deployment module foundations and environments

**Files:**
- Create: `deploy/environments.psd1`
- Create: `deploy/Kairo.Deploy/Kairo.Deploy.psm1`
- Create: `deploy/Kairo.Deploy/Common.ps1`
- Create: `deploy/Kairo.Deploy/Setup.ps1` (empty for now)
- Create: `deploy/Kairo.Deploy/Release.ps1` (empty for now)
- Create: `deploy/tests/Common.Tests.ps1`

**Interfaces:**
- Produces (all exported from module `Kairo.Deploy`):
  - `Get-KairoEnvironment -Name <string> [-Path <string>]` → `[hashtable]` with `Name`, `Project`, `Region`, `SqlTier`, `SqlEdition`, `SqlHighAvailability`, `MinInstances` (`@{ web; api }`).
  - `Get-KairoServiceAccount -Environment <hashtable> -Name <string>` → `"<name>@<project>.iam.gserviceaccount.com"`.
  - `Get-KairoServiceUrl -Environment <hashtable> -ProjectNumber <string> -Service <string>` → URL string.
  - `Get-KairoRegistry -Environment <hashtable>` → `"<region>-docker.pkg.dev/<project>/kairo"`.
  - `Get-KairoImage -Environment <hashtable> -Name <string> -Tag <string>` → image reference.
  - `Hide-KairoSecrets -Text <string>` → text with `--password=` and `--root-password=` values replaced by `***`.
  - `Invoke-Native -FilePath <string> [-ArgumentList <string[]>] [-Capture]` → runs a program, throws on non-zero exit, returns stdout lines when `-Capture`.
  - `Invoke-Gcloud -ArgumentList <string[]> [-Capture]` → `Invoke-Native gcloud` with `--quiet` appended.
  - `Test-Gcloud -ArgumentList <string[]>` → `$true` when the command exits 0; output discarded.
  - Module-scoped names: `$script:Registry = 'kairo'`, `$script:Network = 'kairo'`, `$script:Subnet = 'kairo-run'`, `$script:SqlInstance = 'kairo-sql'`, `$script:Database = 'kairo'`, `$script:SqlSecret = 'kairo-sql-connection'`, `$script:GoogleSecrets` (ordered: `Authentication__Google__ClientId = 'google-client-id'`, `Authentication__Google__ClientSecret = 'google-client-secret'`).

- [ ] **Step 1: Write the environment file and an empty module**

`deploy/environments.psd1`:

```powershell
@{
    development = @{
        Project             = 'kairo2'
        Region              = 'asia-south1'
        SqlTier             = 'db-custom-1-3840'
        SqlEdition          = 'SQLSERVER_2022_EXPRESS'
        SqlHighAvailability = $false
        MinInstances        = @{ web = 0; api = 0 }
    }
    staging     = @{ Project = '' }
    production  = @{ Project = '' }
}
```

`deploy/Kairo.Deploy/Kairo.Deploy.psm1`:

```powershell
. (Join-Path $PSScriptRoot 'Common.ps1')
. (Join-Path $PSScriptRoot 'Setup.ps1')
. (Join-Path $PSScriptRoot 'Release.ps1')

Export-ModuleMember -Function *
```

Create `Common.ps1`, `Setup.ps1` and `Release.ps1` as empty files.

- [ ] **Step 2: Write the failing tests**

`deploy/tests/Common.Tests.ps1`:

```powershell
BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{ Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1' }
    $script:shell = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
}

Describe 'Get-KairoEnvironment' {
    It 'loads development from the environment file' {
        $environment = Get-KairoEnvironment -Name 'development'

        $environment.Name | Should -Be 'development'
        $environment.Project | Should -Be 'kairo2'
        $environment.Region | Should -Be 'asia-south1'
    }

    It 'refuses an unknown environment' {
        { Get-KairoEnvironment -Name 'qa' } | Should -Throw "Unknown environment 'qa'*"
    }

    It 'refuses an environment without a project' {
        { Get-KairoEnvironment -Name 'staging' } | Should -Throw '*staging environment has no project yet*'
    }
}

Describe 'names' {
    It 'derives a Cloud Run service URL' {
        Get-KairoServiceUrl -Environment $environment -ProjectNumber '123' -Service 'kairo-api' |
            Should -Be 'https://kairo-api-123.asia-south1.run.app'
    }

    It 'derives an image reference' {
        Get-KairoImage -Environment $environment -Name 'api' -Tag 'abc123' |
            Should -Be 'asia-south1-docker.pkg.dev/kairo-test/kairo/api:abc123'
    }

    It 'derives a service account' {
        Get-KairoServiceAccount -Environment $environment -Name 'kairo-web' |
            Should -Be 'kairo-web@kairo-test.iam.gserviceaccount.com'
    }
}

Describe 'Hide-KairoSecrets' {
    It 'hides passwords' {
        Hide-KairoSecrets -Text 'gcloud sql instances create --root-password=s3cret --tier x' |
            Should -Be 'gcloud sql instances create --root-password=*** --tier x'
        Hide-KairoSecrets -Text 'gcloud sql users set-password sqlserver --password=s3cret' |
            Should -Be 'gcloud sql users set-password sqlserver --password=***'
    }
}

Describe 'Invoke-Native' {
    It 'returns the output when asked to capture it' {
        Invoke-Native -FilePath $shell -ArgumentList @('-NoProfile', '-Command', 'Write-Output hello') -Capture |
            Should -Be 'hello'
    }

    It 'throws with the exit code when the program fails' {
        { Invoke-Native -FilePath $shell -ArgumentList @('-NoProfile', '-Command', 'exit 3') } |
            Should -Throw '*failed with exit code 3*'
    }

    It 'keeps passwords out of the failure message' {
        $message = $null
        try {
            Invoke-Native -FilePath $shell -ArgumentList @('-NoProfile', '-Command', 'exit 3', '--root-password=s3cret')
        }
        catch {
            $message = $_.Exception.Message
        }

        $message | Should -Not -BeNullOrEmpty
        $message | Should -Not -BeLike '*s3cret*'
    }
}

Describe 'Invoke-Gcloud' {
    It 'adds --quiet so gcloud never waits for an answer' {
        Mock -ModuleName Kairo.Deploy Invoke-Native { }

        Invoke-Gcloud -ArgumentList @('run', 'services', 'list')

        Should -Invoke -ModuleName Kairo.Deploy Invoke-Native -ParameterFilter {
            $FilePath -eq 'gcloud' -and ($ArgumentList -join ' ') -eq 'run services list --quiet'
        }
    }
}
```

- [ ] **Step 3: Run and see them fail**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests -Output Detailed"`
Expected: failures, `Get-KairoEnvironment` is not recognized.

- [ ] **Step 4: Write `Common.ps1`**

```powershell
$script:Registry = 'kairo'
$script:Network = 'kairo'
$script:Subnet = 'kairo-run'
$script:SqlInstance = 'kairo-sql'
$script:Database = 'kairo'
$script:SqlSecret = 'kairo-sql-connection'
$script:GoogleSecrets = [ordered]@{
    'Authentication__Google__ClientId'     = 'google-client-id'
    'Authentication__Google__ClientSecret' = 'google-client-secret'
}

function Get-KairoEnvironment {
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$Path = (Join-Path (Split-Path $PSScriptRoot -Parent) 'environments.psd1')
    )

    $environments = Import-PowerShellDataFile -Path $Path
    if (-not $environments.ContainsKey($Name)) {
        throw "Unknown environment '$Name'. Use one of: $(($environments.Keys | Sort-Object) -join ', ')."
    }

    $environment = $environments[$Name].Clone()
    if ([string]::IsNullOrWhiteSpace($environment.Project)) {
        throw "The $Name environment has no project yet. Set its Project in $Path, then run deploy/setup.ps1 $Name."
    }

    $environment.Name = $Name
    $environment
}

function Get-KairoServiceAccount {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    "$Name@$($Environment.Project).iam.gserviceaccount.com"
}

function Get-KairoServiceUrl {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$ProjectNumber,
        [Parameter(Mandatory)][string]$Service
    )

    "https://$Service-$ProjectNumber.$($Environment.Region).run.app"
}

function Get-KairoRegistry {
    param([Parameter(Mandatory)][hashtable]$Environment)

    "$($Environment.Region)-docker.pkg.dev/$($Environment.Project)/$script:Registry"
}

function Get-KairoImage {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Tag
    )

    "$(Get-KairoRegistry -Environment $Environment)/$($Name):$Tag"
}

function Hide-KairoSecrets {
    param([Parameter(Mandatory)][string]$Text)

    $Text -replace '(--(?:root-)?password=)\S+', '$1***'
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$Capture
    )

    if ($Capture) {
        $output = & $FilePath @ArgumentList
    }
    else {
        & $FilePath @ArgumentList | Out-Host
    }

    if ($LASTEXITCODE -ne 0) {
        throw (Hide-KairoSecrets -Text "$FilePath $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE.")
    }

    if ($Capture) {
        $output
    }
}

function Invoke-Gcloud {
    param([Parameter(Mandatory)][string[]]$ArgumentList, [switch]$Capture)

    Invoke-Native -FilePath 'gcloud' -ArgumentList ($ArgumentList + '--quiet') -Capture:$Capture
}

function Test-Gcloud {
    param([Parameter(Mandatory)][string[]]$ArgumentList)

    $ErrorActionPreference = 'Continue'
    & gcloud @($ArgumentList + '--quiet') 2>&1 | Out-Null
    $LASTEXITCODE -eq 0
}
```

- [ ] **Step 5: Run the tests**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests -Output Detailed"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add deploy
git commit -m "Add the deployment module with environments and a checked gcloud wrapper"
```

---

### Task 5: Create a project's shared resources

**Files:**
- Modify: `deploy/Kairo.Deploy/Setup.ps1`
- Create: `deploy/setup.ps1`
- Create: `deploy/tests/Setup.Tests.ps1`

**Interfaces:**
- Consumes: everything from Task 4.
- Produces:
  - `Initialize-KairoProject -Environment <hashtable>`.
  - `New-KairoPassword` → 32 characters from `ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789`, with at least one upper-case letter, one lower-case letter and one digit.
  - `Set-KairoSecretValue -Environment <hashtable> -Name <string> -Value <string>`, which creates the secret if missing and adds a version with exactly `Value`'s UTF-8 bytes.
  - `Test-KairoSecretHasValue -Environment <hashtable> -Name <string>` → `$true` when the secret has an enabled version.
  - `Add-KairoBinding -ArgumentList <string[]>`: `Invoke-Gcloud` retried up to 5 times, 10 seconds apart, because new service accounts take a moment to appear to IAM.

- [ ] **Step 1: Write the failing tests**

`deploy/tests/Setup.Tests.ps1`:

```powershell
BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{
        Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1'
        SqlTier = 'db-custom-1-3840'; SqlEdition = 'SQLSERVER_2022_EXPRESS'; SqlHighAvailability = $false
        MinInstances = @{ web = 0; api = 0 }
    }
    $script:instanceJson = '{"ipAddresses":[{"type":"PRIVATE","ipAddress":"10.20.0.3"}]}'
}

Describe 'Initialize-KairoProject' {
    BeforeEach {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:exists = $false
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $script:exists }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $line = $ArgumentList -join ' '
            $script:calls.Add($line)
            if ($line -like 'sql instances describe*') { $script:instanceJson }
            if ($line -like 'services vpc-peerings list*' -and $script:exists) { 'servicenetworking-googleapis-com' }
        }
        Mock -ModuleName Kairo.Deploy Set-KairoSecretValue { }
        Mock -ModuleName Kairo.Deploy Start-Sleep { }
    }

    Context 'in an empty project' {
        BeforeEach { Initialize-KairoProject -Environment $environment }

        It 'enables the APIs Kairo needs' {
            $calls | Should -Contain ('services enable run.googleapis.com sqladmin.googleapis.com compute.googleapis.com ' +
                'servicenetworking.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com ' +
                'secretmanager.googleapis.com iam.googleapis.com storage.googleapis.com --project kairo-test')
        }

        It 'creates the registry, the build bucket and the network' {
            $calls | Should -Contain 'artifacts repositories create kairo --repository-format docker --location asia-south1 --project kairo-test'
            $calls | Should -Contain 'storage buckets create gs://kairo-test-builds --location asia-south1 --uniform-bucket-level-access --project kairo-test'
            $calls | Should -Contain 'compute networks create kairo --subnet-mode custom --project kairo-test'
            $calls | Should -Contain 'compute networks subnets create kairo-run --network kairo --region asia-south1 --range 10.10.0.0/24 --project kairo-test'
            $calls | Should -Contain 'compute addresses create kairo-google-services --global --purpose VPC_PEERING --prefix-length 20 --network kairo --project kairo-test'
            $calls | Should -Contain 'services vpc-peerings connect --service servicenetworking.googleapis.com --ranges kairo-google-services --network kairo --project kairo-test'
        }

        It 'creates SQL Server Express with only a private address' {
            $create = $calls | Where-Object { $_ -like 'sql instances create kairo-sql *' }
            $create | Should -BeLike '*--database-version SQLSERVER_2022_EXPRESS*'
            $create | Should -BeLike '*--edition enterprise*'
            $create | Should -BeLike '*--tier db-custom-1-3840*'
            $create | Should -BeLike '*--network projects/kairo-test/global/networks/kairo*'
            $create | Should -BeLike '*--no-assign-ip*'
            $create | Should -BeLike '*--availability-type zonal*'
            $create | Should -BeLike '*--backup-start-time*'
            $create | Should -Match '--root-password=[A-Za-z0-9]{32}( |$)'
            $calls | Should -Contain 'sql databases create kairo --instance kairo-sql --project kairo-test'
        }

        It 'stores a connection string that uses the private address and the new password' {
            $password = [regex]::Match(($calls | Where-Object { $_ -like 'sql instances create*' }), '--root-password=(\S+)').Groups[1].Value
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -ParameterFilter {
                $Name -eq 'kairo-sql-connection' -and
                $Value -eq "Server=10.20.0.3;Database=kairo;User Id=sqlserver;Password=$password;Encrypt=True;TrustServerCertificate=True"
            }
        }

        It 'creates the Google secrets without a value' {
            $calls | Should -Contain 'secrets create google-client-id --replication-policy automatic --project kairo-test'
            $calls | Should -Contain 'secrets create google-client-secret --replication-policy automatic --project kairo-test'
        }

        It 'creates one service account per service' {
            foreach ($name in 'kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build') {
                $calls | Should -Contain "iam service-accounts create $name --project kairo-test"
            }
        }

        It 'lets each service read only the secrets it needs' {
            foreach ($name in 'kairo-api', 'kairo-worker', 'kairo-migrator') {
                $calls | Should -Contain "secrets add-iam-policy-binding kairo-sql-connection --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test"
            }
            foreach ($secret in 'google-client-id', 'google-client-secret') {
                $calls | Should -Contain "secrets add-iam-policy-binding $secret --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test"
            }
            ($calls | Where-Object { $_ -like 'secrets add-iam-policy-binding google-*kairo-web@*' }) | Should -BeNullOrEmpty
        }

        It 'lets Cloud Build read the source, push images and write logs' {
            $build = 'serviceAccount:kairo-build@kairo-test.iam.gserviceaccount.com'
            $calls | Should -Contain "artifacts repositories add-iam-policy-binding kairo --location asia-south1 --member $build --role roles/artifactregistry.writer --project kairo-test"
            $calls | Should -Contain "storage buckets add-iam-policy-binding gs://kairo-test-builds --member $build --role roles/storage.objectViewer --project kairo-test"
            $calls | Should -Contain "projects add-iam-policy-binding kairo-test --member $build --role roles/logging.logWriter --condition None"
        }
    }

    Context 'in a project that is already set up' {
        BeforeEach {
            $script:exists = $true
            Initialize-KairoProject -Environment $environment
        }

        It 'creates nothing and keeps the database password' {
            ($calls | Where-Object { $_ -match ' (create|connect|set-password) ' }) | Should -BeNullOrEmpty
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 0
        }
    }

    Context 'when the instance exists but its connection secret does not' {
        BeforeEach {
            Mock -ModuleName Kairo.Deploy Test-Gcloud { ($ArgumentList -join ' ') -notlike 'secrets describe kairo-sql-connection*' }
            Initialize-KairoProject -Environment $environment
        }

        It 'resets the password and stores a new connection string' {
            $reset = $calls | Where-Object { $_ -like 'sql users set-password sqlserver --instance kairo-sql *' }
            $reset | Should -Match '--password=[A-Za-z0-9]{32}( |$)'
            ($calls | Where-Object { $_ -like 'sql instances create*' }) | Should -BeNullOrEmpty
            Should -Invoke -ModuleName Kairo.Deploy Set-KairoSecretValue -Times 1 -ParameterFilter { $Name -eq 'kairo-sql-connection' }
        }
    }

    Context 'with high availability' {
        It 'creates a regional instance' {
            $highlyAvailable = $environment.Clone()
            $highlyAvailable.SqlHighAvailability = $true

            Initialize-KairoProject -Environment $highlyAvailable

            ($calls | Where-Object { $_ -like 'sql instances create*' }) | Should -BeLike '*--availability-type regional*'
        }
    }
}

Describe 'New-KairoPassword' {
    It 'makes long, mixed, unambiguous passwords that differ each time' {
        $first = New-KairoPassword
        $second = New-KairoPassword

        $first | Should -Match '^[A-HJ-NP-Za-km-z2-9]{32}$'
        $first | Should -MatchExactly '[A-Z]'
        $first | Should -MatchExactly '[a-z]'
        $first | Should -Match '[0-9]'
        $second | Should -Not -Be $first
    }
}

Describe 'Set-KairoSecretValue' {
    It 'adds exactly the given value, with no trailing newline' {
        $script:written = $null
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $true }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $file = ($ArgumentList | Where-Object { $_ -like '--data-file=*' }) -replace '^--data-file=', ''
            $script:written = [IO.File]::ReadAllBytes($file)
        }

        Set-KairoSecretValue -Environment $environment -Name 'kairo-sql-connection' -Value 'Server=10.0.0.1;Password=x'

        [Text.Encoding]::UTF8.GetString($script:written) | Should -BeExactly 'Server=10.0.0.1;Password=x'
    }

    It 'creates the secret first when it is missing' {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName Kairo.Deploy Test-Gcloud { $false }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { $script:calls.Add($ArgumentList -join ' ') }

        Set-KairoSecretValue -Environment $environment -Name 'kairo-sql-connection' -Value 'x'

        $calls[0] | Should -Be 'secrets create kairo-sql-connection --replication-policy automatic --project kairo-test'
        $calls[1] | Should -BeLike 'secrets versions add kairo-sql-connection --data-file=* --project kairo-test'
    }
}

Describe 'Test-KairoSecretHasValue' {
    It 'is true only when the secret has an enabled version' {
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { 'projects/1/secrets/google-client-id/versions/1' }
        Test-KairoSecretHasValue -Environment $environment -Name 'google-client-id' | Should -BeTrue

        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { }
        Test-KairoSecretHasValue -Environment $environment -Name 'google-client-id' | Should -BeFalse
    }
}

Describe 'Add-KairoBinding' {
    It 'retries while a new service account is not yet visible' {
        $script:attempts = 0
        Mock -ModuleName Kairo.Deploy Start-Sleep { }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $script:attempts++
            if ($script:attempts -lt 3) { throw 'Service account does not exist.' }
        }

        Add-KairoBinding -ArgumentList @('secrets', 'add-iam-policy-binding', 'x')

        $script:attempts | Should -Be 3
    }

    It 'gives up after five attempts' {
        Mock -ModuleName Kairo.Deploy Start-Sleep { }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { throw 'Service account does not exist.' }

        { Add-KairoBinding -ArgumentList @('secrets', 'add-iam-policy-binding', 'x') } | Should -Throw '*does not exist*'
        Should -Invoke -ModuleName Kairo.Deploy Invoke-Gcloud -Times 5 -Exactly
    }
}
```

- [ ] **Step 2: Run and see them fail**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests -Output Detailed"`
Expected: the Setup tests fail, `Initialize-KairoProject` is not recognized.

- [ ] **Step 3: Write `Setup.ps1`**

```powershell
$script:Apis = @(
    'run.googleapis.com', 'sqladmin.googleapis.com', 'compute.googleapis.com', 'servicenetworking.googleapis.com',
    'artifactregistry.googleapis.com', 'cloudbuild.googleapis.com', 'secretmanager.googleapis.com',
    'iam.googleapis.com', 'storage.googleapis.com'
)
$script:ServiceAccounts = @('kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build')
$script:PasswordCharacters = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'

function Initialize-KairoProject {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)

    Write-Host "Enabling APIs in $($Environment.Project)"
    Invoke-Gcloud -ArgumentList (@('services', 'enable') + $script:Apis + $scope)

    Initialize-KairoRegistry -Environment $Environment
    Initialize-KairoNetwork -Environment $Environment
    Initialize-KairoDatabase -Environment $Environment
    Initialize-KairoSecrets -Environment $Environment
    Initialize-KairoServiceAccounts -Environment $Environment
}

function Initialize-KairoRegistry {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('artifacts', 'repositories', 'describe', $script:Registry, '--location', $Environment.Region) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('artifacts', 'repositories', 'create', $script:Registry, '--repository-format', 'docker', '--location', $Environment.Region) + $scope)
    }

    $bucket = "gs://$($Environment.Project)-builds"
    if (-not (Test-Gcloud -ArgumentList (@('storage', 'buckets', 'describe', $bucket) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('storage', 'buckets', 'create', $bucket, '--location', $Environment.Region, '--uniform-bucket-level-access') + $scope)
    }
}

function Initialize-KairoNetwork {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('compute', 'networks', 'describe', $script:Network) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('compute', 'networks', 'create', $script:Network, '--subnet-mode', 'custom') + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('compute', 'networks', 'subnets', 'describe', $script:Subnet, '--region', $Environment.Region) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('compute', 'networks', 'subnets', 'create', $script:Subnet, '--network', $script:Network, '--region', $Environment.Region, '--range', '10.10.0.0/24') + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('compute', 'addresses', 'describe', 'kairo-google-services', '--global') + $scope))) {
        Invoke-Gcloud -ArgumentList (@('compute', 'addresses', 'create', 'kairo-google-services', '--global', '--purpose', 'VPC_PEERING', '--prefix-length', '20', '--network', $script:Network) + $scope)
    }

    $peerings = Invoke-Gcloud -ArgumentList (@('services', 'vpc-peerings', 'list', '--network', $script:Network) + $scope) -Capture
    if (-not ($peerings -match 'servicenetworking')) {
        Invoke-Gcloud -ArgumentList (@('services', 'vpc-peerings', 'connect', '--service', 'servicenetworking.googleapis.com', '--ranges', 'kairo-google-services', '--network', $script:Network) + $scope)
    }
}

function Initialize-KairoDatabase {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    $hasInstance = Test-Gcloud -ArgumentList (@('sql', 'instances', 'describe', $script:SqlInstance) + $scope)
    $hasSecret = Test-Gcloud -ArgumentList (@('secrets', 'describe', $script:SqlSecret) + $scope)

    $password = $null
    if (-not $hasInstance) {
        $password = New-KairoPassword
        $availability = 'zonal'
        if ($Environment.SqlHighAvailability) {
            $availability = 'regional'
        }

        Write-Host "Creating Cloud SQL instance $script:SqlInstance. This takes about ten minutes."
        Invoke-Gcloud -ArgumentList (@(
                'sql', 'instances', 'create', $script:SqlInstance,
                '--database-version', $Environment.SqlEdition,
                '--edition', 'enterprise',
                '--tier', $Environment.SqlTier,
                '--region', $Environment.Region,
                '--network', "projects/$($Environment.Project)/global/networks/$script:Network",
                '--no-assign-ip',
                '--storage-type', 'SSD',
                '--storage-size', '10',
                '--storage-auto-increase',
                '--backup-start-time', '20:30',
                '--availability-type', $availability,
                "--root-password=$password"
            ) + $scope)
    }
    elseif (-not $hasSecret) {
        $password = New-KairoPassword
        Invoke-Gcloud -ArgumentList (@('sql', 'users', 'set-password', 'sqlserver', '--instance', $script:SqlInstance, "--password=$password") + $scope)
    }

    if (-not (Test-Gcloud -ArgumentList (@('sql', 'databases', 'describe', $script:Database, '--instance', $script:SqlInstance) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('sql', 'databases', 'create', $script:Database, '--instance', $script:SqlInstance) + $scope)
    }

    if ($password) {
        $address = Get-KairoSqlAddress -Environment $Environment
        Set-KairoSecretValue -Environment $Environment -Name $script:SqlSecret `
            -Value "Server=$address;Database=$script:Database;User Id=sqlserver;Password=$password;Encrypt=True;TrustServerCertificate=True"
    }
}

function Get-KairoSqlAddress {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $json = (Invoke-Gcloud -ArgumentList @('sql', 'instances', 'describe', $script:SqlInstance, '--format=json', '--project', $Environment.Project) -Capture) -join "`n"
    $address = ($json | ConvertFrom-Json).ipAddresses | Where-Object { $_.type -eq 'PRIVATE' } | Select-Object -First 1
    if (-not $address) {
        throw "Cloud SQL instance $script:SqlInstance has no private IP address."
    }

    $address.ipAddress
}

function Initialize-KairoSecrets {
    param([Parameter(Mandatory)][hashtable]$Environment)

    foreach ($secret in $script:GoogleSecrets.Values) {
        New-KairoSecret -Environment $Environment -Name $secret
    }
}

function New-KairoSecret {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    $scope = @('--project', $Environment.Project)
    if (-not (Test-Gcloud -ArgumentList (@('secrets', 'describe', $Name) + $scope))) {
        Invoke-Gcloud -ArgumentList (@('secrets', 'create', $Name, '--replication-policy', 'automatic') + $scope)
    }
}

function Set-KairoSecretValue {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    New-KairoSecret -Environment $Environment -Name $Name
    $file = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllBytes($file, [Text.Encoding]::UTF8.GetBytes($Value))
        Invoke-Gcloud -ArgumentList @('secrets', 'versions', 'add', $Name, "--data-file=$file", '--project', $Environment.Project)
    }
    finally {
        Remove-Item -LiteralPath $file -Force
    }
}

function Test-KairoSecretHasValue {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Name)

    $versions = Invoke-Gcloud -ArgumentList @('secrets', 'versions', 'list', $Name, '--filter=state:ENABLED', '--format=value(name)', '--limit=1', '--project', $Environment.Project) -Capture
    [bool]($versions | Where-Object { $_ })
}

function Initialize-KairoServiceAccounts {
    param([Parameter(Mandatory)][hashtable]$Environment)

    $scope = @('--project', $Environment.Project)
    foreach ($name in $script:ServiceAccounts) {
        if (-not (Test-Gcloud -ArgumentList (@('iam', 'service-accounts', 'describe', (Get-KairoServiceAccount -Environment $Environment -Name $name)) + $scope))) {
            Invoke-Gcloud -ArgumentList (@('iam', 'service-accounts', 'create', $name) + $scope)
        }
    }

    $accessor = @('--role', 'roles/secretmanager.secretAccessor')
    foreach ($name in 'kairo-api', 'kairo-worker', 'kairo-migrator') {
        Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $script:SqlSecret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)") + $accessor + $scope)
    }
    foreach ($secret in $script:GoogleSecrets.Values) {
        Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $secret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-api')") + $accessor + $scope)
    }

    $build = "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-build')"
    Add-KairoBinding -ArgumentList (@('artifacts', 'repositories', 'add-iam-policy-binding', $script:Registry, '--location', $Environment.Region, '--member', $build, '--role', 'roles/artifactregistry.writer') + $scope)
    Add-KairoBinding -ArgumentList (@('storage', 'buckets', 'add-iam-policy-binding', "gs://$($Environment.Project)-builds", '--member', $build, '--role', 'roles/storage.objectViewer') + $scope)
    Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', $build, '--role', 'roles/logging.logWriter', '--condition', 'None')
}

function Add-KairoBinding {
    param([Parameter(Mandatory)][string[]]$ArgumentList)

    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-Gcloud -ArgumentList $ArgumentList
            return
        }
        catch {
            if ($attempt -ge 5) {
                throw
            }

            Start-Sleep -Seconds 10
        }
    }
}

function New-KairoPassword {
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        do {
            $bytes = New-Object byte[] 32
            $random.GetBytes($bytes)
            $password = -join ($bytes | ForEach-Object { $script:PasswordCharacters[$_ % $script:PasswordCharacters.Length] })
        } until ($password -cmatch '[A-Z]' -and $password -cmatch '[a-z]' -and $password -match '[0-9]')
    }
    finally {
        $random.Dispose()
    }

    $password
}
```

`deploy/setup.ps1`:

```powershell
param([Parameter(Mandatory, Position = 0)][string]$Environment)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
Initialize-KairoProject -Environment (Get-KairoEnvironment -Name $Environment)
Write-Host "$Environment is set up."
```

The secret list in Step 1's "creates the Google secrets" test only holds if `Set-KairoSecretValue` is mocked (it is) and `New-KairoSecret` goes through `Invoke-Gcloud` (it does).

- [ ] **Step 4: Run the tests**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests -Output Detailed"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add deploy
git commit -m "Create a project's network, database, secrets and service accounts with setup.ps1"
```

---

### Task 6: Test, build, migrate and deploy

**Files:**
- Modify: `deploy/Kairo.Deploy/Release.ps1`
- Create: `deploy/deploy.ps1`
- Create: `deploy.bat`
- Create: `deploy/cloudbuild.yaml`
- Create: `.gcloudignore`
- Create: `core/src/Kairo.Migrator/Dockerfile`
- Create: `deploy/tests/Release.Tests.ps1`

**Interfaces:**
- Consumes: Tasks 4 and 5.
- Produces:
  - `Invoke-KairoDeploy -Environment <hashtable> -Root <string> [-SkipTests]` → `[hashtable]` of service URLs keyed `kairo-web`, `kairo-api`, `kairo-agents`, `kairo-worker`.
  - `Assert-KairoCleanTree -Root`, `Get-KairoCommit -Root` → 12-character commit, `Invoke-KairoTests -Root`, `Get-KairoProjectNumber -Environment`, `Invoke-KairoBuild -Environment -Root -Tag`, `Invoke-KairoMigration -Environment -Tag`, `Publish-KairoService -Environment -Service -Image -Settings <string[]>`, `Grant-KairoInvoker -Environment -Service -Caller`.

- [ ] **Step 1: Write the failing tests**

`deploy/tests/Release.Tests.ps1`:

```powershell
BeforeAll {
    Import-Module "$PSScriptRoot/../Kairo.Deploy" -Force
    $script:environment = @{
        Name = 'development'; Project = 'kairo-test'; Region = 'asia-south1'
        SqlTier = 'db-custom-1-3840'; SqlEdition = 'SQLSERVER_2022_EXPRESS'; SqlHighAvailability = $false
        MinInstances = @{ web = 0; api = 1 }
    }
    $script:root = 'C:\src\Kairo'
}

Describe 'Invoke-KairoDeploy' {
    BeforeEach {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:googleConfigured = $true
        Mock -ModuleName Kairo.Deploy Assert-KairoCleanTree { }
        Mock -ModuleName Kairo.Deploy Get-KairoCommit { 'abc123def456' }
        Mock -ModuleName Kairo.Deploy Invoke-KairoTests { $script:calls.Add('tests') }
        Mock -ModuleName Kairo.Deploy Get-KairoProjectNumber { '123' }
        Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $script:googleConfigured }
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud { $script:calls.Add($ArgumentList -join ' ') }
    }

    It 'tests, builds, migrates, then deploys the services' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls[0] | Should -Be 'tests'
        $calls[1] | Should -BeLike 'builds submit*'
        $calls[2] | Should -BeLike 'run jobs deploy kairo-migrator*'
        $calls[3] | Should -BeLike 'run jobs execute kairo-migrator*--wait*'
        ($calls | Select-Object -Skip 4 | Where-Object { $_ -like 'run deploy *' }) -replace '^run deploy (\S+).*', '$1' |
            Should -Be @('kairo-agents', 'kairo-api', 'kairo-worker', 'kairo-web')
    }

    It 'skips the tests only when asked' {
        Invoke-KairoDeploy -Environment $environment -Root $root -SkipTests 3>$null | Out-Null

        Should -Invoke -ModuleName Kairo.Deploy Invoke-KairoTests -Times 0
    }

    It 'stops before building when a test fails' {
        Mock -ModuleName Kairo.Deploy Invoke-KairoTests { throw 'dotnet test failed' }

        { Invoke-KairoDeploy -Environment $environment -Root $root } | Should -Throw 'dotnet test failed'
        Should -Invoke -ModuleName Kairo.Deploy Invoke-Gcloud -Times 0
    }

    It 'stops before any service changes when the migration fails' {
        Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
            $line = $ArgumentList -join ' '
            $script:calls.Add($line)
            if ($line -like 'run jobs execute*') { throw 'migration failed' }
        }

        { Invoke-KairoDeploy -Environment $environment -Root $root } | Should -Throw 'migration failed'
        ($calls | Where-Object { $_ -like 'run deploy*' -or $_ -like '*add-iam-policy-binding*' }) | Should -BeNullOrEmpty
    }

    It 'stops before anything when the working tree has changes' {
        Mock -ModuleName Kairo.Deploy Assert-KairoCleanTree { throw 'Commit or stash your changes first.' }

        { Invoke-KairoDeploy -Environment $environment -Root $root } | Should -Throw 'Commit or stash*'
        $calls | Should -BeNullOrEmpty
    }

    It 'builds every image with Cloud Build, tagged with the commit' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $build = $calls | Where-Object { $_ -like 'builds submit*' }
        $build | Should -BeLike "builds submit $root --config $(Join-Path $root 'deploy/cloudbuild.yaml')*"
        $build | Should -BeLike '*--service-account projects/kairo-test/serviceAccounts/kairo-build@kairo-test.iam.gserviceaccount.com*'
        $build | Should -BeLike '*--gcs-source-staging-dir gs://kairo-test-builds/source*'
        $build | Should -BeLike '*--substitutions=_REGISTRY=asia-south1-docker.pkg.dev/kairo-test/kairo,_TAG=abc123def456*'
    }

    It 'runs the Migrator inside the VPC with the connection string' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $job = $calls | Where-Object { $_ -like 'run jobs deploy*' }
        $job | Should -BeLike '*--image asia-south1-docker.pkg.dev/kairo-test/kairo/migrator:abc123def456*'
        $job | Should -BeLike '*--service-account kairo-migrator@kairo-test.iam.gserviceaccount.com*'
        $job | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $job | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest*'
        $job | Should -BeLike '*--max-retries 0*'
    }

    It 'deploys the API privately, inside the VPC, knowing the web address' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $api = $calls | Where-Object { $_ -like 'run deploy kairo-api *' }
        $api | Should -BeLike '*--image asia-south1-docker.pkg.dev/kairo-test/kairo/api:abc123def456*'
        $api | Should -BeLike '*--service-account kairo-api@kairo-test.iam.gserviceaccount.com*'
        $api | Should -BeLike '*--no-allow-unauthenticated*'
        $api | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $api | Should -BeLike '*--session-affinity*'
        $api | Should -BeLike '*--min-instances=1*'
        $api | Should -BeLike '*--set-env-vars=Kairo__PublicOrigin=https://kairo-web-123.asia-south1.run.app*'
        $api | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest,Authentication__Google__ClientId=google-client-id:latest,Authentication__Google__ClientSecret=google-client-secret:latest*'
    }

    It 'leaves Google sign-in off until its secrets have a value' {
        $script:googleConfigured = $false

        Invoke-KairoDeploy -Environment $environment -Root $root 3>$null | Out-Null

        $api = $calls | Where-Object { $_ -like 'run deploy kairo-api *' }
        $api | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest'
        $api | Should -Not -BeLike '*google-client*'
    }

    It 'deploys the worker privately with the database' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $worker = $calls | Where-Object { $_ -like 'run deploy kairo-worker *' }
        $worker | Should -BeLike '*--no-allow-unauthenticated*'
        $worker | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
        $worker | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest*'
    }

    It 'deploys the agent service privately with a long timeout' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $agents = $calls | Where-Object { $_ -like 'run deploy kairo-agents *' }
        $agents | Should -BeLike '*--no-allow-unauthenticated*'
        $agents | Should -BeLike '*--timeout 3600*'
    }

    It 'deploys the web app publicly, pointing at the API' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $web = $calls | Where-Object { $_ -like 'run deploy kairo-web *' }
        $web | Should -BeLike '*--no-invoker-iam-check*'
        $web | Should -BeLike '*--min-instances=0*'
        $web | Should -BeLike '*--set-env-vars=KAIRO_API_URL=https://kairo-api-123.asia-south1.run.app,KAIRO_API_AUDIENCE=https://kairo-api-123.asia-south1.run.app*'
    }

    It 'lets only the right callers invoke the private services' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $grants = $calls | Where-Object { $_ -like 'run services add-iam-policy-binding*' }
        $grants | Should -HaveCount 3
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-api --region asia-south1 --project kairo-test --member serviceAccount:kairo-web@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
        $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-worker@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
    }

    It 'returns the service addresses' {
        $urls = Invoke-KairoDeploy -Environment $environment -Root $root

        $urls['kairo-web'] | Should -Be 'https://kairo-web-123.asia-south1.run.app'
        $urls['kairo-api'] | Should -Be 'https://kairo-api-123.asia-south1.run.app'
    }
}

Describe 'Invoke-KairoTests' {
    It 'runs every suite and stops at the first failure' {
        $script:ran = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName Kairo.Deploy Invoke-Native {
            $script:ran.Add("$FilePath $($ArgumentList -join ' ')")
            if ($FilePath -eq 'uv' -and $ArgumentList -contains 'pytest') { throw 'pytest failed' }
        }

        { Invoke-KairoTests -Root $root } | Should -Throw 'pytest failed'

        $ran[0] | Should -Be "dotnet test --solution $(Join-Path $root 'core/Kairo.slnx')"
        $ran[1] | Should -Be "uv run --directory $(Join-Path $root 'agents') ruff check"
        $ran[2] | Should -Be "uv run --directory $(Join-Path $root 'agents') ruff format --check"
        $ran[3] | Should -Be "uv run --directory $(Join-Path $root 'agents') pytest"
        $ran | Should -HaveCount 4
    }

    It 'runs the web lint and tests' {
        $script:ran = [System.Collections.Generic.List[string]]::new()
        Mock -ModuleName Kairo.Deploy Invoke-Native { $script:ran.Add("$FilePath $($ArgumentList -join ' ')") }

        Invoke-KairoTests -Root $root

        $ran | Should -Contain "npm --prefix $(Join-Path $root 'web') run lint"
        $ran | Should -Contain "npm --prefix $(Join-Path $root 'web') test"
    }
}

Describe 'Assert-KairoCleanTree' {
    It 'refuses uncommitted changes' {
        Mock -ModuleName Kairo.Deploy Invoke-Native { ' M web/src/app/page.tsx' }

        { Assert-KairoCleanTree -Root $root } | Should -Throw 'Commit or stash your changes first*'
    }

    It 'accepts a clean tree' {
        Mock -ModuleName Kairo.Deploy Invoke-Native { }

        { Assert-KairoCleanTree -Root $root } | Should -Not -Throw
    }
}
```

- [ ] **Step 2: Run and see them fail**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests -Output Detailed"`
Expected: the Release tests fail, `Invoke-KairoDeploy` is not recognized.

- [ ] **Step 3: Write `Release.ps1`**

```powershell
$script:Vpc = @('--network', 'kairo', '--subnet', 'kairo-run', '--vpc-egress', 'private-ranges-only')

function Invoke-KairoDeploy {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Root,
        [switch]$SkipTests
    )

    Assert-KairoCleanTree -Root $Root
    $tag = Get-KairoCommit -Root $Root

    if ($SkipTests) {
        Write-Warning 'Skipping the tests.'
    }
    else {
        Invoke-KairoTests -Root $Root
    }

    $number = Get-KairoProjectNumber -Environment $Environment
    $urls = @{}
    foreach ($service in 'kairo-web', 'kairo-api', 'kairo-agents', 'kairo-worker') {
        $urls[$service] = Get-KairoServiceUrl -Environment $Environment -ProjectNumber $number -Service $service
    }

    Invoke-KairoBuild -Environment $Environment -Root $Root -Tag $tag
    Invoke-KairoMigration -Environment $Environment -Tag $tag

    $sqlSecret = "ConnectionStrings__kairo=$($script:SqlSecret):latest"
    $apiSecrets = @($sqlSecret)
    $googleConfigured = $true
    foreach ($secret in $script:GoogleSecrets.Values) {
        if (-not (Test-KairoSecretHasValue -Environment $Environment -Name $secret)) {
            $googleConfigured = $false
        }
    }
    if ($googleConfigured) {
        foreach ($entry in $script:GoogleSecrets.GetEnumerator()) {
            $apiSecrets += "$($entry.Key)=$($entry.Value):latest"
        }
    }

    Publish-KairoService -Environment $Environment -Service 'kairo-agents' -Image (Get-KairoImage -Environment $Environment -Name 'agents' -Tag $tag) `
        -Settings @('--no-allow-unauthenticated', '--timeout', '3600')

    Publish-KairoService -Environment $Environment -Service 'kairo-api' -Image (Get-KairoImage -Environment $Environment -Name 'api' -Tag $tag) `
        -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @(
            '--session-affinity',
            "--min-instances=$($Environment.MinInstances.api)",
            "--set-env-vars=Kairo__PublicOrigin=$($urls['kairo-web'])",
            "--set-secrets=$($apiSecrets -join ',')"
        ))

    Publish-KairoService -Environment $Environment -Service 'kairo-worker' -Image (Get-KairoImage -Environment $Environment -Name 'worker' -Tag $tag) `
        -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @("--set-secrets=$sqlSecret"))

    Publish-KairoService -Environment $Environment -Service 'kairo-web' -Image (Get-KairoImage -Environment $Environment -Name 'web' -Tag $tag) `
        -Settings @(
            '--no-invoker-iam-check',
            "--min-instances=$($Environment.MinInstances.web)",
            "--set-env-vars=KAIRO_API_URL=$($urls['kairo-api']),KAIRO_API_AUDIENCE=$($urls['kairo-api'])"
        )

    Grant-KairoInvoker -Environment $Environment -Service 'kairo-api' -Caller 'kairo-web'
    Grant-KairoInvoker -Environment $Environment -Service 'kairo-agents' -Caller 'kairo-api'
    Grant-KairoInvoker -Environment $Environment -Service 'kairo-agents' -Caller 'kairo-worker'

    Write-Host ''
    Write-Host "Kairo $tag is live in $($Environment.Name) at $($urls['kairo-web'])"
    Write-Host "Google sign-in redirect URI: $($urls['kairo-web'])/api/auth/callback/google"
    if (-not $googleConfigured) {
        Write-Warning 'Google sign-in is off until google-client-id and google-client-secret have a value. See "Deploy" in README.md.'
    }

    $urls
}

function Assert-KairoCleanTree {
    param([Parameter(Mandatory)][string]$Root)

    $changes = Invoke-Native -FilePath 'git' -ArgumentList @('-C', $Root, 'status', '--porcelain') -Capture
    if ($changes) {
        throw "Commit or stash your changes first, so the image tag names exactly what is deployed:`n$($changes -join "`n")"
    }
}

function Get-KairoCommit {
    param([Parameter(Mandatory)][string]$Root)

    ((Invoke-Native -FilePath 'git' -ArgumentList @('-C', $Root, 'rev-parse', '--short=12', 'HEAD') -Capture) | Select-Object -First 1).Trim()
}

function Invoke-KairoTests {
    param([Parameter(Mandatory)][string]$Root)

    $agents = Join-Path $Root 'agents'
    $web = Join-Path $Root 'web'
    Invoke-Native -FilePath 'dotnet' -ArgumentList @('test', '--solution', (Join-Path $Root 'core/Kairo.slnx'))
    Invoke-Native -FilePath 'uv' -ArgumentList @('run', '--directory', $agents, 'ruff', 'check')
    Invoke-Native -FilePath 'uv' -ArgumentList @('run', '--directory', $agents, 'ruff', 'format', '--check')
    Invoke-Native -FilePath 'uv' -ArgumentList @('run', '--directory', $agents, 'pytest')
    Invoke-Native -FilePath 'npm' -ArgumentList @('--prefix', $web, 'run', 'lint')
    Invoke-Native -FilePath 'npm' -ArgumentList @('--prefix', $web, 'test')
}

function Get-KairoProjectNumber {
    param([Parameter(Mandatory)][hashtable]$Environment)

    ((Invoke-Gcloud -ArgumentList @('projects', 'describe', $Environment.Project, '--format=value(projectNumber)') -Capture) | Select-Object -First 1).Trim()
}

function Invoke-KairoBuild {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Tag
    )

    Write-Host "Building images for $Tag"
    Invoke-Gcloud -ArgumentList @(
        'builds', 'submit', $Root,
        '--config', (Join-Path $Root 'deploy/cloudbuild.yaml'),
        '--region', $Environment.Region,
        '--project', $Environment.Project,
        '--service-account', "projects/$($Environment.Project)/serviceAccounts/$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-build')",
        '--gcs-source-staging-dir', "gs://$($Environment.Project)-builds/source",
        "--substitutions=_REGISTRY=$(Get-KairoRegistry -Environment $Environment),_TAG=$Tag"
    )
}

function Invoke-KairoMigration {
    param([Parameter(Mandatory)][hashtable]$Environment, [Parameter(Mandatory)][string]$Tag)

    $scope = @('--region', $Environment.Region, '--project', $Environment.Project)
    Write-Host 'Applying database migrations'
    Invoke-Gcloud -ArgumentList (@(
            'run', 'jobs', 'deploy', 'kairo-migrator',
            '--image', (Get-KairoImage -Environment $Environment -Name 'migrator' -Tag $Tag),
            '--service-account', (Get-KairoServiceAccount -Environment $Environment -Name 'kairo-migrator')
        ) + $scope + $script:Vpc + @(
            "--set-secrets=ConnectionStrings__kairo=$($script:SqlSecret):latest",
            '--max-retries', '0',
            '--task-timeout', '900'
        ))
    Invoke-Gcloud -ArgumentList (@('run', 'jobs', 'execute', 'kairo-migrator') + $scope + @('--wait'))
}

function Publish-KairoService {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Service,
        [Parameter(Mandatory)][string]$Image,
        [string[]]$Settings = @()
    )

    Write-Host "Deploying $Service"
    Invoke-Gcloud -ArgumentList (@(
            'run', 'deploy', $Service,
            '--image', $Image,
            '--service-account', (Get-KairoServiceAccount -Environment $Environment -Name $Service),
            '--region', $Environment.Region,
            '--project', $Environment.Project
        ) + $Settings)
}

function Grant-KairoInvoker {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$Service,
        [Parameter(Mandatory)][string]$Caller
    )

    Invoke-Gcloud -ArgumentList @(
        'run', 'services', 'add-iam-policy-binding', $Service,
        '--region', $Environment.Region,
        '--project', $Environment.Project,
        '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $Caller)",
        '--role', 'roles/run.invoker'
    )
}
```

`deploy/deploy.ps1`:

```powershell
param(
    [Parameter(Mandatory, Position = 0)][string]$Environment,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Kairo.Deploy') -Force
Invoke-KairoDeploy -Environment (Get-KairoEnvironment -Name $Environment) -Root (Split-Path $PSScriptRoot -Parent) -SkipTests:$SkipTests | Out-Null
```

`deploy.bat` (CRLF line endings come from `.gitattributes`):

```bat
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\deploy.ps1" %*
exit /b %ERRORLEVEL%
```

`deploy/cloudbuild.yaml`:

```yaml
steps:
  - id: api
    name: gcr.io/cloud-builders/docker
    args: [build, -f, core/src/Kairo.Api/Dockerfile, -t, "${_REGISTRY}/api:${_TAG}", core]
    waitFor: ["-"]
  - id: worker
    name: gcr.io/cloud-builders/docker
    args: [build, -f, core/src/Kairo.Worker/Dockerfile, -t, "${_REGISTRY}/worker:${_TAG}", core]
    waitFor: ["-"]
  - id: migrator
    name: gcr.io/cloud-builders/docker
    args: [build, -f, core/src/Kairo.Migrator/Dockerfile, -t, "${_REGISTRY}/migrator:${_TAG}", core]
    waitFor: ["-"]
  - id: agents
    name: gcr.io/cloud-builders/docker
    args: [build, -t, "${_REGISTRY}/agents:${_TAG}", agents]
    waitFor: ["-"]
  - id: web
    name: gcr.io/cloud-builders/docker
    args: [build, -t, "${_REGISTRY}/web:${_TAG}", web]
    waitFor: ["-"]
images:
  - "${_REGISTRY}/api:${_TAG}"
  - "${_REGISTRY}/worker:${_TAG}"
  - "${_REGISTRY}/migrator:${_TAG}"
  - "${_REGISTRY}/agents:${_TAG}"
  - "${_REGISTRY}/web:${_TAG}"
substitutions:
  _REGISTRY: ""
  _TAG: ""
options:
  logging: CLOUD_LOGGING_ONLY
  machineType: E2_HIGHCPU_8
timeout: 3600s
```

`.gcloudignore`:

```
.git/
.github/
.claude/
.superpowers/
docs/
TestResults/
**/bin/
**/obj/
**/TestResults/
**/node_modules/
**/.next/
**/.venv/
**/__pycache__/
**/.pytest_cache/
**/.ruff_cache/
web/tsconfig.tsbuildinfo
```

`core/src/Kairo.Migrator/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Kairo.Migrator/Kairo.Migrator.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Kairo.Migrator.dll"]
```

- [ ] **Step 4: Run the tests**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests -Output Detailed"`
Expected: all pass.

- [ ] **Step 5: Check the line endings and the upload list**

Run: `git add -A deploy deploy.bat .gcloudignore core/src/Kairo.Migrator/Dockerfile && git check-attr eol -- deploy.bat` and `gcloud meta list-files-for-upload . | grep -c -E "node_modules|/bin/|/obj/|\.venv"`
Expected: `deploy.bat: eol: crlf`, and a count of `0`.

- [ ] **Step 6: Commit**

```bash
git commit -m "Test, build, migrate and deploy Kairo to Cloud Run with deploy.bat"
```

---

### Task 7: CI and documentation

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `docs/Kairo-SAD.md` (§9 Delivery bullet)

- [ ] **Step 1: Add a CI job for the deployment tests**

Append to `jobs:` in `.github/workflows/ci.yml`:

```yaml
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - shell: pwsh
        run: |
          Install-Module Pester -RequiredVersion 5.9.1 -Scope CurrentUser -Force -SkipPublisherCheck
          Import-Module Pester -RequiredVersion 5.9.1
          Invoke-Pester -Path deploy/tests -CI -Output Detailed
```

On Linux, `Join-Path $root 'core/Kairo.slnx'` with `$root = 'C:\src\Kairo'` gives `C:\src\Kairo/core/Kairo.slnx`; the tests compute their expectations with the same `Join-Path`, so they hold on both systems. Run the Pester suite under `pwsh` once locally if available; otherwise CI is the check.

- [ ] **Step 2: Document deploying**

In `README.md`:
- Add `deploy/` to the repository layout table: "`deploy.bat`, the deployment scripts and their tests".
- Add a row to the "Run the tests" table: Deployment scripts → `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`, with a line that Pester 5.9.1 installs with `Install-Module Pester -RequiredVersion 5.9.1 -Scope CurrentUser -SkipPublisherCheck`.
- Add the Migrator to "Build the container images": `docker build -f core/src/Kairo.Migrator/Dockerfile -t kairo-migrator core`.
- Add a "Deploy" section after "Build the container images":

````markdown
## Deploy

Kairo deploys to Google Cloud with `deploy.bat`. Each environment (development, staging, production) maps to a Google Cloud project in `deploy/environments.psd1`. Only development, the `kairo2` project, is set up.

You need the gcloud CLI, signed in with an account that owns the project.

1. **Set up the project, once.** `powershell -ExecutionPolicy Bypass -File deploy/setup.ps1 development` enables the APIs and creates the network, Cloud SQL, secrets and service accounts. It takes about fifteen minutes the first time and is safe to run again.
2. **Deploy.** Commit your changes, then run `deploy.bat development`. It runs the tests, builds every image with Cloud Build, applies database migrations as a Cloud Run job, deploys the four services and prints the web app's address. Add `-SkipTests` to skip the tests.
3. **Turn on Google sign-in, once per project.** In the Cloud Console, add the redirect URI that `deploy.bat` prints to the project's web OAuth client. Then store the client ID and secret, and deploy again:

   ```bash
   printf '%s' '<client id>' | gcloud secrets versions add google-client-id --data-file=- --project kairo2
   printf '%s' '<client secret>' | gcloud secrets versions add google-client-secret --data-file=- --project kairo2
   ```

In development there is no load balancer: the web app forwards `/api/*` to the core itself, sending a Google identity token. Only the web app is public; the API, worker and agent service accept calls only from Kairo's own service accounts.
````

- [ ] **Step 3: Update the SAD**

In `docs/Kairo-SAD.md` §9, replace the **Delivery** bullet with:

```markdown
- **Delivery.** `deploy.bat` deploys Kairo with the gcloud CLI, taking the target environment (development, staging or production) as an argument; `deploy/environments.psd1` maps each to its project. It runs the tests, builds each service's container image with Cloud Build into Artifact Registry, runs the Migrator as a Cloud Run job inside the VPC to apply EF Core migrations, then deploys the services to Cloud Run. Shared resources such as the network, database, secrets and service accounts are created once per project by `deploy/setup.ps1`. The development project has no load balancer: the web app forwards `/api/*` to the core with a Google identity token.
```

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml README.md docs/Kairo-SAD.md
git commit -m "Document deploying, and run the deployment tests in CI"
```

---

### Task 8: Deploy to kairo2 for real

This task changes a real Google Cloud project and costs money. It runs in the main session with the product owner, not in a subagent.

- [ ] **Step 1: Run every suite**

Run: `dotnet test --solution core/Kairo.slnx` (Docker running), the agent and web commands from the README, and the Pester suite.
Expected: all pass.

- [ ] **Step 2: Set up `kairo2`**

Run in PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File deploy/setup.ps1 development`, with gcloud using `evolixgcpadmin@gmail.com` (`$env:CLOUDSDK_CORE_ACCOUNT = 'evolixgcpadmin@gmail.com'`).
Expected: ends with "development is set up." Run it again: it creates nothing.

If an organization policy or permission blocks a step, stop, fix the script with a test that pins the fix, and rerun.

- [ ] **Step 3: Deploy**

Run: `deploy.bat development` (from PowerShell: `cmd /c deploy.bat development`).
Expected: the build, migration and four deployments succeed, and it prints the web URL and the redirect URI.

- [ ] **Step 4: Check the deployed app**

```powershell
$web = 'https://kairo-web-<number>.asia-south1.run.app'
(Invoke-WebRequest "$web/health" -UseBasicParsing).Content
try { Invoke-WebRequest "$web/api/me" -UseBasicParsing -Headers @{ 'X-Kairo-Request' = '1' } } catch { $_.Exception.Response.StatusCode.value__ }
$api = 'https://kairo-api-<number>.asia-south1.run.app'
(Invoke-WebRequest "$api/health" -UseBasicParsing -Headers @{ Authorization = "Bearer $(gcloud auth print-identity-token)" }).Content
try { Invoke-WebRequest "$api/health" -UseBasicParsing } catch { $_.Exception.Response.StatusCode.value__ }
```

Expected: `{"status":"ok"}`; `401` (from the core, through the web app); `Healthy`; `403` (the API refuses calls without a token).

- [ ] **Step 5: Turn on Google sign-in with the product owner**

Ask the product owner to add the printed redirect URI to the web OAuth client in `kairo2` (creating the client if it doesn't exist), and to store its ID and secret with the README's two commands. Then rerun `deploy.bat development -SkipTests`, open the web URL in a browser, sign in with Google, and create a workspace.
Expected: sign-in lands on the welcome page, and creating a workspace opens its Today page.

- [ ] **Step 6: Commit any fixes**

Every fix found here gets a test and its own commit.
