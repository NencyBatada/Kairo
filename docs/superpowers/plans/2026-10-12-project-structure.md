# Kairo Project Structure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the empty Kairo repository into a runnable skeleton of the web app, the .NET core (API and worker with 14 modules) and the Python agent service. Each part builds, runs, answers a health check and passes its tests.

**Architecture:** One repository with three top-level parts: `core/`, `agents/` and `web/`.
- **Core.** A .NET 10 modular monolith with one project per module. Each module exposes a single public `IModule` class. Two thin ASP.NET Core hosts (API and worker) register every module. Architecture tests enforce the module boundaries.
- **Local development.** An Aspire AppHost starts everything with one command.
- **Delivery.** There's a Dockerfile per deployable and a GitHub Actions workflow that runs every test suite.

**Tech Stack:**
- Core: .NET SDK 10.0.400, ASP.NET Core 10, Aspire 13.6.1, xUnit v3 on Microsoft Testing Platform
- Agent service: Python 3.13 with uv 0.12, FastAPI, ADK 2.11, Gen AI SDK
- Web app: Node 24 with npm, Next.js 16.4, React 19.3, Tailwind 4, Vitest 5

**Spec:** `docs/superpowers/specs/2026-10-12-project-structure-design.md`

## Global Constraints

- **Shell and working directory.** Run every command in Git Bash (the Bash tool) from the repository root `/c/Kairo`, unless a step says otherwise. Paths in this plan are relative to the root.
- **Commits.** Never add `Co-Authored-By` or any AI attribution to commits. The author is the configured git user.
- **No comments in code** (`CLAUDE.md` §3). Files this plan creates contain no comments of any kind: no `//`, `#`, `/* */` or `<!-- -->` comments, no C# XML doc comments, and no Python docstrings. Explanations belong in the README, the spec and the SAD.
- **Files to leave alone.** Never stage `CLAUDE.md` or `.claude/`; they are deliberately untracked. Stage files by explicit path, never with `git add -A` or `git add .`.
- **Branch.** Work on a branch named `feature/project-structure` created from `develop`, or in a worktree on that branch.
- **Versions.**
  - .NET SDK `10.0.400` with roll-forward `latestFeature`; target framework `net10.0`
  - Aspire `13.6.1`
  - Python `>=3.13,<3.14`; uv `0.12.x`
  - Node `24`; Next.js `16.4.0`
- **Names.**
  - Solution: `core/Kairo.slnx`
  - Projects: `Kairo.<Name>`; modules are `Kairo.Modules.<Name>` with class `<Name>Module`
  - Python package: `kairo_agents`; project name `kairo-agents`
  - npm package: `kairo-web`
- **Health endpoints.**
  - .NET hosts: `/alive` (liveness) and `/health` (all checks). Responses contain only the status word.
  - Agent service and web app: `GET /health` returns `{"status":"ok"}`.
- **Port.** Every container image serves HTTP on port 8080.
- **Core build settings.** Warnings are errors, except NuGet audit warnings NU1901–NU1904.
- **Out of scope.** No business logic, database tables, auth, SignalR hub, inter-service calls, Python or web OpenTelemetry, Google API integrations, measurement library, or `deploy.bat` (spec, "Out of scope").

## Review Focus

These are the failure modes the spec implies that are most likely to bite. Each one is pinned by a test or check in the task named in brackets.

1. **Database or Redis outage under Cloud Run probes.** `/alive` must stay 200 `Healthy` when SQL Server and Redis are unreachable. Otherwise Cloud Run restarts healthy instances during an outage. (Task 3: the host tests point connection strings at a closed port.)
2. **Leaking details from `/health` when a dependency is down.** It must return 503 with only `Unhealthy`, with no exception text or connection strings. (Task 3: `Health_endpoint_reports_only_the_status_when_dependencies_are_down`.)
3. **A campaign room ID containing URL characters such as `/` or `?`.** Tab links must stay inside that room. (Task 6: `encodes room ids that contain URL characters`.)
4. **Module wiring.** Any of these must fail a test that names the module:
   - a new module folder that isn't registered in both hosts
   - a module that makes a second type public
   - a module that references another module

   (Task 2: architecture tests with a deliberate violation. Task 3: host registration tests with a deliberate omission.)
5. **Cloud Run starting a container with a `PORT` other than 8080.** The Python and Node images must listen on `$PORT`. (Task 8: run each image with `-e PORT=9090` and request `/health`.)

---

### Task 1: Repository basics

**Files:**
- Create: `.gitattributes`
- Create: `.editorconfig`
- Create: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: LF line endings for every text file except `.bat` and `.cmd` (CRLF). It also ignores build output (`bin/`, `obj/`, `TestResults/`, `node_modules/`, `.venv/` and similar), which later tasks rely on to keep `git status` clean.

- [ ] **Step 1: Create the branch**

Skip this if you're already on `feature/project-structure`, for example in a worktree.
```bash
git switch develop
git switch -c feature/project-structure
```

- [ ] **Step 2: Write `.gitattributes`**

```gitattributes
* text=auto eol=lf
*.bat text eol=crlf
*.cmd text eol=crlf
```

- [ ] **Step 3: Write `.editorconfig`**

```editorconfig
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4

[*.{ts,tsx,js,mjs,cjs,json,jsonc,css,yml,yaml,md}]
indent_size = 2

[*.{csproj,props,targets,slnx,xml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.{bat,cmd}]
end_of_line = crlf

[*.cs]
csharp_style_namespace_declarations = file_scoped:suggestion
dotnet_sort_system_directives_first = true
```

- [ ] **Step 4: Write `.gitignore`**

```gitignore
bin/
obj/
*.user
.vs/
TestResults/

node_modules/

.venv/
__pycache__/
*.py[cod]
.pytest_cache/
.ruff_cache/

.env
.env.*

.idea/
.DS_Store
Thumbs.db
```

- [ ] **Step 5: Verify the rules**

Run:
```bash
git check-ignore -v core/src/X/bin/a.dll agents/.venv/x web/node_modules/x TestResults/x .env.local
git check-attr eol -- deploy.bat README.md
git status --short
```
Expected:
- `check-ignore` prints one matching rule for each of the five paths.
- `check-attr` prints `deploy.bat: eol: crlf` and `README.md: eol: lf`.
- `git status --short` lists only `.editorconfig`, `.gitattributes` and `.gitignore`. `CLAUDE.md` and `.claude/` must not appear.

- [ ] **Step 6: Commit**

```bash
git add .gitattributes .editorconfig .gitignore
git commit -m "Add repository line-ending, editor and ignore rules"
```

---

### Task 2: Core solution and module boundaries

**Files:**
- Create: `global.json`
- Create: `core/Directory.Build.props`, `core/Directory.Packages.props`, `core/Kairo.slnx`
- Create: `core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj`, `core/src/Kairo.SharedKernel/IModule.cs`
- Create: `core/src/Kairo.Contracts/Kairo.Contracts.csproj`
- Create: `core/src/Modules/Directory.Build.props`
- Create: `core/src/Modules/Kairo.Modules.<Name>/Kairo.Modules.<Name>.csproj` and `<Name>Module.cs` for the 14 modules (generated in Step 9 by a temporary script that isn't committed)
- Create: `core/tests/Directory.Build.props`, `core/tests/Shared/ModuleFolders.cs`
- Test: `core/tests/Kairo.ArchitectureTests/Kairo.ArchitectureTests.csproj`, `core/tests/Kairo.ArchitectureTests/ModuleRulesTests.cs`

**Interfaces:**
- Consumes: Task 1's ignore rules.
- Produces:
  - `Kairo.SharedKernel.IModule`:
    - `void AddServices(IServiceCollection services, IConfiguration configuration)`
    - `void MapApi(IEndpointRouteBuilder api)`
    - `void MapJobs(IEndpointRouteBuilder jobs)`
  - 14 public sealed classes `Kairo.Modules.<Name>.<Name>Module : IModule`, where `<Name>` is one of: Identity, Brands, BrandKit, Catalogue, Opportunities, Triggers, CampaignRooms, Approvals, Launch, Care, Measurement, BrandMemory, Notifications, Audit.
  - `Kairo.Testing.ModuleFolders.Names` (`IReadOnlyList<string>`, internal, compiled into every test project): the module assembly names found under `core/src/Modules`, in ordinal order.
  - `core/tests/Directory.Build.props`, which gives every test project xUnit v3 (MTP v2), `OutputType Exe`, `using Xunit;` and the shared helper.
  - Central package versions for every package used by later tasks.

- [ ] **Step 1: Write `global.json` (repository root)**

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

- [ ] **Step 2: Write `core/Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write `core/Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup Label="Aspire">
    <PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.6.1" />
    <PackageVersion Include="Aspire.Hosting.Python" Version="13.6.1" />
    <PackageVersion Include="Aspire.Hosting.Redis" Version="13.6.1" />
    <PackageVersion Include="Aspire.Hosting.SqlServer" Version="13.6.1" />
    <PackageVersion Include="Aspire.Microsoft.Data.SqlClient" Version="13.6.1" />
    <PackageVersion Include="Aspire.StackExchange.Redis" Version="13.6.1" />
  </ItemGroup>

  <ItemGroup Label="Service defaults">
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.10.0" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="10.10.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.19.1" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.19.1" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.19.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.19.0" />
  </ItemGroup>

  <ItemGroup Label="Tests">
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="xunit.v3.mtp-v2" Version="4.0.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Write the shared kernel**

`core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

`core/src/Kairo.SharedKernel/IModule.cs`:
```csharp
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.SharedKernel;

public interface IModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapApi(IEndpointRouteBuilder api);

    void MapJobs(IEndpointRouteBuilder jobs);
}
```

- [ ] **Step 5: Write the contracts project and the shared module settings**

`core/src/Kairo.Contracts/Kairo.Contracts.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

`core/src/Modules/Directory.Build.props`:
```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="$(MSBuildThisFileDirectory)../Kairo.SharedKernel/Kairo.SharedKernel.csproj" />
    <ProjectReference Include="$(MSBuildThisFileDirectory)../Kairo.Contracts/Kairo.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Kairo.SharedKernel" />
    <Using Include="Microsoft.AspNetCore.Routing" />
    <Using Include="Microsoft.Extensions.Configuration" />
    <Using Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Write the shared test settings and helper**

`core/tests/Directory.Build.props`:
```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" />
    <Using Include="Xunit" />
    <Compile Include="$(MSBuildThisFileDirectory)Shared/**/*.cs" LinkBase="Shared" />
  </ItemGroup>
</Project>
```

`core/tests/Shared/ModuleFolders.cs`:
```csharp
namespace Kairo.Testing;

internal static class ModuleFolders
{
    public static IReadOnlyList<string> Names { get; } = Find();

    private static string[] Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kairo.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException($"No Kairo.slnx found above {AppContext.BaseDirectory}.");
        }

        return Directory.GetDirectories(Path.Combine(dir.FullName, "src", "Modules"), "Kairo.Modules.*")
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
```

- [ ] **Step 7: Write the failing architecture tests**

`core/tests/Kairo.ArchitectureTests/Kairo.ArchitectureTests.csproj` (forward slashes keep the wildcard working on Linux):
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Contracts\Kairo.Contracts.csproj" />
    <ProjectReference Include="../../src/Modules/*/Kairo.Modules.*.csproj" />
  </ItemGroup>

</Project>
```

`core/tests/Kairo.ArchitectureTests/ModuleRulesTests.cs`:
```csharp
using System.Reflection;
using Kairo.SharedKernel;
using Kairo.Testing;

namespace Kairo.ArchitectureTests;

public sealed class ModuleRulesTests
{
    public static TheoryData<string> Modules => new(ModuleFolders.Names);

    [Fact]
    public void Kairo_has_modules()
    {
        Assert.NotEmpty(ModuleFolders.Names);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_exposes_only_its_module_class(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var moduleName = assemblyName["Kairo.Modules.".Length..];

        var type = Assert.Single(assembly.GetExportedTypes());

        Assert.Equal($"{assemblyName}.{moduleName}Module", type.FullName);
        Assert.True(type.IsAssignableTo(typeof(IModule)), $"{type.FullName} must implement IModule.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_references_only_SharedKernel_and_Contracts(string assemblyName)
    {
        var references = KairoReferencesOf(Assembly.Load(assemblyName));

        Assert.Subset(new HashSet<string> { "Kairo.SharedKernel", "Kairo.Contracts" }, references);
    }

    [Fact]
    public void Contracts_references_only_SharedKernel()
    {
        var references = KairoReferencesOf(Assembly.Load("Kairo.Contracts"));

        Assert.Subset(new HashSet<string> { "Kairo.SharedKernel" }, references);
    }

    [Fact]
    public void SharedKernel_references_no_other_Kairo_assembly()
    {
        Assert.Empty(KairoReferencesOf(typeof(IModule).Assembly));
    }

    private static HashSet<string> KairoReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Kairo.", StringComparison.Ordinal))
            .ToHashSet();
}
```

Create the solution and run the tests:
```bash
cd core
dotnet new sln -n Kairo
dotnet sln Kairo.slnx add src/Kairo.SharedKernel/Kairo.SharedKernel.csproj src/Kairo.Contracts/Kairo.Contracts.csproj tests/Kairo.ArchitectureTests/Kairo.ArchitectureTests.csproj
cd ..
dotnet test --solution core/Kairo.slnx
```
Expected: FAIL with `total: 5, failed: 3, succeeded: 2`.
- `Kairo_has_modules` fails with `Assert.NotEmpty() Failure: Collection was empty`.
- Both theories fail with `No data found`.

`dotnet new sln` creates `Kairo.slnx` on .NET 10.

- [ ] **Step 8: Write the temporary module generator**

Save this as `core/new-modules.sh`. It writes 14 near-identical modules, one for each module in SAD §3, and is deleted after use. Existing modules are left untouched.
```bash
#!/usr/bin/env bash
set -euo pipefail

for name in Identity Brands BrandKit Catalogue Opportunities Triggers CampaignRooms Approvals Launch Care Measurement BrandMemory Notifications Audit; do
  dir="src/Modules/Kairo.Modules.$name"
  if [[ -d "$dir" ]]; then continue; fi
  mkdir -p "$dir"
  printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$dir/Kairo.Modules.$name.csproj"
  cat > "$dir/${name}Module.cs" <<EOF
namespace Kairo.Modules.$name;

public sealed class ${name}Module : IModule
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
}
EOF
done
```

- [ ] **Step 9: Generate the modules and run the tests**

```bash
cd core
bash new-modules.sh
rm new-modules.sh
dotnet sln Kairo.slnx add src/Modules/*/*.csproj
cd ..
dotnet test --solution core/Kairo.slnx
```
Expected: PASS with `total: 31, failed: 0, succeeded: 31`.

Spot-check one file. `core/src/Modules/Kairo.Modules.Launch/LaunchModule.cs` must be a `public sealed class LaunchModule : IModule` with three empty methods.

- [ ] **Step 10: Commit**

```bash
git add global.json core/Directory.Build.props core/Directory.Packages.props core/Kairo.slnx core/src/Kairo.SharedKernel core/src/Kairo.Contracts core/src/Modules core/tests/Directory.Build.props core/tests/Shared core/tests/Kairo.ArchitectureTests
git status --short   # expect no output: build output is ignored and CLAUDE.md/.claude are excluded
git commit -m "Add core solution with 14 modules and module boundary tests"
```

- [ ] **Step 11: Prove the boundary rule catches a violation, then restore**

Temporarily make Launch depend on Catalogue:
```bash
printf '<Project Sdk="Microsoft.NET.Sdk">\n  <ItemGroup>\n    <ProjectReference Include="../Kairo.Modules.Catalogue/Kairo.Modules.Catalogue.csproj" />\n  </ItemGroup>\n</Project>\n' > core/src/Modules/Kairo.Modules.Launch/Kairo.Modules.Launch.csproj
printf 'namespace Kairo.Modules.Launch;\n\ninternal static class Violation\n{\n    internal static readonly Type Probe = typeof(Kairo.Modules.Catalogue.CatalogueModule);\n}\n' > core/src/Modules/Kairo.Modules.Launch/Violation.cs
dotnet test --solution core/Kairo.slnx
```
Expected: FAIL with `failed: 1, succeeded: 30`. The failure is `Module_references_only_SharedKernel_and_Contracts(assemblyName: "Kairo.Modules.Launch")` with `Assert.Subset() Failure`.

Restore the committed state, then confirm the suite is green again:
```bash
rm core/src/Modules/Kairo.Modules.Launch/Violation.cs
git checkout -- core/src/Modules/Kairo.Modules.Launch/Kairo.Modules.Launch.csproj
git status --short   # expect no output
dotnet test --solution core/Kairo.slnx
```
Expected: PASS with `succeeded: 31`.

---

### Task 3: API and worker hosts

**Files:**
- Create: `core/src/Kairo.ServiceDefaults/Kairo.ServiceDefaults.csproj`, `core/src/Kairo.ServiceDefaults/Extensions.cs`
- Create: `core/src/Kairo.Api/Kairo.Api.csproj`, `Program.cs`, `appsettings.json`, `Properties/launchSettings.json`
- Create: `core/src/Kairo.Worker/Kairo.Worker.csproj`, `Program.cs`, `appsettings.json`, `Properties/launchSettings.json`
- Test: `core/tests/Kairo.Api.Tests/Kairo.Api.Tests.csproj`, `core/tests/Kairo.Api.Tests/ApiHostTests.cs`
- Test: `core/tests/Kairo.Worker.Tests/Kairo.Worker.Tests.csproj`, `core/tests/Kairo.Worker.Tests/WorkerHostTests.cs`

**Interfaces:**
- Consumes:
  - `IModule` and the 14 `<Name>Module` classes (Task 2)
  - `Kairo.Testing.ModuleFolders.Names` (Task 2)
  - `core/tests/Directory.Build.props` (Task 2)
- Produces:
  - Extension methods in namespace `Microsoft.Extensions.Hosting`: `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()`. The latter maps `/health` and `/alive` in every environment.
  - Host `Kairo.Api`: connection strings `kairo` (SQL Server) and `redis`; launch profile `http` on `http://localhost:5100`.
  - Host `Kairo.Worker`: connection string `kairo`; launch profile `http` on `http://localhost:5200`.
  - The Aspire AppHost in Task 7 refers to these hosts as `Projects.Kairo_Api` and `Projects.Kairo_Worker`.

- [ ] **Step 1: Write the service defaults**

These come from Aspire 13.6.1's `aspire-servicedefaults` template, with three changes:
- package versions move to central management
- `MapDefaultEndpoints` maps in every environment, so Cloud Run probes can use the endpoints. The default response is only the status word, so they reveal nothing.
- the template's comments are removed

`core/src/Kairo.ServiceDefaults/Kairo.ServiceDefaults.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>

</Project>
```

`core/src/Kairo.ServiceDefaults/Extensions.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
```

- [ ] **Step 2: Write the host tests**

The test factories point the connection strings at port 1 on localhost. Nothing listens there, so SQL Server and Redis are unreachable and their health checks fail fast. The short timeouts and `ConnectRetryCount=0` / `connectRetry=0` keep the `/health` test quick.

`core/tests/Kairo.Api.Tests/Kairo.Api.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.Api\Kairo.Api.csproj" />
  </ItemGroup>

</Project>
```

`core/tests/Kairo.Api.Tests/ApiHostTests.cs`:
```csharp
using System.Net;
using Kairo.SharedKernel;
using Kairo.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Api.Tests;

public sealed class ApiHostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Alive_endpoint_reports_healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_endpoint_reports_only_the_status_when_dependencies_are_down()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Registers_every_module()
    {
        var registered = factory.Services.GetServices<IModule>()
            .Select(module => module.GetType().Assembly.GetName().Name!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ModuleFolders.Names, registered);
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", "Server=127.0.0.1,1;Database=kairo;User Id=test;Password=test;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=true");
        builder.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false,connectRetry=0,connectTimeout=1000");
    }
}
```

`core/tests/Kairo.Worker.Tests/Kairo.Worker.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.Worker\Kairo.Worker.csproj" />
  </ItemGroup>

</Project>
```

`core/tests/Kairo.Worker.Tests/WorkerHostTests.cs`:
```csharp
using System.Net;
using Kairo.SharedKernel;
using Kairo.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Worker.Tests;

public sealed class WorkerHostTests(WorkerFactory factory) : IClassFixture<WorkerFactory>
{
    [Fact]
    public async Task Alive_endpoint_reports_healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_endpoint_reports_only_the_status_when_dependencies_are_down()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Registers_every_module()
    {
        var registered = factory.Services.GetServices<IModule>()
            .Select(module => module.GetType().Assembly.GetName().Name!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ModuleFolders.Names, registered);
    }
}

public sealed class WorkerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", "Server=127.0.0.1,1;Database=kairo;User Id=test;Password=test;Connect Timeout=1;ConnectRetryCount=0;TrustServerCertificate=true");
    }
}
```

- [ ] **Step 3: Create bare hosts and watch the tests fail**

Both hosts get their project file, settings and a `Program.cs` that only starts the app. The tests can then compile and fail on their assertions.

`core/src/Kairo.Api/Kairo.Api.csproj` (forward slashes in the wildcard):
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Aspire.Microsoft.Data.SqlClient" />
    <PackageReference Include="Aspire.StackExchange.Redis" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kairo.ServiceDefaults\Kairo.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="../Modules/*/Kairo.Modules.*.csproj" />
  </ItemGroup>

</Project>
```

`core/src/Kairo.Worker/Kairo.Worker.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Aspire.Microsoft.Data.SqlClient" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kairo.ServiceDefaults\Kairo.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="../Modules/*/Kairo.Modules.*.csproj" />
  </ItemGroup>

</Project>
```

Both `core/src/Kairo.Api/appsettings.json` and `core/src/Kairo.Worker/appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

`core/src/Kairo.Api/Properties/launchSettings.json`:
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5100",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

`core/src/Kairo.Worker/Properties/launchSettings.json`:
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Temporary `Program.cs` for both hosts:
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
```

Register the projects and run:
```bash
cd core
dotnet sln Kairo.slnx add src/Kairo.ServiceDefaults/Kairo.ServiceDefaults.csproj src/Kairo.Api/Kairo.Api.csproj src/Kairo.Worker/Kairo.Worker.csproj tests/Kairo.Api.Tests/Kairo.Api.Tests.csproj tests/Kairo.Worker.Tests/Kairo.Worker.Tests.csproj
cd ..
dotnet test --solution core/Kairo.slnx
```
Expected: FAIL with `failed: 6, succeeded: 31`. All three tests fail in each host: the two health endpoints return 404, and no modules are registered.

- [ ] **Step 4: Write the real hosts**

`core/src/Kairo.Api/Program.cs`:
```csharp
using Kairo.Modules.Approvals;
using Kairo.Modules.Audit;
using Kairo.Modules.BrandKit;
using Kairo.Modules.BrandMemory;
using Kairo.Modules.Brands;
using Kairo.Modules.CampaignRooms;
using Kairo.Modules.Care;
using Kairo.Modules.Catalogue;
using Kairo.Modules.Identity;
using Kairo.Modules.Launch;
using Kairo.Modules.Measurement;
using Kairo.Modules.Notifications;
using Kairo.Modules.Opportunities;
using Kairo.Modules.Triggers;
using Kairo.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerClient("kairo");
builder.AddRedisClient("redis");

IModule[] modules =
[
    new IdentityModule(),
    new BrandsModule(),
    new BrandKitModule(),
    new CatalogueModule(),
    new OpportunitiesModule(),
    new TriggersModule(),
    new CampaignRoomsModule(),
    new ApprovalsModule(),
    new LaunchModule(),
    new CareModule(),
    new MeasurementModule(),
    new BrandMemoryModule(),
    new NotificationsModule(),
    new AuditModule(),
];

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
    builder.Services.AddSingleton(module);
}

var app = builder.Build();

app.MapDefaultEndpoints();

var api = app.MapGroup("/api");
foreach (var module in modules)
{
    module.MapApi(api);
}

app.Run();
```

`core/src/Kairo.Worker/Program.cs`:
```csharp
using Kairo.Modules.Approvals;
using Kairo.Modules.Audit;
using Kairo.Modules.BrandKit;
using Kairo.Modules.BrandMemory;
using Kairo.Modules.Brands;
using Kairo.Modules.CampaignRooms;
using Kairo.Modules.Care;
using Kairo.Modules.Catalogue;
using Kairo.Modules.Identity;
using Kairo.Modules.Launch;
using Kairo.Modules.Measurement;
using Kairo.Modules.Notifications;
using Kairo.Modules.Opportunities;
using Kairo.Modules.Triggers;
using Kairo.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerClient("kairo");

IModule[] modules =
[
    new IdentityModule(),
    new BrandsModule(),
    new BrandKitModule(),
    new CatalogueModule(),
    new OpportunitiesModule(),
    new TriggersModule(),
    new CampaignRoomsModule(),
    new ApprovalsModule(),
    new LaunchModule(),
    new CareModule(),
    new MeasurementModule(),
    new BrandMemoryModule(),
    new NotificationsModule(),
    new AuditModule(),
];

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
    builder.Services.AddSingleton(module);
}

var app = builder.Build();

app.MapDefaultEndpoints();

var jobs = app.MapGroup("/jobs");
foreach (var module in modules)
{
    module.MapJobs(jobs);
}

app.Run();
```

`Program` needs no `public partial class Program` declaration: .NET 10 makes the generated class public.

- [ ] **Step 5: Run the tests**

Run: `dotnet test --solution core/Kairo.slnx`
Expected: PASS with `total: 37, failed: 0, succeeded: 37`.

On Windows this takes about 8 seconds, because a refused localhost connection takes about 2 seconds to fail there.

- [ ] **Step 6: Commit**

```bash
git add core/Kairo.slnx core/src/Kairo.ServiceDefaults core/src/Kairo.Api core/src/Kairo.Worker core/tests/Kairo.Api.Tests core/tests/Kairo.Worker.Tests
git status --short   # expect no output
git commit -m "Add API and worker hosts with health endpoints and module registration"
```

- [ ] **Step 7: Prove the registration test catches a missing module, then restore**

```bash
sed -i '/new AuditModule(),/d; /using Kairo.Modules.Audit;/d' core/src/Kairo.Worker/Program.cs
dotnet test --solution core/Kairo.slnx
```
Expected: FAIL with `failed: 1`. `WorkerHostTests.Registers_every_module` reports `Collections differ` with `Expected: "Kairo.Modules.Audit"`.

```bash
git checkout -- core/src/Kairo.Worker/Program.cs
dotnet test --solution core/Kairo.slnx
```
Expected: PASS with `succeeded: 37`.

---

### Task 4: Agent service

**Files:**
- Create: `agents/pyproject.toml` (then edited by `uv add`), `agents/uv.lock` (generated), `agents/.python-version`, `agents/README.md`
- Create: `agents/src/kairo_agents/__init__.py`, `agents/src/kairo_agents/main.py`
- Create: `agents/src/kairo_agents/agents/__init__.py` and `agents/src/kairo_agents/agents/{sense,plan,create,check,prove,learn,ask}/__init__.py`
- Create: `agents/src/kairo_agents/tools/__init__.py`, `agents/src/kairo_agents/measurement/__init__.py`, `agents/src/kairo_agents/media/__init__.py`
- Test: `agents/tests/test_health.py`, `agents/tests/test_layout.py`

**Interfaces:**
- Consumes: Task 1's ignore rules (`.venv/`, caches).
- Produces:
  - ASGI app `kairo_agents.main:app`, with `GET /health` returning `{"status": "ok"}`. Task 7 (AppHost) and Task 8 (Dockerfile) use this path.
  - Empty packages `kairo_agents.agents.<name>` for the seven agents, plus `kairo_agents.tools`, `kairo_agents.measurement` and `kairo_agents.media`.

- [ ] **Step 1: Write the project files**

`agents/pyproject.toml`:
```toml
[project]
name = "kairo-agents"
version = "0.1.0"
description = "Kairo agent service: the Sense, Plan, Create, Check, Prove and Learn agents, and Ask Kairo."
readme = "README.md"
requires-python = ">=3.13,<3.14"
dependencies = []

[build-system]
requires = ["uv_build>=0.12.5,<0.13.0"]
build-backend = "uv_build"

[tool.ruff]
line-length = 100

[tool.ruff.lint]
select = ["E", "F", "I", "UP", "B"]

[tool.pytest]
testpaths = ["tests"]
```

`agents/.python-version`:
```
3.13
```

`agents/README.md`:
```markdown
# Kairo agent service

The Python service that runs Kairo's agents (Sense, Plan, Create, Check, Prove and Learn) and Ask Kairo, built with Google's Agent Development Kit and FastAPI. The core calls it once per campaign stage; it never writes to Google Ads.

Run it on its own with `uv run uvicorn kairo_agents.main:app --reload`, or start the whole stack as described in the [root README](../README.md).

| Folder | Holds |
| --- | --- |
| `src/kairo_agents/agents/` | One package per agent. When an agent is built, its `agent.py` exposes `root_agent`, so `uv run adk web src/kairo_agents/agents` opens it in ADK's development UI. |
| `src/kairo_agents/tools/` | Typed clients for the core's internal API |
| `src/kairo_agents/measurement/` | Holdout matching, smallest detectable lift and lift estimates |
| `src/kairo_agents/media/` | FFmpeg helpers for video |
```

Create the package marker as an empty file:
```bash
mkdir -p agents/src/kairo_agents
touch agents/src/kairo_agents/__init__.py
```

- [ ] **Step 2: Add dependencies**

uv must be able to build the package, so `src/kairo_agents/__init__.py` from Step 1 has to exist first.
```bash
cd agents
uv add fastapi "uvicorn[standard]" google-adk google-genai
uv add --dev pytest httpx2 ruff
cd ..
```
Expected: `pyproject.toml` gains a `dependencies` list and a `[dependency-groups] dev` list, and `uv.lock` is created.

FastAPI resolves to `>=0.141.1`, because ADK caps it. Use `httpx2` rather than `httpx`: Starlette 1.7's test client warns that `httpx` is deprecated.

- [ ] **Step 3: Write the failing tests**

`agents/tests/test_health.py`:
```python
from fastapi.testclient import TestClient

from kairo_agents.main import app


def test_health_returns_ok() -> None:
    response = TestClient(app).get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
```

`agents/tests/test_layout.py`:
```python
import importlib

import pytest

AGENTS = ["sense", "plan", "create", "check", "prove", "learn", "ask"]


@pytest.mark.parametrize("name", AGENTS)
def test_agent_package_exists(name: str) -> None:
    importlib.import_module(f"kairo_agents.agents.{name}")
```

Run: `uv run --directory agents pytest`
Expected: FAIL with a collection error, `ModuleNotFoundError: No module named 'kairo_agents.main'`.

- [ ] **Step 4: Write the app and packages**

`agents/src/kairo_agents/main.py`:
```python
from fastapi import FastAPI

app = FastAPI(title="Kairo agent service")


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
```

Create the agent, tools, measurement and media packages as empty `__init__.py` files. What each one is for is in SAD §4 and `agents/README.md`.
```bash
mkdir -p agents/src/kairo_agents/agents/{sense,plan,create,check,prove,learn,ask} agents/src/kairo_agents/{tools,measurement,media}
touch agents/src/kairo_agents/agents/__init__.py agents/src/kairo_agents/agents/{sense,plan,create,check,prove,learn,ask}/__init__.py agents/src/kairo_agents/{tools,measurement,media}/__init__.py
find agents/src -name __init__.py | wc -l   # expect 12
```

- [ ] **Step 5: Run the tests and the linters**

```bash
uv run --directory agents pytest
uv run --directory agents ruff check
uv run --directory agents ruff format --check
```
Expected:
- pytest: `8 passed`, with no warnings.
- `ruff check`: `All checks passed!`
- `ruff format --check`: reports every file as already formatted.

- [ ] **Step 6: Commit**

```bash
git add agents/pyproject.toml agents/uv.lock agents/.python-version agents/README.md agents/src agents/tests
git status --short   # expect no output
git commit -m "Add agent service skeleton with health endpoint and agent packages"
```

---

### Task 5: Web app scaffold and shell

**Files:**
- Create (by `create-next-app`): `web/package.json`, `web/package-lock.json`, `web/tsconfig.json`, `web/eslint.config.mjs`, `web/next.config.ts`, `web/.gitignore`, `web/src/app/globals.css`, `web/src/app/favicon.ico`, and template files that are deleted below
- Modify: `web/next.config.ts`, `web/eslint.config.mjs`, `web/package.json`, `web/src/app/globals.css`, `web/src/app/layout.tsx`, `web/src/app/page.tsx`, `web/README.md`
- Create: `web/vitest.config.mts`, `web/vitest.setup.ts`, `web/public/robots.txt`
- Create: `web/src/components/nav-menu.tsx`, `web/src/components/page-placeholder.tsx`, `web/src/app/health/route.ts`, `web/src/app/today/page.tsx`
- Test: `web/src/components/nav-menu.test.tsx`, `web/src/app/health/route.test.ts`

**Interfaces:**
- Consumes: Task 1's ignore rules.
- Produces:
  - `NavMenu()` from `@/components/nav-menu`, a `<nav aria-label="Main">` with the seven area links
  - `PagePlaceholder({ title, description }: { title: string; description: string })` from `@/components/page-placeholder`
  - `GET /health` returning `{"status":"ok"}`
  - `/` redirecting to `/today`
  - npm scripts `dev`, `build`, `start`, `lint` and `test`. Task 7 (AppHost) runs `next dev`, and Task 8 relies on standalone output.

- [ ] **Step 1: Scaffold with create-next-app**

```bash
npx --yes create-next-app@16.4.0 web --ts --tailwind --eslint --app --src-dir --import-alias "@/*" --use-npm --disable-git --no-react-compiler --no-agents-md --no-agent-feedback --yes
rm web/public/*.svg web/src/app/page.tsx
```
Expected: `Success! Created web`.

npm reports `5 high severity vulnerabilities`. All of them are one dev-only chain: `eslint-config-next` → `fast-glob` → `micromatch` → `braces@3.0.3`, and no fixed `braces` release exists. Do **not** run `npm audit fix --force`, which would downgrade Next.js. Confirm production dependencies are clean:
```bash
npm --prefix web audit --omit=dev
```
Expected: `found 0 vulnerabilities`.

- [ ] **Step 2: Configure the project**

```bash
cd web
npm pkg set name=kairo-web
npm pkg set engines.node=">=24"
npm pkg set scripts.test="vitest run"
npm install -D @types/node@^24
npm install -D vitest @vitejs/plugin-react jsdom @testing-library/react @testing-library/dom
cd ..
printf 'User-agent: *\nDisallow: /\n' > web/public/robots.txt
```
Bump `@types/node` first, because Vitest 5 needs Node types 22 or newer and the template pins `^20`. Keep the template's ESLint 9; its Next.js plugins don't yet support ESLint 10.

Replace `web/next.config.ts`. Keep the template's defaults and add standalone output:
```ts
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
```

`web/vitest.config.mts` (Vite 8 resolves tsconfig paths itself, so no plugin is needed):
```ts
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: { tsconfigPaths: true },
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
  },
});
```

`web/vitest.setup.ts`. Testing Library only cleans up automatically when Vitest's globals are enabled, so without this each test sees the previous test's DOM:
```ts
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => {
  cleanup();
});
```

Replace `web/eslint.config.mjs` with the same configuration minus the template's comments:
```js
import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  globalIgnores([".next/**", "out/**", "build/**", "next-env.d.ts"]),
]);

export default eslintConfig;
```

Delete the template's Arial override from the `body` rule in `web/src/app/globals.css`, so the Geist font the layout loads is actually used:
```bash
sed -i '/font-family: Arial, Helvetica, sans-serif;/d' web/src/app/globals.css
grep -c "font-family" web/src/app/globals.css   # expect 0
```

- [ ] **Step 3: Write the failing tests**

`web/src/components/nav-menu.test.tsx`:
```tsx
import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { NavMenu } from "@/components/nav-menu";

test("lists the seven areas in order, each linking to its screen", () => {
  render(<NavMenu />);

  const nav = screen.getByRole("navigation", { name: "Main" });
  const links = within(nav).getAllByRole("link");

  expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Today", "/today"],
    ["Opportunities", "/opportunities"],
    ["Campaigns", "/campaigns"],
    ["Triggers", "/triggers"],
    ["Impact Lab", "/impact-lab"],
    ["Brand Kit", "/brand-kit"],
    ["Settings", "/settings"],
  ]);
});
```

`web/src/app/health/route.test.ts`:
```ts
import { expect, test } from "vitest";

import { GET } from "./route";

test("reports ok", async () => {
  const response = GET();

  expect(response.status).toBe(200);
  expect(await response.json()).toEqual({ status: "ok" });
});
```

Run: `npm --prefix web test`
Expected: FAIL, `Test Files 2 failed (2)`. Both files fail to resolve their imports.

- [ ] **Step 4: Write the shell**

`web/src/components/nav-menu.tsx`:
```tsx
import Link from "next/link";

const areas = [
  { href: "/today", label: "Today" },
  { href: "/opportunities", label: "Opportunities" },
  { href: "/campaigns", label: "Campaigns" },
  { href: "/triggers", label: "Triggers" },
  { href: "/impact-lab", label: "Impact Lab" },
  { href: "/brand-kit", label: "Brand Kit" },
  { href: "/settings", label: "Settings" },
];

export function NavMenu() {
  return (
    <nav aria-label="Main">
      <ul className="space-y-1">
        {areas.map((area) => (
          <li key={area.href}>
            <Link
              href={area.href}
              className="block rounded-md px-3 py-2 text-sm hover:bg-black/5 dark:hover:bg-white/10"
            >
              {area.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
```

`web/src/components/page-placeholder.tsx`:
```tsx
type PagePlaceholderProps = {
  title: string;
  description: string;
};

export function PagePlaceholder({ title, description }: PagePlaceholderProps) {
  return (
    <section>
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="mt-2 max-w-prose text-zinc-600 dark:text-zinc-400">{description}</p>
    </section>
  );
}
```

`web/src/app/health/route.ts`:
```ts
export function GET() {
  return Response.json({ status: "ok" });
}
```

`web/src/app/page.tsx`:
```tsx
import { redirect } from "next/navigation";

export default function Home() {
  redirect("/today");
}
```

`web/src/app/layout.tsx` (replaces the template):
```tsx
import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { NavMenu } from "@/components/nav-menu";

import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Kairo",
  description:
    "From a rising demand signal to a live, on-brand Google Ads campaign, with proof of the lift it caused.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="flex min-h-full">
        <aside className="w-56 shrink-0 border-r border-black/10 p-4 dark:border-white/15">
          <p className="mb-6 px-3 text-lg font-semibold">Kairo</p>
          <NavMenu />
        </aside>
        <main className="flex-1 p-8">{children}</main>
      </body>
    </html>
  );
}
```

`web/src/app/today/page.tsx`. Today ships with the shell, so the `/` redirect lands on a real page:
```tsx
import { PagePlaceholder } from "@/components/page-placeholder";

export default function TodayPage() {
  return (
    <PagePlaceholder
      title="Today"
      description="Approvals waiting, alerts (stock pauses, pacing, disapprovals), top opportunities and live tests."
    />
  );
}
```

`web/README.md` (replaces the template):
```markdown
# Kairo web app

The Next.js app marketers, approvers and admins use. It talks only to the core API.

Run it on its own with `npm run dev`, or start the whole stack as described in the [root README](../README.md).

- Each screen from PRD section 4 is a route under `src/app/`.
- Components used by one area go next to its routes in a private `_components/` folder.
- Shared clients (core API, Google sign-in, SignalR) will go in `src/lib/`.
- Test with `npm test`, lint with `npm run lint`.
```

- [ ] **Step 5: Run tests, lint and build**

```bash
npm --prefix web test
npm --prefix web run lint
npm --prefix web run build
```
Expected:
- Tests: `Test Files 2 passed (2)`, `Tests 2 passed (2)`.
- Lint: prints no problems.
- Build: ends with a route table listing `/`, `/health` and `/today` as static (`○`).

- [ ] **Step 6: Check the redirect and health route on the production server**

The build output is `standalone`, so run the generated server rather than `next start`. Copy the static assets next to it, as the Dockerfile in Task 8 does:
```bash
cp -r web/public web/.next/standalone/ && cp -r web/.next/static web/.next/standalone/.next/
```
Start the server with `run_in_background: true`:
```bash
PORT=3100 node web/.next/standalone/server.js
```
Then wait for it and check:
```bash
until curl -s http://localhost:3100/health >/dev/null; do sleep 1; done
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:3100/
curl -s http://localhost:3100/health; echo
```
Expected: `307 http://localhost:3100/today`, then `{"status":"ok"}`. Stop the background server afterwards. Everything it used is under the ignored `.next/` folder.

- [ ] **Step 7: Commit**

```bash
git add web/package.json web/package-lock.json web/tsconfig.json web/eslint.config.mjs web/next.config.ts web/.gitignore web/vitest.config.mts web/vitest.setup.ts web/README.md web/public/robots.txt web/src
git status --short   # expect no web/ files left; .next/ and node_modules/ are ignored
git commit -m "Add web app scaffold with app shell, navigation and health route"
```

---

### Task 6: Web screens and campaign room tabs

**Files:**
- Create (by a temporary script that isn't committed): `page.tsx` for `opportunities`, `opportunities/[id]`, `campaigns`, `campaigns/[id]`, `campaigns/[id]/{evidence,brief,assets,launch,performance,results}`, `triggers`, `impact-lab`, `brand-kit` and `settings`, all under `web/src/app/`
- Create: `web/src/app/campaigns/[id]/layout.tsx`, `web/src/app/campaigns/[id]/_components/campaign-room-tabs.tsx`
- Test: `web/src/app/campaigns/[id]/_components/campaign-room-tabs.test.tsx`

**Interfaces:**
- Consumes: `PagePlaceholder` and the app shell (Task 5).
- Produces:
  - A route for every PRD §4 screen
  - `CampaignRoomTabs({ roomId }: { roomId: string })`, a `<nav aria-label="Campaign room">` with six links to `/campaigns/<encoded id>/<tab>`

- [ ] **Step 1: Write the failing tab test**

`web/src/app/campaigns/[id]/_components/campaign-room-tabs.test.tsx`:
```tsx
import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { CampaignRoomTabs } from "./campaign-room-tabs";

function tabLinks() {
  const nav = screen.getByRole("navigation", { name: "Campaign room" });
  return within(nav)
    .getAllByRole("link")
    .map((link) => [link.textContent, link.getAttribute("href")]);
}

test("links to the six tabs of the room", () => {
  render(<CampaignRoomTabs roomId="42" />);

  expect(tabLinks()).toEqual([
    ["Evidence", "/campaigns/42/evidence"],
    ["Brief", "/campaigns/42/brief"],
    ["Assets", "/campaigns/42/assets"],
    ["Launch", "/campaigns/42/launch"],
    ["Performance", "/campaigns/42/performance"],
    ["Results", "/campaigns/42/results"],
  ]);
});

test("encodes room ids that contain URL characters", () => {
  render(<CampaignRoomTabs roomId="a/b?c" />);

  expect(tabLinks()[0]).toEqual(["Evidence", "/campaigns/a%2Fb%3Fc/evidence"]);
});
```

Run: `npm --prefix web test`
Expected: FAIL, `1 failed | 2 passed`. `campaign-room-tabs.test.tsx` can't resolve `./campaign-room-tabs`.

- [ ] **Step 2: Write the tabs and the room layout**

Under Cache Components the room ID is request-time data, so the layout reads it inside `<Suspense>`. Reading it outside makes `next build` fail.

`web/src/app/campaigns/[id]/_components/campaign-room-tabs.tsx`:
```tsx
import Link from "next/link";

const tabs = [
  { segment: "evidence", label: "Evidence" },
  { segment: "brief", label: "Brief" },
  { segment: "assets", label: "Assets" },
  { segment: "launch", label: "Launch" },
  { segment: "performance", label: "Performance" },
  { segment: "results", label: "Results" },
];

export function CampaignRoomTabs({ roomId }: { roomId: string }) {
  const base = `/campaigns/${encodeURIComponent(roomId)}`;

  return (
    <nav aria-label="Campaign room">
      <ul className="flex gap-6 border-b border-black/10 text-sm dark:border-white/15">
        {tabs.map((tab) => (
          <li key={tab.segment}>
            <Link href={`${base}/${tab.segment}`} className="block pb-2 hover:underline">
              {tab.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
```

`web/src/app/campaigns/[id]/layout.tsx`:
```tsx
import { Suspense } from "react";

import { CampaignRoomTabs } from "./_components/campaign-room-tabs";

export default function CampaignRoomLayout({ children, params }: LayoutProps<"/campaigns/[id]">) {
  return (
    <div className="space-y-6">
      <Suspense fallback={null}>
        <RoomTabs params={params} />
      </Suspense>
      {children}
    </div>
  );
}

async function RoomTabs({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <CampaignRoomTabs roomId={id} />;
}
```

- [ ] **Step 3: Generate the screens with a temporary script**

Save this as `web/new-pages.sh`. It writes 14 near-identical pages from one table and is deleted after use.
- Descriptions come from the "What it shows" column of PRD §4. The Evidence tab and the room overview use the Campaign room row and US-4.1.
- Existing pages, including Task 5's `today`, are left untouched.
```bash
#!/usr/bin/env bash
set -euo pipefail

page() {
  if [[ -f "src/app/$1/page.tsx" ]]; then return; fi
  mkdir -p "src/app/$1"
  cat > "src/app/$1/page.tsx" <<EOF
import { PagePlaceholder } from "@/components/page-placeholder";

export default function $2() {
  return (
    <PagePlaceholder
      title="$3"
      description="$4"
    />
  );
}
EOF
}

page "today" "TodayPage" "Today" \
  "Approvals waiting, alerts (stock pauses, pacing, disapprovals), top opportunities and live tests."
page "opportunities" "OpportunitiesPage" "Opportunities" \
  "Ranked cards with trend line, growth, regions, matching products, score and reason. Triggered cards are marked with their source."
page "opportunities/[id]" "OpportunityDetailPage" "Opportunity" \
  "Trend chart, regional view, matching products, sourced reasons and score breakdown."
page "campaigns" "CampaignsPage" "Campaigns" \
  "Every campaign room with stage, status, owner, budget, dates and lift."
page "campaigns/[id]" "CampaignRoomPage" "Campaign room" \
  "Stage tracker with owner and next action, and the activity log of every agent and user action."
page "campaigns/[id]/evidence" "EvidenceTabPage" "Evidence" \
  "The evidence attached when the opportunity was accepted: trend, regions, matching products and sourced reasons."
page "campaigns/[id]/brief" "BriefTabPage" "Brief" \
  "Each field with its reasoning, budget against the limit, test and holdout regions on a map, test window and smallest detectable lift."
page "campaigns/[id]/assets" "AssetsTabPage" "Assets" \
  "Assets by campaign type and language, each with its Guardian score, issues and before-and-after fixes."
page "campaigns/[id]/launch" "LaunchTabPage" "Launch" \
  "Launch summary and checks, then launch progress and Google Ads IDs."
page "campaigns/[id]/performance" "PerformanceTabPage" "Performance" \
  "Pacing against plan, daily spend and results, early lift readings, the automatic pause log and recommendations."
page "campaigns/[id]/results" "ResultsTabPage" "Results" \
  "The impact report: lift with confidence range, spend, cost per extra result, next step and what went to Brand Memory."
page "triggers" "TriggersPage" "Triggers" \
  "Rules by category with source, condition, regions, status, last fired and past results, plus suggested rules."
page "impact-lab" "ImpactLabPage" "Impact Lab" \
  "Past analyses, a guided setup, and results with an actual-versus-expected chart and warnings."
page "brand-kit" "BrandKitPage" "Brand Kit" \
  "Voice, visuals, rules, products, best ads, Brand Memory and version history."
page "settings" "SettingsPage" "Settings" \
  "Connections, users and roles, approvers per gate, budget limit and notification preferences."
```

```bash
cd web && bash new-pages.sh && rm new-pages.sh && cd ..
find web/src/app -name page.tsx | sort
```
Expected: 16 `page.tsx` files: the root redirect, `today` (which the script skips because it already exists) and the 14 new screens.

- [ ] **Step 4: Run tests, lint and build**

```bash
npm --prefix web test
npm --prefix web run lint
npm --prefix web run build
```
Expected:
- Tests: `Test Files 3 passed (3)`, `Tests 4 passed (4)`.
- Lint: prints no problems.
- Build: succeeds. Every `/campaigns/[id]...` and `/opportunities/[id]` route is marked `◐` (Partial Prerender), and the other routes `○` (Static).

- [ ] **Step 5: Commit**

```bash
git add web/src/app
git status --short   # expect no output
git commit -m "Add placeholder screens for every PRD screen and campaign room tabs"
```

---

### Task 7: Aspire AppHost

**Files:**
- Create: `core/src/Kairo.AppHost/Kairo.AppHost.csproj`, `AppHost.cs`, `aspire.config.json`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`
- Modify: `core/Kairo.slnx` (add the project)

**Interfaces:**
- Consumes:
  - `Projects.Kairo_Api` and `Projects.Kairo_Worker` (Task 3), which expose `/health`
  - `kairo_agents.main:app` (Task 4)
  - `web/` with `next dev` and `/health` (Tasks 5–6)
- Produces: `dotnet run --project core/src/Kairo.AppHost` starts six resources: `sql` (database `kairo`), `redis`, `agents`, `api`, `worker` and `web`.

- [ ] **Step 1: Write the AppHost**

`AddNextJsApp` is marked experimental, so the AppHost suppresses diagnostic `ASPIREJAVASCRIPT001`. With warnings treated as errors, it wouldn't build otherwise. If the integration misbehaves, the fallback is `builder.AddJavaScriptApp("web", "../../../web").WithRunScript("dev")`.

`core/src/Kairo.AppHost/Kairo.AppHost.csproj`:
```xml
<Project Sdk="Aspire.AppHost.Sdk/13.6.1">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AspireUseCliBundle>true</AspireUseCliBundle>
    <UserSecretsId>f3b1c6a2-5d4e-4c8f-9a7b-2e6d1c0b9a84</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.JavaScript" />
    <PackageReference Include="Aspire.Hosting.Python" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.SqlServer" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kairo.Api\Kairo.Api.csproj" />
    <ProjectReference Include="..\Kairo.Worker\Kairo.Worker.csproj" />
  </ItemGroup>

</Project>
```

`core/src/Kairo.AppHost/AppHost.cs`:
```csharp
#pragma warning disable ASPIREJAVASCRIPT001

var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("kairo");

var redis = builder.AddRedis("redis");

var agents = builder.AddUvicornApp("agents", "../../../agents", "kairo_agents.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health");

var api = builder.AddProject<Projects.Kairo_Api>("api")
    .WithReference(sql).WaitFor(sql)
    .WithReference(redis).WaitFor(redis)
    .WithReference(agents)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WithHttpHealthCheck("/health");

builder.AddNextJsApp("web", "../../../web")
    .WithReference(api)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
```

`core/src/Kairo.AppHost/aspire.config.json`:
```json
{
  "appHost": {
    "path": "Kairo.AppHost.csproj"
  }
}
```

`core/src/Kairo.AppHost/appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Aspire.Hosting.Dcp": "Warning"
    }
  }
}
```

`core/src/Kairo.AppHost/appsettings.Development.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

`core/src/Kairo.AppHost/Properties/launchSettings.json`. These are the template's values. The `https` profile is the default; the `http` profile would also need `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`.
```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:17233;http://localhost:15233",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21288",
        "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL": "https://localhost:22119"
      }
    },
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:15233",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "http://localhost:19147",
        "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL": "http://localhost:20163"
      }
    }
  }
}
```

- [ ] **Step 2: Add to the solution and build**

```bash
cd core && dotnet sln Kairo.slnx add src/Kairo.AppHost/Kairo.AppHost.csproj && cd ..
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx --no-build
```
Expected:
- Build: `Build succeeded.` with 0 warnings. The first build may take about 30 seconds while `AspireUseCliBundle` fetches the Aspire CLI bundle through `dnx`.
- Tests: `succeeded: 37`.

- [ ] **Step 3: Run the whole stack (requires Docker)**

Make sure Docker is running. If `docker info` fails, start Docker Desktop, or ask the user to start it, and wait until `docker info` succeeds.

Create a marker so the wait loop can tell this run's log apart from earlier ones:
```bash
touch core/src/Kairo.AppHost/obj/run-marker
```

Start the AppHost with `run_in_background: true`:
```bash
dotnet run --project core/src/Kairo.AppHost
```
`dotnet run` hands off to the Aspire CLI bundle. It prints a dashboard URL with a login token, and writes its full log to `~/.aspire/logs/cli_*.log`.

Wait until all seven resources are ready: the `sql` server, the `kairo` database, `redis`, `agents`, `api`, `worker` and `web`. The first run pulls the SQL Server and Redis images and can take over ten minutes. Run the loop with `run_in_background: true`, or re-run it if it hits the tool timeout:
```bash
until LOG=$(find ~/.aspire/logs -name 'cli_*.log' -newer core/src/Kairo.AppHost/obj/run-marker | head -1) && [ -n "$LOG" ]; do sleep 2; done
until [ "$(grep -oE "Resource '(sql|kairo|redis|agents|api|worker|web)' is ready" "$LOG" | sort -u | wc -l)" -ge 7 ] || grep -qE "Unhandled exception|FailedToStart" "$LOG"; do sleep 5; done
grep -oE "Resource '[a-z]+' is ready" "$LOG" | sort -u
grep -cE "Unhandled exception|FailedToStart" "$LOG"
```
Expected: seven `is ready` lines (`agents`, `api`, `kairo`, `redis`, `sql`, `web`, `worker`), then `0`.

If the images have finished downloading but the loop still hasn't ended, open the dashboard URL that the AppHost printed. The criterion is that every resource shows **Running** and **Healthy**. A resource in **Waiting** names the dependency it's waiting for.

`api` reports ready only when its `/health` passes. That means it reached SQL Server and Redis, which proves the connection-string wiring.

Stop the AppHost's background task. Then confirm, using the PowerShell tool, that no orchestrator, Uvicorn or Next.js processes are left over:
```powershell
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'Kairo\\(agents|web)' -or $_.Name -match '^dcp' } | Select-Object ProcessId, Name
```
Expected: no output. If anything is listed, stop it with `Stop-Process -Id <id>`.

- [ ] **Step 4: Commit**

```bash
git add core/Kairo.slnx core/src/Kairo.AppHost
git commit -m "Add Aspire AppHost that runs the whole stack locally"
```

---

### Task 8: Container images

**Files:**
- Create: `core/.dockerignore`, `core/src/Kairo.Api/Dockerfile`, `core/src/Kairo.Worker/Dockerfile`
- Create: `agents/.dockerignore`, `agents/Dockerfile`
- Create: `web/.dockerignore`, `web/Dockerfile`

**Interfaces:**
- Consumes: the four deployables from Tasks 3–6.
- Produces: images listening on port 8080. The Python and Node images honour `$PORT`. The future `deploy.bat` builds these four images.

- [ ] **Step 1: Write the core images**

`core/.dockerignore`:
```
**/bin/
**/obj/
**/.vs/
**/*.user
tests/
src/Kairo.AppHost/
```

Both core images build from the `core/` folder, as in `docker build -f core/src/Kairo.Api/Dockerfile core`. The ASP.NET base image already listens on port 8080 (`ASPNETCORE_HTTP_PORTS`), which is Cloud Run's default. `$APP_UID` is its built-in non-root user.

`core/src/Kairo.Api/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Kairo.Api/Kairo.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Kairo.Api.dll"]
```

`core/src/Kairo.Worker/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Kairo.Worker/Kairo.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Kairo.Worker.dll"]
```

- [ ] **Step 2: Write the agent service image**

`agents/.dockerignore`:
```
.venv/
**/__pycache__/
.pytest_cache/
.ruff_cache/
tests/
```

`agents/Dockerfile`:
- FFmpeg is installed because the Create agent will add voiceover, captions and logo to generated video.
- Dependencies are installed before the source is copied, so code changes don't reinstall them.
```dockerfile
FROM python:3.13-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=ghcr.io/astral-sh/uv:0.12.5 /uv /uvx /bin/

WORKDIR /app
ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never

COPY pyproject.toml uv.lock README.md ./
RUN uv sync --locked --no-dev --no-install-project

COPY src ./src
RUN uv sync --locked --no-dev

RUN useradd --system --uid 10001 kairo
USER kairo

ENV PATH="/app/.venv/bin:$PATH" \
    PORT=8080
EXPOSE 8080
CMD ["sh", "-c", "exec uvicorn kairo_agents.main:app --host 0.0.0.0 --port ${PORT}"]
```

- [ ] **Step 3: Write the web image**

`web/.dockerignore`:
```
node_modules
.next
coverage
npm-debug.log*
.env*
Dockerfile
.dockerignore
```

`web/Dockerfile`:
```dockerfile
FROM node:24-slim AS dependencies
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM node:24-slim AS builder
WORKDIR /app
COPY --from=dependencies /app/node_modules ./node_modules
COPY . .
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1
RUN npm run build

FROM node:24-slim AS runner
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=8080 \
    HOSTNAME=0.0.0.0
COPY --from=builder --chown=node:node /app/public ./public
RUN mkdir .next && chown node:node .next
COPY --from=builder --chown=node:node /app/.next/standalone ./
COPY --from=builder --chown=node:node /app/.next/static ./.next/static
USER node
EXPOSE 8080
CMD ["node", "server.js"]
```

- [ ] **Step 4: Build all four images**

```bash
docker build -f core/src/Kairo.Api/Dockerfile -t kairo-api core
docker build -f core/src/Kairo.Worker/Dockerfile -t kairo-worker core
docker build -t kairo-agents agents
docker build -t kairo-web web
```
Expected: each build ends with `naming to docker.io/library/kairo-<name>` and no errors.

- [ ] **Step 5: Run each image and check its health endpoint**

The core images need connection strings to start. These point at a closed port, which is enough for `/alive`. The Python and Node images run with `PORT=9090` to prove they honour Cloud Run's `$PORT` (Review Focus 5).
```bash
docker run -d --rm --name check-api -p 18081:8080 -e ConnectionStrings__kairo="Server=127.0.0.1,1;Database=kairo;User Id=x;Password=x;TrustServerCertificate=true" -e ConnectionStrings__redis="127.0.0.1:1,abortConnect=false" kairo-api
docker run -d --rm --name check-worker -p 18082:8080 -e ConnectionStrings__kairo="Server=127.0.0.1,1;Database=kairo;User Id=x;Password=x;TrustServerCertificate=true" kairo-worker
docker run -d --rm --name check-agents -p 18083:9090 -e PORT=9090 kairo-agents
docker run -d --rm --name check-web -p 18084:9090 -e PORT=9090 kairo-web
until curl -sf http://localhost:18081/alive >/dev/null && curl -sf http://localhost:18082/alive >/dev/null && curl -sf http://localhost:18083/health >/dev/null && curl -sf http://localhost:18084/health >/dev/null; do sleep 2; done
curl -s http://localhost:18081/alive; echo
curl -s http://localhost:18082/alive; echo
curl -s http://localhost:18083/health; echo
curl -s http://localhost:18084/health; echo
docker stop check-api check-worker check-agents check-web
```
Expected output lines: `Healthy`, `Healthy`, `{"status":"ok"}`, `{"status":"ok"}`.

If the until-loop hangs for more than 2 minutes, inspect `docker logs check-<name>`.

- [ ] **Step 6: Commit**

```bash
git add core/.dockerignore core/src/Kairo.Api/Dockerfile core/src/Kairo.Worker/Dockerfile agents/.dockerignore agents/Dockerfile web/.dockerignore web/Dockerfile
git commit -m "Add container images for the API, worker, agent service and web app"
```

---

### Task 9: Continuous integration

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes:
  - `global.json` (Task 2)
  - the test commands from Tasks 2–6
  - `agents/uv.lock` and `web/package-lock.json`
- Produces: three parallel jobs (`core`, `agents`, `web`) on pushes and pull requests to `main` and `develop`.

- [ ] **Step 1: Write the workflow**

`.github/workflows/ci.yml`:
```yaml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]

permissions:
  contents: read

jobs:
  core:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - run: dotnet build core/Kairo.slnx
      - run: dotnet test --solution core/Kairo.slnx --no-build

  agents:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: agents
    steps:
      - uses: actions/checkout@v7
      - uses: astral-sh/setup-uv@v10
        with:
          version: "0.12.5"
          enable-cache: true
      - run: uv sync --locked
      - run: uv run ruff check
      - run: uv run ruff format --check
      - run: uv run pytest

  web:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: web
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-node@v7
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: web/package-lock.json
      - run: npm ci
      - run: npm run lint
      - run: npm test
      - run: npm run build
```

- [ ] **Step 2: Lint the workflow**

`MSYS_NO_PATHCONV=1` stops Git Bash from rewriting `/repo` into a Windows path. `pwd -W` gives Docker the Windows form of the repository path.
```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/repo" -w /repo rhysd/actionlint:latest -color
```
Expected: no output, exit code 0.

The workflow itself is first exercised when the branch is pushed to GitHub. Pushing isn't part of this plan.

Known risk: `Kairo.AppHost` sets `AspireUseCliBundle`, so building the solution fetches the Aspire CLI bundle through `dnx`, which ships with the .NET 10 SDK. This works locally. If it fails on the CI runner with `ASPIRE009`, change the core job's build step to `dotnet build core/Kairo.slnx -p:AspireUseCliBundle=false -p:NoWarn=ASPIRE010`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "Add CI workflow that builds and tests the core, agent service and web app"
```

---

### Task 10: README and final verification

**Files:**
- Modify: `README.md` (currently empty)

**Interfaces:**
- Consumes: every command from Tasks 2–9.
- Produces: the root README, covering prerequisites, running the stack, tests and conventions.

- [ ] **Step 1: Write `README.md`**

````markdown
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
````

- [ ] **Step 2: Verify every done criterion from the spec**

Run from the repository root:
```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx --no-build
uv run --directory agents ruff check
uv run --directory agents ruff format --check
uv run --directory agents pytest
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
git status --short
```
Expected:
- `Build succeeded.` with 0 warnings
- core tests: `succeeded: 37`
- agent service: `All checks passed!`, every file already formatted, and `8 passed`
- web app: lint clean, `Tests 4 passed (4)`, and a successful build
- `git status --short` lists only `README.md`

Done criteria 4 (the AppHost runs healthy) and 5 (the images build) were verified in Tasks 7 and 8. If any code changed since then, re-run those checks.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Add README covering prerequisites, running, testing and conventions"
```
````
