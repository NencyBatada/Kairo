# Kairo Analytics Data Feeds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every night, load each brand's Google Ads performance, GA4 outcomes and sales into a BigQuery dataset of its own, by day and region, and let brands add sales by CSV upload or from a BigQuery table they already keep.

**Architecture:**
- **Measurement module.** The empty `Kairo.Modules.Measurement` stub gains a `Feeds` folder and the SQL schema `measurement`, with three tables: `FeedRuns`, `SalesUploads` and `SalesSources`.
- **BigQuery writes.** One service, `BigQueryTables.ReplaceDaysAsync`, does every write. It loads rows into a staging table, then runs one script that deletes the days being loaded and inserts the new rows. Kairo's own calls use the service account's Application Default Credentials.
- **Nightly run.** Cloud Scheduler calls `POST /jobs/analytics/daily` on the worker at 05:00 India time. The worker lists every brand, and runs three feeds per brand in its own DI scope: Google Ads reports, GA4 Data API totals, and the brand's own sales table. Each feed uses the brand's existing connection through `IConnections`.
- **API.** Brands upload sales CSVs, set or remove a sales table, and read each feed's status.

**Tech Stack:**
- **Core:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 on SQL Server 2022.
- **Google APIs:** plain `HttpClient` calls to Google Ads REST `v25` (`googleAds:searchStream`), GA4 Data API `v1beta` (`runReport`), Analytics Admin API `v1beta`, and BigQuery API `v2`.
- **Credentials:** `Google.Apis.Auth` 1.73.0, for Application Default Credentials only.
- **Tests:** xUnit v3, Testcontainers SQL Server, and Pester 5.9.1 for the deployment scripts.

**Spec:** `docs/superpowers/specs/2026-10-13-analytics-data-feeds-design.md`

## Global Constraints

- **Worktree and branch.**
  - Work only in `C:\Kairo-worktrees\analytics-feeds`, on branch `feature/analytics-data-feeds`.
  - Run every command in Git Bash from `/c/Kairo-worktrees/analytics-feeds`, unless a step says otherwise.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user (`CLAUDE.md` §1).
  - Stage files by explicit path, never with `git add -A` or `git add .`. Never stage `CLAUDE.md` or `.claude/`.
  - Don't push.
- **No comments in code** (`CLAUDE.md` §3). That means no `//`, `#`, `/* */` or `<!-- -->` comments, and no XML doc comments. Generated EF Core migrations go through `core/scripts/tidy-migrations.sh`.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build must finish with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container.
- **Module rules stay true.**
  - `Kairo.Modules.Measurement` exposes exactly one public type, `MeasurementModule`, and adds no `ProjectReference`. Every other class in it is `internal`.
  - Its tables live only in the `measurement` schema.
  - The only contract change is `IBrandDirectory.ListAllAsync` in `Kairo.Contracts`.
- **Feeds**, as stored and returned: `google-ads`, `ga4`, `sales-table`.
- **Run statuses:** `running`, `succeeded`, `failed`, `skipped`. **Skip reasons:** `not_connected`, `needs_reconnect`, `already_running`.
- **Feed states** in `GET …/analytics/feeds`: `not_set_up`, `needs_connection`, `waiting`, `running`, `failing`, `ok`.
- **BigQuery names.**
  - Dataset: `brand_` + the brand ID as `Guid.ToString("N")`.
  - Tables: `ads_campaign_daily`, `ads_ad_group_daily`, `ads_ad_daily`, `ads_region_daily`, `ga4_region_daily`, `sales_daily`.
  - Staging tables: `_staging_<table>_<32 hex>`.
  - `sales_daily.source` is `upload` or `table`.
- **Audit actions:** `analytics.sales_uploaded`, `analytics.sales_source_set`, `analytics.sales_source_removed`.
- **Timings and limits.**
  - Window: from 7 days ago through yesterday. The first successful run for a feed and an external ID reaches back 13 months.
  - A `running` record counts as running for 2 hours.
  - Run records older than 90 days are deleted at the end of each nightly run.
  - Uploads: at most 10 MB, at most 20 problems reported.
  - Geo target names are looked up in batches of 500.
  - GA4 `runReport` pages are at most 100,000 rows.
- **Time zones:** the Google Ads account's `customer.time_zone`, the GA4 property's `timeZone`, and `Asia/Kolkata` for sales and uploads.
- **Configuration.**
  - New: `Kairo:Analytics:Project` (empty turns feeds and uploads off) and `Kairo:Analytics:Location` (default `asia-south1`).
  - Existing, now also needed by the worker: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, `Kairo:Google:AdsApiVersion` (default `v25`).
  - Aspire: `Parameters:analytics-project`, used only when present.
- **Named HTTP client** for every Measurement call to Google: `kairo-measurement-google`. Tests replace its primary handler with `FakeGoogleApis`, and replace `IServiceAccessToken` with `FakeServiceToken`.
- **Deployment names:** service account `kairo-scheduler`, Cloud Scheduler job `kairo-analytics-daily`, schedule `0 5 * * *` in `Asia/Kolkata`, attempt deadline `1800s`, `0` retries.
- **Versions.**
  - `Google.Apis.Auth`: 1.73.0, the version `Google.Cloud.Kms.V1` 3.27.0 already brings in
  - Google Ads REST API: `v25`
- **Running tests.**
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`
  - Whole core: `dotnet test --solution core/Kairo.slnx`
  - Deployment scripts: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
  - Every task ends with the whole core suite passing. A task that adds or changes API endpoints (Tasks 4, 5 and 9) also regenerates the API contract and stages `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts`:
    ```bash
    KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
    npm --prefix web run generate:api
    ```

## Review Focus

These are the inputs most likely to hurt a real user. Each one is pinned by a test in the task named in brackets.

1. **A sales CSV saved by Excel or Google Sheets.** It has a byte order mark, Windows line endings, a quoted region with a comma such as `"Delhi, NCR"`, and a blank last line. It must load as written. (Task 2: `Reads_a_byte_order_mark_windows_line_endings_quoted_commas_and_a_blank_last_line`.)
2. **The replace script fails halfway**, for example on a BigQuery quota error. The table must keep its old rows, because the delete and insert run in one transaction, and the staging table must not be left behind. (Task 3: `A_failed_replace_keeps_the_rows_and_removes_the_staging_table`.)
3. **A client account reached through a manager.** Every report, including the geo target name lookup, must send `login-customer-id`, or Google answers 403 and the feed silently fails every night. (Task 6: `A_client_reached_through_a_manager_is_read_through_the_manager`.)
4. **One brand's Google account breaks mid-run**, with a 500 from Google Ads. That brand's run must be recorded as failed with Google's message, and every other brand must still load. (Task 6: `One_failing_brand_still_lets_the_others_finish`.)
5. **A manual `?brandId=` run while the nightly run is still going.** The second run must skip the busy feed as `already_running`, rather than racing it on the same staging and target tables. (Task 6: `A_feed_that_is_already_running_is_skipped`.)

## Where this plan differs from the spec

1. **No record when no sales table is set.** The spec records skips for missing connections. A brand that never set a sales table gets no `sales-table` record at all, so the run records aren't filled with a skip every night for something the brand never asked for. The status still shows `not_set_up`.
2. **A row Google doesn't place in a state** has `NULL` `region_id`, `region_name` and `country_code`. The spec says "empty"; `NULL` is what BigQuery joins and `IS NULL` checks expect.
3. **The feed status endpoint works without an analytics project.** It reads only SQL Server. The nightly job and uploads return `503` without one, as the spec says.
4. **Removing a sales table that isn't set** returns `204` too, so the call is safe to repeat. It is audited only when there was one.
5. **The running record is written before the access token is fetched.** A feed's external ID comes from `ConnectionStatus.ExternalId`. When `GetAccessAsync` then throws `ConnectionUnavailableException`, that same record becomes `skipped` with the reason, so a brand never gets two records for one feed in one run.
6. **The staging table is deleted when a load or the script fails.** The spec drops it only at the end of the script. Without this, a failure leaves a `_staging_` table in the brand's dataset for good.
7. **`uploadedBy` is the user ID.** The spec's response names the field but not its type. The web app already has the team list to show a name.
8. **Each nightly-run test uses its own database.** `IBrandDirectory.ListAllAsync` sees every brand in the shared test database, and refreshing other test classes' tokens against this test's fake Google would mark their connections Needs reconnect.
9. **`lastSuccessAt` is the success's `FinishedAt`**, and `lastRunAt` is the latest record's `StartedAt`.
10. **Parallel branch.** The notifications branch also edits `FakeGoogleApis.cs` and `KairoApiFactory.cs`. When both branches meet in `develop`, keep both sides of any conflict in those files.

## File structure

**Contracts and Brands**
- Modify `core/src/Kairo.Contracts/Brands/IBrandDirectory.cs`: `ListAllAsync`.
- Modify `core/src/Modules/Kairo.Modules.Brands/Workspaces/BrandDirectory.cs`: reads every brand in a system scope.

**Measurement module** (`core/src/Modules/Kairo.Modules.Measurement/`)
- `Kairo.Modules.Measurement.csproj`: `Google.Apis.Auth`, and `InternalsVisibleTo` for the integration tests.
- `MeasurementModule.cs`: registers the module's parts, maps the API and the job.
- `Data/MeasurementDbContext.cs`, `Data/MeasurementDbContextFactory.cs`, `Data/Migrations/*`: the three tables.
- `Feeds/FeedRun.cs`, `Feeds/SalesUpload.cs`, `Feeds/SalesSource.cs`: the entities.
- `Feeds/FeedNames.cs`: feed names, run statuses, skip reasons and feed states.
- `Feeds/FeedSetup.cs`: DI registrations and API routes.
- `Feeds/AnalyticsOptions.cs`: the project, location, Ads API version and dataset name.
- `Feeds/FeedClock.cs`: "today" in a time zone, and the feed window.
- `Feeds/MeasurementHttp.cs`: the named client, JSON calls, and `GoogleApiException`.
- `Feeds/ServiceAccessToken.cs`: `IServiceAccessToken` and its Application Default Credentials version.
- `Feeds/BigQueryClient.cs`: queries with parameters and result paging.
- `Feeds/TableSchemas.cs`: the six tables' columns.
- `Feeds/BigQueryTables.cs`: datasets, tables, loads, and the replace script.
- `Feeds/SalesCsv.cs`: parsing and checking uploads.
- `Feeds/SalesEndpoints.cs`: uploads and the sales table source.
- `Feeds/BigQueryTableCheck.cs`: checking a brand's table through its BigQuery connection.
- `Feeds/IFeed.cs`, `Feeds/BrandFeeds.cs`, `Feeds/FeedRunner.cs`, `Feeds/FeedJobs.cs`: the nightly run.
- `Feeds/GoogleAdsReports.cs`, `Feeds/AdsRows.cs`, `Feeds/GoogleAdsFeed.cs`: Google Ads.
- `Feeds/Ga4Reports.cs`, `Feeds/Ga4Feed.cs`: GA4.
- `Feeds/SalesTableFeed.cs`: the brand's sales table.
- `Feeds/FeedEndpoints.cs`: feed status.

**Tests** (`core/tests/Kairo.IntegrationTests/`)
- `Kairo.IntegrationTests.csproj`: references the worker with the extern alias `worker`.
- `Api/FakeGoogleApis.cs`: the service token, Ads `searchStream`, GA4 Admin and Data API, and routing to the fake BigQuery.
- `Api/FakeBigQuery.cs`: an in-memory BigQuery for Kairo's project and the brand's project.
- `Api/FakeServiceToken.cs`, `Api/KairoApiFactory.cs`, `Api/KairoWorkerFactory.cs`.
- `Feeds/*Tests.cs` and `Feeds/FeedRunRows.cs`, `Feeds/SalesFiles.cs`.
- `Persistence/MeasurementTableTests.cs`.

**Hosts and deployment**
- `core/src/Kairo.AppHost/AppHost.cs`: the worker gets the Google client, and both get the analytics project.
- `core/Directory.Packages.props`: `Google.Apis.Auth`.
- `deploy/Kairo.Deploy/Common.ps1`, `Setup.ps1`, `Release.ps1`, and their tests.

**Docs**
- `docs/Kairo-SAD.md`, `README.md`.

---

### Task 1: Measurement storage

**Files:**
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Kairo.Modules.Measurement.csproj`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Data/MeasurementDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Data/MeasurementDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedRun.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesUpload.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesSource.cs`
- Create (generated): `core/src/Modules/Kairo.Modules.Measurement/Data/Migrations/<timestamp>_Feeds.cs`, `<timestamp>_Feeds.Designer.cs`, `MeasurementDbContextModelSnapshot.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/MeasurementTableTests.cs`

**Interfaces:**
- Consumes: `ModuleDbContext`, `IBrandOwned`, `KairoDbContextOptions`, and the `CreateBrandAccessPredicate`, `AddBrandSeparation` and `DropBrandSeparation` migration helpers.
- Produces:
  - `MeasurementDbContext` with `SchemaName = "measurement"` and `DbSet`s `FeedRuns`, `SalesUploads`, `SalesSources`.
  - `FeedRun`: `Guid Id`, `Guid BrandId`, `string Feed`, `string? ExternalId`, `string Status`, `string? Reason`, `DateOnly? FromDate`, `DateOnly? ToDate`, `int Rows`, `string? Error`, `DateTimeOffset StartedAt`, `DateTimeOffset? FinishedAt`.
  - `SalesUpload`: `Guid Id`, `Guid BrandId`, `string FileName`, `int Rows`, `DateOnly FirstDate`, `DateOnly LastDate`, `int Days`, `Guid UploadedBy`, `DateTimeOffset UploadedAt`.
  - `SalesSource`: `Guid BrandId` (the key), `string TableRef`, `string DateColumn`, `string RegionColumn`, `string OrdersColumn`, `string RevenueColumn`, `string Location`, `Guid SetBy`, `DateTimeOffset SetAt`.

- [ ] **Step 1: Write the failing test**

Create `core/tests/Kairo.IntegrationTests/Persistence/MeasurementTableTests.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class MeasurementTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("FeedRuns")]
    [InlineData("SalesUploads")]
    [InlineData("SalesSources")]
    public async Task The_table_is_covered_by_the_measurement_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'measurement.{table}')",
            Guid.Empty,
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Fact]
    public async Task A_workspace_has_at_most_one_sales_source()
    {
        var brandId = Guid.NewGuid();
        await InsertSourceAsync(brandId);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertSourceAsync(brandId));

        Assert.Equal(2627, duplicate.Number);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_a_feed_run()
    {
        var brandId = Guid.NewGuid();
        await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO measurement.FeedRuns (Id, BrandId, Feed, Status, [Rows], StartedAt)
            VALUES (NEWID(), @brand, N'google-ads', N'succeeded', 0, SYSDATETIMEOFFSET());
            SELECT 0;
            """,
            brandId,
            Guid.Empty);

        var seen = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @other;
            SELECT COUNT(*) FROM measurement.FeedRuns WHERE BrandId = @brand;
            """,
            brandId,
            Guid.NewGuid());

        Assert.Equal(0, seen);
    }

    private Task InsertSourceAsync(Guid brandId) => ScalarAsync(
        """
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO measurement.SalesSources (BrandId, TableRef, DateColumn, RegionColumn, OrdersColumn, RevenueColumn, Location, SetBy, SetAt)
        VALUES (@brand, N'acme-data.shop.daily_orders', N'order_date', N'state', N'order_count', N'net_revenue', N'asia-south1', NEWID(), SYSDATETIMEOFFSET());
        SELECT 0;
        """,
        brandId,
        Guid.Empty);

    private async Task<int> ScalarAsync(string sql, Guid brandId, Guid otherBrandId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@other", otherBrandId);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
```

- [ ] **Step 2: Run the test to see it fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.MeasurementTableTests"
```

Expected: all five fail. The three policy tests count 0 predicates; the other two throw `SqlException` 208 (invalid object name `measurement.SalesSources` / `measurement.FeedRuns`).

- [ ] **Step 3: Let the tests see the module's internals**

Replace `core/src/Modules/Kairo.Modules.Measurement/Kairo.Modules.Measurement.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Add the entities**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedRun.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal sealed class FeedRun : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Feed { get; set; } = "";

    public string? ExternalId { get; set; }

    public string Status { get; set; } = "";

    public string? Reason { get; set; }

    public DateOnly? FromDate { get; set; }

    public DateOnly? ToDate { get; set; }

    public int Rows { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesUpload.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal sealed class SalesUpload : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string FileName { get; set; } = "";

    public int Rows { get; set; }

    public DateOnly FirstDate { get; set; }

    public DateOnly LastDate { get; set; }

    public int Days { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesSource.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal sealed class SalesSource : IBrandOwned
{
    public Guid BrandId { get; set; }

    public string TableRef { get; set; } = "";

    public string DateColumn { get; set; } = "";

    public string RegionColumn { get; set; } = "";

    public string OrdersColumn { get; set; } = "";

    public string RevenueColumn { get; set; } = "";

    public string Location { get; set; } = "";

    public Guid SetBy { get; set; }

    public DateTimeOffset SetAt { get; set; }
}
```

- [ ] **Step 5: Add the context and its design-time factory**

Create `core/src/Modules/Kairo.Modules.Measurement/Data/MeasurementDbContext.cs`:

```csharp
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Data;

internal sealed class MeasurementDbContext(DbContextOptions<MeasurementDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "measurement";

    public DbSet<FeedRun> FeedRuns => Set<FeedRun>();

    public DbSet<SalesUpload> SalesUploads => Set<SalesUpload>();

    public DbSet<SalesSource> SalesSources => Set<SalesSource>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FeedRun>(run =>
        {
            run.ToTable("FeedRuns");
            run.HasIndex(r => new { r.BrandId, r.Feed, r.StartedAt });
            run.HasIndex(r => r.StartedAt);
            run.Property(r => r.Feed).HasMaxLength(20);
            run.Property(r => r.ExternalId).HasMaxLength(300);
            run.Property(r => r.Status).HasMaxLength(20);
            run.Property(r => r.Reason).HasMaxLength(40);
            run.Property(r => r.Error).HasMaxLength(1000);
        });

        modelBuilder.Entity<SalesUpload>(upload =>
        {
            upload.ToTable("SalesUploads");
            upload.HasIndex(u => new { u.BrandId, u.UploadedAt });
            upload.Property(u => u.FileName).HasMaxLength(200);
        });

        modelBuilder.Entity<SalesSource>(source =>
        {
            source.ToTable("SalesSources");
            source.HasKey(s => s.BrandId);
            source.Property(s => s.TableRef).HasMaxLength(300);
            source.Property(s => s.DateColumn).HasMaxLength(300);
            source.Property(s => s.RegionColumn).HasMaxLength(300);
            source.Property(s => s.OrdersColumn).HasMaxLength(300);
            source.Property(s => s.RevenueColumn).HasMaxLength(300);
            source.Property(s => s.Location).HasMaxLength(64);
        });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Data/MeasurementDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Measurement.Data;

internal sealed class MeasurementDbContextFactory : IDesignTimeDbContextFactory<MeasurementDbContext>
{
    public MeasurementDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<MeasurementDbContext>(
            MeasurementDbContext.SchemaName,
            (options, database) => new MeasurementDbContext(options, database));
}
```

- [ ] **Step 6: Register the context and apply its migrations**

Replace `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs` with:

```csharp
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement;

public sealed class MeasurementModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MeasurementDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, MeasurementDbContext.SchemaName));
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<MeasurementDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 7: Generate and tidy the migration**

```bash
dotnet tool restore
dotnet ef migrations add Feeds --project core/src/Modules/Kairo.Modules.Measurement --startup-project core/src/Kairo.Migrator --context MeasurementDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Measurement/Data/Migrations/*.cs
```

Open the new `core/src/Modules/Kairo.Modules.Measurement/Data/Migrations/<timestamp>_Feeds.cs`:
- In `Up`, add these as the last lines, after the generated `CreateIndex` calls:

  ```csharp
              migrationBuilder.CreateBrandAccessPredicate("measurement");
              migrationBuilder.AddBrandSeparation("measurement", "FeedRuns");
              migrationBuilder.AddBrandSeparation("measurement", "SalesUploads");
              migrationBuilder.AddBrandSeparation("measurement", "SalesSources");
  ```

- In `Down`, add this as the first line:

  ```csharp
              migrationBuilder.DropBrandSeparation("measurement");
  ```

Then check that `grep -rn "auto-generated\|public partial class" core/src/Modules/Kairo.Modules.Measurement/Data/Migrations` prints nothing.

- [ ] **Step 8: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.MeasurementTableTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.SecurityPolicyCoverageTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
dotnet test --solution core/Kairo.slnx
```

Expected: all pass with 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Kairo.Modules.Measurement.csproj \
  core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs \
  core/src/Modules/Kairo.Modules.Measurement/Data \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedRun.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesUpload.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesSource.cs \
  core/tests/Kairo.IntegrationTests/Persistence/MeasurementTableTests.cs
git commit -m "Store feed runs, sales uploads and sales sources in the measurement schema"
```

---

### Task 2: Reading sales CSV files

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesCsv.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/SalesCsvTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `record SalesRow(DateOnly Date, string Region, long Orders, decimal Revenue)`
  - `record SalesFile(IReadOnlyList<SalesRow> Rows, IReadOnlyList<string> Errors)` with `bool IsValid`
  - `static SalesFile SalesCsv.Parse(Stream stream, DateOnly today)`, plus `SalesCsv.MaxErrors = 20` and `SalesCsv.MaxRegionLength = 100`
  - A valid file has every row and no errors. An invalid file has no rows and up to 20 errors, each `"row N: …"` except file-level ones.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/SalesCsvTests.cs`:

```csharp
using System.Text;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class SalesCsvTests
{
    private const string Header = "date,region,orders,revenue\n";

    private static readonly DateOnly Today = new(2026, 10, 13);

    [Fact]
    public void Reads_the_columns_in_any_order_and_letter_case()
    {
        var file = Parse("Revenue,DATE,orders, Region \n1500.50,2026-10-01,12,Maharashtra\n");

        Assert.True(file.IsValid);
        Assert.Equal(new[] { new SalesRow(new DateOnly(2026, 10, 1), "Maharashtra", 12, 1500.50m) }, file.Rows);
    }

    [Fact]
    public void Reads_a_byte_order_mark_windows_line_endings_quoted_commas_and_a_blank_last_line()
    {
        var text = "date,region,orders,revenue\r\n2026-10-01,\"Delhi, NCR\",3,99\r\n2026-10-02,\"Tamil \"\"Nadu\"\"\",1,10.5\r\n\r\n";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var file = SalesCsv.Parse(new MemoryStream(bytes), Today);

        Assert.True(file.IsValid, string.Join("; ", file.Errors));
        Assert.Equal(
            new[]
            {
                new SalesRow(new DateOnly(2026, 10, 1), "Delhi, NCR", 3, 99m),
                new SalesRow(new DateOnly(2026, 10, 2), "Tamil \"Nadu\"", 1, 10.5m),
            },
            file.Rows);
    }

    [Theory]
    [InlineData("2026-13-01,Goa,1,1", "row 2: date must be YYYY-MM-DD")]
    [InlineData("01/10/2026,Goa,1,1", "row 2: date must be YYYY-MM-DD")]
    [InlineData("2026-10-14,Goa,1,1", "row 2: date can't be after today")]
    [InlineData("2026-10-01,  ,1,1", "row 2: region is empty")]
    [InlineData("2026-10-01,Goa,1.5,1", "row 2: orders must be a whole number, 0 or more")]
    [InlineData("2026-10-01,Goa,-1,1", "row 2: orders must be a whole number, 0 or more")]
    [InlineData("2026-10-01,Goa,١,1", "row 2: orders must be a whole number, 0 or more")]
    [InlineData("2026-10-01,Goa,1,1.234", "row 2: revenue must be a number, 0 or more, with up to 2 decimal places")]
    [InlineData("2026-10-01,Goa,1,1e3", "row 2: revenue must be a number, 0 or more, with up to 2 decimal places")]
    [InlineData("2026-10-01,Goa,1,\"1,000\"", "row 2: revenue must be a number, 0 or more, with up to 2 decimal places")]
    [InlineData("2026-10-01,Goa,1,1,5", "row 2: expected 4 values but found 5")]
    public void Reports_each_broken_rule(string line, string expected)
    {
        var file = Parse(Header + line + "\n");

        Assert.False(file.IsValid);
        Assert.Empty(file.Rows);
        Assert.Equal(new[] { expected }, file.Errors);
    }

    [Fact]
    public void Reports_a_region_over_100_characters()
    {
        var file = Parse(Header + $"2026-10-01,{new string('a', 101)},1,1\n");

        Assert.Equal(new[] { "row 2: region is longer than 100 characters" }, file.Errors);
    }

    [Fact]
    public void Reports_a_repeated_date_and_region_whatever_the_letter_case()
    {
        var file = Parse(Header + "2026-10-01,Goa,1,1\n2026-10-01,GOA,2,2\n");

        Assert.Equal(new[] { "row 3: date and region already appear in row 2" }, file.Errors);
    }

    [Fact]
    public void Stops_at_20_problems()
    {
        var file = Parse(Header + string.Concat(Enumerable.Repeat("bad,Goa,1,1\n", 30)));

        Assert.Equal(SalesCsv.MaxErrors, file.Errors.Count);
        Assert.Equal("row 21: date must be YYYY-MM-DD", file.Errors[^1]);
    }

    [Theory]
    [InlineData("date,region,orders\n2026-10-01,Goa,1\n")]
    [InlineData("date,region,orders,revenue,notes\n2026-10-01,Goa,1,1,x\n")]
    [InlineData("day,region,orders,revenue\n2026-10-01,Goa,1,1\n")]
    public void Refuses_a_header_without_exactly_the_four_columns(string text)
    {
        Assert.Equal(new[] { "row 1: the header must name date, region, orders and revenue" }, Parse(text).Errors);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_UTF8()
    {
        var file = SalesCsv.Parse(new MemoryStream([0x64, 0xFF, 0xFE, 0x00]), Today);

        Assert.Equal(new[] { "the file isn't UTF-8 text" }, file.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Header)]
    [InlineData(Header + "\n\n")]
    public void Refuses_a_file_without_rows(string text)
    {
        var errors = Parse(text).Errors;

        Assert.Single(errors);
        Assert.Contains(errors[0], new[] { "the file is empty", "the file has no rows" });
    }

    [Fact]
    public void Refuses_a_quote_that_is_never_closed()
    {
        Assert.Equal(new[] { "row 2: a quoted value is never closed" }, Parse(Header + "2026-10-01,\"Goa,1,1\n").Errors);
    }

    private static SalesFile Parse(string text) => SalesCsv.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)), Today);
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.SalesCsvTests"
```

Expected: the build fails with `CS0246: The type or namespace name 'SalesFile' could not be found`.

- [ ] **Step 3: Write the parser**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesCsv.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record SalesRow(DateOnly Date, string Region, long Orders, decimal Revenue);

internal sealed record SalesFile(IReadOnlyList<SalesRow> Rows, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static partial class SalesCsv
{
    public const int MaxErrors = 20;
    public const int MaxRegionLength = 100;

    private static readonly string[] Columns = ["date", "region", "orders", "revenue"];

    public static SalesFile Parse(Stream stream, DateOnly today)
    {
        string text;
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return Invalid("the file isn't UTF-8 text");
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var records = Records(text, out var unclosed);
        if (unclosed)
        {
            return Invalid($"row {records.Count}: a quoted value is never closed");
        }

        if (records.Count == 0)
        {
            return Invalid("the file is empty");
        }

        var header = records[0].Select(name => name.Trim().ToLowerInvariant()).ToArray();
        if (header.Length != Columns.Length || !Columns.All(column => Array.IndexOf(header, column) >= 0))
        {
            return Invalid("row 1: the header must name date, region, orders and revenue");
        }

        var position = Columns.ToDictionary(column => column, column => Array.IndexOf(header, column));
        var rows = new List<SalesRow>();
        var errors = new List<string>();
        var seen = new Dictionary<(DateOnly, string), int>();
        for (var index = 1; index < records.Count && errors.Count < MaxErrors; index++)
        {
            var values = records[index];
            var row = index + 1;
            if (values.Count == 1 && values[0].Trim().Length == 0)
            {
                continue;
            }

            if (values.Count != Columns.Length)
            {
                errors.Add($"row {row}: expected {Columns.Length} values but found {values.Count}");
                continue;
            }

            var problems = new List<string>();
            if (!DateOnly.TryParseExact(values[position["date"]].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                problems.Add("date must be YYYY-MM-DD");
            }
            else if (date > today)
            {
                problems.Add("date can't be after today");
            }

            var region = values[position["region"]].Trim();
            if (region.Length == 0)
            {
                problems.Add("region is empty");
            }
            else if (region.Length > MaxRegionLength)
            {
                problems.Add($"region is longer than {MaxRegionLength} characters");
            }

            var ordersText = values[position["orders"]].Trim();
            var orders = 0L;
            if (!WholeNumber().IsMatch(ordersText)
                || !long.TryParse(ordersText, NumberStyles.None, CultureInfo.InvariantCulture, out orders))
            {
                problems.Add("orders must be a whole number, 0 or more");
            }

            var revenueText = values[position["revenue"]].Trim();
            var revenue = 0m;
            if (!Money().IsMatch(revenueText)
                || !decimal.TryParse(revenueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out revenue))
            {
                problems.Add("revenue must be a number, 0 or more, with up to 2 decimal places");
            }

            var key = (date, region.ToUpperInvariant());
            if (problems.Count == 0 && seen.TryGetValue(key, out var first))
            {
                problems.Add($"date and region already appear in row {first}");
            }

            foreach (var problem in problems.Take(MaxErrors - errors.Count))
            {
                errors.Add($"row {row}: {problem}");
            }

            if (problems.Count == 0)
            {
                seen[key] = row;
                rows.Add(new SalesRow(date, region, orders, revenue));
            }
        }

        if (errors.Count > 0)
        {
            return new SalesFile([], errors);
        }

        return rows.Count == 0 ? Invalid("the file has no rows") : new SalesFile(rows, []);
    }

    private static SalesFile Invalid(string error) => new([], [error]);

    private static List<List<string>> Records(string text, out bool unclosed)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        unclosed = quoted;
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    [GeneratedRegex("^[0-9]{1,18}$")]
    private static partial Regex WholeNumber();

    [GeneratedRegex(@"^[0-9]{1,20}(\.[0-9]{1,2})?$")]
    private static partial Regex Money();
}
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.SalesCsvTests"
dotnet test --solution core/Kairo.slnx
```

Expected: all pass with 0 warnings. In `Refuses_a_file_without_rows`, `""` gives "the file is empty" and the other two give "the file has no rows".

- [ ] **Step 5: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesCsv.cs core/tests/Kairo.IntegrationTests/Feeds/SalesCsvTests.cs
git commit -m "Read and check sales CSV files"
```

---

### Task 3: Writing to BigQuery

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Kairo.Modules.Measurement.csproj`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/AnalyticsOptions.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/MeasurementHttp.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/ServiceAccessToken.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryClient.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/TableSchemas.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryTables.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Create: `core/tests/Kairo.IntegrationTests/Api/FakeBigQuery.cs`
- Create: `core/tests/Kairo.IntegrationTests/Api/FakeServiceToken.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/BigQueryTablesTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `AnalyticsOptions` (singleton): `string? Project`, `bool Enabled`, `string Location`, `string AdsApiVersion`, `static string DatasetFor(Guid brandId)`, `const string NotSetUp`.
  - `MeasurementHttp.ClientName = "kairo-measurement-google"`, `MeasurementHttp.Json`, and the `HttpClient` extensions `RequestAsync(method, url, accessToken, HttpContent?, ct, params (string Name, string Value)[] headers)` and `CallAsync(method, url, accessToken, object? body, ct, params headers)` returning a cloned `JsonElement`; `MeasurementHttp.ReadAsync(HttpResponseMessage, ct)`.
  - `GoogleApiException(HttpStatusCode status, string body)` with `Status`; its message is `"Google answered {status}: {Google's message}"`.
  - `interface IServiceAccessToken { Task<string> GetAsync(CancellationToken) }` and `ApplicationDefaultAccessToken`.
  - `record QueryParameter` with `QueryParameter.Date(name, DateOnly)` and `QueryParameter.Dates(name, IEnumerable<DateOnly>)`.
  - `BigQueryClient.QueryAsync(string project, string accessToken, string sql, string location, IReadOnlyList<QueryParameter> parameters, CancellationToken)` returning `IReadOnlyList<string?[]>`, every page read; `BigQueryClient.BaseUrl` and `UploadUrl`.
  - `record TableColumn(string Name, string Type)`, `record TableSchema(string Name, IReadOnlyList<TableColumn> Columns)` with `object SchemaJson()`, and `TableSchemas.AdsCampaignDaily`, `AdsAdGroupDaily`, `AdsAdDaily`, `AdsRegionDaily`, `Ga4RegionDaily`, `SalesDaily`.
  - `BigQueryTables.ReplaceDaysAsync(Guid brandId, TableSchema table, IReadOnlyCollection<DateOnly> dates, IReadOnlyList<JsonObject> rows, CancellationToken)` returning the number of rows loaded. Rows hold every column except `loaded_at`, which it adds. Dates are `"yyyy-MM-dd"` strings and `NUMERIC` values are strings.
  - `ReplaceScript.For(string project, string dataset, TableSchema table, string? staging)`.
  - `FeedSetup.AddFeeds(IServiceCollection)`.
  - Tests: `FakeGoogleApis.ServiceToken`, `FakeGoogleApis.BigQuery` (a `FakeBigQuery` with `KairoProject = "kairo-analytics-test"` and `BrandProject = "acme-data"`), `FakeServiceToken`, and `KairoApiFactory.AnalyticsProject` (default `FakeBigQuery.KairoProject`, `null` turns analytics off).

- [ ] **Step 1: Add the fake BigQuery**

Create `core/tests/Kairo.IntegrationTests/Api/FakeBigQuery.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kairo.IntegrationTests.Api;

public sealed record FakeQuery(string Project, string AccessToken, string Location, string Sql, JsonElement Parameters);

public sealed record BrandTable(string Location, (string Name, string Type)[] Fields);

public sealed record SalesTableRow(string Date, string Region, string Orders, string Revenue);

public sealed partial class FakeBigQuery
{
    public const string KairoProject = "kairo-analytics-test";
    public const string BrandProject = "acme-data";

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, string> _datasets = new();
    private readonly Dictionary<string, List<JsonObject>> _tables = new();
    private readonly ConcurrentDictionary<string, Queue<object[]>> _pages = new();

    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<JsonObject> TableInserts { get; } = new();

    public ConcurrentQueue<FakeQuery> Queries { get; } = new();

    public bool FailScripts { get; set; }

    public HttpStatusCode BrandTableRead { get; set; } = HttpStatusCode.OK;

    public ConcurrentDictionary<string, BrandTable> BrandTables { get; } = new()
    {
        ["shop.daily_orders"] = new BrandTable(
            "asia-south1",
            [("order_date", "DATE"), ("state", "STRING"), ("order_count", "INTEGER"), ("net_revenue", "NUMERIC"), ("notes", "STRING")]),
    };

    public List<SalesTableRow> SalesTableRows { get; } = [];

    public int SalesPageSize { get; set; } = 2;

    public int Count(string call) => Calls.Count(entry => entry == call);

    public string? DatasetLocation(string dataset) => _datasets.GetValueOrDefault(dataset);

    public IReadOnlyList<string> TableNames(string dataset)
    {
        lock (_lock)
        {
            return _tables.Keys.Where(key => key.StartsWith(dataset + ".", StringComparison.Ordinal)).Select(key => key[(dataset.Length + 1)..]).ToList();
        }
    }

    public IReadOnlyList<JsonObject> Rows(string dataset, string table)
    {
        lock (_lock)
        {
            return _tables.TryGetValue($"{dataset}.{table}", out var rows) ? rows.Select(row => (JsonObject)row.DeepClone()).ToList() : [];
        }
    }

    public HttpResponseMessage? Handle(HttpRequestMessage request, string body, string accessToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var upload = path.StartsWith("/upload/", StringComparison.Ordinal);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(upload ? 4 : 3).ToArray();
        if (parts.Length == 0)
        {
            return null;
        }

        var project = parts[0];
        var rest = parts[1..];
        var method = request.Method.Method;
        if (project == KairoProject)
        {
            if (accessToken != FakeGoogleApis.ServiceToken)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return (method, rest) switch
            {
                ("POST", ["datasets"]) => InsertDataset(body),
                ("POST", ["datasets", var dataset, "tables"]) => InsertTable(dataset, body),
                ("POST", ["jobs"]) when upload => Load(request, body),
                ("GET", ["jobs", var jobId]) => Json(HttpStatusCode.OK, new { jobReference = new { jobId }, status = new { state = "DONE" } }),
                ("POST", ["queries"]) => Script(body),
                ("DELETE", ["datasets", var dataset, "tables", var table]) => DeleteTable(dataset, table),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        if (project == BrandProject)
        {
            if (accessToken == FakeGoogleApis.ServiceToken)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return (method, rest) switch
            {
                ("GET", ["datasets", var dataset, "tables", var table]) => BrandTableOf(dataset, table),
                ("POST", ["queries"]) => SalesQuery(accessToken, body),
                ("GET", ["queries", var jobId]) => NextPage(jobId),
                _ => null,
            };
        }

        return null;
    }

    private HttpResponseMessage InsertDataset(string body)
    {
        var request = JsonNode.Parse(body)!;
        var dataset = request["datasetReference"]!["datasetId"]!.GetValue<string>();
        Calls.Enqueue($"datasets.insert {dataset}");
        return _datasets.TryAdd(dataset, request["location"]?.GetValue<string>() ?? "US")
            ? Json(HttpStatusCode.OK, new { id = $"{KairoProject}:{dataset}" })
            : Json(HttpStatusCode.Conflict, new { error = new { message = "Already Exists: Dataset" } });
    }

    private HttpResponseMessage InsertTable(string dataset, string body)
    {
        var request = JsonNode.Parse(body)!.AsObject();
        var table = request["tableReference"]!["tableId"]!.GetValue<string>();
        Calls.Enqueue($"tables.insert {dataset}.{table}");
        TableInserts.Enqueue(request);
        if (!_datasets.ContainsKey(dataset))
        {
            return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Dataset" } });
        }

        lock (_lock)
        {
            if (!_tables.TryAdd($"{dataset}.{table}", []))
            {
                return Json(HttpStatusCode.Conflict, new { error = new { message = "Already Exists: Table" } });
            }
        }

        return Json(HttpStatusCode.OK, new { id = $"{KairoProject}:{dataset}.{table}" });
    }

    private HttpResponseMessage Load(HttpRequestMessage request, string body)
    {
        var boundary = request.Content!.Headers.ContentType!.Parameters.Single(parameter => parameter.Name == "boundary").Value!.Trim('"');
        var sections = body.Split("--" + boundary)
            .Select(section => section.Trim('\r', '\n'))
            .Where(section => section.Length > 0 && section != "--")
            .Select(section => section[(section.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..])
            .ToList();
        var job = JsonNode.Parse(sections[0])!;
        var destination = job["configuration"]!["load"]!["destinationTable"]!;
        var dataset = destination["datasetId"]!.GetValue<string>();
        var table = destination["tableId"]!.GetValue<string>();
        Calls.Enqueue($"load {dataset}.{table}");
        if (!_datasets.ContainsKey(dataset))
        {
            return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Dataset" } });
        }

        var rows = sections.Count < 2
            ? []
            : sections[1].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonNode.Parse(line)!.AsObject())
                .ToList();
        lock (_lock)
        {
            _tables[$"{dataset}.{table}"] = rows;
        }

        return Json(HttpStatusCode.OK, new { jobReference = job["jobReference"], status = new { state = "DONE" } });
    }

    private HttpResponseMessage Script(string body)
    {
        var request = JsonDocument.Parse(body).RootElement;
        var sql = request.GetProperty("query").GetString()!;
        Queries.Enqueue(new FakeQuery(KairoProject, FakeGoogleApis.ServiceToken, request.GetProperty("location").GetString()!, sql, request.GetProperty("queryParameters").Clone()));
        if (FailScripts)
        {
            return Json(HttpStatusCode.BadRequest, new { error = new { message = "Quota exceeded" } });
        }

        var target = DeleteTarget().Match(sql);
        if (!target.Success)
        {
            return Json(HttpStatusCode.BadRequest, new { error = new { message = "Unexpected script" } });
        }

        var dataset = target.Groups["dataset"].Value;
        var dates = DateValues(request, "dates").ToHashSet();
        var staging = StagingSource().Match(sql);
        lock (_lock)
        {
            if (!_tables.TryGetValue($"{dataset}.{target.Groups["table"].Value}", out var rows))
            {
                return Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Table" } });
            }

            rows.RemoveAll(row => dates.Contains(row["date"]!.GetValue<string>()));
            if (staging.Success)
            {
                var stagingKey = $"{dataset}.{staging.Groups["table"].Value}";
                rows.AddRange(_tables[stagingKey]);
                _tables.Remove(stagingKey);
            }
        }

        return Json(HttpStatusCode.OK, new { jobComplete = true, jobReference = new { projectId = KairoProject, jobId = $"script_{Guid.NewGuid():N}" } });
    }

    private HttpResponseMessage DeleteTable(string dataset, string table)
    {
        Calls.Enqueue($"tables.delete {dataset}.{table}");
        lock (_lock)
        {
            return _tables.Remove($"{dataset}.{table}") ? new HttpResponseMessage(HttpStatusCode.NoContent) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private HttpResponseMessage BrandTableOf(string dataset, string table)
    {
        if (BrandTableRead != HttpStatusCode.OK)
        {
            return Json(BrandTableRead, new { error = new { message = "Access Denied" } });
        }

        return BrandTables.TryGetValue($"{dataset}.{table}", out var found)
            ? Json(HttpStatusCode.OK, new
            {
                tableReference = new { projectId = BrandProject, datasetId = dataset, tableId = table },
                location = found.Location,
                schema = new { fields = found.Fields.Select(field => new { name = field.Name, type = field.Type, mode = "NULLABLE" }).ToArray() },
            })
            : Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Table" } });
    }

    private HttpResponseMessage SalesQuery(string accessToken, string body)
    {
        var request = JsonDocument.Parse(body).RootElement;
        Queries.Enqueue(new FakeQuery(BrandProject, accessToken, request.GetProperty("location").GetString()!, request.GetProperty("query").GetString()!, request.GetProperty("queryParameters").Clone()));
        var from = DateValues(request, "from").Single();
        var to = DateValues(request, "to").Single();
        var rows = SalesTableRows
            .Where(row => string.CompareOrdinal(row.Date, from) >= 0 && string.CompareOrdinal(row.Date, to) <= 0)
            .Select(row => (object)new { f = new object[] { new { v = row.Date }, new { v = row.Region }, new { v = row.Orders }, new { v = row.Revenue } } })
            .Chunk(SalesPageSize);
        return Page($"sales_{Guid.NewGuid():N}", new Queue<object[]>(rows));
    }

    private HttpResponseMessage NextPage(string jobId) =>
        _pages.TryRemove(jobId, out var pages) ? Page(jobId, pages) : Json(HttpStatusCode.NotFound, new { error = new { message = "Not found: Job" } });

    private HttpResponseMessage Page(string jobId, Queue<object[]> pages)
    {
        var page = pages.Count > 0 ? pages.Dequeue() : [];
        string? pageToken = null;
        if (pages.Count > 0)
        {
            pageToken = $"page-{pages.Count}";
            _pages[jobId] = pages;
        }

        return Json(HttpStatusCode.OK, new { jobComplete = true, jobReference = new { projectId = BrandProject, jobId }, rows = page, pageToken });
    }

    private static IEnumerable<string> DateValues(JsonElement request, string name)
    {
        var parameter = request.GetProperty("queryParameters").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == name);
        var value = parameter.GetProperty("parameterValue");
        return value.TryGetProperty("arrayValues", out var values)
            ? values.EnumerateArray().Select(entry => entry.GetProperty("value").GetString()!).ToList()
            : [value.GetProperty("value").GetString()!];
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };

    [GeneratedRegex(@"DELETE FROM `(?<project>[^.`]+)\.(?<dataset>[^.`]+)\.(?<table>[^`]+)` WHERE date IN UNNEST\(@dates\);")]
    private static partial Regex DeleteTarget();

    [GeneratedRegex(@"FROM `[^.`]+\.[^.`]+\.(?<table>_staging_[^`]+)`;")]
    private static partial Regex StagingSource();
}
```

Create `core/tests/Kairo.IntegrationTests/Api/FakeServiceToken.cs`:

```csharp
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Api;

internal sealed class FakeServiceToken : IServiceAccessToken
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(FakeGoogleApis.ServiceToken);
}
```

- [ ] **Step 2: Route BigQuery calls to the fake and accept the service token**

In `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`:
- Below `public const string ClientSecret = "kairo-test-secret";` add:

  ```csharp
      public const string ServiceToken = "kairo-service-account-token";
  ```

- Below `public ConcurrentQueue<string> Revoked { get; } = new();` add:

  ```csharp
      public FakeBigQuery BigQuery { get; } = new();
  ```

- In `SendAsync`, replace:

  ```csharp
          if (request.Headers.Authorization?.Parameter is not { } accessToken || !_accessTokens.ContainsKey(accessToken))
          {
              return new HttpResponseMessage(HttpStatusCode.Unauthorized);
          }
  ```

  with:

  ```csharp
          if (request.Headers.Authorization?.Parameter is not { } accessToken
              || (accessToken != ServiceToken && !_accessTokens.ContainsKey(accessToken)))
          {
              return new HttpResponseMessage(HttpStatusCode.Unauthorized);
          }

          if (uri.Host == "bigquery.googleapis.com" && BigQuery.Handle(request, body, accessToken) is { } answer)
          {
              return answer;
          }
  ```

- [ ] **Step 3: Give the test API the fake service token and an analytics project**

Replace `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs` with:

```csharp
using System.Net;
using System.Runtime.CompilerServices;
using Kairo.Modules.Measurement.Feeds;
using Kairo.SharedKernel.Http;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.IntegrationTests.Api;

public sealed class KairoApiFactory(string connectionString, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://kairo.test";

    private readonly ConditionalWeakTable<HttpClient, CookieContainer> _cookies = new();

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public FakeGoogle Google { get; } = new();

    public FakeGoogleApis GoogleApis { get; } = new();

    public string? AnalyticsProject { get; init; } = FakeBigQuery.KairoProject;

    public HttpClient CreateBrowser(bool withRequestHeader = true)
    {
        var cookies = new CookieContainer();
        var browser = CreateDefaultClient(new Uri(PublicOrigin), new CookieContainerHandler(cookies));
        if (withRequestHeader)
        {
            browser.DefaultRequestHeaders.Add(KairoApi.RequestHeader, "1");
        }

        _cookies.Add(browser, cookies);
        return browser;
    }

    public string CookieHeader(HttpClient browser) =>
        _cookies.TryGetValue(browser, out var cookies) ? cookies.GetCookieHeader(new Uri(PublicOrigin)) : "";

    public async Task<HttpClient> SignInAsync(GoogleUser user)
    {
        var browser = CreateBrowser();
        var location = await SignInAsync(browser, user, "/");
        if (location != "/")
        {
            throw new InvalidOperationException($"Sign-in ended at {location}.");
        }

        return browser;
    }

    public async Task<string> SignInAsync(HttpClient browser, GoogleUser user, string? returnUrl) =>
        (await CompleteSignInAsync(browser, user, returnUrl)).Headers.Location!.OriginalString;

    public async Task<HttpResponseMessage> CompleteSignInAsync(HttpClient browser, GoogleUser user, string? returnUrl)
    {
        var challenge = await browser.GetAsync($"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
        var authorize = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var code = Google.IssueCode(user, authorize["nonce"]!);
        var callbackPath = new Uri(authorize["redirect_uri"]!).AbsolutePath;
        return await browser.GetAsync($"{callbackPath}?code={code}&state={Uri.EscapeDataString(authorize["state"]!)}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", connectionString);
        builder.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false,connectRetry=0,connectTimeout=1000");
        builder.UseSetting("Authentication:Google:ClientId", FakeGoogle.ClientId);
        builder.UseSetting("Authentication:Google:ClientSecret", FakeGoogleApis.ClientSecret);
        builder.UseSetting("Kairo:PublicOrigin", PublicOrigin);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        if (AnalyticsProject is not null)
        {
            builder.UseSetting("Kairo:Analytics:Project", AnalyticsProject);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigureAll<OpenIdConnectOptions>(Google.Configure);
            services.AddHttpClient("kairo-google").ConfigurePrimaryHttpMessageHandler(() => GoogleApis);
            services.AddHttpClient("kairo-measurement-google").ConfigurePrimaryHttpMessageHandler(() => GoogleApis);
            services.AddSingleton<IServiceAccessToken, FakeServiceToken>();
            configureServices?.Invoke(services);
        });
    }
}
```

- [ ] **Step 4: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/BigQueryTablesTests.cs`:

```csharp
using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Feeds;

public sealed class BigQueryTablesTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly DateOnly First = new(2026, 10, 1);
    private static readonly DateOnly Second = new(2026, 10, 2);
    private static readonly DateOnly Third = new(2026, 10, 3);

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);
    private readonly Guid _brandId = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeBigQuery BigQuery => _api.GoogleApis.BigQuery;

    private string Dataset => AnalyticsOptions.DatasetFor(_brandId);

    [Fact]
    public async Task The_first_write_creates_the_dataset_and_a_day_partitioned_table_once()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));
        await ReplaceAsync([Second], Sale(Second, "Goa", 2));

        Assert.Equal(1, BigQuery.Count($"datasets.insert {Dataset}"));
        Assert.Equal(1, BigQuery.Count($"tables.insert {Dataset}.sales_daily"));
        Assert.Equal("asia-south1", BigQuery.DatasetLocation(Dataset));
        var table = BigQuery.TableInserts.Single(insert => insert["tableReference"]!["datasetId"]!.GetValue<string>() == Dataset);
        Assert.Equal("DAY", table["timePartitioning"]!["type"]!.GetValue<string>());
        Assert.Equal("date", table["timePartitioning"]!["field"]!.GetValue<string>());
    }

    [Fact]
    public async Task Each_row_carries_the_time_it_was_loaded()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));

        var row = Assert.Single(BigQuery.Rows(Dataset, "sales_daily"));
        var loadedAt = DateTimeOffset.Parse(row["loaded_at"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        Assert.True(Math.Abs((loadedAt - _api.Clock.GetUtcNow()).TotalMilliseconds) < 1);
    }

    [Fact]
    public async Task Running_the_same_load_twice_gives_the_same_rows()
    {
        await ReplaceAsync([First, Second], Sale(First, "Goa", 1), Sale(Second, "Goa", 2));
        await ReplaceAsync([First, Second], Sale(First, "Goa", 1), Sale(Second, "Goa", 2));

        Assert.Equal(new[] { 1L, 2L }, Orders());
    }

    [Fact]
    public async Task A_day_that_now_has_no_rows_is_emptied()
    {
        await ReplaceAsync([First, Second], Sale(First, "Goa", 1), Sale(Second, "Goa", 2));
        await ReplaceAsync([First, Second], Sale(First, "Goa", 5));

        var row = Assert.Single(BigQuery.Rows(Dataset, "sales_daily"));
        Assert.Equal("2026-10-01", row["date"]!.GetValue<string>());
        Assert.Equal(5, row["orders"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_load_with_no_rows_only_deletes()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));
        var loads = BigQuery.Calls.Count(call => call.StartsWith("load ", StringComparison.Ordinal));

        await ReplaceAsync([First]);

        Assert.Empty(BigQuery.Rows(Dataset, "sales_daily"));
        Assert.Equal(loads, BigQuery.Calls.Count(call => call.StartsWith("load ", StringComparison.Ordinal)));
        Assert.Equal($"DELETE FROM `{FakeBigQuery.KairoProject}.{Dataset}.sales_daily` WHERE date IN UNNEST(@dates);", BigQuery.Queries.Last().Sql);
    }

    [Fact]
    public async Task Days_that_are_not_being_loaded_are_untouched()
    {
        await ReplaceAsync([First, Second, Third], Sale(First, "Goa", 1), Sale(Second, "Goa", 2), Sale(Third, "Goa", 3));
        await ReplaceAsync([Second], Sale(Second, "Goa", 20));

        Assert.Equal(new[] { 1L, 20L, 3L }, Orders());
    }

    [Fact]
    public async Task A_failed_replace_keeps_the_rows_and_removes_the_staging_table()
    {
        await ReplaceAsync([First], Sale(First, "Goa", 1));
        BigQuery.FailScripts = true;

        var failure = await Assert.ThrowsAsync<GoogleApiException>(() => ReplaceAsync([First], Sale(First, "Goa", 9)));

        Assert.Equal("Google answered 400: Quota exceeded", failure.Message);
        Assert.Equal(new[] { 1L }, Orders());
        Assert.DoesNotContain(BigQuery.TableNames(Dataset), table => table.StartsWith("_staging_", StringComparison.Ordinal));
        Assert.Contains(BigQuery.Calls, call => call.StartsWith($"tables.delete {Dataset}._staging_sales_daily_", StringComparison.Ordinal));
    }

    [Fact]
    public void The_script_deletes_the_days_and_inserts_the_staged_rows_in_one_transaction()
    {
        var script = ReplaceScript.For("p", "d", TableSchemas.SalesDaily, "_staging_sales_daily_1");

        var expected = """
            BEGIN TRANSACTION;
            DELETE FROM `p.d.sales_daily` WHERE date IN UNNEST(@dates);
            INSERT INTO `p.d.sales_daily` (date, region, orders, revenue, source, loaded_at) SELECT date, region, orders, revenue, source, loaded_at FROM `p.d._staging_sales_daily_1`;
            COMMIT TRANSACTION;
            DROP TABLE `p.d._staging_sales_daily_1`;
            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), script.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_tables_have_the_documented_columns()
    {
        Assert.Equal(
            "date DATE, customer_id STRING, campaign_id STRING, region_id STRING, region_name STRING, country_code STRING, currency STRING, "
            + "impressions INT64, clicks INT64, cost NUMERIC, conversions FLOAT64, conversions_value NUMERIC, loaded_at TIMESTAMP",
            string.Join(", ", TableSchemas.AdsRegionDaily.Columns.Select(column => $"{column.Name} {column.Type}")));
        Assert.Equal(
            "date DATE, property_id STRING, country STRING, region STRING, currency STRING, sessions INT64, key_events FLOAT64, "
            + "transactions INT64, purchase_revenue NUMERIC, loaded_at TIMESTAMP",
            string.Join(", ", TableSchemas.Ga4RegionDaily.Columns.Select(column => $"{column.Name} {column.Type}")));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private Task<int> ReplaceAsync(IReadOnlyCollection<DateOnly> dates, params JsonObject[] rows) =>
        _api.Services.GetRequiredService<BigQueryTables>().ReplaceDaysAsync(_brandId, TableSchemas.SalesDaily, dates, rows, Token);

    private long[] Orders() => BigQuery.Rows(Dataset, "sales_daily")
        .OrderBy(row => row["date"]!.GetValue<string>(), StringComparer.Ordinal)
        .Select(row => row["orders"]!.GetValue<long>())
        .ToArray();

    private static JsonObject Sale(DateOnly date, string region, long orders) => new()
    {
        ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["region"] = region,
        ["orders"] = orders,
        ["revenue"] = "10.00",
        ["source"] = "upload",
    };
}
```

- [ ] **Step 5: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.BigQueryTablesTests"
```

Expected: the build fails with `CS0234: The type or namespace name 'AnalyticsOptions' does not exist in the namespace 'Kairo.Modules.Measurement.Feeds'` (and the same for `IServiceAccessToken`).

- [ ] **Step 6: Add the credentials package**

In `core/Directory.Packages.props`, replace:

```xml
  <ItemGroup Label="Google">
    <PackageVersion Include="Google.Cloud.Kms.V1" Version="3.27.0" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup Label="Google">
    <PackageVersion Include="Google.Apis.Auth" Version="1.73.0" />
    <PackageVersion Include="Google.Cloud.Kms.V1" Version="3.27.0" />
  </ItemGroup>
```

Replace `core/src/Modules/Kairo.Modules.Measurement/Kairo.Modules.Measurement.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Google.Apis.Auth" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 7: Add the options, HTTP helpers and credentials**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/AnalyticsOptions.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal sealed class AnalyticsOptions(IConfiguration configuration)
{
    public const string NotSetUp = "Analytics data isn't set up in this environment.";
    public const string DefaultLocation = "asia-south1";

    public string? Project => configuration["Kairo:Analytics:Project"] is { Length: > 0 } project ? project : null;

    public bool Enabled => Project is not null;

    public string Location => configuration["Kairo:Analytics:Location"] is { Length: > 0 } location ? location : DefaultLocation;

    public string AdsApiVersion => configuration["Kairo:Google:AdsApiVersion"] is { Length: > 0 } version ? version : "v25";

    public static string DatasetFor(Guid brandId) => $"brand_{brandId:N}";
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/MeasurementHttp.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.Modules.Measurement.Feeds;

internal static class MeasurementHttp
{
    public const string ClientName = "kairo-measurement-google";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> RequestAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        HttpContent? content,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<JsonElement> CallAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        object? body,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        using var response = await client.RequestAsync(
            method, url, accessToken, body is null ? null : JsonContent.Create(body, options: Json), cancellationToken, headers);
        return await ReadAsync(response, cancellationToken);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleApiException(response.StatusCode, text);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return document.RootElement.Clone();
    }
}

internal sealed class GoogleApiException(HttpStatusCode status, string body)
    : Exception($"Google answered {(int)status}: {MessageOf(body)}")
{
    public HttpStatusCode Status { get; } = status;

    private static string MessageOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            return Shorten(body);
        }

        return Shorten(body);
    }

    private static string Shorten(string body) => body.Length > 300 ? body[..300] : body;
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/ServiceAccessToken.cs`:

```csharp
using Google.Apis.Auth.OAuth2;

namespace Kairo.Modules.Measurement.Feeds;

internal interface IServiceAccessToken
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationDefaultAccessToken : IServiceAccessToken
{
    public const string Scope = "https://www.googleapis.com/auth/bigquery";

    private GoogleCredential? _credential;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)).CreateScoped(Scope);
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}
```

- [ ] **Step 8: Add queries, schemas and the replace**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryClient.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record QueryParameter(string Name, string Type, string? Value = null, IReadOnlyList<string>? Values = null)
{
    public static QueryParameter Date(string name, DateOnly value) => new(name, "DATE", Iso(value));

    public static QueryParameter Dates(string name, IEnumerable<DateOnly> values) => new(name, "DATE", Values: values.Select(Iso).ToList());

    public object ToJson() => Values is null
        ? new { name = Name, parameterType = new { type = Type }, parameterValue = new { value = Value } }
        : new
        {
            name = Name,
            parameterType = new { type = "ARRAY", arrayType = new { type = Type } },
            parameterValue = new { arrayValues = Values.Select(value => new { value }).ToArray() },
        };

    private static string Iso(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed class BigQueryClient(IHttpClientFactory http)
{
    public const string BaseUrl = "https://bigquery.googleapis.com/bigquery/v2";
    public const string UploadUrl = "https://bigquery.googleapis.com/upload/bigquery/v2";

    public static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);

    public async Task<IReadOnlyList<string?[]>> QueryAsync(
        string project,
        string accessToken,
        string sql,
        string location,
        IReadOnlyList<QueryParameter> parameters,
        CancellationToken cancellationToken)
    {
        var client = http.CreateClient(MeasurementHttp.ClientName);
        var projectUrl = $"{BaseUrl}/projects/{Uri.EscapeDataString(project)}";
        var page = await client.CallAsync(
            HttpMethod.Post,
            $"{projectUrl}/queries",
            accessToken,
            new
            {
                query = sql,
                useLegacySql = false,
                location,
                parameterMode = "NAMED",
                queryParameters = parameters.Select(parameter => parameter.ToJson()).ToArray(),
                timeoutMs = 60000,
            },
            cancellationToken);
        var jobId = page.GetProperty("jobReference").GetProperty("jobId").GetString()!;
        var rows = new List<string?[]>();
        while (true)
        {
            if (page.TryGetProperty("jobComplete", out var complete) && complete.ValueKind == JsonValueKind.False)
            {
                await Task.Delay(PollDelay, cancellationToken);
                page = await client.CallAsync(HttpMethod.Get, ResultsUrl(projectUrl, jobId, location, null), accessToken, null, cancellationToken);
                continue;
            }

            if (page.TryGetProperty("rows", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                rows.AddRange(list.EnumerateArray().Select(row => row.GetProperty("f").EnumerateArray()
                    .Select(cell => cell.GetProperty("v") is { ValueKind: JsonValueKind.String } value ? value.GetString() : null)
                    .ToArray()));
            }

            if (!page.TryGetProperty("pageToken", out var token) || token.ValueKind != JsonValueKind.String || token.GetString() is not { Length: > 0 } next)
            {
                return rows;
            }

            page = await client.CallAsync(HttpMethod.Get, ResultsUrl(projectUrl, jobId, location, next), accessToken, null, cancellationToken);
        }
    }

    private static string ResultsUrl(string projectUrl, string jobId, string location, string? pageToken) =>
        QueryHelpers.AddQueryString(
            $"{projectUrl}/queries/{Uri.EscapeDataString(jobId)}",
            new Dictionary<string, string?> { ["location"] = location, ["timeoutMs"] = "60000", ["pageToken"] = pageToken });
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/TableSchemas.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal sealed record TableColumn(string Name, string Type);

internal sealed record TableSchema(string Name, IReadOnlyList<TableColumn> Columns)
{
    public object SchemaJson() => new
    {
        fields = Columns.Select(column => new { name = column.Name, type = column.Type, mode = column.Name == "date" ? "REQUIRED" : "NULLABLE" }).ToArray(),
    };
}

internal static class TableSchemas
{
    private static readonly TableColumn[] Metrics =
    [
        new("impressions", "INT64"),
        new("clicks", "INT64"),
        new("cost", "NUMERIC"),
        new("conversions", "FLOAT64"),
        new("conversions_value", "NUMERIC"),
    ];

    public static readonly TableSchema AdsCampaignDaily = Of(
        "ads_campaign_daily",
        [Text("customer_id"), Text("campaign_id"), Text("campaign_name"), Text("channel"), Text("campaign_status"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsAdGroupDaily = Of(
        "ads_ad_group_daily",
        [Text("customer_id"), Text("campaign_id"), Text("ad_group_id"), Text("ad_group_name"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsAdDaily = Of(
        "ads_ad_daily",
        [Text("customer_id"), Text("campaign_id"), Text("ad_group_id"), Text("ad_id"), Text("ad_type"), Text("currency"), .. Metrics]);

    public static readonly TableSchema AdsRegionDaily = Of(
        "ads_region_daily",
        [Text("customer_id"), Text("campaign_id"), Text("region_id"), Text("region_name"), Text("country_code"), Text("currency"), .. Metrics]);

    public static readonly TableSchema Ga4RegionDaily = Of(
        "ga4_region_daily",
        [
            Text("property_id"), Text("country"), Text("region"), Text("currency"),
            new("sessions", "INT64"), new("key_events", "FLOAT64"), new("transactions", "INT64"), new("purchase_revenue", "NUMERIC"),
        ]);

    public static readonly TableSchema SalesDaily = Of(
        "sales_daily",
        [Text("region"), new("orders", "INT64"), new("revenue", "NUMERIC"), Text("source")]);

    private static TableColumn Text(string name) => new(name, "STRING");

    private static TableSchema Of(string name, TableColumn[] columns) =>
        new(name, [new("date", "DATE"), .. columns, new("loaded_at", "TIMESTAMP")]);
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryTables.cs`:

```csharp
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal static class ReplaceScript
{
    public static string For(string project, string dataset, TableSchema table, string? staging)
    {
        var target = $"`{project}.{dataset}.{table.Name}`";
        var delete = $"DELETE FROM {target} WHERE date IN UNNEST(@dates);";
        if (staging is null)
        {
            return delete;
        }

        var columns = string.Join(", ", table.Columns.Select(column => column.Name));
        var source = $"`{project}.{dataset}.{staging}`";
        return $"""
            BEGIN TRANSACTION;
            {delete}
            INSERT INTO {target} ({columns}) SELECT {columns} FROM {source};
            COMMIT TRANSACTION;
            DROP TABLE {source};
            """;
    }
}

internal sealed class BigQueryTables(
    IHttpClientFactory http,
    BigQueryClient bigQuery,
    IServiceAccessToken serviceToken,
    AnalyticsOptions options,
    TimeProvider time,
    ILogger<BigQueryTables> logger)
{
    private readonly ConcurrentDictionary<string, bool> _ready = new();

    public async Task<int> ReplaceDaysAsync(
        Guid brandId,
        TableSchema table,
        IReadOnlyCollection<DateOnly> dates,
        IReadOnlyList<JsonObject> rows,
        CancellationToken cancellationToken)
    {
        var project = options.Project ?? throw new InvalidOperationException(AnalyticsOptions.NotSetUp);
        var dataset = AnalyticsOptions.DatasetFor(brandId);
        var token = await serviceToken.GetAsync(cancellationToken);
        var client = http.CreateClient(MeasurementHttp.ClientName);
        await EnsureTableAsync(client, token, project, dataset, table, cancellationToken);

        string? staging = null;
        try
        {
            if (rows.Count > 0)
            {
                staging = $"_staging_{table.Name}_{Guid.NewGuid():N}";
                await LoadAsync(client, token, project, dataset, staging, table, rows, cancellationToken);
            }

            await bigQuery.QueryAsync(
                project,
                token,
                ReplaceScript.For(project, dataset, table, staging),
                options.Location,
                [QueryParameter.Dates("dates", dates.Order())],
                cancellationToken);
            return rows.Count;
        }
        catch when (staging is not null)
        {
            await DropQuietlyAsync(client, token, project, dataset, staging);
            throw;
        }
    }

    private async Task EnsureTableAsync(HttpClient client, string token, string project, string dataset, TableSchema table, CancellationToken cancellationToken)
    {
        var datasetKey = $"{project}.{dataset}";
        if (!_ready.ContainsKey(datasetKey))
        {
            await CreateAsync(
                client,
                token,
                $"{BigQueryClient.BaseUrl}/projects/{project}/datasets",
                new { datasetReference = new { projectId = project, datasetId = dataset }, location = options.Location },
                cancellationToken);
            _ready[datasetKey] = true;
        }

        var tableKey = $"{datasetKey}.{table.Name}";
        if (!_ready.ContainsKey(tableKey))
        {
            await CreateAsync(
                client,
                token,
                $"{BigQueryClient.BaseUrl}/projects/{project}/datasets/{dataset}/tables",
                new
                {
                    tableReference = new { projectId = project, datasetId = dataset, tableId = table.Name },
                    schema = table.SchemaJson(),
                    timePartitioning = new { type = "DAY", field = "date" },
                },
                cancellationToken);
            _ready[tableKey] = true;
        }
    }

    private static async Task CreateAsync(HttpClient client, string token, string url, object body, CancellationToken cancellationToken)
    {
        using var response = await client.RequestAsync(HttpMethod.Post, url, token, JsonContent.Create(body, options: MeasurementHttp.Json), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            await MeasurementHttp.ReadAsync(response, cancellationToken);
        }
    }

    private async Task LoadAsync(
        HttpClient client,
        string token,
        string project,
        string dataset,
        string staging,
        TableSchema table,
        IReadOnlyList<JsonObject> rows,
        CancellationToken cancellationToken)
    {
        var loadedAt = time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
        var lines = new StringBuilder();
        foreach (var row in rows)
        {
            row["loaded_at"] = loadedAt;
            lines.Append(row.ToJsonString()).Append('\n');
        }

        var jobId = $"kairo_load_{Guid.NewGuid():N}";
        var job = new
        {
            jobReference = new { projectId = project, jobId, location = options.Location },
            configuration = new
            {
                load = new
                {
                    destinationTable = new { projectId = project, datasetId = dataset, tableId = staging },
                    schema = table.SchemaJson(),
                    sourceFormat = "NEWLINE_DELIMITED_JSON",
                    writeDisposition = "WRITE_TRUNCATE",
                    createDisposition = "CREATE_IF_NEEDED",
                },
            },
        };
        var data = new ByteArrayContent(Encoding.UTF8.GetBytes(lines.ToString()));
        data.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var content = new MultipartContent("related")
        {
            new StringContent(JsonSerializer.Serialize(job, MeasurementHttp.Json), Encoding.UTF8, "application/json"),
            data,
        };

        using var response = await client.RequestAsync(
            HttpMethod.Post, $"{BigQueryClient.UploadUrl}/projects/{project}/jobs?uploadType=multipart", token, content, cancellationToken);
        var state = await MeasurementHttp.ReadAsync(response, cancellationToken);
        while (true)
        {
            var status = state.GetProperty("status");
            if (status.TryGetProperty("errorResult", out var error))
            {
                throw new GoogleApiException(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { error }));
            }

            if (status.GetProperty("state").GetString() == "DONE")
            {
                return;
            }

            await Task.Delay(BigQueryClient.PollDelay, cancellationToken);
            state = await client.CallAsync(
                HttpMethod.Get, $"{BigQueryClient.BaseUrl}/projects/{project}/jobs/{jobId}?location={options.Location}", token, null, cancellationToken);
        }
    }

    private async Task DropQuietlyAsync(HttpClient client, string token, string project, string dataset, string staging)
    {
        try
        {
            using var response = await client.RequestAsync(
                HttpMethod.Delete, $"{BigQueryClient.BaseUrl}/projects/{project}/datasets/{dataset}/tables/{staging}", token, null, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove the staging table {Staging} in {Dataset}.", staging, dataset);
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedSetup
{
    public static void AddFeeds(this IServiceCollection services)
    {
        services.AddHttpClient(MeasurementHttp.ClientName);
        services.AddSingleton<AnalyticsOptions>();
        services.AddSingleton<IServiceAccessToken, ApplicationDefaultAccessToken>();
        services.AddSingleton<BigQueryClient>();
        services.AddSingleton<BigQueryTables>();
    }
}
```

In `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`:
- Add `using Kairo.Modules.Measurement.Feeds;` below `using Kairo.Modules.Measurement.Data;`.
- In `AddServices`, after the `AddDbContext` call, add:

  ```csharp
          services.AddFeeds();
  ```

- [ ] **Step 9: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.BigQueryTablesTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
dotnet test --solution core/Kairo.slnx
```

Expected: all pass with 0 warnings. The existing connection tests still pass, because the fake BigQuery returns `null` for the routes they use (`GET /bigquery/v2/projects`, `GET …/acme-data/datasets` and `POST …/acme-data/jobs`).

- [ ] **Step 10: Commit**

```bash
git add core/Directory.Packages.props \
  core/src/Modules/Kairo.Modules.Measurement/Kairo.Modules.Measurement.csproj \
  core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/AnalyticsOptions.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/MeasurementHttp.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/ServiceAccessToken.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryClient.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/TableSchemas.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryTables.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/tests/Kairo.IntegrationTests/Api/FakeBigQuery.cs \
  core/tests/Kairo.IntegrationTests/Api/FakeServiceToken.cs \
  core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs \
  core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs \
  core/tests/Kairo.IntegrationTests/Feeds/BigQueryTablesTests.cs
git commit -m "Replace whole days in a brand's BigQuery tables through a staging table"
```

---

### Task 4: Sales uploads

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedClock.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/FeedClockTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/SalesFiles.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/SalesUploadTests.cs`

**Interfaces:**
- Consumes: `SalesCsv.Parse`, `SalesRow` (Task 2); `BigQueryTables.ReplaceDaysAsync`, `TableSchemas.SalesDaily`, `AnalyticsOptions` (Task 3); `MeasurementDbContext.SalesUploads` (Task 1).
- Produces:
  - `FeedClock.IndiaTimeZone = "Asia/Kolkata"`, `FeedClock.Today(TimeProvider, string? timeZoneId)` (unknown or empty zones fall back to India), `FeedClock.Iso(DateOnly)`.
  - `record FeedWindow(DateOnly From, DateOnly To)` with `FeedWindow.For(DateOnly today, bool firstRun)` and `IReadOnlyList<DateOnly> Days()`.
  - `SalesRows.Upload = "upload"`, `SalesRows.Table = "table"`, `SalesRows.Of(SalesRow, string source)` returning the `sales_daily` row.
  - `record SalesUploadView(Guid Id, string FileName, int Rows, DateOnly FirstDate, DateOnly LastDate, int Days, Guid UploadedBy, DateTimeOffset UploadedAt)`.
  - `SalesEndpoints.MaxFileBytes`, `UploadAsync`, `ListUploadsAsync`.
  - `FeedSetup.MapFeeds(IEndpointRouteBuilder api)`, which later tasks extend. Routes:
    - `POST /api/brands/{brandId}/analytics/sales/uploads` (admins and marketers, multipart field `file`) → `201`, `400` with `errors.file`, or `503`
    - `GET /api/brands/{brandId}/analytics/sales/uploads` (members) → the latest 50, newest first
  - Tests: `SalesFiles.Header`, `SalesFiles.Csv(string text, string fileName = "sales.csv")`, `SalesFiles.RowsOf(FakeBigQuery, Guid brandId)` returning `"date region orders revenue source"` strings in order.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/FeedClockTests.cs`:

```csharp
using Kairo.Modules.Measurement.Feeds;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.IntegrationTests.Feeds;

public sealed class FeedClockTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("Asia/Kolkata", "2026-10-14")]
    [InlineData("America/Los_Angeles", "2026-10-13")]
    [InlineData("Mars/Olympus", "2026-10-14")]
    [InlineData("", "2026-10-14")]
    [InlineData(null, "2026-10-14")]
    public void Today_is_the_date_in_the_time_zone(string? timeZone, string expected)
    {
        Assert.Equal(expected, FeedClock.Iso(FeedClock.Today(_time, timeZone)));
    }

    [Fact]
    public void A_window_covers_the_7_days_through_yesterday()
    {
        var window = FeedWindow.For(new DateOnly(2026, 10, 14), firstRun: false);

        Assert.Equal(new FeedWindow(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13)), window);
        Assert.Equal(7, window.Days().Count);
        Assert.Equal(new DateOnly(2026, 10, 13), window.Days()[^1]);
    }

    [Fact]
    public void A_first_window_reaches_back_13_months()
    {
        var window = FeedWindow.For(new DateOnly(2026, 10, 14), firstRun: true);

        Assert.Equal(new DateOnly(2025, 9, 14), window.From);
        Assert.Equal(new DateOnly(2026, 10, 13), window.To);
        Assert.Equal(395, window.Days().Count);
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Feeds/SalesFiles.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

internal static class SalesFiles
{
    public const string Header = "date,region,orders,revenue\n";

    public static MultipartFormDataContent Csv(string text, string fileName = "sales.csv")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    public static string[] RowsOf(FakeBigQuery bigQuery, Guid brandId) => bigQuery.Rows(AnalyticsOptions.DatasetFor(brandId), "sales_daily")
        .Select(row => string.Join(
            ' ',
            row["date"]!.GetValue<string>(),
            row["region"]!.GetValue<string>(),
            row["orders"]!.GetValue<long>(),
            row["revenue"]!.GetValue<string>(),
            row["source"]!.GetValue<string>()))
        .Order(StringComparer.Ordinal)
        .ToArray();
}
```

Create `core/tests/Kairo.IntegrationTests/Feeds/SalesUploadTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class SalesUploadTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string OneRow = SalesFiles.Header + "2026-10-01,Goa,1,10\n";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_upload_loads_the_file_and_is_recorded()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(
            Uploads(brandId),
            SalesFiles.Csv(SalesFiles.Header + "2026-10-01,Goa,3,450.50\n2026-10-02,Goa,1,99\n2026-10-02,Kerala,2,10\n", "october.csv"),
            Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var upload = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("october.csv", upload.GetProperty("fileName").GetString());
        Assert.Equal(3, upload.GetProperty("rows").GetInt32());
        Assert.Equal("2026-10-01", upload.GetProperty("firstDate").GetString());
        Assert.Equal("2026-10-02", upload.GetProperty("lastDate").GetString());
        Assert.Equal(2, upload.GetProperty("days").GetInt32());
        Assert.Equal(admin.UserId, upload.GetProperty("uploadedBy").GetGuid());
        Assert.Equal(
            new[] { "2026-10-01 Goa 3 450.50 upload", "2026-10-02 Goa 1 99 upload", "2026-10-02 Kerala 2 10 upload" },
            SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));

        var listed = await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token);
        Assert.Equal(upload.GetProperty("id").GetGuid(), Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Contains("analytics.sales_uploaded", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Theory]
    [InlineData("marketer", HttpStatusCode.Created)]
    [InlineData("approver", HttpStatusCode.Forbidden)]
    public async Task Admins_and_marketers_can_upload_but_approvers_cannot(string role, HttpStatusCode expected)
    {
        var (admin, brandId) = await WorkspaceAsync();
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var response = await member.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(OneRow), Token);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_file_over_10_MB_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = OneRow + new string(' ', (int)SalesEndpoints.MaxFileBytes);

        var response = await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(huge), Token);

        Assert.Equal(new[] { "the file is larger than 10 MB" }, await FileErrorsAsync(response));
        Assert.Empty(SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));
    }

    [Fact]
    public async Task An_invalid_file_lists_its_problems_and_loads_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(
            Uploads(brandId), SalesFiles.Csv(SalesFiles.Header + "2026-10-01,Goa,x,1\n2026-10-01,,1,1\n"), Token);

        Assert.Equal(
            new[] { "row 2: orders must be a whole number, 0 or more", "row 3: region is empty" },
            await FileErrorsAsync(response));
        Assert.Empty(SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token)).EnumerateArray());
    }

    [Fact]
    public async Task A_request_without_a_file_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(
            Uploads(brandId), new MultipartFormDataContent { { new StringContent("x"), "notes" } }, Token);

        Assert.Equal(new[] { "Choose a CSV file to upload." }, await FileErrorsAsync(response));
    }

    [Fact]
    public async Task A_new_upload_replaces_only_the_dates_it_contains()
    {
        var (admin, brandId) = await WorkspaceAsync();
        (await admin.Browser.PostAsync(
            Uploads(brandId),
            SalesFiles.Csv(SalesFiles.Header + "2026-10-01,Goa,1,1\n2026-10-02,Goa,2,2\n2026-10-03,Goa,3,3\n"),
            Token)).EnsureSuccessStatusCode();

        (await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(SalesFiles.Header + "2026-10-02,Goa,20,20\n"), Token))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            new[] { "2026-10-01 Goa 1 1 upload", "2026-10-02 Goa 20 20 upload", "2026-10-03 Goa 3 3 upload" },
            SalesFiles.RowsOf(_api.GoogleApis.BigQuery, brandId));
        Assert.Equal(2, (await admin.Browser.GetFromJsonAsync<JsonElement>(Uploads(brandId), Token)).GetArrayLength());
    }

    [Fact]
    public async Task Without_an_analytics_project_uploads_are_unavailable()
    {
        await using var api = new KairoApiFactory(sqlServer.KairoConnectionString) { AnalyticsProject = null };
        var admin = await Workspaces.SignInAsync(api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var response = await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(OneRow), Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_uploads()
    {
        var (admin, brandId) = await WorkspaceAsync();
        (await admin.Browser.PostAsync(Uploads(brandId), SalesFiles.Csv(OneRow), Token)).EnsureSuccessStatusCode();
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(Uploads(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Uploads(Guid brandId) => $"/api/brands/{brandId}/analytics/sales/uploads";

    private static async Task<string[]> FileErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").GetProperty("file").EnumerateArray().Select(error => error.GetString()!).ToArray();
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.SalesUploadTests"
```

Expected: the build fails with `CS0103: The name 'FeedClock' does not exist in the current context` and `CS0103: The name 'SalesEndpoints' does not exist in the current context`.

- [ ] **Step 3: Add the clock and the window**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedClock.cs`:

```csharp
using System.Globalization;

namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedClock
{
    public const string IndiaTimeZone = "Asia/Kolkata";

    public static DateOnly Today(TimeProvider time, string? timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone(timeZoneId)).DateTime);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TimeZoneInfo Zone(string? timeZoneId) =>
        !string.IsNullOrEmpty(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(IndiaTimeZone);
}

internal sealed record FeedWindow(DateOnly From, DateOnly To)
{
    public static FeedWindow For(DateOnly today, bool firstRun) =>
        new(firstRun ? today.AddMonths(-13) : today.AddDays(-7), today.AddDays(-1));

    public IReadOnlyList<DateOnly> Days()
    {
        var days = new List<DateOnly>();
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }
}
```

- [ ] **Step 4: Add the upload endpoints**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesEndpoints.cs`:

```csharp
using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record SalesUploadView(
    Guid Id,
    string FileName,
    int Rows,
    DateOnly FirstDate,
    DateOnly LastDate,
    int Days,
    Guid UploadedBy,
    DateTimeOffset UploadedAt)
{
    public static SalesUploadView Of(SalesUpload upload) => new(
        upload.Id, upload.FileName, upload.Rows, upload.FirstDate, upload.LastDate, upload.Days, upload.UploadedBy, upload.UploadedAt);
}

internal static class SalesRows
{
    public const string Upload = "upload";
    public const string Table = "table";

    public static JsonObject Of(SalesRow row, string source) => new()
    {
        ["date"] = FeedClock.Iso(row.Date),
        ["region"] = row.Region,
        ["orders"] = row.Orders,
        ["revenue"] = row.Revenue.ToString(CultureInfo.InvariantCulture),
        ["source"] = source,
    };
}

internal static class SalesEndpoints
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const string ChooseFile = "Choose a CSV file to upload.";
    public const string TooLarge = "the file is larger than 10 MB";
    public const int ListedUploads = 50;

    public static async Task<Results<Created<SalesUploadView>, ValidationProblem, ProblemHttpResult>> UploadAsync(
        IFormFile? file,
        MeasurementDbContext db,
        BigQueryTables tables,
        AnalyticsOptions options,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AnalyticsOptions.NotSetUp);
        }

        if (file is null)
        {
            return FileProblem([ChooseFile]);
        }

        if (file.Length > MaxFileBytes)
        {
            return FileProblem([TooLarge]);
        }

        SalesFile parsed;
        await using (var stream = file.OpenReadStream())
        {
            parsed = SalesCsv.Parse(stream, FeedClock.Today(time, FeedClock.IndiaTimeZone));
        }

        if (!parsed.IsValid)
        {
            return FileProblem(parsed.Errors);
        }

        var dates = parsed.Rows.Select(row => row.Date).Distinct().Order().ToList();
        await tables.ReplaceDaysAsync(
            member.BrandId,
            TableSchemas.SalesDaily,
            dates,
            parsed.Rows.Select(row => SalesRows.Of(row, SalesRows.Upload)).ToList(),
            cancellationToken);

        var name = Path.GetFileName(file.FileName);
        var upload = new SalesUpload
        {
            Id = Guid.NewGuid(),
            FileName = name.Length == 0 ? "sales.csv" : name.Length > 200 ? name[..200] : name,
            Rows = parsed.Rows.Count,
            FirstDate = dates[0],
            LastDate = dates[^1],
            Days = dates.Count,
            UploadedBy = member.UserId,
            UploadedAt = time.GetUtcNow(),
        };
        db.SalesUploads.Add(upload);
        await db.SaveChangesAsync(cancellationToken);

        var view = SalesUploadView.Of(upload);
        await audit.RecordAsync(new AuditRecord("analytics.sales_uploaded", "sales_upload", upload.Id.ToString(), After: view), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/analytics/sales/uploads", view);
    }

    public static async Task<Ok<SalesUploadView[]>> ListUploadsAsync(MeasurementDbContext db, CancellationToken cancellationToken)
    {
        var uploads = await db.SalesUploads
            .OrderByDescending(upload => upload.UploadedAt)
            .Take(ListedUploads)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(uploads.Select(SalesUploadView.Of).ToArray());
    }

    private static ValidationProblem FileProblem(IReadOnlyList<string> errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = errors.ToArray() });
}
```

- [ ] **Step 5: Map the routes**

Replace `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs` with:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedSetup
{
    public static void AddFeeds(this IServiceCollection services)
    {
        services.AddHttpClient(MeasurementHttp.ClientName);
        services.AddSingleton<AnalyticsOptions>();
        services.AddSingleton<IServiceAccessToken, ApplicationDefaultAccessToken>();
        services.AddSingleton<BigQueryClient>();
        services.AddSingleton<BigQueryTables>();
    }

    public static void MapFeeds(this IEndpointRouteBuilder api)
    {
        var analytics = api.MapBrandApi().MapGroup("/analytics");
        var sales = analytics.MapGroup("/sales");
        sales.MapPost("/uploads", SalesEndpoints.UploadAsync)
            .RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer)
            .DisableAntiforgery();
        sales.MapGet("/uploads", SalesEndpoints.ListUploadsAsync);
    }
}
```

`DisableAntiforgery` is needed because binding an `IFormFile` adds antiforgery metadata, and the API has no antiforgery middleware. The `X-Kairo-Request` header already protects every write.

In `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`, replace the empty `MapApi` with:

```csharp
    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapFeeds();
    }
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.FeedClockTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.SalesUploadTests"
```

Expected: all pass with 0 warnings.

- [ ] **Step 7: Regenerate the API contract and run everything**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --solution core/Kairo.slnx
npm --prefix web run lint
```

Expected: `openapi.json` gains the two `/analytics/sales/uploads` operations, the upload with a `multipart/form-data` body. `schema.ts` changes to match. Everything passes.

- [ ] **Step 8: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedClock.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesEndpoints.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs \
  core/tests/Kairo.IntegrationTests/Feeds/FeedClockTests.cs \
  core/tests/Kairo.IntegrationTests/Feeds/SalesFiles.cs \
  core/tests/Kairo.IntegrationTests/Feeds/SalesUploadTests.cs
git commit -m "Let admins and marketers upload sales as CSV files"
git add core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Add the sales upload endpoints to the API contract"
```

---

### Task 5: The brand's own sales table

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryTableCheck.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/SalesSourceTests.cs`

**Interfaces:**
- Consumes: `IConnections` (`GetStatusAsync`, `GetAccessAsync`) and `ConnectionUnavailableException` from `Kairo.Contracts.Connections`; `MeasurementHttp`, `BigQueryClient.BaseUrl` (Task 3); `SalesSource` (Task 1).
- Produces:
  - `record TableRef(string Project, string Dataset, string Table)` with `static TableRef? Parse(string?)` and `ToString()` = `project.dataset.table`.
  - `record TableField(string Name, string Type, string Mode)`, `record TableInfo(string Location, IReadOnlyList<TableField> Fields)`.
  - `BigQueryTableCheck.FindAsync(TableRef, string accessToken, CancellationToken)` returning `null` when Google answers 403 or 404.
  - `SalesColumnRules.Types`, `SalesColumnRules.IsSafeName(string?)`, `SalesColumnRules.Problem(string role, string column, TableInfo table)`.
  - `record SalesColumns(string Date, string Region, string Orders, string Revenue)`, `record SalesSourceRequest(string? Table, SalesColumnsRequest? Columns)`, `record SalesSourceView(string Table, SalesColumns Columns, string Location, Guid SetBy, DateTimeOffset SetAt)`.
  - Routes, all under `/api/brands/{brandId}/analytics/sales/source`:
    - `GET` (members) → `200` with the source, or `204`
    - `PUT` (admins) → `200`, `400` with `errors.table` or `errors.columns.<role>`, or `409`
    - `DELETE` (admins) → `204`

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/SalesSourceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Feeds;

public sealed class SalesSourceTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string CannotRead = "Kairo can't read this table with the connected Google account.";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_admin_chooses_a_table_after_Kairo_checks_it()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("acme-data.shop.daily_orders", saved.GetProperty("table").GetString());
        Assert.Equal("order_date", saved.GetProperty("columns").GetProperty("date").GetString());
        Assert.Equal("net_revenue", saved.GetProperty("columns").GetProperty("revenue").GetString());
        Assert.Equal("asia-south1", saved.GetProperty("location").GetString());
        Assert.Equal(admin.UserId, saved.GetProperty("setBy").GetGuid());

        var read = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal("acme-data.shop.daily_orders", read.GetProperty("table").GetString());
        Assert.Contains("analytics.sales_source_set", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task No_table_reads_as_no_content()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync(Source(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Without_the_BigQuery_connection_a_table_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("daily_orders")]
    [InlineData("acme-data.shop")]
    [InlineData("acme data.shop.daily_orders")]
    [InlineData("acme-data.shop.daily-orders")]
    [InlineData("acme-data.shop.daily_orders.extra")]
    [InlineData("acme-data.shop.`daily_orders`")]
    public async Task A_name_that_is_not_project_dataset_table_is_refused(string table)
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(table: table), Token));

        Assert.True(errors.ContainsKey("table"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_table_Kairo_cannot_read_is_refused(HttpStatusCode answer)
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();
        _api.GoogleApis.BigQuery.BrandTableRead = answer;

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token));

        Assert.Equal(new[] { CannotRead }, errors["table"]);
    }

    [Fact]
    public async Task A_table_that_does_not_exist_is_refused()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(table: "acme-data.shop.missing"), Token));

        Assert.Equal(new[] { CannotRead }, errors["table"]);
    }

    [Fact]
    public async Task A_missing_column_is_refused()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(orders: "order_total"), Token));

        Assert.Equal(new[] { "The table has no column named order_total." }, errors["columns.orders"]);
    }

    [Fact]
    public async Task A_column_of_the_wrong_type_is_refused()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(date: "state"), Token));

        Assert.Equal(new[] { "state is a STRING column, but date needs DATE, DATETIME or TIMESTAMP." }, errors["columns.date"]);
    }

    [Theory]
    [InlineData("net`revenue")]
    [InlineData("net revenue")]
    [InlineData("1revenue")]
    [InlineData("")]
    public async Task A_column_name_that_cannot_be_quoted_safely_is_refused(string revenue)
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();

        var errors = await ErrorsAsync(await admin.Browser.PutAsJsonAsync(Source(brandId), Body(revenue: revenue), Token));

        Assert.True(errors.ContainsKey("columns.revenue"));
    }

    [Fact]
    public async Task Removing_the_table_stops_copying_and_is_safe_to_repeat()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();
        (await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token)).EnsureSuccessStatusCode();

        var first = await admin.Browser.DeleteAsync(Source(brandId), Token);
        var second = await admin.Browser.DeleteAsync(Source(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Single(await Workspaces.AuditActionsAsync(admin.Browser, brandId), action => action == "analytics.sales_source_removed");
    }

    [Fact]
    public async Task Another_workspace_cannot_read_or_change_the_table()
    {
        var (admin, brandId) = await ConnectedWorkspaceAsync();
        (await admin.Browser.PutAsJsonAsync(Source(brandId), Body(), Token)).EnsureSuccessStatusCode();
        var (outsider, _) = await WorkspaceAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Browser.DeleteAsync(Source(brandId), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Source(Guid brandId) => $"/api/brands/{brandId}/analytics/sales/source";

    private static object Body(
        string table = "acme-data.shop.daily_orders",
        string date = "order_date",
        string region = "state",
        string orders = "order_count",
        string revenue = "net_revenue") =>
        new { table, columns = new { date, region, orders, revenue } };

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").EnumerateObject()
            .ToDictionary(error => error.Name, error => error.Value.EnumerateArray().Select(message => message.GetString()!).ToArray());
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<(Member Admin, Guid BrandId)> ConnectedWorkspaceAsync()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");
        return (admin, brandId);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.SalesSourceTests"
```

Expected: every test fails with `404 Not Found` where it expected another status, because the `/analytics/sales/source` routes don't exist yet.

- [ ] **Step 3: Add the table check**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryTableCheck.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed partial record TableRef(string Project, string Dataset, string Table)
{
    public static TableRef? Parse(string? value)
    {
        var match = Pattern().Match(value ?? "");
        return match.Success
            ? new TableRef(match.Groups["project"].Value, match.Groups["dataset"].Value, match.Groups["table"].Value)
            : null;
    }

    public override string ToString() => $"{Project}.{Dataset}.{Table}";

    [GeneratedRegex("^(?<project>[A-Za-z0-9_-]+)\\.(?<dataset>[A-Za-z0-9_-]+)\\.(?<table>[A-Za-z0-9_]+)$")]
    private static partial Regex Pattern();
}

internal sealed record TableField(string Name, string Type, string Mode);

internal sealed record TableInfo(string Location, IReadOnlyList<TableField> Fields);

internal sealed class BigQueryTableCheck(IHttpClientFactory http)
{
    public async Task<TableInfo?> FindAsync(TableRef table, string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(MeasurementHttp.ClientName).RequestAsync(
            HttpMethod.Get,
            $"{BigQueryClient.BaseUrl}/projects/{table.Project}/datasets/{table.Dataset}/tables/{table.Table}",
            accessToken,
            null,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        var resource = await MeasurementHttp.ReadAsync(response, cancellationToken);
        var fields = new List<TableField>();
        if (resource.TryGetProperty("schema", out var schema) && schema.TryGetProperty("fields", out var list))
        {
            fields.AddRange(list.EnumerateArray().Select(field => new TableField(
                field.GetProperty("name").GetString()!,
                field.GetProperty("type").GetString()!,
                field.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString()! : "NULLABLE")));
        }

        return new TableInfo(resource.GetProperty("location").GetString()!, fields);
    }
}

internal static partial class SalesColumnRules
{
    public static readonly IReadOnlyDictionary<string, string[]> Types = new Dictionary<string, string[]>
    {
        ["date"] = ["DATE", "DATETIME", "TIMESTAMP"],
        ["region"] = ["STRING"],
        ["orders"] = ["INTEGER", "INT64", "NUMERIC", "BIGNUMERIC"],
        ["revenue"] = ["NUMERIC", "BIGNUMERIC", "FLOAT", "FLOAT64", "INTEGER", "INT64"],
    };

    public static bool IsSafeName(string? name) => name is not null && ColumnName().IsMatch(name);

    public static string? Problem(string role, string column, TableInfo table)
    {
        var field = table.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return $"The table has no column named {column}.";
        }

        var allowed = Types[role];
        return field.Mode != "REPEATED" && allowed.Contains(field.Type)
            ? null
            : $"{column} is a {field.Type} column, but {role} needs {Readable(allowed)}.";
    }

    private static string Readable(string[] types) =>
        types.Length == 1 ? types[0] : $"{string.Join(", ", types[..^1])} or {types[^1]}";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ColumnName();
}
```

- [ ] **Step 4: Add the source endpoints**

In `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesEndpoints.cs`:
- Add `using Kairo.Contracts.Connections;` below `using Kairo.Contracts.Audit;`.
- Above `internal static class SalesRows`, add:

  ```csharp
  internal sealed record SalesColumns(string Date, string Region, string Orders, string Revenue);

  internal sealed record SalesColumnsRequest(string? Date, string? Region, string? Orders, string? Revenue);

  internal sealed record SalesSourceRequest(string? Table, SalesColumnsRequest? Columns);

  internal sealed record SalesSourceView(string Table, SalesColumns Columns, string Location, Guid SetBy, DateTimeOffset SetAt)
  {
      public static SalesSourceView Of(SalesSource source) => new(
          source.TableRef,
          new SalesColumns(source.DateColumn, source.RegionColumn, source.OrdersColumn, source.RevenueColumn),
          source.Location,
          source.SetBy,
          source.SetAt);
  }
  ```

- Below `public const int ListedUploads = 50;`, add:

  ```csharp
      public const string ConnectBigQuery = "Connect BigQuery before choosing a sales table.";
      public const string BadTable = "Use project.dataset.table, made of letters, digits, underscores and hyphens.";
      public const string CannotRead = "Kairo can't read this table with the connected Google account.";
      public const string BadColumn = "Use a column name made of letters, digits and underscores.";
  ```

- Below `ListUploadsAsync`, add:

  ```csharp
      public static async Task<Results<Ok<SalesSourceView>, NoContent>> GetSourceAsync(MeasurementDbContext db, CancellationToken cancellationToken)
      {
          if (await db.SalesSources.SingleOrDefaultAsync(cancellationToken) is { } source)
          {
              return TypedResults.Ok(SalesSourceView.Of(source));
          }

          return TypedResults.NoContent();
      }

      public static async Task<Results<Ok<SalesSourceView>, ValidationProblem, ProblemHttpResult>> SetSourceAsync(
          SalesSourceRequest request,
          MeasurementDbContext db,
          IConnections connections,
          BigQueryTableCheck check,
          CurrentMember member,
          IAuditLog audit,
          TimeProvider time,
          CancellationToken cancellationToken)
      {
          if ((await connections.GetStatusAsync(ConnectedProduct.BigQuery, cancellationToken)).State != ConnectionState.Connected)
          {
              return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: ConnectBigQuery);
          }

          var errors = new Dictionary<string, string[]>();
          var table = TableRef.Parse(request.Table);
          if (table is null)
          {
              errors["table"] = [BadTable];
          }

          var columns = new Dictionary<string, string?>
          {
              ["date"] = request.Columns?.Date,
              ["region"] = request.Columns?.Region,
              ["orders"] = request.Columns?.Orders,
              ["revenue"] = request.Columns?.Revenue,
          };
          foreach (var (role, column) in columns)
          {
              if (!SalesColumnRules.IsSafeName(column))
              {
                  errors[$"columns.{role}"] = [BadColumn];
              }
          }

          if (errors.Count > 0)
          {
              return TypedResults.ValidationProblem(errors);
          }

          ConnectionAccess access;
          try
          {
              access = await connections.GetAccessAsync(ConnectedProduct.BigQuery, cancellationToken);
          }
          catch (ConnectionUnavailableException)
          {
              return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: ConnectBigQuery);
          }

          if (await check.FindAsync(table!, access.AccessToken, cancellationToken) is not { } info)
          {
              return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["table"] = [CannotRead] });
          }

          foreach (var (role, column) in columns)
          {
              if (SalesColumnRules.Problem(role, column!, info) is { } problem)
              {
                  errors[$"columns.{role}"] = [problem];
              }
          }

          if (errors.Count > 0)
          {
              return TypedResults.ValidationProblem(errors);
          }

          var source = await db.SalesSources.SingleOrDefaultAsync(cancellationToken);
          var before = source is null ? null : SalesSourceView.Of(source);
          if (source is null)
          {
              source = new SalesSource();
              db.SalesSources.Add(source);
          }

          source.TableRef = table!.ToString();
          source.DateColumn = columns["date"]!;
          source.RegionColumn = columns["region"]!;
          source.OrdersColumn = columns["orders"]!;
          source.RevenueColumn = columns["revenue"]!;
          source.Location = info.Location;
          source.SetBy = member.UserId;
          source.SetAt = time.GetUtcNow();
          await db.SaveChangesAsync(cancellationToken);

          var after = SalesSourceView.Of(source);
          await audit.RecordAsync(
              new AuditRecord("analytics.sales_source_set", "sales_source", member.BrandId.ToString(), before, after),
              cancellationToken);
          return TypedResults.Ok(after);
      }

      public static async Task<NoContent> RemoveSourceAsync(
          MeasurementDbContext db,
          CurrentMember member,
          IAuditLog audit,
          CancellationToken cancellationToken)
      {
          if (await db.SalesSources.SingleOrDefaultAsync(cancellationToken) is { } source)
          {
              var before = SalesSourceView.Of(source);
              db.SalesSources.Remove(source);
              await db.SaveChangesAsync(cancellationToken);
              await audit.RecordAsync(
                  new AuditRecord("analytics.sales_source_removed", "sales_source", member.BrandId.ToString(), before),
                  cancellationToken);
          }

          return TypedResults.NoContent();
      }
  ```

- [ ] **Step 5: Register the check and map the routes**

In `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`:
- In `AddFeeds`, after `services.AddSingleton<BigQueryTables>();`, add:

  ```csharp
          services.AddSingleton<BigQueryTableCheck>();
  ```

- In `MapFeeds`, after the two upload routes, add:

  ```csharp
          var source = sales.MapGroup("/source");
          source.MapGet("", SalesEndpoints.GetSourceAsync);
          source.MapPut("", SalesEndpoints.SetSourceAsync).RequireBrandRole(BrandRoles.Admin);
          source.MapDelete("", SalesEndpoints.RemoveSourceAsync).RequireBrandRole(BrandRoles.Admin);
  ```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.SalesSourceTests"
```

Expected: all pass with 0 warnings.

- [ ] **Step 7: Regenerate the API contract and run everything**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --solution core/Kairo.slnx
npm --prefix web run lint
```

Expected: `openapi.json` gains `GET`, `PUT` and `DELETE` on `/analytics/sales/source`. Everything passes.

- [ ] **Step 8: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Feeds/BigQueryTableCheck.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesEndpoints.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/tests/Kairo.IntegrationTests/Feeds/SalesSourceTests.cs
git commit -m "Let admins point Kairo at a sales table in their own BigQuery, checked before it is saved"
git add core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Add the sales table endpoints to the API contract"
```

---

### Task 6: The nightly run, with Google Ads

**Files:**
- Modify: `core/src/Kairo.Contracts/Brands/IBrandDirectory.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Workspaces/BrandDirectory.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedNames.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/IFeed.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/BrandFeeds.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedRunner.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedJobs.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/AdsRows.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/GoogleAdsReports.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/GoogleAdsFeed.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`
- Create: `core/tests/Kairo.IntegrationTests/Api/KairoWorkerFactory.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`
- Create: `core/tests/Kairo.IntegrationTests/Feeds/FeedRunRows.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/BrandListTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/AdsRowsTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/NightlyGoogleAdsTests.cs`

**Interfaces:**
- Consumes: `IConnections`, `ConnectionStatus`, `ConnectionAccess` (with `ManagerId`), `ConnectionUnavailableException`; `BigQueryTables`, `TableSchemas`, `AnalyticsOptions`, `MeasurementHttp` (Task 3); `FeedClock`, `FeedWindow` (Task 4); `FeedRun` (Task 1); `IsolatedDatabase.CreateAsync` from `Kairo.IntegrationTests.Live`.
- Produces:
  - `IBrandDirectory.ListAllAsync(CancellationToken)` returning every brand's `BrandSummary`, ordered by name then ID.
  - `FeedNames.GoogleAds`, `Ga4`, `SalesTable`; `RunStatuses.Running`, `Succeeded`, `Failed`, `Skipped`; `SkipReasons.NotConnected`, `NeedsReconnect`, `AlreadyRunning`; `FeedStates.NotSetUp`, `NeedsConnection`, `Waiting`, `Running`, `Failing`, `Ok`.
  - `interface IFeed { string Name; ConnectedProduct Product; Task<bool> IsSetUpAsync(ct); string TargetOf(ConnectionStatus); Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, ct) }`. Feeds are scoped and run in registration order.
  - `record FeedResult(FeedWindow Window, int Rows)`, `record FeedOutcome(Guid BrandId, string Feed, string Status, int Rows)`, `record DailyRun(int Brands, IReadOnlyList<FeedOutcome> Runs)`.
  - `BrandFeeds.RunningFor` (2 hours), `BrandFeeds.RunAsync(Guid brandId, ct)`.
  - `FeedRunner.RunAsync(IReadOnlyList<BrandSummary>, ct)` and `FeedRunner.KeepRecordsFor` (90 days).
  - Worker route `POST /jobs/analytics/daily?brandId=` → `200` with `{ brands, runs: [{ brandId, feed, status, rows }] }`, `404`, or `503`.
  - `AdsAccount(string CustomerId, string TimeZone, string Currency)`, `GeoTarget(string Id, string? Name, string? CountryCode)`, `AdsRows.Campaign/AdGroup/Ad/Region`, `AdsRows.Text(JsonElement, params string[] path)`, `AdsRows.Money(decimal)`.
  - `AdsQueries.Customer`, `Campaigns`, `AdGroups`, `Ads`, `Regions`, `GeoTargets`; `GoogleAdsReports.RegionField`, `RegionJson`, `GeoBatch`.
  - Tests: `KairoWorkerFactory(string connectionString, FakeGoogleApis googleApis, DateTimeOffset now)` with `Clock`, `AnalyticsProject` and `RunDailyAsync(Guid? brandId = null)`; `FakeGoogleApis.AdsTimeZone`, `AdsEmptyDays`, `AdsFailures`, `AdsReports`; `FeedRunRows.ListAsync` and `FeedRunRows.InsertAsync`; `record StoredRun`.

- [ ] **Step 1: Check the region segment with Google**

Before writing code, confirm that `segments.geo_target_state` can be selected from `geographic_view` in Google Ads API v25:

1. Open `https://developers.google.com/google-ads/api/fields/v25/query_validator`.
2. Paste:

   ```sql
   SELECT segments.date, campaign.id, segments.geo_target_state, metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value FROM geographic_view WHERE segments.date BETWEEN '2026-10-07' AND '2026-10-13' AND geographic_view.location_type = 'LOCATION_OF_PRESENCE'
   ```

3. **Valid:** keep `RegionField = "segments.geo_target_state"` and `RegionJson = "geoTargetState"` as written below.
4. **Invalid because of the segment:** use `segments.geo_target_region` instead. Set `RegionField = "segments.geo_target_region"` and `RegionJson = "geoTargetRegion"` in Step 7. The fake Google Ads and the tests read both constants, so nothing else changes. Say which one you used in this task's commit message.

Task 12 checks the result again against the demo Google Ads test account.

- [ ] **Step 2: Reference the worker from the integration tests**

Replace `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Testcontainers.MsSql" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Migrator\Kairo.Migrator.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Api\Kairo.Api.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Worker\Kairo.Worker.csproj" Aliases="worker" />
  </ItemGroup>

</Project>
```

The alias keeps the worker's `Program` apart from the API's. Both hosts were tried together in one test run while this plan was written: the build had 0 warnings, and both answered `/alive`.

Create `core/tests/Kairo.IntegrationTests/Api/KairoWorkerFactory.cs`:

```csharp
extern alias worker;

using Kairo.Modules.Measurement.Feeds;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Kairo.IntegrationTests.Api;

public sealed class KairoWorkerFactory(string connectionString, FakeGoogleApis googleApis, DateTimeOffset now)
    : WebApplicationFactory<worker::Program>
{
    public FakeTimeProvider Clock { get; } = new(now);

    public string? AnalyticsProject { get; init; } = FakeBigQuery.KairoProject;

    public async Task<HttpResponseMessage> RunDailyAsync(Guid? brandId = null)
    {
        using var client = CreateClient();
        return await client.PostAsync(brandId is { } id ? $"/jobs/analytics/daily?brandId={id}" : "/jobs/analytics/daily", null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", connectionString);
        builder.UseSetting("Authentication:Google:ClientId", FakeGoogle.ClientId);
        builder.UseSetting("Authentication:Google:ClientSecret", FakeGoogleApis.ClientSecret);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        if (AnalyticsProject is not null)
        {
            builder.UseSetting("Kairo:Analytics:Project", AnalyticsProject);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient("kairo-google").ConfigurePrimaryHttpMessageHandler(() => googleApis);
            services.AddHttpClient("kairo-measurement-google").ConfigurePrimaryHttpMessageHandler(() => googleApis);
            services.AddSingleton<IServiceAccessToken, FakeServiceToken>();
        });
    }
}
```

The worker shares the API test's `FakeGoogleApis`, so it can refresh the refresh tokens the API's consent flow issued. It reads Data Protection keys from the same database, so it can open the tokens the API sealed.

- [ ] **Step 3: Add the fake Google Ads reports**

In `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`:
- Replace the using block at the top with:

  ```csharp
  using System.Collections.Concurrent;
  using System.Globalization;
  using System.Net;
  using System.Net.Http.Json;
  using System.Security.Cryptography;
  using System.Text;
  using System.Text.Json;
  using System.Text.RegularExpressions;
  using Kairo.Modules.Measurement.Feeds;
  using Microsoft.AspNetCore.WebUtilities;
  ```

- Below `public sealed record IssuedCode(string Code, string RefreshToken, string Subject);` add:

  ```csharp
  public sealed record AdsReport(string Customer, string? Login, string Query);
  ```

- Below `public HttpStatusCode BigQueryJobs { get; set; } = HttpStatusCode.OK;` add:

  ```csharp
      public string AdsTimeZone { get; set; } = "Asia/Kolkata";

      public HashSet<string> AdsEmptyDays { get; } = [];

      public HashSet<string> AdsFailures { get; } = [];

      public ConcurrentQueue<AdsReport> AdsReports { get; } = new();
  ```

- In `Product`, below the line `("googleads.googleapis.com", _) when !AdsReachable(path, login) => new HttpResponseMessage(HttpStatusCode.Forbidden),` add:

  ```csharp
              ("googleads.googleapis.com", _) when path.EndsWith("/googleAds:searchStream", StringComparison.Ordinal) => AdsStream(path, body, login),
  ```

- Above `private static bool AdsReachable(string path, string? login) =>` add:

  ```csharp
      private HttpResponseMessage AdsStream(string path, string body, string? login)
      {
          var customer = path.Split('/')[3];
          var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString()!;
          AdsReports.Enqueue(new AdsReport(customer, login, query));
          if (AdsFailures.Contains(customer))
          {
              return Json(
                  HttpStatusCode.InternalServerError,
                  new[] { new { error = new { code = 500, message = "Internal error encountered.", status = "INTERNAL" } } });
          }

          object[] results;
          if (query.EndsWith("FROM customer", StringComparison.Ordinal))
          {
              results = [new { customer = new { resourceName = $"customers/{customer}", id = customer, timeZone = AdsTimeZone, currencyCode = "INR" } }];
          }
          else if (query.Contains("FROM geo_target_constant", StringComparison.Ordinal))
          {
              results = [new { geoTargetConstant = new { resourceName = "geoTargetConstants/21342", id = "21342", name = "Maharashtra", countryCode = "IN" } }];
          }
          else
          {
              results = AdsDays(query).Select(date => AdsResult(query, date)).ToArray();
          }

          return Json(HttpStatusCode.OK, new[] { new { results, requestId = "fake-request" } });
      }

      private IEnumerable<string> AdsDays(string query)
      {
          var range = Regex.Match(query, "segments.date BETWEEN '(?<from>[0-9-]+)' AND '(?<to>[0-9-]+)'");
          var from = DateOnly.ParseExact(range.Groups["from"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
          var to = DateOnly.ParseExact(range.Groups["to"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
          for (var day = from; day <= to; day = day.AddDays(1))
          {
              var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
              if (!AdsEmptyDays.Contains(date))
              {
                  yield return date;
              }
          }
      }

      private static object AdsResult(string query, string date)
      {
          var metrics = new { impressions = "1000", clicks = "50", costMicros = "2500000", conversions = 3.0, conversionsValue = 4500.5 };
          if (query.Contains("FROM geographic_view", StringComparison.Ordinal))
          {
              return new Dictionary<string, object>
              {
                  ["segments"] = new Dictionary<string, object> { ["date"] = date, [GoogleAdsReports.RegionJson] = "geoTargetConstants/21342" },
                  ["campaign"] = new { id = "9001" },
                  ["metrics"] = metrics,
              };
          }

          if (query.Contains("FROM ad_group_ad", StringComparison.Ordinal))
          {
              return new
              {
                  segments = new { date },
                  campaign = new { id = "9001" },
                  adGroup = new { id = "8001" },
                  adGroupAd = new { ad = new { id = "7001", type = "RESPONSIVE_SEARCH_AD" } },
                  metrics,
              };
          }

          if (query.Contains("FROM ad_group", StringComparison.Ordinal))
          {
              return new { segments = new { date }, campaign = new { id = "9001" }, adGroup = new { id = "8001", name = "Running shoes" }, metrics };
          }

          return new
          {
              segments = new { date },
              campaign = new { id = "9001", name = "Diwali Search", advertisingChannelType = "SEARCH", status = "ENABLED" },
              metrics,
          };
      }
  ```

Create `core/tests/Kairo.IntegrationTests/Feeds/FeedRunRows.cs`:

```csharp
using System.Data;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Feeds;

public sealed record StoredRun(
    string Feed,
    string? ExternalId,
    string Status,
    string? Reason,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Rows,
    string? Error,
    DateTimeOffset StartedAt);

internal static class FeedRunRows
{
    public static async Task<List<StoredRun>> ListAsync(string connectionString, Guid brandId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT Feed, ExternalId, Status, Reason, FromDate, ToDate, [Rows], Error, StartedAt
            FROM measurement.FeedRuns
            WHERE BrandId = @brand
            ORDER BY StartedAt, Feed;
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        var runs = new List<StoredRun>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            runs.Add(new StoredRun(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDateTimeOffset(8)));
        }

        return runs;
    }

    public static async Task InsertAsync(
        string connectionString,
        Guid brandId,
        string feed,
        string status,
        DateTimeOffset startedAt,
        DateOnly? toDate = null,
        string? error = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO measurement.FeedRuns (Id, BrandId, Feed, ExternalId, Status, FromDate, ToDate, [Rows], Error, StartedAt, FinishedAt)
            VALUES (NEWID(), @brand, @feed, N'seeded', @status, @to, @to, 0, @error, @started,
                    CASE WHEN @status = N'running' THEN NULL ELSE DATEADD(minute, 1, @started) END);
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@feed", feed);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.Add(new SqlParameter("@to", SqlDbType.Date) { Value = toDate is { } date ? date.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@error", SqlDbType.NVarChar, 1000) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@started", SqlDbType.DateTimeOffset) { Value = startedAt });
        await command.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 4: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/BrandListTests.cs`:

```csharp
using Kairo.Contracts.Brands;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Feeds;

public sealed class BrandListTests(SqlServer sqlServer) : IAsyncLifetime
{
    private KairoApiFactory _api = null!;

    public async ValueTask InitializeAsync() => _api = new KairoApiFactory(await IsolatedDatabase.CreateAsync(sqlServer));

    [Fact]
    public async Task System_work_sees_every_workspace()
    {
        var first = await Workspaces.SignInAsync(_api);
        var second = await Workspaces.SignInAsync(_api);
        var ids = new[]
        {
            await Workspaces.CreateAsync(first.Browser),
            await Workspaces.CreateAsync(first.Browser),
            await Workspaces.CreateAsync(second.Browser),
        };

        await using var scope = _api.Services.CreateAsyncScope();
        var brands = await scope.ServiceProvider.GetRequiredService<IBrandDirectory>().ListAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ids.Order(), brands.Select(brand => brand.Id).Order());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

Create `core/tests/Kairo.IntegrationTests/Feeds/AdsRowsTests.cs`:

```csharp
using System.Text.Json;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class AdsRowsTests
{
    private static readonly AdsAccount Account = new("1111111111", "Asia/Kolkata", "INR");

    [Fact]
    public void A_campaign_row_turns_micros_into_money_and_keeps_ids_as_text()
    {
        var row = Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001","name":"Diwali","advertisingChannelType":"SEARCH","status":"ENABLED"},"metrics":{"impressions":"1000","clicks":"50","costMicros":"1234567","conversions":2.5,"conversionsValue":99.99}}""");

        Assert.Equal(
            """{"date":"2026-10-01","customer_id":"1111111111","campaign_id":"9001","campaign_name":"Diwali","channel":"SEARCH","campaign_status":"ENABLED","currency":"INR","impressions":1000,"clicks":50,"cost":"1.234567","conversions":2.5,"conversions_value":"99.99"}""",
            AdsRows.Campaign(row, Account).ToJsonString());
    }

    [Fact]
    public void Missing_metrics_count_as_zero()
    {
        var mapped = AdsRows.AdGroup(Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001"},"adGroup":{"id":"8001","name":"Shoes"}}"""), Account);

        Assert.Equal("8001", mapped["ad_group_id"]!.GetValue<string>());
        Assert.Equal("Shoes", mapped["ad_group_name"]!.GetValue<string>());
        Assert.Equal(0, mapped["impressions"]!.GetValue<long>());
        Assert.Equal("0", mapped["cost"]!.GetValue<string>());
        Assert.Equal(0d, mapped["conversions"]!.GetValue<double>());
        Assert.Equal("0", mapped["conversions_value"]!.GetValue<string>());
    }

    [Fact]
    public void An_ad_row_names_the_ad_and_its_type()
    {
        var mapped = AdsRows.Ad(
            Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001"},"adGroup":{"id":"8001"},"adGroupAd":{"ad":{"id":"7001","type":"RESPONSIVE_SEARCH_AD"}},"metrics":{}}"""),
            Account);

        Assert.Equal("7001", mapped["ad_id"]!.GetValue<string>());
        Assert.Equal("RESPONSIVE_SEARCH_AD", mapped["ad_type"]!.GetValue<string>());
        Assert.Equal("8001", mapped["ad_group_id"]!.GetValue<string>());
    }

    [Fact]
    public void A_region_row_takes_its_name_from_the_geo_target()
    {
        var row = Parse($$"""{"segments":{"date":"2026-10-01","{{GoogleAdsReports.RegionJson}}":"geoTargetConstants/21342"},"campaign":{"id":"9001"},"metrics":{}}""");
        var names = new Dictionary<string, GeoTarget> { ["geoTargetConstants/21342"] = new("21342", "Maharashtra", "IN") };

        var mapped = AdsRows.Region(row, Account, names);

        Assert.Equal("21342", mapped["region_id"]!.GetValue<string>());
        Assert.Equal("Maharashtra", mapped["region_name"]!.GetValue<string>());
        Assert.Equal("IN", mapped["country_code"]!.GetValue<string>());
    }

    [Fact]
    public void A_region_row_Google_does_not_place_in_a_state_has_no_region()
    {
        var mapped = AdsRows.Region(
            Parse("""{"segments":{"date":"2026-10-01"},"campaign":{"id":"9001"},"metrics":{}}"""), Account, new Dictionary<string, GeoTarget>());

        Assert.True(mapped.ContainsKey("region_id"));
        Assert.Null(mapped["region_id"]);
        Assert.Null(mapped["region_name"]);
        Assert.Null(mapped["country_code"]);
    }

    [Fact]
    public void The_region_report_asks_for_the_window_and_where_people_were()
    {
        Assert.Equal(
            $"SELECT segments.date, campaign.id, {GoogleAdsReports.RegionField}, metrics.impressions, metrics.clicks, metrics.cost_micros, "
            + "metrics.conversions, metrics.conversions_value FROM geographic_view "
            + "WHERE segments.date BETWEEN '2026-10-07' AND '2026-10-13' AND geographic_view.location_type = 'LOCATION_OF_PRESENCE'",
            AdsQueries.Regions(new FeedWindow(new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13))));
    }

    [Fact]
    public void Geo_targets_are_looked_up_by_resource_name()
    {
        Assert.Equal(
            "SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.country_code FROM geo_target_constant "
            + "WHERE geo_target_constant.resource_name IN ('geoTargetConstants/1', 'geoTargetConstants/2')",
            AdsQueries.GeoTargets(["geoTargetConstants/1", "geoTargetConstants/2"]));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
```

Create `core/tests/Kairo.IntegrationTests/Feeds/NightlyGoogleAdsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class NightlyGoogleAdsTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const int FirstWindowDays = 395;

    private static readonly DateTimeOffset Night = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private static readonly string[] AdsTables = ["ads_campaign_daily", "ads_ad_group_daily", "ads_ad_daily", "ads_region_daily"];

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeGoogleApis Google => _api.GoogleApis;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Night);
    }

    [Fact]
    public async Task A_nightly_run_loads_the_four_Google_Ads_tables()
    {
        var brandId = await BrandWithAdsAsync();

        var run = await RunAsync();

        Assert.Equal(1, run.GetProperty("brands").GetInt32());
        var outcome = Outcome(run, brandId);
        Assert.Equal("succeeded", outcome.GetProperty("status").GetString());
        Assert.Equal(FirstWindowDays * AdsTables.Length, outcome.GetProperty("rows").GetInt32());
        var dataset = AnalyticsOptions.DatasetFor(brandId);
        foreach (var table in AdsTables)
        {
            Assert.Equal(FirstWindowDays, Google.BigQuery.Rows(dataset, table).Count);
        }

        var campaign = Day(dataset, "ads_campaign_daily", "2026-10-13");
        Assert.Equal("1111111111", campaign["customer_id"]!.GetValue<string>());
        Assert.Equal("9001", campaign["campaign_id"]!.GetValue<string>());
        Assert.Equal("SEARCH", campaign["channel"]!.GetValue<string>());
        Assert.Equal("INR", campaign["currency"]!.GetValue<string>());
        Assert.Equal("2.5", campaign["cost"]!.GetValue<string>());
        Assert.Equal(1000, campaign["impressions"]!.GetValue<long>());
        Assert.Equal("Running shoes", Day(dataset, "ads_ad_group_daily", "2026-10-13")["ad_group_name"]!.GetValue<string>());
        Assert.Equal("RESPONSIVE_SEARCH_AD", Day(dataset, "ads_ad_daily", "2026-10-13")["ad_type"]!.GetValue<string>());
        var region = Day(dataset, "ads_region_daily", "2026-10-13");
        Assert.Equal("21342", region["region_id"]!.GetValue<string>());
        Assert.Equal("Maharashtra", region["region_name"]!.GetValue<string>());
        Assert.Equal("IN", region["country_code"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_dataset_and_tables_are_created_once()
    {
        var brandId = await BrandWithAdsAsync();

        await RunAsync();
        await RunAsync();

        var dataset = AnalyticsOptions.DatasetFor(brandId);
        Assert.Equal(1, Google.BigQuery.Count($"datasets.insert {dataset}"));
        foreach (var table in AdsTables)
        {
            Assert.Equal(1, Google.BigQuery.Count($"tables.insert {dataset}.{table}"));
        }
    }

    [Fact]
    public async Task The_first_run_reaches_back_13_months_and_later_runs_cover_7_days()
    {
        var brandId = await BrandWithAdsAsync();

        await RunAsync();
        var first = CampaignQueries()[^1];
        await RunAsync();
        var second = CampaignQueries()[^1];

        Assert.Contains("segments.date BETWEEN '2025-09-14' AND '2026-10-13'", first);
        Assert.Contains("segments.date BETWEEN '2026-10-07' AND '2026-10-13'", second);
        var windows = (await FeedRunRows.ListAsync(_database, brandId))
            .Where(stored => stored.Feed == FeedNames.GoogleAds)
            .Select(stored => (stored.FromDate, stored.ToDate));
        Assert.Equal(
            new (DateOnly?, DateOnly?)[]
            {
                (new DateOnly(2025, 9, 14), new DateOnly(2026, 10, 13)),
                (new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13)),
            },
            windows);
    }

    [Fact]
    public async Task The_window_follows_the_accounts_time_zone()
    {
        Google.AdsTimeZone = "America/Los_Angeles";
        await BrandWithAdsAsync();

        await RunAsync();

        Assert.Contains("segments.date BETWEEN '2025-09-13' AND '2026-10-12'", Assert.Single(CampaignQueries()));
    }

    [Fact]
    public async Task A_client_reached_through_a_manager_is_read_through_the_manager()
    {
        var brandId = await BrandWithAdsAsync("4444444444");

        var run = await RunAsync();

        Assert.Equal("succeeded", Outcome(run, brandId).GetProperty("status").GetString());
        var reports = Google.AdsReports.Where(report => report.Customer == "4444444444").ToList();
        Assert.Contains(reports, report => report.Query.Contains("FROM geo_target_constant", StringComparison.Ordinal));
        Assert.All(reports, report => Assert.Equal("2222222222", report.Login));
    }

    [Fact]
    public async Task A_brand_without_Google_Ads_is_recorded_as_skipped()
    {
        var (_, brandId) = await WorkspaceAsync();

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        var stored = Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.GoogleAds);
        Assert.Equal("not_connected", stored.Reason);
        Assert.Empty(Google.AdsReports);
    }

    [Fact]
    public async Task A_rejected_token_is_recorded_as_needing_a_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        Google.Reject(issued.RefreshToken);

        await RunAsync();
        await RunAsync();

        var stored = (await FeedRunRows.ListAsync(_database, brandId)).Where(stored => stored.Feed == FeedNames.GoogleAds).ToList();
        Assert.Equal(new[] { "skipped", "skipped" }, stored.Select(run => run.Status));
        Assert.Equal(new[] { "needs_reconnect", "needs_reconnect" }, stored.Select(run => run.Reason));
        Assert.Equal("needsReconnect", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_feed_that_is_already_running_is_skipped()
    {
        var brandId = await BrandWithAdsAsync();
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.GoogleAds, "running", Night.AddMinutes(-30));

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        Assert.Contains(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Reason == "already_running");
        Assert.Empty(CampaignQueries());
    }

    [Fact]
    public async Task A_running_record_older_than_2_hours_does_not_block_the_feed()
    {
        var brandId = await BrandWithAdsAsync();
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.GoogleAds, "running", Night.AddHours(-3));

        var run = await RunAsync();

        Assert.Equal("succeeded", Outcome(run, brandId).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_run_for_one_brand_touches_only_that_brand()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var first = await BrandWithAdsAsync(admin: admin);
        var second = await BrandWithAdsAsync(admin: admin);

        var run = await RunAsync(first);

        Assert.Equal(1, run.GetProperty("brands").GetInt32());
        Assert.All(run.GetProperty("runs").EnumerateArray(), outcome => Assert.Equal(first, outcome.GetProperty("brandId").GetGuid()));
        Assert.Empty(Google.BigQuery.TableNames(AnalyticsOptions.DatasetFor(second)));
    }

    [Fact]
    public async Task An_unknown_brand_is_not_found()
    {
        using var response = await _worker.RunDailyAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task One_failing_brand_still_lets_the_others_finish()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var healthy = await BrandWithAdsAsync(admin: admin);
        var broken = await BrandWithAdsAsync("3333333333", admin);
        Google.AdsFailures.Add("3333333333");

        var run = await RunAsync();

        Assert.Equal("succeeded", Outcome(run, healthy).GetProperty("status").GetString());
        Assert.Equal("failed", Outcome(run, broken).GetProperty("status").GetString());
        var failed = Assert.Single(await FeedRunRows.ListAsync(_database, broken), stored => stored.Feed == FeedNames.GoogleAds);
        Assert.Equal("Google answered 500: Internal error encountered.", failed.Error);
        Assert.Equal(FirstWindowDays, Google.BigQuery.Rows(AnalyticsOptions.DatasetFor(healthy), "ads_campaign_daily").Count);
    }

    [Fact]
    public async Task Run_records_older_than_90_days_are_deleted()
    {
        var (_, brandId) = await WorkspaceAsync();
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.SalesTable, "succeeded", Night.AddDays(-91));
        await FeedRunRows.InsertAsync(_database, brandId, FeedNames.SalesTable, "succeeded", Night.AddDays(-89));

        await RunAsync();

        var old = (await FeedRunRows.ListAsync(_database, brandId)).Where(stored => stored.StartedAt < Night.AddDays(-1));
        Assert.Equal(new[] { Night.AddDays(-89) }, old.Select(stored => stored.StartedAt));
    }

    [Fact]
    public async Task Without_an_analytics_project_the_job_is_unavailable()
    {
        await using var worker = new KairoWorkerFactory(_database, Google, Night) { AnalyticsProject = null };

        using var response = await worker.RunDailyAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private async Task<JsonElement> RunAsync(Guid? brandId = null)
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await _worker.RunDailyAsync(brandId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync(Member? admin = null)
    {
        admin ??= await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<Guid> BrandWithAdsAsync(string account = "1111111111", Member? admin = null)
    {
        var (owner, brandId) = await WorkspaceAsync(admin);
        await Connections.CompleteAsync(_api, owner.Browser, await Connections.StartAsync(owner.Browser, brandId, "google-ads"));
        (await owner.Browser.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = account }, Token))
            .EnsureSuccessStatusCode();
        return brandId;
    }

    private List<string> CampaignQueries() => Google.AdsReports
        .Select(report => report.Query)
        .Where(query => query.Contains(" FROM campaign WHERE ", StringComparison.Ordinal))
        .ToList();

    private JsonObject Day(string dataset, string table, string date) =>
        Google.BigQuery.Rows(dataset, table).Single(row => row["date"]!.GetValue<string>() == date);

    private static JsonElement Outcome(JsonElement run, Guid brandId, string feed = FeedNames.GoogleAds) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == feed);
}
```

- [ ] **Step 5: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.AdsRowsTests"
```

Expected: the build fails with `CS0246: The type or namespace name 'AdsAccount' could not be found` and `CS1061: 'IBrandDirectory' does not contain a definition for 'ListAllAsync'`.

- [ ] **Step 6: List every brand for system work**

Replace `core/src/Kairo.Contracts/Brands/IBrandDirectory.cs` with:

```csharp
namespace Kairo.Contracts.Brands;

public interface IBrandDirectory
{
    Task<IReadOnlyList<BrandSummary>> GetAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<BrandSummary>> ListAllAsync(CancellationToken cancellationToken);
}

public sealed record BrandSummary(Guid Id, string Handle, string Name);
```

In `core/src/Modules/Kairo.Modules.Brands/Workspaces/BrandDirectory.cs`, below `GetAsync`, add:

```csharp
    public async Task<IReadOnlyList<BrandSummary>> ListAllAsync(CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Brands
                .OrderBy(brand => brand.Name)
                .ThenBy(brand => brand.Id)
                .Select(brand => new BrandSummary(brand.Id, brand.Handle, brand.Name))
                .ToListAsync(cancellationToken);
        }
    }
```

- [ ] **Step 7: Add the Google Ads reports**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/AdsRows.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record AdsAccount(string CustomerId, string TimeZone, string Currency);

internal sealed record GeoTarget(string Id, string? Name, string? CountryCode);

internal static class AdsRows
{
    public static JsonObject Campaign(JsonElement row, AdsAccount account) => WithMetrics(
        new JsonObject
        {
            ["date"] = Text(row, "segments", "date"),
            ["customer_id"] = account.CustomerId,
            ["campaign_id"] = Text(row, "campaign", "id"),
            ["campaign_name"] = Text(row, "campaign", "name"),
            ["channel"] = Text(row, "campaign", "advertisingChannelType"),
            ["campaign_status"] = Text(row, "campaign", "status"),
            ["currency"] = account.Currency,
        },
        row);

    public static JsonObject AdGroup(JsonElement row, AdsAccount account) => WithMetrics(
        new JsonObject
        {
            ["date"] = Text(row, "segments", "date"),
            ["customer_id"] = account.CustomerId,
            ["campaign_id"] = Text(row, "campaign", "id"),
            ["ad_group_id"] = Text(row, "adGroup", "id"),
            ["ad_group_name"] = Text(row, "adGroup", "name"),
            ["currency"] = account.Currency,
        },
        row);

    public static JsonObject Ad(JsonElement row, AdsAccount account) => WithMetrics(
        new JsonObject
        {
            ["date"] = Text(row, "segments", "date"),
            ["customer_id"] = account.CustomerId,
            ["campaign_id"] = Text(row, "campaign", "id"),
            ["ad_group_id"] = Text(row, "adGroup", "id"),
            ["ad_id"] = Text(row, "adGroupAd", "ad", "id"),
            ["ad_type"] = Text(row, "adGroupAd", "ad", "type"),
            ["currency"] = account.Currency,
        },
        row);

    public static string? RegionOf(JsonElement row) => Text(row, "segments", GoogleAdsReports.RegionJson);

    public static JsonObject Region(JsonElement row, AdsAccount account, IReadOnlyDictionary<string, GeoTarget> names)
    {
        var resource = RegionOf(row);
        var geo = resource is null
            ? null
            : names.TryGetValue(resource, out var found) ? found : new GeoTarget(resource[(resource.LastIndexOf('/') + 1)..], null, null);
        return WithMetrics(
            new JsonObject
            {
                ["date"] = Text(row, "segments", "date"),
                ["customer_id"] = account.CustomerId,
                ["campaign_id"] = Text(row, "campaign", "id"),
                ["region_id"] = geo?.Id,
                ["region_name"] = geo?.Name,
                ["country_code"] = geo?.CountryCode,
                ["currency"] = account.Currency,
            },
            row);
    }

    public static string? Text(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null,
        };
    }

    public static string Money(decimal value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static JsonObject WithMetrics(JsonObject row, JsonElement source)
    {
        row["impressions"] = Whole(source, "impressions");
        row["clicks"] = Whole(source, "clicks");
        row["cost"] = Money(Whole(source, "costMicros") / 1_000_000m);
        row["conversions"] = Number(source, "conversions");
        row["conversions_value"] = Money((decimal)Number(source, "conversionsValue"));
        return row;
    }

    private static long Whole(JsonElement row, string metric) =>
        Text(row, "metrics", metric) is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : 0;

    private static double Number(JsonElement row, string metric) =>
        Text(row, "metrics", metric) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : 0;
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/GoogleAdsReports.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal static class AdsQueries
{
    public const string Metrics = "metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions, metrics.conversions_value";

    public const string Customer = "SELECT customer.id, customer.time_zone, customer.currency_code FROM customer";

    public static string Campaigns(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, campaign.name, campaign.advertising_channel_type, campaign.status, {Metrics} FROM campaign WHERE {During(window)}";

    public static string AdGroups(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, ad_group.id, ad_group.name, {Metrics} FROM ad_group WHERE {During(window)}";

    public static string Ads(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, ad_group.id, ad_group_ad.ad.id, ad_group_ad.ad.type, {Metrics} FROM ad_group_ad WHERE {During(window)}";

    public static string Regions(FeedWindow window) =>
        $"SELECT segments.date, campaign.id, {GoogleAdsReports.RegionField}, {Metrics} FROM geographic_view "
        + $"WHERE {During(window)} AND geographic_view.location_type = 'LOCATION_OF_PRESENCE'";

    public static string GeoTargets(IEnumerable<string> resourceNames) =>
        "SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.country_code FROM geo_target_constant "
        + $"WHERE geo_target_constant.resource_name IN ({string.Join(", ", resourceNames.Select(name => $"'{name}'"))})";

    private static string During(FeedWindow window) =>
        $"segments.date BETWEEN '{FeedClock.Iso(window.From)}' AND '{FeedClock.Iso(window.To)}'";
}

internal sealed class GoogleAdsReports(IHttpClientFactory http, AnalyticsOptions options)
{
    public const string RegionField = "segments.geo_target_state";
    public const string RegionJson = "geoTargetState";
    public const int GeoBatch = 500;

    public async Task<AdsAccount> DescribeAsync(ConnectionAccess access, CancellationToken cancellationToken)
    {
        var customer = (await SearchAsync(access, AdsQueries.Customer, cancellationToken)).Single().GetProperty("customer");
        return new AdsAccount(
            access.ExternalId,
            AdsRows.Text(customer, "timeZone") ?? FeedClock.IndiaTimeZone,
            AdsRows.Text(customer, "currencyCode") ?? "");
    }

    public async Task<List<JsonObject>> CampaignsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken) =>
        (await SearchAsync(access, AdsQueries.Campaigns(window), cancellationToken)).Select(row => AdsRows.Campaign(row, account)).ToList();

    public async Task<List<JsonObject>> AdGroupsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken) =>
        (await SearchAsync(access, AdsQueries.AdGroups(window), cancellationToken)).Select(row => AdsRows.AdGroup(row, account)).ToList();

    public async Task<List<JsonObject>> AdsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken) =>
        (await SearchAsync(access, AdsQueries.Ads(window), cancellationToken)).Select(row => AdsRows.Ad(row, account)).ToList();

    public async Task<List<JsonObject>> RegionsAsync(ConnectionAccess access, AdsAccount account, FeedWindow window, CancellationToken cancellationToken)
    {
        var rows = await SearchAsync(access, AdsQueries.Regions(window), cancellationToken);
        var names = new Dictionary<string, GeoTarget>();
        foreach (var batch in rows.Select(AdsRows.RegionOf).OfType<string>().Distinct().Chunk(GeoBatch))
        {
            foreach (var row in await SearchAsync(access, AdsQueries.GeoTargets(batch), cancellationToken))
            {
                var geo = row.GetProperty("geoTargetConstant");
                if (AdsRows.Text(geo, "id") is { } id)
                {
                    names[$"geoTargetConstants/{id}"] = new GeoTarget(id, AdsRows.Text(geo, "name"), AdsRows.Text(geo, "countryCode"));
                }
            }
        }

        return rows.Select(row => AdsRows.Region(row, account, names)).ToList();
    }

    private async Task<IReadOnlyList<JsonElement>> SearchAsync(ConnectionAccess access, string query, CancellationToken cancellationToken)
    {
        (string Name, string Value)[] headers = access.ManagerId is { } manager ? [("login-customer-id", manager)] : [];
        var batches = await http.CreateClient(MeasurementHttp.ClientName).CallAsync(
            HttpMethod.Post,
            $"https://googleads.googleapis.com/{options.AdsApiVersion}/customers/{access.ExternalId}/googleAds:searchStream",
            access.AccessToken,
            new { query },
            cancellationToken,
            headers);
        var results = new List<JsonElement>();
        if (batches.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var batch in batches.EnumerateArray())
        {
            if (batch.TryGetProperty("results", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                results.AddRange(list.EnumerateArray());
            }
        }

        return results;
    }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/GoogleAdsFeed.cs`:

```csharp
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class GoogleAdsFeed(GoogleAdsReports reports, BigQueryTables tables, TimeProvider time) : IFeed
{
    public string Name => FeedNames.GoogleAds;

    public ConnectedProduct Product => ConnectedProduct.GoogleAds;

    public Task<bool> IsSetUpAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public string TargetOf(ConnectionStatus status) => status.ExternalId ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken)
    {
        var account = await reports.DescribeAsync(access, cancellationToken);
        var window = FeedWindow.For(FeedClock.Today(time, account.TimeZone), firstRun);
        var days = window.Days();
        var rows = await tables.ReplaceDaysAsync(
            brandId, TableSchemas.AdsCampaignDaily, days, await reports.CampaignsAsync(access, account, window, cancellationToken), cancellationToken);
        rows += await tables.ReplaceDaysAsync(
            brandId, TableSchemas.AdsAdGroupDaily, days, await reports.AdGroupsAsync(access, account, window, cancellationToken), cancellationToken);
        rows += await tables.ReplaceDaysAsync(
            brandId, TableSchemas.AdsAdDaily, days, await reports.AdsAsync(access, account, window, cancellationToken), cancellationToken);
        rows += await tables.ReplaceDaysAsync(
            brandId, TableSchemas.AdsRegionDaily, days, await reports.RegionsAsync(access, account, window, cancellationToken), cancellationToken);
        return new FeedResult(window, rows);
    }
}
```

- [ ] **Step 8: Add the nightly run**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedNames.cs`:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedNames
{
    public const string GoogleAds = "google-ads";
    public const string Ga4 = "ga4";
    public const string SalesTable = "sales-table";
}

internal static class RunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal static class SkipReasons
{
    public const string NotConnected = "not_connected";
    public const string NeedsReconnect = "needs_reconnect";
    public const string AlreadyRunning = "already_running";
}

internal static class FeedStates
{
    public const string NotSetUp = "not_set_up";
    public const string NeedsConnection = "needs_connection";
    public const string Waiting = "waiting";
    public const string Running = "running";
    public const string Failing = "failing";
    public const string Ok = "ok";
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/IFeed.cs`:

```csharp
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal interface IFeed
{
    string Name { get; }

    ConnectedProduct Product { get; }

    Task<bool> IsSetUpAsync(CancellationToken cancellationToken);

    string TargetOf(ConnectionStatus status);

    Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken);
}

internal sealed record FeedResult(FeedWindow Window, int Rows);

internal sealed record FeedOutcome(Guid BrandId, string Feed, string Status, int Rows);

internal sealed record DailyRun(int Brands, IReadOnlyList<FeedOutcome> Runs);
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/BrandFeeds.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class BrandFeeds(
    IEnumerable<IFeed> feeds,
    IConnections connections,
    MeasurementDbContext db,
    TimeProvider time,
    ILogger<BrandFeeds> logger)
{
    public const int MaxErrorLength = 1000;

    public static readonly TimeSpan RunningFor = TimeSpan.FromHours(2);

    public async Task<IReadOnlyList<FeedOutcome>> RunAsync(Guid brandId, CancellationToken cancellationToken)
    {
        var outcomes = new List<FeedOutcome>();
        foreach (var feed in feeds)
        {
            if (await RunAsync(brandId, feed, cancellationToken) is { } outcome)
            {
                outcomes.Add(outcome);
            }
        }

        return outcomes;
    }

    private async Task<FeedOutcome?> RunAsync(Guid brandId, IFeed feed, CancellationToken cancellationToken)
    {
        if (!await feed.IsSetUpAsync(cancellationToken))
        {
            return null;
        }

        var status = await connections.GetStatusAsync(feed.Product, cancellationToken);
        var now = time.GetUtcNow();
        var run = new FeedRun { Id = Guid.NewGuid(), Feed = feed.Name, ExternalId = status.ExternalId, StartedAt = now };
        db.FeedRuns.Add(run);
        if (status.State != ConnectionState.Connected)
        {
            var reason = status.State == ConnectionState.NeedsReconnect ? SkipReasons.NeedsReconnect : SkipReasons.NotConnected;
            return await FinishAsync(brandId, run, RunStatuses.Skipped, reason, cancellationToken);
        }

        var since = now - RunningFor;
        if (await db.FeedRuns.AnyAsync(
                existing => existing.Feed == feed.Name && existing.Status == RunStatuses.Running && existing.StartedAt > since,
                cancellationToken))
        {
            return await FinishAsync(brandId, run, RunStatuses.Skipped, SkipReasons.AlreadyRunning, cancellationToken);
        }

        var target = feed.TargetOf(status);
        run.ExternalId = target;
        var firstRun = !await db.FeedRuns.AnyAsync(
            existing => existing.Feed == feed.Name && existing.ExternalId == target && existing.Status == RunStatuses.Succeeded,
            cancellationToken);
        run.Status = RunStatuses.Running;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var access = await connections.GetAccessAsync(feed.Product, cancellationToken);
            var result = await feed.RunAsync(brandId, access, firstRun, cancellationToken);
            run.FromDate = result.Window.From;
            run.ToDate = result.Window.To;
            run.Rows = result.Rows;
            return await FinishAsync(brandId, run, RunStatuses.Succeeded, null, cancellationToken);
        }
        catch (ConnectionUnavailableException unavailable)
        {
            var reason = unavailable.Reason == SkipReasons.NotConnected ? SkipReasons.NotConnected : SkipReasons.NeedsReconnect;
            return await FinishAsync(brandId, run, RunStatuses.Skipped, reason, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The {Feed} feed failed for brand {BrandId}.", feed.Name, brandId);
            run.Error = exception.Message.Length > MaxErrorLength ? exception.Message[..MaxErrorLength] : exception.Message;
            return await FinishAsync(brandId, run, RunStatuses.Failed, null, cancellationToken);
        }
    }

    private async Task<FeedOutcome> FinishAsync(Guid brandId, FeedRun run, string status, string? reason, CancellationToken cancellationToken)
    {
        run.Status = status;
        run.Reason = reason;
        run.FinishedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new FeedOutcome(brandId, run.Feed, status, run.Rows);
    }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedRunner.cs`:

```csharp
using Kairo.Contracts.Brands;
using Kairo.Modules.Measurement.Data;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class FeedRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<FeedRunner> logger)
{
    public static readonly TimeSpan KeepRecordsFor = TimeSpan.FromDays(90);

    public async Task<DailyRun> RunAsync(IReadOnlyList<BrandSummary> brands, CancellationToken cancellationToken)
    {
        var outcomes = new List<FeedOutcome>();
        foreach (var brand in brands)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, cancellationToken);
                outcomes.AddRange(await scope.ServiceProvider.GetRequiredService<BrandFeeds>().RunAsync(brand.Id, cancellationToken));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "The data feeds failed for brand {BrandId}.", brand.Id);
            }
        }

        await DeleteOldRecordsAsync(cancellationToken);
        return new DailyRun(brands.Count, outcomes);
    }

    private async Task DeleteOldRecordsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var cutoff = time.GetUtcNow() - KeepRecordsFor;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            await scope.ServiceProvider.GetRequiredService<MeasurementDbContext>().FeedRuns
                .Where(run => run.StartedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedJobs.cs`:

```csharp
using Kairo.Contracts.Brands;
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedJobs
{
    public static void MapFeedJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/analytics/daily", DailyAsync);

    private static async Task<Results<Ok<DailyRun>, NotFound, ProblemHttpResult>> DailyAsync(
        Guid? brandId,
        AnalyticsOptions options,
        IBrandDirectory brands,
        FeedRunner runner,
        IHostApplicationLifetime lifetime)
    {
        if (!options.Enabled)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AnalyticsOptions.NotSetUp);
        }

        var stopping = lifetime.ApplicationStopping;
        var targets = brandId is { } id ? await brands.GetAsync([id], stopping) : await brands.ListAllAsync(stopping);
        if (brandId is not null && targets.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await runner.RunAsync(targets, stopping));
    }
}
```

The run takes its cancellation from the application stopping, not from the request. Cloud Scheduler stops waiting after 30 minutes, and the run carries on.

- [ ] **Step 9: Register the run and map the job**

Replace `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs` with:

```csharp
namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedSetup
{
    public static void AddFeeds(this IServiceCollection services)
    {
        services.AddHttpClient(MeasurementHttp.ClientName);
        services.AddSingleton<AnalyticsOptions>();
        services.AddSingleton<IServiceAccessToken, ApplicationDefaultAccessToken>();
        services.AddSingleton<BigQueryClient>();
        services.AddSingleton<BigQueryTables>();
        services.AddSingleton<BigQueryTableCheck>();
        services.AddSingleton<GoogleAdsReports>();
        services.AddScoped<IFeed, GoogleAdsFeed>();
        services.AddScoped<BrandFeeds>();
        services.AddScoped<FeedRunner>();
    }

    public static void MapFeeds(this IEndpointRouteBuilder api)
    {
        var analytics = api.MapBrandApi().MapGroup("/analytics");
        var sales = analytics.MapGroup("/sales");
        sales.MapPost("/uploads", SalesEndpoints.UploadAsync)
            .RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer)
            .DisableAntiforgery();
        sales.MapGet("/uploads", SalesEndpoints.ListUploadsAsync);

        var source = sales.MapGroup("/source");
        source.MapGet("", SalesEndpoints.GetSourceAsync);
        source.MapPut("", SalesEndpoints.SetSourceAsync).RequireBrandRole(BrandRoles.Admin);
        source.MapDelete("", SalesEndpoints.RemoveSourceAsync).RequireBrandRole(BrandRoles.Admin);
    }
}
```

In `core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs`, replace the empty `MapJobs` with:

```csharp
    public void MapJobs(IEndpointRouteBuilder jobs)
    {
        jobs.MapFeedJobs();
    }
```

- [ ] **Step 10: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.AdsRowsTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.BrandListTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.NightlyGoogleAdsTests"
dotnet test --project core/tests/Kairo.Worker.Tests
dotnet test --project core/tests/Kairo.ArchitectureTests
dotnet test --solution core/Kairo.slnx
```

Expected: all pass with 0 warnings. The API's OpenAPI document is unchanged, because the job route is mapped only on the worker.

- [ ] **Step 11: Commit**

```bash
git add core/src/Kairo.Contracts/Brands/IBrandDirectory.cs \
  core/src/Modules/Kairo.Modules.Brands/Workspaces/BrandDirectory.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedNames.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/IFeed.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/BrandFeeds.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedRunner.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedJobs.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/AdsRows.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/GoogleAdsReports.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/GoogleAdsFeed.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/src/Modules/Kairo.Modules.Measurement/MeasurementModule.cs \
  core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj \
  core/tests/Kairo.IntegrationTests/Api/KairoWorkerFactory.cs \
  core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs \
  core/tests/Kairo.IntegrationTests/Feeds/FeedRunRows.cs \
  core/tests/Kairo.IntegrationTests/Feeds/BrandListTests.cs \
  core/tests/Kairo.IntegrationTests/Feeds/AdsRowsTests.cs \
  core/tests/Kairo.IntegrationTests/Feeds/NightlyGoogleAdsTests.cs
git commit -m "Load each brand's Google Ads reports into BigQuery every night, by day and region"
```

---

### Task 7: GA4 totals

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/Ga4Reports.cs`
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/Ga4Feed.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/Ga4RowsTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/NightlyGa4Tests.cs`

**Interfaces:**
- Consumes: `IFeed`, `FeedResult`, `FeedNames.Ga4` (Task 6); `BigQueryTables`, `TableSchemas.Ga4RegionDaily`, `MeasurementHttp` (Task 3); `FeedClock`, `FeedWindow` (Task 4); `AdsRows.Text` and `AdsRows.Money` (Task 6); `KairoWorkerFactory`, `FeedRunRows` (Task 6).
- Produces:
  - `record Ga4Property(string Id, string TimeZone, string Currency)`.
  - `Ga4Rows.Of(JsonElement row, Ga4Property property)` returning a `ga4_region_daily` row.
  - `Ga4Reports.PageSize = 100_000`, `Ga4Reports.DescribeAsync(ConnectionAccess, ct)`, `Ga4Reports.DailyAsync(ConnectionAccess, Ga4Property, FeedWindow, ct)`, reading every page.
  - `Ga4Feed`, registered after `GoogleAdsFeed`.
  - Tests: `FakeGoogleApis.Ga4TimeZone` (default `Asia/Kolkata`), `Ga4PageRows` (default 100), `Ga4Requests`.

- [ ] **Step 1: Add the fake GA4 Data API**

In `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`:
- Below `public ConcurrentQueue<AdsReport> AdsReports { get; } = new();` add:

  ```csharp
      public string Ga4TimeZone { get; set; } = "Asia/Kolkata";

      public int Ga4PageRows { get; set; } = 100;

      public ConcurrentQueue<JsonElement> Ga4Requests { get; } = new();
  ```

- In `Product`, replace:

  ```csharp
              ("analyticsadmin.googleapis.com", "/v1beta/properties/777") => Json(Ga4Read, new { name = "properties/777" }),
  ```

  with:

  ```csharp
              ("analyticsadmin.googleapis.com", "/v1beta/properties/777") =>
                  Json(Ga4Read, new { name = "properties/777", timeZone = Ga4TimeZone, currencyCode = "INR" }),
              ("analyticsdata.googleapis.com", "/v1beta/properties/777:runReport") => Ga4Report(body),
  ```

- Above `private static bool AdsReachable(string path, string? login) =>` add:

  ```csharp
      private HttpResponseMessage Ga4Report(string body)
      {
          var request = JsonDocument.Parse(body).RootElement.Clone();
          Ga4Requests.Enqueue(request);
          var range = request.GetProperty("dateRanges")[0];
          var from = DateOnly.ParseExact(range.GetProperty("startDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
          var to = DateOnly.ParseExact(range.GetProperty("endDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
          var rows = new List<object>();
          for (var day = from; day <= to; day = day.AddDays(1))
          {
              var date = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
              rows.Add(Ga4Row(date, "Maharashtra", "120", "4", "3", "5600.75"));
              if (day == from)
              {
                  rows.Add(Ga4Row(date, "(not set)", "7", "0", "0", "0"));
              }
          }

          var offset = request.TryGetProperty("offset", out var start) ? start.GetInt32() : 0;
          return Json(HttpStatusCode.OK, new { rows = rows.Skip(offset).Take(Ga4PageRows).ToArray(), rowCount = rows.Count });
      }

      private static object Ga4Row(string date, string region, params string[] metrics) => new
      {
          dimensionValues = new[] { new { value = date }, new { value = "India" }, new { value = region } },
          metricValues = metrics.Select(value => new { value }).ToArray(),
      };
  ```

- [ ] **Step 2: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/Ga4RowsTests.cs`:

```csharp
using System.Text.Json;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class Ga4RowsTests
{
    [Fact]
    public void A_report_row_becomes_a_ga4_region_daily_row()
    {
        var row = JsonDocument.Parse(
            """{"dimensionValues":[{"value":"20261007"},{"value":"India"},{"value":"Goa"}],"metricValues":[{"value":"12"},{"value":"1.5"},{"value":"2"},{"value":"999.5"}]}""")
            .RootElement;

        Assert.Equal(
            """{"date":"2026-10-07","property_id":"777","country":"India","region":"Goa","currency":"INR","sessions":12,"key_events":1.5,"transactions":2,"purchase_revenue":"999.5"}""",
            Ga4Rows.Of(row, new Ga4Property("777", "Asia/Kolkata", "INR")).ToJsonString());
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Feeds/NightlyGa4Tests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class NightlyGa4Tests(SqlServer sqlServer) : IAsyncLifetime
{
    private const int FirstWindowRows = 396;

    private static readonly DateTimeOffset Night = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeGoogleApis Google => _api.GoogleApis;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Night);
    }

    [Fact]
    public async Task A_nightly_run_loads_GA4_totals_by_day_and_region()
    {
        var brandId = await BrandWithGa4Async();

        var run = await RunAsync();

        var outcome = Outcome(run, brandId);
        Assert.Equal("succeeded", outcome.GetProperty("status").GetString());
        Assert.Equal(FirstWindowRows, outcome.GetProperty("rows").GetInt32());
        var rows = Google.BigQuery.Rows(AnalyticsOptions.DatasetFor(brandId), "ga4_region_daily");
        Assert.Equal(FirstWindowRows, rows.Count);
        var day = rows.Single(row => row["date"]!.GetValue<string>() == "2026-10-13");
        Assert.Equal("777", day["property_id"]!.GetValue<string>());
        Assert.Equal("India", day["country"]!.GetValue<string>());
        Assert.Equal("Maharashtra", day["region"]!.GetValue<string>());
        Assert.Equal("INR", day["currency"]!.GetValue<string>());
        Assert.Equal(120, day["sessions"]!.GetValue<long>());
        Assert.Equal(4d, day["key_events"]!.GetValue<double>());
        Assert.Equal(3, day["transactions"]!.GetValue<long>());
        Assert.Equal("5600.75", day["purchase_revenue"]!.GetValue<string>());
        Assert.Contains(rows, row => row["region"]!.GetValue<string>() == "(not set)" && row["date"]!.GetValue<string>() == "2025-09-14");
    }

    [Fact]
    public async Task Every_page_of_the_report_is_read()
    {
        await BrandWithGa4Async();

        await RunAsync();

        Assert.Equal(new[] { 0, 100, 200, 300 }, Google.Ga4Requests.Select(request => request.GetProperty("offset").GetInt32()));
        Assert.All(Google.Ga4Requests, request => Assert.Equal(Ga4Reports.PageSize, request.GetProperty("limit").GetInt32()));
    }

    [Fact]
    public async Task The_window_follows_the_propertys_time_zone()
    {
        Google.Ga4TimeZone = "America/Los_Angeles";
        await BrandWithGa4Async();

        await RunAsync();
        var first = Google.Ga4Requests.First().GetProperty("dateRanges")[0];
        await RunAsync();
        var later = Google.Ga4Requests.Last().GetProperty("dateRanges")[0];

        Assert.Equal("2025-09-13", first.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-12", first.GetProperty("endDate").GetString());
        Assert.Equal("2026-10-06", later.GetProperty("startDate").GetString());
        Assert.Equal("2026-10-12", later.GetProperty("endDate").GetString());
    }

    [Fact]
    public async Task A_brand_without_GA4_is_recorded_as_skipped()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        Assert.Equal("not_connected", Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.Ga4).Reason);
        Assert.Empty(Google.Ga4Requests);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private async Task<JsonElement> RunAsync()
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await _worker.RunDailyAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<Guid> BrandWithGa4Async()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "ga4");
        return brandId;
    }

    private static JsonElement Outcome(JsonElement run, Guid brandId) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == FeedNames.Ga4);
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.Ga4RowsTests"
```

Expected: the build fails with `CS0103: The name 'Ga4Rows' does not exist in the current context`.

- [ ] **Step 4: Add the GA4 reports and the feed**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/Ga4Reports.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record Ga4Property(string Id, string TimeZone, string Currency);

internal static class Ga4Rows
{
    public static JsonObject Of(JsonElement row, Ga4Property property)
    {
        var dimensions = Values(row, "dimensionValues");
        var metrics = Values(row, "metricValues");
        return new JsonObject
        {
            ["date"] = FeedClock.Iso(DateOnly.ParseExact(dimensions[0] ?? "", "yyyyMMdd", CultureInfo.InvariantCulture)),
            ["property_id"] = property.Id,
            ["country"] = dimensions[1],
            ["region"] = dimensions[2],
            ["currency"] = property.Currency,
            ["sessions"] = Whole(metrics[0]),
            ["key_events"] = double.Parse(metrics[1] ?? "0", CultureInfo.InvariantCulture),
            ["transactions"] = Whole(metrics[2]),
            ["purchase_revenue"] = AdsRows.Money(decimal.Parse(metrics[3] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture)),
        };
    }

    private static string?[] Values(JsonElement row, string name) =>
        row.GetProperty(name).EnumerateArray().Select(value => AdsRows.Text(value, "value")).ToArray();

    private static long Whole(string? text) => (long)decimal.Parse(text ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
}

internal sealed class Ga4Reports(IHttpClientFactory http)
{
    public const int PageSize = 100_000;

    private const string AdminUrl = "https://analyticsadmin.googleapis.com/v1beta";
    private const string DataUrl = "https://analyticsdata.googleapis.com/v1beta";

    public async Task<Ga4Property> DescribeAsync(ConnectionAccess access, CancellationToken cancellationToken)
    {
        var property = await http.CreateClient(MeasurementHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{AdminUrl}/properties/{Uri.EscapeDataString(access.ExternalId)}", access.AccessToken, null, cancellationToken);
        return new Ga4Property(
            access.ExternalId,
            AdsRows.Text(property, "timeZone") ?? FeedClock.IndiaTimeZone,
            AdsRows.Text(property, "currencyCode") ?? "");
    }

    public async Task<List<JsonObject>> DailyAsync(ConnectionAccess access, Ga4Property property, FeedWindow window, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(MeasurementHttp.ClientName);
        var rows = new List<JsonObject>();
        var offset = 0;
        while (true)
        {
            var page = await client.CallAsync(
                HttpMethod.Post,
                $"{DataUrl}/properties/{Uri.EscapeDataString(property.Id)}:runReport",
                access.AccessToken,
                new
                {
                    dateRanges = new[] { new { startDate = FeedClock.Iso(window.From), endDate = FeedClock.Iso(window.To) } },
                    dimensions = new[] { new { name = "date" }, new { name = "country" }, new { name = "region" } },
                    metrics = new[] { new { name = "sessions" }, new { name = "keyEvents" }, new { name = "transactions" }, new { name = "purchaseRevenue" } },
                    limit = PageSize,
                    offset,
                },
                cancellationToken);
            var read = 0;
            if (page.TryGetProperty("rows", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in list.EnumerateArray())
                {
                    rows.Add(Ga4Rows.Of(row, property));
                    read++;
                }
            }

            offset += read;
            var total = page.TryGetProperty("rowCount", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;
            if (read == 0 || offset >= total)
            {
                return rows;
            }
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/Ga4Feed.cs`:

```csharp
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed class Ga4Feed(Ga4Reports reports, BigQueryTables tables, TimeProvider time) : IFeed
{
    public string Name => FeedNames.Ga4;

    public ConnectedProduct Product => ConnectedProduct.Ga4;

    public Task<bool> IsSetUpAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public string TargetOf(ConnectionStatus status) => status.ExternalId ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken)
    {
        var property = await reports.DescribeAsync(access, cancellationToken);
        var window = FeedWindow.For(FeedClock.Today(time, property.TimeZone), firstRun);
        var rows = await tables.ReplaceDaysAsync(
            brandId,
            TableSchemas.Ga4RegionDaily,
            window.Days(),
            await reports.DailyAsync(access, property, window, cancellationToken),
            cancellationToken);
        return new FeedResult(window, rows);
    }
}
```

In `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`, replace:

```csharp
        services.AddSingleton<GoogleAdsReports>();
        services.AddScoped<IFeed, GoogleAdsFeed>();
```

with:

```csharp
        services.AddSingleton<GoogleAdsReports>();
        services.AddSingleton<Ga4Reports>();
        services.AddScoped<IFeed, GoogleAdsFeed>();
        services.AddScoped<IFeed, Ga4Feed>();
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.Ga4RowsTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.NightlyGa4Tests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.NightlyGoogleAdsTests"
dotnet test --solution core/Kairo.slnx
```

Expected: all pass with 0 warnings. The Google Ads tests still pass, because they pick out the `google-ads` outcome by name.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Feeds/Ga4Reports.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/Ga4Feed.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs \
  core/tests/Kairo.IntegrationTests/Feeds/Ga4RowsTests.cs \
  core/tests/Kairo.IntegrationTests/Feeds/NightlyGa4Tests.cs
git commit -m "Load each brand's GA4 totals by day and region every night"
```

---

### Task 8: Copying the brand's sales table

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesTableFeed.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/NightlySalesTableTests.cs`

**Interfaces:**
- Consumes: `IFeed`, `FeedResult`, `FeedNames.SalesTable` (Task 6); `BigQueryClient.QueryAsync`, `QueryParameter`, `BigQueryTables`, `TableSchemas.SalesDaily` (Task 3); `SalesRows.Table` (Task 4); `SalesSource` (Task 1); the source endpoints (Task 5); `FakeBigQuery.SalesTableRows`, `SalesPageSize`, `Queries` (Task 3); `SalesFiles` (Task 4).
- Produces:
  - `SalesTableQuery.For(SalesSource)`, the aggregate query.
  - `SalesTableFeed`, registered after `Ga4Feed`. With no source set it isn't set up, so no record is written. Its external ID is the source's `TableRef`. The query runs in the BigQuery connection's project (`ConnectionAccess.ExternalId`) with the brand's access token, in the source's location.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/NightlySalesTableTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class NightlySalesTableTests(SqlServer sqlServer) : IAsyncLifetime
{
    private static readonly DateTimeOffset Night = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FakeBigQuery BigQuery => _api.GoogleApis.BigQuery;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, Night);
    }

    [Fact]
    public async Task A_nightly_run_copies_the_brands_sales_table()
    {
        var (_, brandId) = await BrandWithSalesTableAsync();
        BigQuery.SalesTableRows.AddRange(
        [
            new SalesTableRow("2026-10-05", "Goa", "4", "400.50"),
            new SalesTableRow("2026-10-06", "Goa", "2", "200"),
            new SalesTableRow("2026-10-06", "Kerala", "1", "10"),
            new SalesTableRow("2025-01-01", "Goa", "9", "9"),
        ]);

        var run = await RunAsync();

        var outcome = Outcome(run, brandId);
        Assert.Equal("succeeded", outcome.GetProperty("status").GetString());
        Assert.Equal(3, outcome.GetProperty("rows").GetInt32());
        Assert.Equal(
            new[] { "2026-10-05 Goa 4 400.50 table", "2026-10-06 Goa 2 200 table", "2026-10-06 Kerala 1 10 table" },
            SalesFiles.RowsOf(BigQuery, brandId));
        var query = Assert.Single(BigQuery.Queries, query => query.Project == FakeBigQuery.BrandProject);
        Assert.StartsWith("access-", query.AccessToken);
        Assert.Equal("asia-south1", query.Location);
        Assert.Contains("FROM `acme-data.shop.daily_orders`", query.Sql);
        Assert.Contains("CAST(SUM(`order_count`) AS INT64) AS orders", query.Sql);
        Assert.Contains("CAST(SUM(`net_revenue`) AS NUMERIC) AS revenue", query.Sql);
        Assert.Contains("WHERE DATE(`order_date`) BETWEEN @from AND @to", query.Sql);
    }

    [Fact]
    public async Task The_sales_window_is_in_India_time()
    {
        await BrandWithSalesTableAsync();

        await RunAsync();

        var parameters = Assert.Single(BigQuery.Queries, query => query.Project == FakeBigQuery.BrandProject).Parameters;
        Assert.Equal("2025-09-14", ParameterValue(parameters, "from"));
        Assert.Equal("2026-10-13", ParameterValue(parameters, "to"));
    }

    [Fact]
    public async Task Without_a_sales_table_nothing_is_recorded()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");

        var run = await RunAsync();

        Assert.DoesNotContain(run.GetProperty("runs").EnumerateArray(), outcome => outcome.GetProperty("feed").GetString() == FeedNames.SalesTable);
        Assert.DoesNotContain(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.SalesTable);
    }

    [Fact]
    public async Task A_sales_table_without_the_BigQuery_connection_is_skipped()
    {
        var (admin, brandId) = await BrandWithSalesTableAsync();
        (await admin.Browser.DeleteAsync($"/api/brands/{brandId}/connections/bigquery", Token)).EnsureSuccessStatusCode();

        var run = await RunAsync();

        Assert.Equal("skipped", Outcome(run, brandId).GetProperty("status").GetString());
        var stored = Assert.Single(await FeedRunRows.ListAsync(_database, brandId), stored => stored.Feed == FeedNames.SalesTable);
        Assert.Equal("not_connected", stored.Reason);
    }

    [Fact]
    public async Task The_later_of_an_upload_and_a_table_copy_wins_each_day()
    {
        var (admin, brandId) = await BrandWithSalesTableAsync();
        BigQuery.SalesTableRows.Add(new SalesTableRow("2026-10-06", "Goa", "2", "200"));
        var upload = SalesFiles.Header + "2026-10-06,Punjab,1,50\n2026-10-06,Goa,50,5000\n";
        (await admin.Browser.PostAsync($"/api/brands/{brandId}/analytics/sales/uploads", SalesFiles.Csv(upload), Token)).EnsureSuccessStatusCode();

        await RunAsync();

        Assert.Equal(new[] { "2026-10-06 Goa 2 200 table" }, SalesFiles.RowsOf(BigQuery, brandId));

        (await admin.Browser.PostAsync($"/api/brands/{brandId}/analytics/sales/uploads", SalesFiles.Csv(upload), Token)).EnsureSuccessStatusCode();

        Assert.Equal(new[] { "2026-10-06 Goa 50 5000 upload", "2026-10-06 Punjab 1 50 upload" }, SalesFiles.RowsOf(BigQuery, brandId));
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private async Task<JsonElement> RunAsync()
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await _worker.RunDailyAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> BrandWithSalesTableAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");
        (await admin.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/analytics/sales/source",
            new
            {
                table = "acme-data.shop.daily_orders",
                columns = new { date = "order_date", region = "state", orders = "order_count", revenue = "net_revenue" },
            },
            Token)).EnsureSuccessStatusCode();
        return (admin, brandId);
    }

    private static string? ParameterValue(JsonElement parameters, string name) =>
        parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == name)
            .GetProperty("parameterValue").GetProperty("value").GetString();

    private static JsonElement Outcome(JsonElement run, Guid brandId) =>
        run.GetProperty("runs").EnumerateArray()
            .Single(outcome => outcome.GetProperty("brandId").GetGuid() == brandId && outcome.GetProperty("feed").GetString() == FeedNames.SalesTable);
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.NightlySalesTableTests"
```

Expected: `A_nightly_run_copies_the_brands_sales_table`, `The_sales_window_is_in_India_time`, `A_sales_table_without_the_BigQuery_connection_is_skipped` and `The_later_of_an_upload_and_a_table_copy_wins_each_day` fail, because no `sales-table` outcome exists (`Sequence contains no matching element`) and no query reaches the brand's project. `Without_a_sales_table_nothing_is_recorded` passes already, since nothing writes `sales-table` records yet; it guards the behaviour once the feed exists.

- [ ] **Step 3: Add the feed**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesTableFeed.cs`:

```csharp
using System.Globalization;
using System.Text.Json.Nodes;
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement.Feeds;

internal static class SalesTableQuery
{
    public static string For(SalesSource source) => $"""
        SELECT DATE(`{source.DateColumn}`) AS date, CAST(`{source.RegionColumn}` AS STRING) AS region,
               CAST(SUM(`{source.OrdersColumn}`) AS INT64) AS orders, CAST(SUM(`{source.RevenueColumn}`) AS NUMERIC) AS revenue
        FROM `{source.TableRef}`
        WHERE DATE(`{source.DateColumn}`) BETWEEN @from AND @to
        GROUP BY 1, 2
        """;
}

internal sealed class SalesTableFeed(MeasurementDbContext db, BigQueryClient bigQuery, BigQueryTables tables, TimeProvider time) : IFeed
{
    private SalesSource? _source;

    public string Name => FeedNames.SalesTable;

    public ConnectedProduct Product => ConnectedProduct.BigQuery;

    public async Task<bool> IsSetUpAsync(CancellationToken cancellationToken)
    {
        _source = await db.SalesSources.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return _source is not null;
    }

    public string TargetOf(ConnectionStatus status) => _source?.TableRef ?? "";

    public async Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken)
    {
        var source = _source ?? throw new InvalidOperationException("No sales table is set.");
        var window = FeedWindow.For(FeedClock.Today(time, FeedClock.IndiaTimeZone), firstRun);
        var results = await bigQuery.QueryAsync(
            access.ExternalId,
            access.AccessToken,
            SalesTableQuery.For(source),
            source.Location,
            [QueryParameter.Date("from", window.From), QueryParameter.Date("to", window.To)],
            cancellationToken);
        var rows = results
            .Select(result => new JsonObject
            {
                ["date"] = result[0],
                ["region"] = result[1],
                ["orders"] = long.Parse(result[2] ?? "0", CultureInfo.InvariantCulture),
                ["revenue"] = result[3] ?? "0",
                ["source"] = SalesRows.Table,
            })
            .ToList();
        return new FeedResult(window, await tables.ReplaceDaysAsync(brandId, TableSchemas.SalesDaily, window.Days(), rows, cancellationToken));
    }
}
```

The column names and the table name were checked against safe patterns when the source was saved (Task 5), so quoting them in backticks can't break out of the query.

In `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`, replace:

```csharp
        services.AddScoped<IFeed, Ga4Feed>();
```

with:

```csharp
        services.AddScoped<IFeed, Ga4Feed>();
        services.AddScoped<IFeed, SalesTableFeed>();
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.NightlySalesTableTests"
dotnet test --solution core/Kairo.slnx
```

Expected: all pass with 0 warnings. `A_nightly_run_copies_the_brands_sales_table` reads two result pages, because the fake returns 2 rows per page.

- [ ] **Step 5: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Feeds/SalesTableFeed.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/tests/Kairo.IntegrationTests/Feeds/NightlySalesTableTests.cs
git commit -m "Copy each brand's own sales table into BigQuery every night"
```

---

### Task 9: Feed status and the permission matrix

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Feeds/FeedStatusTests.cs`

**Interfaces:**
- Consumes: `FeedRun`, `MeasurementDbContext` (Task 1); `FeedNames`, `RunStatuses`, `FeedStates`, `BrandFeeds.RunningFor` (Task 6); `AnalyticsOptions.DatasetFor` (Task 3); `IConnections.GetStatusAsync`; `FeedRunRows.InsertAsync` (Task 6).
- Produces:
  - `FeedState.Of(ConnectionState connection, bool isSetUp, FeedRun? latest, DateTimeOffset now)`.
  - `record FeedStatusView(string Feed, string State, DateTimeOffset? LastRunAt, DateTimeOffset? LastSuccessAt, DateOnly? CoveredThrough, string? LastError)`, `record FeedsView(string Dataset, FeedStatusView[] Feeds)`.
  - `GET /api/brands/{brandId}/analytics/feeds` (members), with the feeds in the order `google-ads`, `ga4`, `sales-table`.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Feeds/FeedStatusTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class FeedStatusTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => _api.Clock.GetUtcNow();

    [Fact]
    public async Task A_new_workspace_has_no_feed_set_up()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var status = await StatusAsync(admin, brandId);

        Assert.Equal(AnalyticsOptions.DatasetFor(brandId), status.GetProperty("dataset").GetString());
        var feeds = status.GetProperty("feeds").EnumerateArray().ToList();
        Assert.Equal(new[] { "google-ads", "ga4", "sales-table" }, feeds.Select(feed => feed.GetProperty("feed").GetString()));
        Assert.All(feeds, feed =>
        {
            Assert.Equal("not_set_up", feed.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, feed.GetProperty("lastRunAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, feed.GetProperty("coveredThrough").ValueKind);
        });
    }

    [Fact]
    public async Task A_connected_feed_waits_for_its_first_run()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();

        Assert.Equal("waiting", (await AdsFeedAsync(admin, brandId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_skipped_latest_run_still_counts_as_waiting()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "skipped", Now.AddHours(-1));

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("waiting", feed.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, feed.GetProperty("lastRunAt").ValueKind);
    }

    [Fact]
    public async Task A_connection_that_needs_attention_shows_on_its_feed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);
        (await admin.Browser.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token)).EnsureSuccessStatusCode();

        Assert.Equal("needs_connection", (await AdsFeedAsync(admin, brandId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_succeeded_run_is_ok_and_says_how_far_the_data_reaches()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        var started = Now.AddHours(-1);
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "succeeded", started, new DateOnly(2026, 10, 12));

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("ok", feed.GetProperty("state").GetString());
        Assert.Equal("2026-10-12", feed.GetProperty("coveredThrough").GetString());
        Assert.Equal(started.AddMinutes(1), feed.GetProperty("lastSuccessAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, feed.GetProperty("lastError").ValueKind);
    }

    [Fact]
    public async Task A_run_in_progress_shows_as_running()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "running", Now.AddMinutes(-30));

        Assert.Equal("running", (await AdsFeedAsync(admin, brandId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_run_that_never_finished_shows_as_failing()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "running", Now.AddHours(-3));

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("failing", feed.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, feed.GetProperty("lastError").ValueKind);
    }

    [Fact]
    public async Task A_failed_run_is_failing_and_keeps_the_last_good_day()
    {
        var (admin, brandId) = await AdsWorkspaceAsync();
        await FeedRunRows.InsertAsync(sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "succeeded", Now.AddDays(-2), new DateOnly(2026, 10, 10));
        await FeedRunRows.InsertAsync(
            sqlServer.KairoConnectionString, brandId, FeedNames.GoogleAds, "failed", Now.AddHours(-1), error: "Google answered 500: boom");

        var feed = await AdsFeedAsync(admin, brandId);

        Assert.Equal("failing", feed.GetProperty("state").GetString());
        Assert.Equal("Google answered 500: boom", feed.GetProperty("lastError").GetString());
        Assert.Equal("2026-10-10", feed.GetProperty("coveredThrough").GetString());
    }

    [Fact]
    public async Task The_sales_table_feed_needs_a_table()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "bigquery");

        Assert.Equal("not_set_up", (await FeedAsync(admin, brandId, "sales-table")).GetProperty("state").GetString());

        (await admin.Browser.PutAsJsonAsync(
            $"/api/brands/{brandId}/analytics/sales/source",
            new
            {
                table = "acme-data.shop.daily_orders",
                columns = new { date = "order_date", region = "state", orders = "order_count", revenue = "net_revenue" },
            },
            Token)).EnsureSuccessStatusCode();

        Assert.Equal("waiting", (await FeedAsync(admin, brandId, "sales-table")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Another_workspace_cannot_read_the_feeds()
    {
        var (_, brandId) = await WorkspaceAsync();
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync($"/api/brands/{brandId}/analytics/feeds", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static async Task<JsonElement> StatusAsync(Member admin, Guid brandId) =>
        await admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/analytics/feeds", Token);

    private static async Task<JsonElement> FeedAsync(Member admin, Guid brandId, string feed) =>
        (await StatusAsync(admin, brandId)).GetProperty("feeds").EnumerateArray().Single(entry => entry.GetProperty("feed").GetString() == feed);

    private static Task<JsonElement> AdsFeedAsync(Member admin, Guid brandId) => FeedAsync(admin, brandId, "google-ads");

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<(Member Admin, Guid BrandId)> AdsWorkspaceAsync()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin.Browser, brandId, "google-ads");
        return (admin, brandId);
    }
}
```

In `core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs`, add these rows at the end of the `endpoints` array, after `(HttpMethod.Delete, $"{brand}/connections/google-ads", null, true),`:

```csharp
            (HttpMethod.Get, $"{brand}/analytics/feeds", null, false),
            (HttpMethod.Get, $"{brand}/analytics/sales/uploads", null, false),
            (HttpMethod.Get, $"{brand}/analytics/sales/source", null, false),
            (HttpMethod.Put, $"{brand}/analytics/sales/source", new { table = "bad" }, true),
            (HttpMethod.Delete, $"{brand}/analytics/sales/source", null, true),
```

The upload route is left out of the matrix, because marketers may upload too; `SalesUploadTests.Admins_and_marketers_can_upload_but_approvers_cannot` covers it.

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.FeedStatusTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.PermissionTests"
```

Expected: the status tests fail with `404 (Not Found)` from `GetFromJsonAsync`, except `Another_workspace_cannot_read_the_feeds`, which already gets its 404. The permission matrix passes, because the sales routes exist since Task 5 and the missing `/feeds` route returns 404 rather than 403 for every role.

- [ ] **Step 3: Add the status endpoint**

Create `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedEndpoints.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;

namespace Kairo.Modules.Measurement.Feeds;

internal sealed record FeedStatusView(
    string Feed,
    string State,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? LastSuccessAt,
    DateOnly? CoveredThrough,
    string? LastError);

internal sealed record FeedsView(string Dataset, FeedStatusView[] Feeds);

internal static class FeedState
{
    public static string Of(ConnectionState connection, bool isSetUp, FeedRun? latest, DateTimeOffset now)
    {
        if (!isSetUp || connection == ConnectionState.NotConnected)
        {
            return FeedStates.NotSetUp;
        }

        if (connection is ConnectionState.NeedsReconnect or ConnectionState.ChoosingAccount)
        {
            return FeedStates.NeedsConnection;
        }

        if (latest is null || latest.Status == RunStatuses.Skipped)
        {
            return FeedStates.Waiting;
        }

        if (latest.Status == RunStatuses.Running)
        {
            return latest.StartedAt > now - BrandFeeds.RunningFor ? FeedStates.Running : FeedStates.Failing;
        }

        return latest.Status == RunStatuses.Failed ? FeedStates.Failing : FeedStates.Ok;
    }
}

internal static class FeedEndpoints
{
    private static readonly (string Feed, ConnectedProduct Product)[] Feeds =
    [
        (FeedNames.GoogleAds, ConnectedProduct.GoogleAds),
        (FeedNames.Ga4, ConnectedProduct.Ga4),
        (FeedNames.SalesTable, ConnectedProduct.BigQuery),
    ];

    public static async Task<Ok<FeedsView>> StatusAsync(
        MeasurementDbContext db,
        IConnections connections,
        CurrentMember member,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hasSource = await db.SalesSources.AnyAsync(cancellationToken);
        var views = new List<FeedStatusView>();
        foreach (var (feed, product) in Feeds)
        {
            var connection = (await connections.GetStatusAsync(product, cancellationToken)).State;
            var latest = await LatestAsync(db, feed, null, cancellationToken);
            var success = await LatestAsync(db, feed, RunStatuses.Succeeded, cancellationToken);
            var state = FeedState.Of(connection, feed != FeedNames.SalesTable || hasSource, latest, now);
            var failure = state == FeedStates.Failing ? await LatestAsync(db, feed, RunStatuses.Failed, cancellationToken) : null;
            views.Add(new FeedStatusView(feed, state, latest?.StartedAt, success?.FinishedAt, success?.ToDate, failure?.Error));
        }

        return TypedResults.Ok(new FeedsView(AnalyticsOptions.DatasetFor(member.BrandId), views.ToArray()));
    }

    private static Task<FeedRun?> LatestAsync(MeasurementDbContext db, string feed, string? status, CancellationToken cancellationToken) =>
        db.FeedRuns
            .Where(run => run.Feed == feed && (status == null || run.Status == status))
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
```

In `core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs`, in `MapFeeds`, below `var analytics = api.MapBrandApi().MapGroup("/analytics");` add:

```csharp
        analytics.MapGet("/feeds", FeedEndpoints.StatusAsync);
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Feeds.FeedStatusTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.PermissionTests"
```

Expected: all pass with 0 warnings.

- [ ] **Step 5: Regenerate the API contract and run everything**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --solution core/Kairo.slnx
npm --prefix web run lint
```

Expected: `openapi.json` gains `GET /api/brands/{brandId}/analytics/feeds`. Everything passes.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedEndpoints.cs \
  core/src/Modules/Kairo.Modules.Measurement/Feeds/FeedSetup.cs \
  core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs \
  core/tests/Kairo.IntegrationTests/Feeds/FeedStatusTests.cs
git commit -m "Show each feed's state, last run and how far its data reaches"
git add core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Add the feed status endpoint to the API contract"
```

---

### Task 10: Deployment and Aspire

**Files:**
- Modify: `deploy/Kairo.Deploy/Common.ps1`
- Modify: `deploy/Kairo.Deploy/Setup.ps1`
- Modify: `deploy/Kairo.Deploy/Release.ps1`
- Modify: `deploy/tests/Setup.Tests.ps1`
- Modify: `deploy/tests/Release.Tests.ps1`
- Modify: `core/src/Kairo.AppHost/AppHost.cs`

**Interfaces:**
- Consumes: `Kairo:Analytics:Project`, `Kairo:Analytics:Location` and the worker route `/jobs/analytics/daily` (Tasks 3 and 6); the existing `Invoke-Gcloud`, `Test-Gcloud`, `Add-KairoBinding`, `Grant-KairoInvoker`, `Get-KairoServiceAudience` and `Get-KairoServiceAccount`.
- Produces:
  - `$script:AnalyticsJob = 'kairo-analytics-daily'` in `Common.ps1`.
  - `Publish-KairoSchedule -Environment <hashtable> -WorkerUrl <string>`, which creates the Scheduler job or updates it when `gcloud scheduler jobs describe` finds it.
  - Setup: the `analyticsdata` and `cloudscheduler` APIs, the `kairo-scheduler` service account, BigQuery Data Owner and Job User for `kairo-api` and `kairo-worker`, and the Google secrets for `kairo-worker`.
  - Release: the worker gets `--timeout 3600`, the Google secrets and the analytics settings; the API gets the analytics settings; `kairo-scheduler` may invoke the worker.

- [ ] **Step 1: Write the failing deployment tests**

In `deploy/tests/Setup.Tests.ps1`, inside `Context 'in an empty project'`:
- Replace the `It 'enables the APIs Kairo needs'` block with:

  ```powershell
          It 'enables the APIs Kairo needs' {
              $calls | Should -Contain ('services enable run.googleapis.com sqladmin.googleapis.com compute.googleapis.com ' +
                  'servicenetworking.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com ' +
                  'secretmanager.googleapis.com iam.googleapis.com storage.googleapis.com cloudkms.googleapis.com ' +
                  'googleads.googleapis.com youtube.googleapis.com merchantapi.googleapis.com analyticsadmin.googleapis.com ' +
                  'bigquery.googleapis.com analyticsdata.googleapis.com cloudscheduler.googleapis.com --project kairo-test')
          }
  ```

- Replace the `It 'creates one service account per service'` block with:

  ```powershell
          It 'creates one service account per service, and one for Cloud Scheduler' {
              foreach ($name in 'kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build', 'kairo-scheduler') {
                  $calls | Should -Contain "iam service-accounts create $name --project kairo-test"
              }
          }
  ```

- Replace the `It 'lets each service read only the secrets it needs'` block with:

  ```powershell
          It 'lets each service read only the secrets it needs' {
              foreach ($name in 'kairo-api', 'kairo-worker', 'kairo-migrator') {
                  $calls | Should -Contain "secrets add-iam-policy-binding kairo-sql-connection --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test"
              }
              foreach ($secret in 'google-client-id', 'google-client-secret') {
                  foreach ($name in 'kairo-api', 'kairo-worker') {
                      $calls | Should -Contain "secrets add-iam-policy-binding $secret --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/secretmanager.secretAccessor --project kairo-test"
                  }
              }
              ($calls | Where-Object { $_ -like 'secrets add-iam-policy-binding google-*kairo-web@*' }) | Should -BeNullOrEmpty
              ($calls | Where-Object { $_ -like 'secrets add-iam-policy-binding *kairo-scheduler@*' }) | Should -BeNullOrEmpty
          }
  ```

- After the `It 'lets only the API and the worker use the token key'` block, add:

  ```powershell
          It 'lets the API and the worker write brand datasets and run BigQuery jobs' {
              foreach ($name in 'kairo-api', 'kairo-worker') {
                  foreach ($role in 'roles/bigquery.dataOwner', 'roles/bigquery.jobUser') {
                      $calls | Should -Contain "projects add-iam-policy-binding kairo-test --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role $role --condition None"
                  }
              }
              ($calls | Where-Object { $_ -like 'projects add-iam-policy-binding*bigquery*kairo-web@*' -or $_ -like 'projects add-iam-policy-binding*kairo-web@*bigquery*' }) | Should -BeNullOrEmpty
          }
  ```

In `deploy/tests/Release.Tests.ps1`, inside `Describe 'Invoke-KairoDeploy'`:
- Replace the `BeforeEach` block with:

  ```powershell
      BeforeEach {
          $script:calls = [System.Collections.Generic.List[string]]::new()
          $script:googleConfigured = $true
          $script:scheduleExists = $false
          Mock -ModuleName Kairo.Deploy Assert-KairoCleanTree { }
          Mock -ModuleName Kairo.Deploy Get-KairoCommit { 'abc123def456' }
          Mock -ModuleName Kairo.Deploy Invoke-KairoTests { $script:calls.Add('tests') }
          Mock -ModuleName Kairo.Deploy Get-KairoProjectNumber { '123' }
          Mock -ModuleName Kairo.Deploy Test-KairoSecretHasValue { $script:googleConfigured }
          Mock -ModuleName Kairo.Deploy Test-Gcloud { $script:scheduleExists }
          Mock -ModuleName Kairo.Deploy Invoke-Gcloud {
              $line = $ArgumentList -join ' '
              $script:calls.Add($line)
              if ($line -like 'run services describe kairo-api *') { 'https://kairo-api-abc123-el.a.run.app' }
              if ($line -like 'run services describe kairo-worker *') { 'https://kairo-worker-abc123-el.a.run.app' }
          }
      }
  ```

- Replace the `It 'leaves Google sign-in off until its secrets have a value'` block with:

  ```powershell
      It 'leaves Google sign-in off until its secrets have a value' {
          $script:googleConfigured = $false

          Invoke-KairoDeploy -Environment $environment -Root $root 3>$null | Out-Null

          foreach ($service in 'kairo-api', 'kairo-worker') {
              $deploy = $calls | Where-Object { $_ -like "run deploy $service *" }
              $deploy | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest'
              $deploy | Should -Not -BeLike '*google-client*'
          }
      }
  ```

- Replace the `It 'deploys the worker privately with the database'` block with:

  ```powershell
      It 'deploys the worker privately with the database, the Google client and an hour to finish a run' {
          Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

          $worker = $calls | Where-Object { $_ -like 'run deploy kairo-worker *' }
          $worker | Should -BeLike '*--no-allow-unauthenticated*'
          $worker | Should -BeLike '*--network kairo --subnet kairo-run --vpc-egress private-ranges-only*'
          $worker | Should -BeLike '*--timeout 3600*'
          $worker | Should -BeLike '*--set-secrets=ConnectionStrings__kairo=kairo-sql-connection:latest,Authentication__Google__ClientId=google-client-id:latest,Authentication__Google__ClientSecret=google-client-secret:latest*'
      }

      It 'tells the API and the worker where brand datasets live' {
          Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

          foreach ($service in 'kairo-api', 'kairo-worker') {
              ($calls | Where-Object { $_ -like "run deploy $service *" }) |
                  Should -BeLike '*Kairo__Analytics__Project=kairo-test,Kairo__Analytics__Location=asia-south1*'
          }
      }

      It 'creates the nightly analytics job when it is missing' {
          Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

          Should -Invoke -ModuleName Kairo.Deploy Test-Gcloud -ParameterFilter {
              ($ArgumentList -join ' ') -eq 'scheduler jobs describe kairo-analytics-daily --location asia-south1 --project kairo-test'
          }
          $calls | Should -Contain ('scheduler jobs create http kairo-analytics-daily --schedule 0 5 * * * --time-zone Asia/Kolkata ' +
              '--uri https://kairo-worker-abc123-el.a.run.app/jobs/analytics/daily --http-method POST ' +
              '--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com ' +
              '--oidc-token-audience https://kairo-worker-abc123-el.a.run.app --attempt-deadline 1800s --max-retry-attempts 0 ' +
              '--location asia-south1 --project kairo-test')
      }

      It 'updates the nightly analytics job when it already exists' {
          $script:scheduleExists = $true

          Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

          ($calls | Where-Object { $_ -like 'scheduler jobs create *' }) | Should -BeNullOrEmpty
          ($calls | Where-Object { $_ -like 'scheduler jobs update http kairo-analytics-daily *' }) |
              Should -BeLike '*--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com*'
      }
  ```

- Replace the `It 'lets only the right callers invoke the private services'` block with:

  ```powershell
      It 'lets only the right callers invoke the private services' {
          Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

          $grants = $calls | Where-Object { $_ -like 'run services add-iam-policy-binding*' }
          $grants | Should -HaveCount 4
          $grants | Should -Contain 'run services add-iam-policy-binding kairo-api --region asia-south1 --project kairo-test --member serviceAccount:kairo-web@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
          $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-api@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
          $grants | Should -Contain 'run services add-iam-policy-binding kairo-agents --region asia-south1 --project kairo-test --member serviceAccount:kairo-worker@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
          $grants | Should -Contain 'run services add-iam-policy-binding kairo-worker --region asia-south1 --project kairo-test --member serviceAccount:kairo-scheduler@kairo-test.iam.gserviceaccount.com --role roles/run.invoker'
      }
  ```

- [ ] **Step 2: Run the deployment tests to see them fail**

```bash
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
```

Expected: 9 tests fail. In Setup: the APIs, the service accounts, the secrets and the BigQuery roles. In Release: the worker settings, the dataset settings, the two Scheduler tests and the invoker grants. `leaves Google sign-in off until its secrets have a value` already passes, because today's worker gets only the database secret. If the count differs, check that each failure is one of these before going on.

- [ ] **Step 3: Set up the project for the feeds**

In `deploy/Kairo.Deploy/Common.ps1`, below `$script:TokenKey = 'connection-tokens'`, add:

```powershell
$script:AnalyticsJob = 'kairo-analytics-daily'
```

In `deploy/Kairo.Deploy/Setup.ps1`:
- Replace the `$script:Apis` and `$script:ServiceAccounts` lines at the top with:

  ```powershell
  $script:Apis = @(
      'run.googleapis.com', 'sqladmin.googleapis.com', 'compute.googleapis.com', 'servicenetworking.googleapis.com',
      'artifactregistry.googleapis.com', 'cloudbuild.googleapis.com', 'secretmanager.googleapis.com',
      'iam.googleapis.com', 'storage.googleapis.com', 'cloudkms.googleapis.com',
      'googleads.googleapis.com', 'youtube.googleapis.com', 'merchantapi.googleapis.com', 'analyticsadmin.googleapis.com',
      'bigquery.googleapis.com', 'analyticsdata.googleapis.com', 'cloudscheduler.googleapis.com'
  )
  $script:ServiceAccounts = @('kairo-web', 'kairo-api', 'kairo-worker', 'kairo-agents', 'kairo-migrator', 'kairo-build', 'kairo-scheduler')
  ```

- In `Initialize-KairoServiceAccounts`, replace:

  ```powershell
      foreach ($secret in $script:GoogleSecrets.Values) {
          Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $secret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name 'kairo-api')") + $accessor + $scope)
      }
  ```

  with:

  ```powershell
      foreach ($secret in $script:GoogleSecrets.Values) {
          foreach ($name in 'kairo-api', 'kairo-worker') {
              Add-KairoBinding -ArgumentList (@('secrets', 'add-iam-policy-binding', $secret, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)") + $accessor + $scope)
          }
      }

      foreach ($name in 'kairo-api', 'kairo-worker') {
          foreach ($role in 'roles/bigquery.dataOwner', 'roles/bigquery.jobUser') {
              Add-KairoBinding -ArgumentList @('projects', 'add-iam-policy-binding', $Environment.Project, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)", '--role', $role, '--condition', 'None')
          }
      }
  ```

- [ ] **Step 4: Deploy the worker for the feeds and schedule the nightly run**

In `deploy/Kairo.Deploy/Release.ps1`, in `Invoke-KairoDeploy`:
- Replace:

  ```powershell
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
  ```

  with:

  ```powershell
      $analytics = "Kairo__Analytics__Project=$($Environment.Project),Kairo__Analytics__Location=$($Environment.Region)"
      $serviceSecrets = @($sqlSecret)
      $googleConfigured = $true
      foreach ($secret in $script:GoogleSecrets.Values) {
          if (-not (Test-KairoSecretHasValue -Environment $Environment -Name $secret)) {
              $googleConfigured = $false
          }
      }
      if ($googleConfigured) {
          foreach ($entry in $script:GoogleSecrets.GetEnumerator()) {
              $serviceSecrets += "$($entry.Key)=$($entry.Value):latest"
          }
      }
  ```

- In the `kairo-api` settings, replace:

  ```powershell
              "--set-env-vars=Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName",
              "--set-secrets=$($apiSecrets -join ',')"
  ```

  with:

  ```powershell
              "--set-env-vars=Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName,$analytics",
              "--set-secrets=$($serviceSecrets -join ',')"
  ```

- Replace the `kairo-worker` deploy:

  ```powershell
      Publish-KairoService -Environment $Environment -Service 'kairo-worker' -Image (Get-KairoImage -Environment $Environment -Name 'worker' -Tag $tag) `
          -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @("--set-env-vars=Kairo__Kms__KeyName=$keyName", "--set-secrets=$sqlSecret"))
  ```

  with:

  ```powershell
      Publish-KairoService -Environment $Environment -Service 'kairo-worker' -Image (Get-KairoImage -Environment $Environment -Name 'worker' -Tag $tag) `
          -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @(
              '--timeout', '3600',
              "--set-env-vars=Kairo__Kms__KeyName=$keyName,$analytics",
              "--set-secrets=$($serviceSecrets -join ',')"
          ))
  ```

- Replace:

  ```powershell
      Grant-KairoInvoker -Environment $Environment -Service 'kairo-agents' -Caller 'kairo-worker'
  ```

  with:

  ```powershell
      Grant-KairoInvoker -Environment $Environment -Service 'kairo-agents' -Caller 'kairo-worker'
      Grant-KairoInvoker -Environment $Environment -Service 'kairo-worker' -Caller 'kairo-scheduler'
      Publish-KairoSchedule -Environment $Environment -WorkerUrl (Get-KairoServiceAudience -Environment $Environment -Service 'kairo-worker')
  ```

- At the end of the file, add:

  ```powershell

  function Publish-KairoSchedule {
      param(
          [Parameter(Mandatory)][hashtable]$Environment,
          [Parameter(Mandatory)][string]$WorkerUrl
      )

      $scope = @('--location', $Environment.Region, '--project', $Environment.Project)
      $verb = 'create'
      if (Test-Gcloud -ArgumentList (@('scheduler', 'jobs', 'describe', $script:AnalyticsJob) + $scope)) {
          $verb = 'update'
      }

      Write-Host "Scheduling $script:AnalyticsJob"
      Invoke-Gcloud -ArgumentList (@(
              'scheduler', 'jobs', $verb, 'http', $script:AnalyticsJob,
              '--schedule', '0 5 * * *',
              '--time-zone', 'Asia/Kolkata',
              '--uri', "$WorkerUrl/jobs/analytics/daily",
              '--http-method', 'POST',
              '--oidc-service-account-email', (Get-KairoServiceAccount -Environment $Environment -Name 'kairo-scheduler'),
              '--oidc-token-audience', $WorkerUrl,
              '--attempt-deadline', '1800s',
              '--max-retry-attempts', '0'
          ) + $scope)
  }
  ```

The worker is reached at the address Cloud Run reports for it (`status.url`), which is also the OIDC audience it accepts, the same way the web app signs its calls to the API.

- [ ] **Step 5: Give the local worker the Google client and the analytics project**

In `core/src/Kairo.AppHost/AppHost.cs`, replace:

```csharp
builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health");
```

with:

```csharp
var worker = builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WaitForCompletion(migrator)
    .WithEnvironment("Authentication__Google__ClientId", googleClientId)
    .WithEnvironment("Authentication__Google__ClientSecret", googleClientSecret)
    .WithHttpHealthCheck("/health");

if (builder.Configuration["Parameters:analytics-project"] is { Length: > 0 } analyticsProject)
{
    api.WithEnvironment("Kairo__Analytics__Project", analyticsProject);
    worker.WithEnvironment("Kairo__Analytics__Project", analyticsProject);
}
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: every Pester test passes, and the core builds with 0 warnings and passes.

- [ ] **Step 7: Commit**

```bash
git add deploy/Kairo.Deploy/Common.ps1 deploy/Kairo.Deploy/Setup.ps1 deploy/Kairo.Deploy/Release.ps1 \
  deploy/tests/Setup.Tests.ps1 deploy/tests/Release.Tests.ps1 core/src/Kairo.AppHost/AppHost.cs
git commit -m "Schedule the nightly analytics run and give the worker what it needs to load brand data"
```

---

### Task 11: Documentation

**Files:**
- Modify: `docs/Kairo-SAD.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the names, routes and settings from Tasks 1 to 10.
- Produces: the SAD and README describing what was built.

- [ ] **Step 1: Update the SAD**

In `docs/Kairo-SAD.md`:
- In the diagram, below `        A --> B[("BigQuery")]` add:

  ```
          C -->|nightly feeds| B
  ```

- Replace the paragraph `The web app talks only to the core. The core calls the agent service for each stage and is the only path to Google Ads, YouTube and Merchant Center, while the agents call Google's models and read analytics from BigQuery.` with:

  ```
  The web app talks only to the core. The core calls the agent service for each stage and is the only path to Google Ads, YouTube and Merchant Center. Every night it also loads each brand's Google Ads, GA4 and sales data into BigQuery, where the agents read analytics. The agents call Google's models.
  ```

- In §2, replace the Analytics row with:

  ```
  | Analytics | BigQuery, loaded nightly by the core worker from the Google Ads API, the GA4 Data API, sales uploads and the brand's own tables | Performance, sales and stock history, and measurement results |
  ```

- In §2, replace the Jobs and events row with:

  ```
  | Jobs and events | Cloud Scheduler (including the nightly `kairo-analytics-daily` run), Cloud Tasks, Pub/Sub | Scheduled refreshes and checks, rate-limited and retried jobs, and events between modules |
  ```

- In §3, replace the Measurement row with:

  ```
  | Measurement | Test windows, impact reports, Impact Lab analyses, data feed runs, sales uploads and sales sources | Load each brand's Google Ads, GA4 and sales data into BigQuery every night; start Prove runs when a test ends, and store results |
  ```

- In §6 BigQuery, replace the Google Ads, GA4 and Brand sales rows with:

  ```
  | Google Ads | Campaign, ad group, ad and region performance by day | Core worker, nightly, through the Google Ads API |
  | GA4 | Sessions, key events, transactions and revenue by region and day | Core worker, nightly, through the GA4 Data API |
  | Brand sales | Orders and revenue by region and day | CSV uploads, or a nightly copy of a table in the brand's own BigQuery |
  ```

- Replace `Each brand gets its own datasets, so access can be granted and removed per brand.` with:

  ```
  Each brand gets its own datasets, starting with `brand_<brand ID>` for these feeds in Kairo's project, so access can be granted and removed per brand. Every load replaces whole days, so a rerun never duplicates rows, and the later of an upload or a table copy wins each day.
  ```

- In §7, delete the `BigQuery Data Transfer Service` row.
- In §7, replace the Google Ads API row's first cell after the name, `Keyword metrics, performance and recommendations; creating campaigns, pausing and resuming ads, budgets and bid targets`, with `Keyword metrics, performance and recommendations, nightly performance reports; creating campaigns, pausing and resuming ads, budgets and bid targets`.
- In §7, replace the Google Analytics 4 row with:

  ```
  | Google Analytics 4 | Outcome data for measurement and Impact Lab | The GA4 Data API from the core worker, with the brand's consent, nightly by region and day | Totals only; raw events need the brand's own BigQuery export, which Kairo doesn't read |
  ```

- In §9, replace the Core worker row with:

  ```
  | Core worker | Cloud Run service | Scales to zero; 60-minute request timeout; triggered by Cloud Scheduler, Cloud Tasks and Pub/Sub |
  ```

- In the decisions table, replace the AD-6 row's last cell, `Built-in Google Ads and GA4 transfers, and time-series analysis at scale`, with `Per-brand datasets, whole-day loads and time-series analysis at scale`.

- [ ] **Step 2: Update the README**

In `README.md`, in "Set up account connections", replace step 1:

```
1. **APIs.** `deploy/setup.ps1` enables the Google Ads, YouTube Data, Merchant, Analytics Admin, BigQuery and Cloud KMS APIs. Run it once before connecting accounts locally, too.
```

with:

```
1. **APIs.** `deploy/setup.ps1` enables the Google Ads, YouTube Data, Merchant, Analytics Admin, Analytics Data, BigQuery, Cloud Scheduler and Cloud KMS APIs. Run it once before connecting accounts locally, too.
```

Below the "Set up account connections" section, before `## Run everything locally`, add:

````markdown
## Run the analytics feeds locally

Every night at 05:00 India time, Cloud Scheduler calls the worker. It loads each brand's Google Ads reports, GA4 totals and sales table into a BigQuery dataset of its own, `brand_<brand ID>`. Admins and marketers can also upload sales as a CSV file with the columns `date`, `region`, `orders` and `revenue`.

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
````

- [ ] **Step 3: Check the docs**

```bash
grep -n "Data Transfer Service" docs/Kairo-SAD.md README.md
grep -n "kairo-analytics-daily" docs/Kairo-SAD.md
```

Expected: the first prints nothing; the second prints the Jobs and events row.

- [ ] **Step 4: Commit**

```bash
git add docs/Kairo-SAD.md README.md
git commit -m "Describe the nightly analytics feeds in the SAD and the README"
```

---

### Task 12: Final verification

**Files:** none, unless a check fails.

- [ ] **Step 1: Run every suite**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
git status --short
```

Expected: 0 warnings, every test passes, and `git status` shows nothing uncommitted.

- [ ] **Step 2: Check the commits carry no attribution**

```bash
git log origin/develop..HEAD --format=%B | grep -i "co-authored\|claude\|generated with" || echo "clean"
```

Expected: `clean`.

- [ ] **Step 3: Run the feeds against `kairo2` with the demo account**

This needs the user's Google sign-in, the demo Google Ads test account and Docker. Ask the user before starting it, and report what happened either way.

1. `gcloud auth application-default login`, then `dotnet user-secrets set "Parameters:analytics-project" "kairo2" --project core/src/Kairo.AppHost`.
2. `dotnet run --project core/src/Kairo.AppHost`, sign in at http://localhost:8000, and connect the demo Google Ads test account in Settings → Connections.
3. `curl -X POST http://localhost:5200/jobs/analytics/daily`. Expect `200`, with `google-ads` `succeeded`.
4. Check the dataset:

   ```bash
   bq ls --project_id kairo2 | grep brand_
   bq query --project_id kairo2 --use_legacy_sql=false 'SELECT COUNT(*) AS days, COUNTIF(region_name IS NOT NULL) AS named FROM `kairo2.brand_<id>.ads_region_daily`'
   ```

   - **Region segment:** if the run failed with a Google error naming `segments.geo_target_state`, switch to `segments.geo_target_region` as in Task 6 Step 1, rerun the Task 6 tests, commit, and run this step again. Test accounts usually serve no impressions, so an empty table with a successful run is expected; the check is that Google accepted the query.
5. Upload a CSV through the gateway, with the session cookie copied from the browser:

   ```bash
   printf 'date,region,orders,revenue\n2026-10-01,Goa,3,450.50\n' > /tmp/sales.csv
   curl -X POST http://localhost:8000/api/brands/<id>/analytics/sales/uploads -H "X-Kairo-Request: 1" -H "Cookie: <cookie>" -F file=@/tmp/sales.csv
   ```

   Expect `201`, and `sales_daily` gains the row.
6. Open http://localhost:8000/api/brands/<id>/analytics/feeds in the browser. Expect `google-ads` to be `ok`.
7. Delete the test brand's dataset afterwards: `bq rm -r -f -d kairo2:brand_<id>`.

- [ ] **Step 4: Hand over**

Report to the user: the suites' results, the manual run's results (or that it wasn't run), which region segment is in use, and that `deploy/setup.ps1 development` must be re-run before the next deploy (new APIs, the `kairo-scheduler` account and the BigQuery roles). Then ask whether to merge `feature/analytics-data-feeds` into `develop`. Don't mark the task complete in `docs/task.md` until it is merged.

---

## Self-review notes

- **Spec coverage.**
  - Datasets and tables, and replacing days: Task 3.
  - Nightly run (trigger, each brand, windows, skips, response, `503`): Task 6.
  - Google Ads: Task 6. GA4: Task 7. Sales from the brand's table: Task 8.
  - Sales uploads (format, checking, loading, response, list, `503`): Tasks 2 and 4.
  - The brand's sales table endpoints and checks: Task 5.
  - Run records, `90`-day purge and status states: Tasks 1, 6 and 9.
  - Audit actions: Tasks 4 and 5.
  - Code layout, configuration and the named client: Tasks 1, 3 and 10.
  - Deployment: Task 10. Done criteria 4 and 5: Tasks 11 and 12.
  - Unit tests for `SalesCsv`, row mapping and the generated GAQL and SQL: Tasks 2, 3, 6 and 7.
- **Out of scope, as in the spec:** web screens, alerting admins about failing feeds, the Kairo history and Measurement datasets, the raw GA4 export, same-day pacing, and direct dataset access for brands.
- **Names checked across tasks:** `ReplaceDaysAsync`, `TableSchemas.*`, `FeedNames.*`, `RunStatuses.*`, `SkipReasons.*`, `FeedStates.*`, `BrandFeeds.RunningFor`, `FeedClock.Iso`, `FeedWindow.For`, `SalesRows.Upload`/`Table`, `GoogleAdsReports.RegionField`/`RegionJson`, `FakeBigQuery.KairoProject`/`BrandProject`, `FakeGoogleApis.ServiceToken`, and `KairoWorkerFactory.RunDailyAsync`.
- **Tried while writing the plan:** referencing the worker with the extern alias `worker` next to the API (0 warnings; both hosts answered `/alive` in one test run), and the `Google.Apis.Auth` 1.73.0 calls in `ApplicationDefaultAccessToken` (0 warnings with warnings as errors). Both probes were reverted.
- **Not verifiable before running:** whether Google accepts `segments.geo_target_state` on `geographic_view` (Task 6 Step 1 and Task 12 Step 3 cover it).
