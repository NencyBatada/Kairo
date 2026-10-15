# Kairo Catalogue Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An admin imports the brand's products from a file or a feed link in Google's Merchant Center format. The feed refreshes every hour. Every stock change is recorded, and every product missing a title, price, link, image or stock status is listed and can be fixed.

**Architecture:**
- **Module.** `Kairo.Modules.Catalogue` gets its own `catalogue` schema with four tables: `Products`, `Sources`, `Imports` and `StockEvents`. All four sit under row-level brand separation.
- **Reading files.** A pure parser reads CSV, TSV, RSS and Atom (gzip unpacked first) into checked product offers, keeping up to 50 problems.
- **Importing.**
  - Each import runs inside the caller's transaction, under a non-blocking application lock per brand (`catalogue:{brandId:N}`).
  - It stores the raw body in the `catalogue-imports` bucket and compares the content hash with the last successful import. It then applies the full snapshot in batches of 1,000: add, update, remove, return, stock events, dropped fixes and missing flags.
- **Feeds.**
  - Feeds are fetched through the existing `SafeWebClient` (50 MB, 120 seconds).
  - A worker job (`POST /jobs/catalogue/refresh`) refreshes every brand with a feed, 4 at a time. Cloud Scheduler calls it hourly.
  - Three failed refreshes in a row send one `connection.problem` notification to admins.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 on SQL Server 2022 (owned JSON column for fixes, primitive collection for extra images), `System.Xml` with DTDs prohibited, `System.IO.Compression`, xUnit v3 with Testcontainers, Pester 5.9.1.

**Spec:** `docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md`, section **Catalogue** (Format, Tables, Importing, API, Hourly refresh, Side effects), plus **Shared building blocks → File store** for the new bucket, and the catalogue lines of **Testing** and **Done when**.

## Global Constraints

- **Worktree and branch.**
  - Work only in `D:\VSTS\Kairo-worktrees\catalogue-import`, on branch `feature/catalogue-import`.
  - Run every command in Git Bash from `/d/VSTS/Kairo-worktrees/catalogue-import`, unless a step says otherwise.
  - Before any `dotnet` command, run `export DOTNET_ROOT=/c/Users/talha.khan/.dotnet PATH="/c/Users/talha.khan/.dotnet:$PATH"`. SDK 10.0.400 lives there, not in Program Files.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user (`AGENTS.md` §1).
  - Stage files by explicit path, never with `git add -A` or `git add .`. Never stage `CLAUDE.md` or `.claude/`.
  - Don't push, and never touch `docs/task.md` on this branch.
- **No comments in code** (`AGENTS.md` §3): no `//`, `#`, `/* */` or `<!-- -->` comments and no XML doc comments. Generated EF Core migrations go through `core/scripts/tidy-migrations.sh`.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build ends with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container. The run needs about 2 GB of free memory.
- **Module rules stay true.**
  - `Kairo.Modules.Catalogue` exposes exactly one public type, `CatalogueModule`, and adds no `ProjectReference`. Everything else in it is `internal`, and its tables live only in the `catalogue` schema.
  - There are no contract changes. The module reads brands through the existing `IBrandDirectory` (`BrandSummary.Currency`, `BrandSummary.Handle`).
- **Names, exactly.**
  - Source kinds: `file`, `feed`.
  - Import statuses: `succeeded`, `unchanged`, `failed`, `suspicious`.
  - Product statuses: `active`, `removed`.
  - Availability values: `in_stock`, `out_of_stock`, `preorder`, `backorder`.
  - Error codes:
    - File: `too_large`, `unreadable`, `no_id_column`, `too_many_products`.
    - Safeguard: `no_valid_rows`, `large_removal`.
    - Feed fetch: the safe fetch codes as they come (`too_large`, `timeout`, `blocked_address`, `bad_status`, `unreachable`).
  - Missing detail names (filter values and view lists): `title`, `price`, `link`, `image`, `stock`; the filter also takes `any`.
  - Fixable fields (JSON names): `title`, `price` (with `currency`), `link`, `imageLink`, `category`.
  - Audit actions:
    - `catalogue.file_imported` and `catalogue.feed_refreshed`, target type `catalogue_import`.
    - `catalogue.feed_set` and `catalogue.feed_removed`, target type `catalogue_source`, target id = brand id.
    - `catalogue.product_fixed`, target type `catalogue_product`.
  - Live events: `catalogue.imported` with `{ importId, status }`, and `catalogue.product_changed` with `{ productId }`, both to everyone.
  - Notification: kind `connection.problem` to admins. Title `Your product feed isn't refreshing`; body `Kairo couldn't refresh your product feed 3 times in a row.`; link `/{handle}/brand-kit/catalogue`.
  - Application lock: `catalogue:{brandId:N}`.
  - Bucket: logical `catalogue-imports`; key `{brandId:N}/imports/{importId:N}`.
  - Worker job: `POST /jobs/catalogue/refresh` with optional `?brandId={guid}`.
  - Cloud Scheduler job: `kairo-catalogue-hourly`, `0 * * * *`, `Asia/Kolkata`.
- **Limits.**
  - Files: 50 MB unpacked and 50,000 products. The upload request limit is 50 MB + 64 KB.
  - Feed fetch: 50 MB and 120 seconds.
  - Problems kept: 50.
  - Field lengths:
    - `id` at most 50; `title` 150; `description` 5,000; `brand` 70; `item_group_id` 50.
    - Categories 750; web addresses 2,000.
    - GTIN: 8 to 14 digits. Up to 10 additional images.
  - Amounts: above 0 and at most 999,999,999,999,999, stored as `decimal(19,4)`.
  - Safeguard: more than half of the active products, once there are at least 20.
  - Save batch: 1,000 products. Hourly run: 4 brands at once. Alert after 3 failures in a row.
  - Product list: `pageSize` 1 to 200, default 50. Stock events shown: 20. Summary categories: 100.
- **Messages, exactly** (each is a constant in the code and a literal in the tests):
  - Import: `The file is larger than 50 MB.`, `Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format.`, `The file has no id column. Every product needs an id.`, `The file has more than 50,000 products.`, `No product in this import could be read, so Kairo changed nothing.`, `This import would remove {n} of your {m} products, so Kairo changed nothing. ` + (`If that's right, import the file again and allow large removals.` for files, `Check the feed, then refresh it.` for feeds).
  - Upload: `Choose a product file to upload.` (400 on `file`), `The file is larger than 50 MB.` (400 on `file`), `Kairo is already importing this catalogue. Try again in a minute.` (409).
  - Feed: `Use a full web address starting with http:// or https://.` (400 on `url`), `There's no feed link to refresh.` (409), and fetch failures (400 on `url` when setting the feed): `The feed is larger than 50 MB.`, `The feed didn't finish downloading within 120 seconds.`, `Kairo can't read this address. Use a public http or https link.`, `The feed address answered with an error.`, `Kairo couldn't reach this address.`
  - Fixes: `Stock can't be fixed here. Change it in the file or feed.`, `Kairo can't fix this field.`, `Use a title of up to 150 characters.`, `Use a price above 0.`, `Use a three-letter currency code, such as INR.`, `Send the currency with a price.`, `Use a category of up to 750 characters.`, `The import already supplies the {title|price|link|image link|category}, so it can't be fixed here.`
  - Product list: `Use a page number from 1.`, `Use a page size from 1 to 200.`, `Use active or removed.`, `Use in_stock, out_of_stock, preorder or backorder.`, `Use any, title, price, link, image or stock.`
- **Running tests.**
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`
  - The catalogue tests: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"`
  - Architecture: `dotnet test --project core/tests/Kairo.ArchitectureTests`
  - Whole core: `dotnet test --solution core/Kairo.slnx`
  - Deployment scripts: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
  - Tasks 3, 4 and 5 add API endpoints, so each regenerates the contract and stages `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts`. The first time in this worktree, run `npm --prefix web ci`.
    ```bash
    KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
    npm --prefix web run generate:api
    ```

## Review Focus

1. **A CSV exported by a spreadsheet**: a UTF-8 byte-order mark before `id`, and descriptions with quoted commas, quotes and line breaks. The header must still be found and the multi-line row read as one product. Pinned by `CatalogueFileTests.Reads_a_spreadsheet_csv_with_a_byte_order_mark_and_quoted_fields` (Task 2).
2. **Product ids that differ only in case** (`SKU-1` and `sku-1`), in one file or across imports. SQL Server compares them as equal, so a naive import would break the unique index and fail with a 500. The later row must be invalid, and a changed case must update the same product. Pinned by `CatalogueFileTests.A_repeated_id_whatever_its_case_makes_the_later_row_invalid` (Task 2) and `FileImportTests.An_id_whose_case_changes_is_the_same_product` (Task 3).
3. **A feed served as a gzip file** (`products.xml.gz`, `application/gzip`, no `Content-Encoding`). It must be unpacked, and a small gzip that unpacks past 50 MB must be refused rather than fill memory. Pinned by `FeedTests.A_gzipped_xml_feed_is_imported` (Task 4) and `CatalogueFileTests.Refuses_a_file_larger_than_50_MB_once_unpacked` (Task 2).
4. **An XML feed that declares a document type** (an external entity aimed at a local file). It must be refused as unreadable, never resolved. Pinned by `CatalogueFileTests.Refuses_xml_that_declares_a_document_type` (Task 2).
5. **Two admins refreshing the feed at the same moment**, or a refresh during the hourly run. Exactly one import runs; the other gets `409` or is skipped, and never two imports or a deadlock. Pinned by `FeedTests.Two_refreshes_at_once_import_once` (Task 4) and `HourlyRefreshTests.A_brand_already_importing_is_skipped` (Task 6).

## Rulings already made while planning

These go into the ledger as Task 0 rulings, so the final review can weigh them.

- **Holding the lock.** Imports wait on nothing. Every writer (file import, set, refresh or remove the feed, fix a product, the hourly job) takes `catalogue:{brandId:N}` with `sp_getapplock @LockTimeout = 0`, through a new `KairoDatabase.TryLockAsync`. In the API a busy lock is a `409`; in the job, the brand is skipped. The spec names the `409` only for refresh. The other writers get the same answer, because waiting 15 seconds and then failing with a 500 would be worse.
- **Comparing with the last import.** `unchanged` compares with the latest `succeeded` import of the brand, whatever its source, and needs the same kind and feed address. Comparing per source would mark a re-uploaded file `unchanged` even after a feed had changed the products in between.
- **Unchanged bodies.** An `unchanged` import doesn't store its body again (its `StorageKey` is null), since the identical body is already stored. Hourly unchanged 50 MB feeds would otherwise fill the bucket with copies.
- **Setting a feed that can't be read.** `PUT /feed` answers `400` on `url` with the parse message when the feed downloads but isn't a product file (`failed`), as well as when the fetch fails. A `suspicious` result still saves the source and returns `200`.
- **`LastImportId`** is the import that last changed the product. Unchanged products keep theirs, so an hourly unchanged import writes nothing.
- **Ordering imports.** `Imports` gains an identity column `Number`, used after `StartedAt` to order imports. Tests run on a frozen clock, so `StartedAt` ties.
- **New products** write no stock event; changes to and from `removed` do. An empty availability is stored as null, and a change to or from it is recorded with a null `From` or `To`.
- **Duplicate ids.** The first row with an id is kept; later rows with the same id (ignoring case) are invalid.
- **Sale price.** It is ignored, with a warning, when the product has no valid price, as well as when its currency differs.
- **Notification link.** `/{handle}/brand-kit/catalogue`. The web app has no catalogue route yet; Nency's **Catalogue screens** should use this one, and the final message to the user says so.
- **Fixes column.** `Fixes` is an EF Core owned type stored as JSON (`ToJson`), so filters and the summary can use `Title ?? Fixes.Title` in SQL.

---

### Task 1: Catalogue tables and a lock that doesn't wait

**Files:**
- Modify: `core/src/Kairo.SharedKernel/Persistence/KairoDatabase.cs` (the `LockAsync` method)
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/Kairo.Modules.Catalogue.csproj`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/Product.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFixes.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/MissingDetails.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/StockEvent.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/CatalogueImport.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/CatalogueSource.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportNames.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Data/CatalogueDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Data/CatalogueDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Data/Migrations/*_Catalogue.cs` (generated)
- Test: `core/tests/Kairo.IntegrationTests/Persistence/AppLockTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/CatalogueTableTests.cs`

**Interfaces:**
- Consumes: `KairoDatabase`, `ModuleDbContext`, `IBrandOwned`, `BrandSeparation` migration helpers, `KairoDbContextOptions.CreateForDesignTime`.
- Produces:
  - `KairoDatabase.TryLockAsync(string resource, CancellationToken) → Task<bool>`. It needs a transaction, like `LockAsync`.
  - `CatalogueDbContext` (schema `catalogue`) with `Products`, `Sources`, `Imports` and `StockEvents`.
  - `Product`, with every attribute plus `Fixes` (`ProductFixes`), `Missing` (`MissingDetails`), `Status`, `FirstSeenAt`, `UpdatedAt`, `RemovedAt` and `LastImportId`.
  - `ProductFixes { Title, Price, Currency, Link, ImageLink, Category }`.
  - `MissingDetails` flags: `None`, `Title`, `Price`, `Link`, `Image`, `Stock`, `All`.
  - `ProductStatuses.Active` and `ProductStatuses.Removed`; `Availabilities.InStock`, `OutOfStock`, `Preorder`, `Backorder` and `All`.
  - `StockEvent { Id, BrandId, ProductId, From, To, ImportId, At }`.
  - `CatalogueImport`, with every spec column plus `Number` (identity); `Problems` is a JSON string, default `"[]"`.
  - `CatalogueSource`, keyed by `BrandId`.
  - `SourceKinds.File` and `SourceKinds.Feed`; `ImportStatuses.Succeeded`, `Unchanged`, `Failed` and `Suspicious`.

- [ ] **Step 1: Write the failing lock tests**

Create `core/tests/Kairo.IntegrationTests/Persistence/AppLockTests.cs`:

```csharp
using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Persistence;

public sealed class AppLockTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_free_lock_is_taken_at_once()
    {
        await using var database = new KairoDatabase(sqlServer.KairoConnectionString);
        await database.BeginTransactionAsync(Token);

        Assert.True(await database.TryLockAsync(NewResource(), Token));
    }

    [Fact]
    public async Task A_lock_another_transaction_holds_is_not_taken()
    {
        var resource = NewResource();
        await using var holder = new KairoDatabase(sqlServer.KairoConnectionString);
        await holder.BeginTransactionAsync(Token);
        await holder.LockAsync(resource, Token);
        await using var other = new KairoDatabase(sqlServer.KairoConnectionString);
        await other.BeginTransactionAsync(Token);

        Assert.False(await other.TryLockAsync(resource, Token));
    }

    [Fact]
    public async Task The_lock_is_free_again_once_the_holder_commits()
    {
        var resource = NewResource();
        await using var holder = new KairoDatabase(sqlServer.KairoConnectionString);
        await holder.BeginTransactionAsync(Token);
        Assert.True(await holder.TryLockAsync(resource, Token));
        await holder.CommitAsync(Token);
        await using var other = new KairoDatabase(sqlServer.KairoConnectionString);
        await other.BeginTransactionAsync(Token);

        Assert.True(await other.TryLockAsync(resource, Token));
    }

    [Fact]
    public async Task Trying_a_lock_needs_a_transaction()
    {
        await using var database = new KairoDatabase(sqlServer.KairoConnectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.TryLockAsync(NewResource(), Token));
    }

    private static string NewResource() => $"test:{Guid.NewGuid():N}";
}
```

- [ ] **Step 2: Run the lock tests to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.AppLockTests"`
Expected: the build fails with `CS1061: 'KairoDatabase' does not contain a definition for 'TryLockAsync'`.

- [ ] **Step 3: Add `TryLockAsync`**

In `core/src/Kairo.SharedKernel/Persistence/KairoDatabase.cs`, replace the whole `LockAsync` method with:

```csharp
    public async Task LockAsync(string resource, CancellationToken cancellationToken)
    {
        if (!await GetAppLockAsync(resource, 15000, cancellationToken))
        {
            throw new TimeoutException($"Could not lock {resource}.");
        }
    }

    public Task<bool> TryLockAsync(string resource, CancellationToken cancellationToken) =>
        GetAppLockAsync(resource, 0, cancellationToken);
```

and add this private method just above `EndTransactionAsync`:

```csharp
    private async Task<bool> GetAppLockAsync(string resource, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("A lock needs a transaction.");
        await using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @timeout;
            SELECT @result;
            """;
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = timeoutMilliseconds });
        return (int)(await command.ExecuteScalarAsync(cancellationToken))! >= 0;
    }
```

- [ ] **Step 4: Run the lock tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.AppLockTests"`
Expected: 4 passed.

- [ ] **Step 5: Write the failing table tests**

Create `core/tests/Kairo.IntegrationTests/Persistence/CatalogueTableTests.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class CatalogueTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Products")]
    [InlineData("Sources")]
    [InlineData("Imports")]
    [InlineData("StockEvents")]
    public async Task The_table_is_covered_by_the_catalogue_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'catalogue.{table}')",
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Fact]
    public async Task Offer_ids_are_unique_in_a_workspace_whatever_their_case()
    {
        var brandId = Guid.NewGuid();
        await InsertProductAsync(brandId, "SKU-1");

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertProductAsync(brandId, "sku-1"));

        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task Two_workspaces_can_use_the_same_offer_id()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await InsertProductAsync(first, "SKU-1");
        await InsertProductAsync(second, "SKU-1");

        var count = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM catalogue.Products WHERE OfferId = N'SKU-1' AND BrandId IN (@brand, @other);
            """,
            first,
            second);

        Assert.Equal(2, count);
    }

    private Task InsertProductAsync(Guid brandId, string offerId) => ScalarAsync(
        """
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO catalogue.Products (Id, BrandId, OfferId, AdditionalImageLinks, Fixes, Missing, Status, FirstSeenAt, UpdatedAt, LastImportId)
        VALUES (NEWID(), @brand, @offer, N'[]', N'{}', 0, N'active', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NEWID());
        SELECT 0;
        """,
        brandId,
        offer: offerId);

    private async Task<int> ScalarAsync(string sql, Guid brandId, Guid? other = null, string? offer = null)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@other", other ?? Guid.Empty);
        command.Parameters.AddWithValue("@offer", offer ?? "");
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
```

- [ ] **Step 6: Run the table tests to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.CatalogueTableTests"`
Expected: 6 failed. The policy tests find 0 predicates, and the inserts fail with `Invalid object name 'catalogue.Products'`.

- [ ] **Step 7: Let the tests see the module's internals**

Replace `core/src/Modules/Kairo.Modules.Catalogue/Kairo.Modules.Catalogue.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 8: Write the entities**

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/MissingDetails.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Products;

[Flags]
internal enum MissingDetails
{
    None = 0,
    Title = 1,
    Price = 2,
    Link = 4,
    Image = 8,
    Stock = 16,
    All = Title | Price | Link | Image | Stock,
}

internal static class ProductStatuses
{
    public const string Active = "active";
    public const string Removed = "removed";
}

internal static class Availabilities
{
    public const string InStock = "in_stock";
    public const string OutOfStock = "out_of_stock";
    public const string Preorder = "preorder";
    public const string Backorder = "backorder";

    public static readonly string[] All = [InStock, OutOfStock, Preorder, Backorder];
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFixes.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Products;

internal sealed class ProductFixes
{
    public string? Title { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string? Link { get; set; }

    public string? ImageLink { get; set; }

    public string? Category { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/Product.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Products;

internal sealed class Product : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string OfferId { get; set; } = "";

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Link { get; set; }

    public string? ImageLink { get; set; }

    public List<string> AdditionalImageLinks { get; set; } = [];

    public decimal? Price { get; set; }

    public decimal? SalePrice { get; set; }

    public string? Currency { get; set; }

    public string? Availability { get; set; }

    public string? Category { get; set; }

    public string? GoogleCategory { get; set; }

    public string? Brand { get; set; }

    public string? Gtin { get; set; }

    public string? ItemGroupId { get; set; }

    public ProductFixes Fixes { get; set; } = new();

    public MissingDetails Missing { get; set; }

    public string Status { get; set; } = ProductStatuses.Active;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid LastImportId { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/StockEvent.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Products;

internal sealed class StockEvent : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid ProductId { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public Guid ImportId { get; set; }

    public DateTimeOffset At { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportNames.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Imports;

internal static class SourceKinds
{
    public const string File = "file";
    public const string Feed = "feed";
}

internal static class ImportStatuses
{
    public const string Succeeded = "succeeded";
    public const string Unchanged = "unchanged";
    public const string Failed = "failed";
    public const string Suspicious = "suspicious";
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/CatalogueImport.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Imports;

internal sealed class CatalogueImport : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public long Number { get; set; }

    public string Kind { get; set; } = SourceKinds.File;

    public string? FileName { get; set; }

    public string? FeedUrl { get; set; }

    public string Status { get; set; } = ImportStatuses.Failed;

    public int Rows { get; set; }

    public int Valid { get; set; }

    public int Invalid { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    public int Removed { get; set; }

    public int Unchanged { get; set; }

    public string Problems { get; set; } = "[]";

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ContentHash { get; set; }

    public string? StorageKey { get; set; }

    public Guid? StartedBy { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/CatalogueSource.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Imports;

internal sealed class CatalogueSource : IBrandOwned
{
    public Guid BrandId { get; set; }

    public string Kind { get; set; } = SourceKinds.File;

    public string? FeedUrl { get; set; }

    public Guid SetBy { get; set; }

    public DateTimeOffset SetAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? LastSucceededAt { get; set; }

    public int FailuresInRow { get; set; }
}
```

- [ ] **Step 9: Write the database context and its design-time factory**

Create `core/src/Modules/Kairo.Modules.Catalogue/Data/CatalogueDbContext.cs`:

```csharp
using Kairo.Modules.Catalogue.Imports;
using Kairo.Modules.Catalogue.Products;

namespace Kairo.Modules.Catalogue.Data;

internal sealed class CatalogueDbContext(DbContextOptions<CatalogueDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "catalogue";

    public DbSet<Product> Products => Set<Product>();

    public DbSet<CatalogueSource> Sources => Set<CatalogueSource>();

    public DbSet<CatalogueImport> Imports => Set<CatalogueImport>();

    public DbSet<StockEvent> StockEvents => Set<StockEvent>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products");
            product.HasIndex(p => new { p.BrandId, p.OfferId }).IsUnique();
            product.HasIndex(p => new { p.BrandId, p.Status, p.Missing });
            product.Property(p => p.OfferId).HasMaxLength(50);
            product.Property(p => p.Title).HasMaxLength(150);
            product.Property(p => p.Link).HasMaxLength(2000);
            product.Property(p => p.ImageLink).HasMaxLength(2000);
            product.Property(p => p.Price).HasPrecision(19, 4);
            product.Property(p => p.SalePrice).HasPrecision(19, 4);
            product.Property(p => p.Currency).HasMaxLength(3);
            product.Property(p => p.Availability).HasMaxLength(20);
            product.Property(p => p.Category).HasMaxLength(750);
            product.Property(p => p.GoogleCategory).HasMaxLength(750);
            product.Property(p => p.Brand).HasMaxLength(70);
            product.Property(p => p.Gtin).HasMaxLength(14);
            product.Property(p => p.ItemGroupId).HasMaxLength(50);
            product.Property(p => p.Status).HasMaxLength(20);
            product.OwnsOne(p => p.Fixes, fixes =>
            {
                fixes.ToJson();
                fixes.Property(f => f.Title).HasJsonPropertyName("title");
                fixes.Property(f => f.Price).HasJsonPropertyName("price");
                fixes.Property(f => f.Currency).HasJsonPropertyName("currency");
                fixes.Property(f => f.Link).HasJsonPropertyName("link");
                fixes.Property(f => f.ImageLink).HasJsonPropertyName("imageLink");
                fixes.Property(f => f.Category).HasJsonPropertyName("category");
            });
            product.Navigation(p => p.Fixes).IsRequired();
        });

        modelBuilder.Entity<CatalogueSource>(source =>
        {
            source.ToTable("Sources");
            source.HasKey(s => s.BrandId);
            source.Property(s => s.BrandId).ValueGeneratedNever();
            source.Property(s => s.Kind).HasMaxLength(10);
            source.Property(s => s.FeedUrl).HasMaxLength(2000);
        });

        modelBuilder.Entity<CatalogueImport>(import =>
        {
            import.ToTable("Imports");
            import.HasIndex(i => new { i.BrandId, i.StartedAt });
            import.Property(i => i.Number).UseIdentityColumn();
            import.Property(i => i.Kind).HasMaxLength(10);
            import.Property(i => i.FileName).HasMaxLength(200);
            import.Property(i => i.FeedUrl).HasMaxLength(2000);
            import.Property(i => i.Status).HasMaxLength(20);
            import.Property(i => i.ErrorCode).HasMaxLength(50);
            import.Property(i => i.ErrorMessage).HasMaxLength(500);
            import.Property(i => i.ContentHash).HasMaxLength(64);
            import.Property(i => i.StorageKey).HasMaxLength(200);
        });

        modelBuilder.Entity<StockEvent>(stock =>
        {
            stock.ToTable("StockEvents");
            stock.HasIndex(e => new { e.BrandId, e.ProductId, e.At });
            stock.Property(e => e.From).HasMaxLength(20);
            stock.Property(e => e.To).HasMaxLength(20);
        });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Data/CatalogueDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Catalogue.Data;

internal sealed class CatalogueDbContextFactory : IDesignTimeDbContextFactory<CatalogueDbContext>
{
    public CatalogueDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<CatalogueDbContext>(
            CatalogueDbContext.SchemaName,
            (options, database) => new CatalogueDbContext(options, database));
}
```

- [ ] **Step 10: Register the context and migrate it**

Replace `core/src/Modules/Kairo.Modules.Catalogue/CatalogueModule.cs` with:

```csharp
using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue;

public sealed class CatalogueModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogueDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, CatalogueDbContext.SchemaName));
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogueDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 11: Generate the migration and add brand separation**

Run:

```bash
dotnet tool restore
dotnet ef migrations add Catalogue --project core/src/Modules/Kairo.Modules.Catalogue --startup-project core/src/Kairo.Migrator --context CatalogueDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Catalogue/Data/Migrations/*.cs
```

Expected: `Done.` Three files appear in `Data/Migrations`: `*_Catalogue.cs`, `*_Catalogue.Designer.cs` and `CatalogueDbContextModelSnapshot.cs`. In `*_Catalogue.cs`, the `Fixes` column is `nvarchar(max)`, not `json`, and `Number` has `.Annotation("SqlServer:Identity", "1, 1")`. If `Fixes` came out as `json`, stop: SQL Server 2022 has no `json` type. Rule on adding `.HasColumnType("nvarchar(max)")` to the owned JSON configuration.

In `*_Catalogue.cs`, add these as the last lines of `Up`:

```csharp
            migrationBuilder.CreateBrandAccessPredicate("catalogue");
            migrationBuilder.AddBrandSeparation("catalogue", "Products");
            migrationBuilder.AddBrandSeparation("catalogue", "Sources");
            migrationBuilder.AddBrandSeparation("catalogue", "Imports");
            migrationBuilder.AddBrandSeparation("catalogue", "StockEvents");
```

and this as the first line of `Down`:

```csharp
            migrationBuilder.DropBrandSeparation("catalogue");
```

The file needs `using Kairo.SharedKernel.Persistence;` at the top if the build says `AddBrandSeparation` isn't found; the BrandKit migration relies on the module's global usings, so it should not be needed.

- [ ] **Step 12: Run the table tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.CatalogueTableTests"`
Expected: 6 passed.

- [ ] **Step 13: Run the architecture tests**

Run: `dotnet test --project core/tests/Kairo.ArchitectureTests`
Expected: all passed. `CatalogueModule` is still the module's only public type.

- [ ] **Step 14: Commit**

```bash
git add core/src/Kairo.SharedKernel/Persistence/KairoDatabase.cs core/src/Modules/Kairo.Modules.Catalogue core/tests/Kairo.IntegrationTests/Persistence/AppLockTests.cs core/tests/Kairo.IntegrationTests/Persistence/CatalogueTableTests.cs
git commit -m "Add the catalogue tables and a lock that doesn't wait"
```

### Task 2: Reading Merchant Center product files

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/ParsedCatalogue.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/CatalogueFileException.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/Attributes.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/RawRow.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/DelimitedText.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/XmlProducts.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/ProductValues.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/ProductRows.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Format/CatalogueFile.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/ProductValuesTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/CatalogueFileTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1. The parser is pure.
- Produces:
  - `CatalogueFile.Parse(byte[] body, string currency) → ParsedCatalogue`. It throws `CatalogueFileException` (`Code`, `Message`). Also `CatalogueFile.MaxBytes = 52_428_800` and `CatalogueFile.MaxProducts = 50_000`.
  - `ParsedCatalogue(IReadOnlyList<ProductOffer> Offers, int Rows, int Invalid, IReadOnlyList<ImportProblem> Problems)`.
  - `ProductOffer(string OfferId, string? Title, string? Description, string? Link, string? ImageLink, IReadOnlyList<string> AdditionalImageLinks, decimal? Price, decimal? SalePrice, string? Currency, string? Availability, string? Category, string? GoogleCategory, string? Brand, string? Gtin, string? ItemGroupId)`.
  - `ImportProblem(int Row, string Attribute, string Message)`.
  - `ProductValues`:
    - `IsWebAddress(string) → bool`.
    - The limits: `MaxTitleLength`, `MaxCategoryLength` and the rest.
    - `Amount(string? value, string currency, string attribute, Action<string,string> warn) → Money?`, where `Money(decimal Amount, string Currency)`.
    - `MaxAmount = 999_999_999_999_999m`.

- [ ] **Step 1: Write the failing value tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/ProductValuesTests.cs`:

```csharp
using System.Globalization;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class ProductValuesTests
{
    private readonly List<(string Attribute, string Message)> _warnings = [];

    [Theory]
    [InlineData("1499.00 INR", "1499.00", "INR")]
    [InlineData("INR 1499", "1499", "INR")]
    [InlineData("1499", "1499", "INR")]
    [InlineData("20 usd", "20", "USD")]
    [InlineData("1499.00INR", "1499.00", "INR")]
    [InlineData("1,499.50 INR", "1499.50", "INR")]
    [InlineData("1,49,999 INR", "149999", "INR")]
    [InlineData("1,234,567.25 INR", "1234567.25", "INR")]
    [InlineData("12.12345 INR", "12.1235", "INR")]
    public void Reads_an_amount_with_an_optional_currency(string value, string amount, string currency)
    {
        var money = ProductValues.Amount(value, "INR", "price", Warn);

        Assert.NotNull(money);
        Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), money.Amount);
        Assert.Equal(currency, money.Currency);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("cheap")]
    [InlineData("0")]
    [InlineData("-5 INR")]
    [InlineData("12,34 EUR")]
    [InlineData("INR 10 USD")]
    [InlineData("₹1499")]
    [InlineData("1000000000000000 INR")]
    [InlineData("99999999999999999999999999999999999")]
    public void Leaves_a_bad_amount_empty_with_a_warning(string value)
    {
        Assert.Null(ProductValues.Amount(value, "INR", "price", Warn));

        Assert.Equal(
            ("price", "The price isn't an amount above 0 with an optional currency, such as 1499.00 INR, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Theory]
    [InlineData("in_stock", "in_stock")]
    [InlineData("in stock", "in_stock")]
    [InlineData("IN_STOCK", "in_stock")]
    [InlineData("Out Of Stock", "out_of_stock")]
    [InlineData("preorder", "preorder")]
    [InlineData("Backorder", "backorder")]
    public void Reads_availability_accepting_spaces_and_any_case(string value, string expected)
    {
        Assert.Equal(expected, ProductValues.Availability(value, Warn));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Leaves_an_unknown_availability_empty_with_a_warning()
    {
        Assert.Null(ProductValues.Availability("maybe", Warn));

        Assert.Equal(
            ("availability", "The availability isn't in_stock, out_of_stock, preorder or backorder, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("00012345678905", true)]
    [InlineData("1234567", false)]
    [InlineData("123456789012345", false)]
    [InlineData("1234-5678", false)]
    public void Accepts_a_gtin_of_8_to_14_digits(string value, bool accepted)
    {
        Assert.Equal(accepted ? value : null, ProductValues.Gtin(value, Warn));
        Assert.Equal(accepted ? 0 : 1, _warnings.Count);
    }

    [Fact]
    public void Leaves_text_over_its_limit_empty_with_a_warning()
    {
        Assert.Null(ProductValues.Text(new string('d', 5001), ProductValues.MaxDescriptionLength, "description", Warn));
        Assert.Equal("x", ProductValues.Text("x", ProductValues.MaxTitleLength, "title", Warn));

        Assert.Equal(
            ("description", "The description is longer than 5,000 characters, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Theory]
    [InlineData("ftp://acme.example/i/1.jpg")]
    [InlineData("acme.example/i/1.jpg")]
    [InlineData("/i/1.jpg")]
    public void Leaves_an_address_that_isnt_a_web_address_empty_with_a_warning(string value)
    {
        Assert.Null(ProductValues.Url(value, "image_link", Warn));

        Assert.Equal(
            ("image_link", "The image link isn't a full web address starting with http:// or https://, so it was left empty."),
            Assert.Single(_warnings));
    }

    [Fact]
    public void Refuses_a_web_address_over_2000_characters()
    {
        Assert.True(ProductValues.IsWebAddress("https://acme.example/p/1"));
        Assert.False(ProductValues.IsWebAddress("https://acme.example/" + new string('p', 2000)));
    }

    private void Warn(string attribute, string message) => _warnings.Add((attribute, message));
}
```

- [ ] **Step 2: Write the failing file tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/CatalogueFileTests.cs`:

```csharp
using System.IO.Compression;
using System.Text;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class CatalogueFileTests
{
    private const string Unreadable = "Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format.";

    [Fact]
    public void Reads_a_spreadsheet_csv_with_a_byte_order_mark_and_quoted_fields()
    {
        var csv = "\uFEFFid,title,description,price,availability\r\n"
            + "SKU-1,Runner,\"Light, quick and \"\"comfy\"\"\nfor every day\",1499.00 INR,in stock\r\n"
            + "SKU-2,Walker,Plain,999 INR,out of stock\r\n";

        var catalogue = Parse(csv);

        Assert.Equal(2, catalogue.Rows);
        Assert.Equal(0, catalogue.Invalid);
        var first = catalogue.Offers[0];
        Assert.Equal("SKU-1", first.OfferId);
        Assert.Equal("Light, quick and \"comfy\"\nfor every day", first.Description);
        Assert.Equal(1499.00m, first.Price);
        Assert.Equal("INR", first.Currency);
        Assert.Equal("in_stock", first.Availability);
        Assert.Equal("out_of_stock", catalogue.Offers[1].Availability);
    }

    [Fact]
    public void Reads_a_tab_separated_file()
    {
        var offer = Assert.Single(Parse("id\ttitle\tprice\tlink\nSKU-1\tRunner, red\t1499 INR\thttps://acme.example/p/1\n").Offers);

        Assert.Equal("Runner, red", offer.Title);
        Assert.Equal(1499m, offer.Price);
        Assert.Equal("https://acme.example/p/1", offer.Link);
    }

    [Fact]
    public void Matches_attribute_names_ignoring_case_a_g_prefix_and_spaces()
    {
        var offer = Assert.Single(Parse("ID,G:Title,Image Link,g:item group id\nSKU-1,Runner,https://acme.example/i/1.jpg,GROUP-1\n").Offers);

        Assert.Equal("Runner", offer.Title);
        Assert.Equal("https://acme.example/i/1.jpg", offer.ImageLink);
        Assert.Equal("GROUP-1", offer.ItemGroupId);
    }

    [Fact]
    public void Ignores_unknown_columns_and_blank_lines()
    {
        var catalogue = Parse("\nid,title,colour\n\nSKU-1,Runner,red\n\n");

        Assert.Equal(1, catalogue.Rows);
        Assert.Equal("Runner", Assert.Single(catalogue.Offers).Title);
    }

    [Fact]
    public void Reads_an_rss_feed()
    {
        const string rss = """

            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0">
              <channel>
                <title>Acme</title>
                <item>
                  <g:id>SKU-1</g:id>
                  <title>Runner</title>
                  <link>https://acme.example/p/1</link>
                  <description>Light shoes</description>
                  <g:price>1499.00 INR</g:price>
                  <g:availability>in_stock</g:availability>
                  <g:additional_image_link>https://acme.example/i/1a.jpg</g:additional_image_link>
                  <g:additional_image_link>https://acme.example/i/1b.jpg</g:additional_image_link>
                </item>
                <item>
                  <g:id>SKU-2</g:id>
                  <title>Plain title</title>
                  <g:title>Walker</g:title>
                </item>
              </channel>
            </rss>
            """;

        var catalogue = Parse(rss);

        Assert.Equal(2, catalogue.Rows);
        var first = catalogue.Offers[0];
        Assert.Equal("Runner", first.Title);
        Assert.Equal("https://acme.example/p/1", first.Link);
        Assert.Equal("Light shoes", first.Description);
        Assert.Equal(1499.00m, first.Price);
        Assert.Equal(["https://acme.example/i/1a.jpg", "https://acme.example/i/1b.jpg"], first.AdditionalImageLinks);
        Assert.Equal("Walker", catalogue.Offers[1].Title);
    }

    [Fact]
    public void Reads_an_atom_feed()
    {
        const string atom = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:g="http://base.google.com/ns/1.0">
              <title>Acme</title>
              <entry>
                <g:id>SKU-1</g:id>
                <title>Runner</title>
                <link rel="alternate" href="https://acme.example/p/1"/>
                <summary>Light shoes</summary>
                <g:price>INR 1499</g:price>
              </entry>
            </feed>
            """;

        var offer = Assert.Single(Parse(atom).Offers);

        Assert.Equal("SKU-1", offer.OfferId);
        Assert.Equal("Runner", offer.Title);
        Assert.Equal("https://acme.example/p/1", offer.Link);
        Assert.Equal("Light shoes", offer.Description);
        Assert.Equal(1499m, offer.Price);
    }

    [Fact]
    public void Unpacks_a_gzipped_file()
    {
        var offer = Assert.Single(CatalogueFile.Parse(Gzip(Encoding.UTF8.GetBytes("id,title\nSKU-1,Runner\n")), "INR").Offers);

        Assert.Equal("Runner", offer.Title);
    }

    [Fact]
    public void Splits_additional_image_links_in_text_and_keeps_ten()
    {
        var links = string.Join(",", Enumerable.Range(1, 12).Select(i => $"https://acme.example/i/{i}.jpg"));

        var catalogue = Parse($"id,additional_image_link\nSKU-1,\"{links}\"\n");

        var offer = Assert.Single(catalogue.Offers);
        Assert.Equal(10, offer.AdditionalImageLinks.Count);
        Assert.Equal("https://acme.example/i/1.jpg", offer.AdditionalImageLinks[0]);
        Assert.Equal(
            new ImportProblem(2, "additional_image_link", "Only the first 10 additional image links were kept."),
            Assert.Single(catalogue.Problems));
    }

    [Fact]
    public void Takes_the_first_product_type_else_the_google_category()
    {
        var catalogue = Parse(
            "id,product_type,google_product_category\n"
            + "SKU-1,\"Shoes > Running,Sale\",Apparel & Accessories > Shoes\n"
            + "SKU-2,,Apparel & Accessories > Shoes\n");

        Assert.Equal("Shoes > Running", catalogue.Offers[0].Category);
        Assert.Equal("Apparel & Accessories > Shoes", catalogue.Offers[0].GoogleCategory);
        Assert.Equal("Apparel & Accessories > Shoes", catalogue.Offers[1].Category);
    }

    [Fact]
    public void A_repeated_id_whatever_its_case_makes_the_later_row_invalid()
    {
        var catalogue = Parse("id,title\nSKU-1,First\nsku-1,Second\n");

        Assert.Equal("First", Assert.Single(catalogue.Offers).Title);
        Assert.Equal(1, catalogue.Invalid);
        Assert.Equal(new ImportProblem(3, "id", "The id sku-1 appears more than once."), Assert.Single(catalogue.Problems));
    }

    [Fact]
    public void A_missing_or_long_id_makes_the_row_invalid()
    {
        var catalogue = Parse($"id,title\n,No id\n{new string('x', 51)},Long id\nSKU-1,Fine\n");

        Assert.Equal(3, catalogue.Rows);
        Assert.Equal(2, catalogue.Invalid);
        Assert.Equal("Fine", Assert.Single(catalogue.Offers).Title);
        Assert.Equal(
            new[] { new ImportProblem(2, "id", "The id is missing."), new ImportProblem(3, "id", "The id is longer than 50 characters.") },
            catalogue.Problems.ToArray());
    }

    [Fact]
    public void A_value_that_breaks_its_rule_is_left_empty_and_the_product_kept()
    {
        var catalogue = Parse($"id,title,link,price,availability,gtin\nSKU-1,{new string('t', 151)},ftp://acme.example/p/1,cheap,maybe,12\n");

        var offer = Assert.Single(catalogue.Offers);
        Assert.Null(offer.Title);
        Assert.Null(offer.Link);
        Assert.Null(offer.Price);
        Assert.Null(offer.Currency);
        Assert.Null(offer.Availability);
        Assert.Null(offer.Gtin);
        Assert.Equal(0, catalogue.Invalid);
        Assert.Equal(new[] { "title", "link", "price", "availability", "gtin" }, catalogue.Problems.Select(p => p.Attribute).ToArray());
        Assert.All(catalogue.Problems, problem => Assert.Equal(2, problem.Row));
    }

    [Fact]
    public void A_sale_price_in_another_currency_or_without_a_price_is_ignored()
    {
        var catalogue = Parse("id,price,sale_price\nSKU-1,1499 INR,999 INR\nSKU-2,1499 INR,12 USD\nSKU-3,,999 INR\n");

        Assert.Equal(new decimal?[] { 999m, null, null }, catalogue.Offers.Select(o => o.SalePrice).ToArray());
        Assert.Equal(
            new[]
            {
                "The sale price was ignored because it isn't in the same currency as the price.",
                "The sale price was ignored because the product has no price.",
            },
            catalogue.Problems.Select(p => p.Message).ToArray());
    }

    [Fact]
    public void Uses_the_brands_currency_when_a_price_has_none()
    {
        var offer = Assert.Single(CatalogueFile.Parse(Encoding.UTF8.GetBytes("id,price\nSKU-1,20\n"), "USD").Offers);

        Assert.Equal("USD", offer.Currency);
    }

    [Fact]
    public void Keeps_only_the_first_50_problems()
    {
        var catalogue = Parse("id,price\n" + string.Concat(Enumerable.Range(1, 60).Select(i => $"SKU-{i},cheap\n")));

        Assert.Equal(60, catalogue.Offers.Count);
        Assert.Equal(50, catalogue.Problems.Count);
        Assert.Equal(51, catalogue.Problems[^1].Row);
    }

    [Fact]
    public void Refuses_a_file_without_an_id_column() =>
        AssertRefused("title,price\nRunner,1499\n", "no_id_column", "The file has no id column. Every product needs an id.");

    [Fact]
    public void Refuses_an_empty_file() => AssertRefused(" \r\n ", "unreadable", Unreadable);

    [Fact]
    public void Refuses_broken_xml() => AssertRefused("<rss><channel><item><g:id>1</g:id>", "unreadable", Unreadable);

    [Fact]
    public void Refuses_xml_that_isnt_a_feed() => AssertRefused("<html><body>Shop</body></html>", "unreadable", Unreadable);

    [Fact]
    public void Refuses_xml_that_declares_a_document_type()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY secret SYSTEM "file:///c:/windows/win.ini">]>
            <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0"><channel><item><g:id>SKU-1</g:id><title>&secret;</title></item></channel></rss>
            """;

        AssertRefused(xxe, "unreadable", Unreadable);
    }

    [Fact]
    public void Refuses_a_broken_gzip()
    {
        var error = Assert.Throws<CatalogueFileException>(() => CatalogueFile.Parse([0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02, 0x03], "INR"));

        Assert.Equal("unreadable", error.Code);
    }

    [Fact]
    public void Refuses_a_file_larger_than_50_MB()
    {
        var error = Assert.Throws<CatalogueFileException>(() => CatalogueFile.Parse(new byte[CatalogueFile.MaxBytes + 1], "INR"));

        Assert.Equal("too_large", error.Code);
        Assert.Equal("The file is larger than 50 MB.", error.Message);
    }

    [Fact]
    public void Refuses_a_file_larger_than_50_MB_once_unpacked()
    {
        var bomb = Gzip(new byte[CatalogueFile.MaxBytes + 1]);

        var error = Assert.Throws<CatalogueFileException>(() => CatalogueFile.Parse(bomb, "INR"));

        Assert.True(bomb.Length < 1024 * 1024);
        Assert.Equal("too_large", error.Code);
    }

    [Fact]
    public void Refuses_more_than_50000_products() =>
        AssertRefused(
            "id\n" + string.Concat(Enumerable.Range(1, CatalogueFile.MaxProducts + 1).Select(i => $"S{i}\n")),
            "too_many_products",
            "The file has more than 50,000 products.");

    [Fact]
    public void Reads_50000_products()
    {
        var catalogue = Parse("id\n" + string.Concat(Enumerable.Range(1, CatalogueFile.MaxProducts).Select(i => $"S{i}\n")));

        Assert.Equal(CatalogueFile.MaxProducts, catalogue.Offers.Count);
    }

    private static ParsedCatalogue Parse(string text) => CatalogueFile.Parse(Encoding.UTF8.GetBytes(text), "INR");

    private static void AssertRefused(string text, string code, string message)
    {
        var error = Assert.Throws<CatalogueFileException>(() => Parse(text));

        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.Message);
    }

    private static byte[] Gzip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }
}
```

- [ ] **Step 3: Run both test classes to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"`
Expected: the build fails with `CS0234: The type or namespace name 'Format' does not exist in the namespace 'Kairo.Modules.Catalogue'`.

- [ ] **Step 4: Write the result types and errors**

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/ParsedCatalogue.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Format;

internal sealed record ImportProblem(int Row, string Attribute, string Message);

internal sealed record ProductOffer(
    string OfferId,
    string? Title,
    string? Description,
    string? Link,
    string? ImageLink,
    IReadOnlyList<string> AdditionalImageLinks,
    decimal? Price,
    decimal? SalePrice,
    string? Currency,
    string? Availability,
    string? Category,
    string? GoogleCategory,
    string? Brand,
    string? Gtin,
    string? ItemGroupId);

internal sealed record ParsedCatalogue(
    IReadOnlyList<ProductOffer> Offers,
    int Rows,
    int Invalid,
    IReadOnlyList<ImportProblem> Problems);
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/CatalogueFileException.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Format;

internal static class CatalogueFileErrors
{
    public const string TooLarge = "too_large";
    public const string Unreadable = "unreadable";
    public const string NoIdColumn = "no_id_column";
    public const string TooManyProducts = "too_many_products";
}

internal sealed class CatalogueFileException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static CatalogueFileException TooLarge() =>
        new(CatalogueFileErrors.TooLarge, "The file is larger than 50 MB.");

    public static CatalogueFileException Unreadable() =>
        new(CatalogueFileErrors.Unreadable, "Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format.");

    public static CatalogueFileException NoIdColumn() =>
        new(CatalogueFileErrors.NoIdColumn, "The file has no id column. Every product needs an id.");

    public static CatalogueFileException TooManyProducts() =>
        new(CatalogueFileErrors.TooManyProducts, "The file has more than 50,000 products.");
}
```

- [ ] **Step 5: Write attribute names and raw rows**

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/Attributes.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Format;

internal static class Attributes
{
    public const string Id = "id";
    public const string Title = "title";
    public const string Description = "description";
    public const string Link = "link";
    public const string ImageLink = "image_link";
    public const string AdditionalImageLink = "additional_image_link";
    public const string Price = "price";
    public const string SalePrice = "sale_price";
    public const string Availability = "availability";
    public const string ProductType = "product_type";
    public const string GoogleProductCategory = "google_product_category";
    public const string Brand = "brand";
    public const string Gtin = "gtin";
    public const string ItemGroupId = "item_group_id";

    public static readonly string[] ListValued = [AdditionalImageLink, ProductType];

    public static string Normalise(string name)
    {
        var normal = name.Trim().TrimStart('\uFEFF').Trim().ToLowerInvariant();
        if (normal.StartsWith("g:", StringComparison.Ordinal))
        {
            normal = normal[2..];
        }

        return normal.Replace(' ', '_');
    }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/RawRow.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Format;

internal sealed class RawRow(int row)
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    public int Row { get; } = row;

    public bool IsEmpty => _values.Count == 0;

    public void Add(string attribute, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (!_values.TryGetValue(attribute, out var values))
        {
            _values[attribute] = values = [];
        }

        values.Add(trimmed);
    }

    public string? First(string attribute) => _values.TryGetValue(attribute, out var values) ? values[0] : null;

    public IReadOnlyList<string> All(string attribute) => _values.TryGetValue(attribute, out var values) ? values : [];
}
```

- [ ] **Step 6: Write the delimited text reader**

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/DelimitedText.cs`:

```csharp
using System.Text;

namespace Kairo.Modules.Catalogue.Format;

internal static class DelimitedText
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static List<RawRow> Read(byte[] content, int start)
    {
        var text = Utf8.GetString(content, start, content.Length - start);
        using var records = Records(text).GetEnumerator();
        string[]? header = null;
        while (records.MoveNext())
        {
            if (records.Current.Any(field => field.Trim().Length > 0))
            {
                header = records.Current.Select(Attributes.Normalise).ToArray();
                break;
            }
        }

        if (header is null)
        {
            throw CatalogueFileException.Unreadable();
        }

        if (!header.Contains(Attributes.Id))
        {
            throw CatalogueFileException.NoIdColumn();
        }

        var rows = new List<RawRow>();
        var number = 1;
        while (records.MoveNext())
        {
            number++;
            var row = new RawRow(number);
            var fields = records.Current;
            for (var i = 0; i < fields.Count && i < header.Length; i++)
            {
                if (Attributes.ListValued.Contains(header[i]))
                {
                    foreach (var part in fields[i].Split(','))
                    {
                        row.Add(header[i], part);
                    }
                }
                else
                {
                    row.Add(header[i], fields[i]);
                }
            }

            if (!row.IsEmpty)
            {
                CatalogueFile.CheckCount(rows.Count + 1);
                rows.Add(row);
            }
        }

        return rows;
    }

    private static char DelimiterOf(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
            {
                end = text.Length;
            }

            var line = text.AsSpan(start, end - start);
            if (!line.IsWhiteSpace())
            {
                return line.Contains('\t') ? '\t' : ',';
            }

            start = end + 1;
        }

        return ',';
    }

    private static IEnumerable<List<string>> Records(string text)
    {
        var delimiter = DelimiterOf(text);
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                    i++;
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i += 2;
                }
                else
                {
                    quoted = false;
                    i++;
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
                i++;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                i++;
            }
            else if (c is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                yield return fields;
                fields = [];
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                field.Append(c);
                i++;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}
```

The header search treats records with only empty fields as blank, so a file that starts with blank lines still finds its header, and that header is row 1. The delimiter comes from the first line that isn't blank, for the same reason.

- [ ] **Step 7: Write the XML reader**

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/XmlProducts.cs`:

```csharp
using System.Xml;
using System.Xml.Linq;

namespace Kairo.Modules.Catalogue.Format;

internal static class XmlProducts
{
    private static readonly XNamespace Google = "http://base.google.com/ns/1.0";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    public static List<RawRow> Read(byte[] content, int start)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(content, start, content.Length - start), Settings);
            reader.MoveToContent();
            var isAtom = reader.LocalName == "feed" && reader.NamespaceURI == Atom.NamespaceName;
            if (!isAtom && reader.LocalName != "rss")
            {
                throw CatalogueFileException.Unreadable();
            }

            var itemName = isAtom ? "entry" : "item";
            var rows = new List<RawRow>();
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == itemName)
                {
                    var item = (XElement)XNode.ReadFrom(reader);
                    CatalogueFile.CheckCount(rows.Count + 1);
                    rows.Add(RowOf(rows.Count + 1, item, isAtom));
                }
                else
                {
                    reader.Read();
                }
            }

            return rows;
        }
        catch (XmlException)
        {
            throw CatalogueFileException.Unreadable();
        }
    }

    private static RawRow RowOf(int number, XElement item, bool isAtom)
    {
        var row = new RawRow(number);
        foreach (var element in item.Elements().Where(element => element.Name.Namespace == Google))
        {
            row.Add(Attributes.Normalise(element.Name.LocalName), element.Value);
        }

        var plain = isAtom ? Atom : XNamespace.None;
        AddMissing(row, Attributes.Title, item.Element(plain + "title")?.Value);
        AddMissing(
            row,
            Attributes.Link,
            isAtom
                ? item.Elements(Atom + "link").Select(link => (string?)link.Attribute("href")).FirstOrDefault(href => href is not null)
                : item.Element(plain + "link")?.Value);
        AddMissing(row, Attributes.Description, item.Element(plain + (isAtom ? "summary" : "description"))?.Value);
        return row;
    }

    private static void AddMissing(RawRow row, string attribute, string? value)
    {
        if (row.First(attribute) is null && value is not null)
        {
            row.Add(attribute, value);
        }
    }
}
```

- [ ] **Step 8: Write the value rules**

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/ProductValues.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Catalogue.Format;

internal sealed record Money(decimal Amount, string Currency);

internal static partial class ProductValues
{
    public const int MaxTitleLength = 150;
    public const int MaxDescriptionLength = 5000;
    public const int MaxBrandLength = 70;
    public const int MaxItemGroupIdLength = 50;
    public const int MaxCategoryLength = 750;
    public const int MaxUrlLength = 2000;
    public const int MaxAdditionalImages = 10;
    public const decimal MaxAmount = 999_999_999_999_999m;

    private static readonly string[] Availabilities = ["in_stock", "out_of_stock", "preorder", "backorder"];

    public static string? Text(string? value, int maxLength, string attribute, Action<string, string> warn)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        warn(attribute, $"The {Label(attribute)} is longer than {maxLength.ToString("N0", CultureInfo.InvariantCulture)} characters, so it was left empty.");
        return null;
    }

    public static bool IsWebAddress(string value) =>
        value.Length <= MaxUrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static string? Url(string? value, string attribute, Action<string, string> warn)
    {
        if (value is null || IsWebAddress(value))
        {
            return value;
        }

        warn(attribute, $"The {Label(attribute)} isn't a full web address starting with http:// or https://, so it was left empty.");
        return null;
    }

    public static List<string> AdditionalImages(IReadOnlyList<string> values, Action<string, string> warn)
    {
        var links = new List<string>();
        foreach (var value in values)
        {
            if (Url(value, Attributes.AdditionalImageLink, warn) is not { } link)
            {
                continue;
            }

            if (links.Count == MaxAdditionalImages)
            {
                warn(Attributes.AdditionalImageLink, "Only the first 10 additional image links were kept.");
                break;
            }

            links.Add(link);
        }

        return links;
    }

    public static Money? Amount(string? value, string currency, string attribute, Action<string, string> warn)
    {
        if (value is null)
        {
            return null;
        }

        var match = MoneyPattern().Match(value);
        if (match.Success
            && !(match.Groups["before"].Success && match.Groups["after"].Success)
            && decimal.TryParse(match.Groups["amount"].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            && amount > 0
            && amount <= MaxAmount)
        {
            var code = match.Groups["before"].Success ? match.Groups["before"].Value
                : match.Groups["after"].Success ? match.Groups["after"].Value
                : currency;
            return new Money(Math.Round(amount, 4, MidpointRounding.AwayFromZero), code.ToUpperInvariant());
        }

        warn(attribute, $"The {Label(attribute)} isn't an amount above 0 with an optional currency, such as 1499.00 INR, so it was left empty.");
        return null;
    }

    public static string? Availability(string? value, Action<string, string> warn)
    {
        if (value is null)
        {
            return null;
        }

        var normal = value.Trim().ToLowerInvariant().Replace(' ', '_');
        if (Availabilities.Contains(normal))
        {
            return normal;
        }

        warn(Attributes.Availability, "The availability isn't in_stock, out_of_stock, preorder or backorder, so it was left empty.");
        return null;
    }

    public static string? Gtin(string? value, Action<string, string> warn)
    {
        if (value is null || (value.Length is >= 8 and <= 14 && value.All(char.IsAsciiDigit)))
        {
            return value;
        }

        warn(Attributes.Gtin, "The GTIN isn't 8 to 14 digits, so it was left empty.");
        return null;
    }

    private static string Label(string attribute) => attribute switch
    {
        Attributes.ImageLink => "image link",
        Attributes.AdditionalImageLink => "additional image link",
        Attributes.SalePrice => "sale price",
        Attributes.ProductType => "product type",
        Attributes.GoogleProductCategory => "Google product category",
        Attributes.ItemGroupId => "item group id",
        _ => attribute,
    };

    [GeneratedRegex(@"^(?:(?<before>[A-Za-z]{3})\s*)?(?<amount>\d{1,3}(?:,\d{2,3})*,\d{3}(?:\.\d+)?|\d+(?:\.\d+)?)(?:\s*(?<after>[A-Za-z]{3}))?$")]
    private static partial Regex MoneyPattern();
}
```

- [ ] **Step 9: Write the row checks and the entry point**

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/ProductRows.cs`:

```csharp
namespace Kairo.Modules.Catalogue.Format;

internal static class ProductRows
{
    public const int MaxProblems = 50;
    public const int MaxIdLength = 50;

    public static ParsedCatalogue Check(IReadOnlyList<RawRow> rows, string currency)
    {
        var offers = new List<ProductOffer>(rows.Count);
        var problems = new List<ImportProblem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = 0;
        foreach (var row in rows)
        {
            void Note(string attribute, string message)
            {
                if (problems.Count < MaxProblems)
                {
                    problems.Add(new ImportProblem(row.Row, attribute, message));
                }
            }

            var id = row.First(Attributes.Id);
            var error = id is null ? "The id is missing."
                : id.Length > MaxIdLength ? $"The id is longer than {MaxIdLength} characters."
                : seen.Add(id) ? null
                : $"The id {id} appears more than once.";
            if (error is not null)
            {
                invalid++;
                Note(Attributes.Id, error);
                continue;
            }

            offers.Add(OfferOf(row, id!, currency, Note));
        }

        return new ParsedCatalogue(offers, rows.Count, invalid, problems);
    }

    private static ProductOffer OfferOf(RawRow row, string id, string currency, Action<string, string> warn)
    {
        var title = ProductValues.Text(row.First(Attributes.Title), ProductValues.MaxTitleLength, Attributes.Title, warn);
        var description = ProductValues.Text(row.First(Attributes.Description), ProductValues.MaxDescriptionLength, Attributes.Description, warn);
        var link = ProductValues.Url(row.First(Attributes.Link), Attributes.Link, warn);
        var imageLink = ProductValues.Url(row.First(Attributes.ImageLink), Attributes.ImageLink, warn);
        var additionalImageLinks = ProductValues.AdditionalImages(row.All(Attributes.AdditionalImageLink), warn);
        var price = ProductValues.Amount(row.First(Attributes.Price), currency, Attributes.Price, warn);
        var salePrice = SalePrice(ProductValues.Amount(row.First(Attributes.SalePrice), currency, Attributes.SalePrice, warn), price, warn);
        var availability = ProductValues.Availability(row.First(Attributes.Availability), warn);
        var productType = ProductValues.Text(row.First(Attributes.ProductType), ProductValues.MaxCategoryLength, Attributes.ProductType, warn);
        var googleCategory = ProductValues.Text(row.First(Attributes.GoogleProductCategory), ProductValues.MaxCategoryLength, Attributes.GoogleProductCategory, warn);
        var brand = ProductValues.Text(row.First(Attributes.Brand), ProductValues.MaxBrandLength, Attributes.Brand, warn);
        var gtin = ProductValues.Gtin(row.First(Attributes.Gtin), warn);
        var itemGroupId = ProductValues.Text(row.First(Attributes.ItemGroupId), ProductValues.MaxItemGroupIdLength, Attributes.ItemGroupId, warn);
        return new ProductOffer(
            id,
            title,
            description,
            link,
            imageLink,
            additionalImageLinks,
            price?.Amount,
            salePrice?.Amount,
            price?.Currency,
            availability,
            productType ?? googleCategory,
            googleCategory,
            brand,
            gtin,
            itemGroupId);
    }

    private static Money? SalePrice(Money? sale, Money? price, Action<string, string> warn)
    {
        if (sale is null)
        {
            return null;
        }

        if (price is null)
        {
            warn(Attributes.SalePrice, "The sale price was ignored because the product has no price.");
            return null;
        }

        if (sale.Currency != price.Currency)
        {
            warn(Attributes.SalePrice, "The sale price was ignored because it isn't in the same currency as the price.");
            return null;
        }

        return sale;
    }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Format/CatalogueFile.cs`:

```csharp
using System.IO.Compression;

namespace Kairo.Modules.Catalogue.Format;

internal static class CatalogueFile
{
    public const int MaxBytes = 50 * 1024 * 1024;
    public const int MaxProducts = 50_000;

    public static ParsedCatalogue Parse(byte[] body, string currency)
    {
        var content = Unpack(body);
        var textStart = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        var first = textStart;
        while (first < content.Length && content[first] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            first++;
        }

        if (first == content.Length)
        {
            throw CatalogueFileException.Unreadable();
        }

        var rows = content[first] == (byte)'<' ? XmlProducts.Read(content, first) : DelimitedText.Read(content, textStart);
        return ProductRows.Check(rows, currency);
    }

    public static void CheckCount(int count)
    {
        if (count > MaxProducts)
        {
            throw CatalogueFileException.TooManyProducts();
        }
    }

    private static byte[] Unpack(byte[] body)
    {
        if (body.Length > MaxBytes)
        {
            throw CatalogueFileException.TooLarge();
        }

        if (body.Length < 2 || body[0] != 0x1F || body[1] != 0x8B)
        {
            return body;
        }

        try
        {
            using var input = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxBytes)
                {
                    throw CatalogueFileException.TooLarge();
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw CatalogueFileException.Unreadable();
        }
    }
}
```

XML is read from its first non-blank byte, because an XML declaration must come first. Delimited text is read from just after the byte-order mark, so a TSV whose first column is empty keeps its leading tab.

- [ ] **Step 10: Run both test classes to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"`
Expected: all passed, 0 failed (about 60 test cases).

- [ ] **Step 11: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Catalogue/Format core/tests/Kairo.IntegrationTests/Catalogue/ProductValuesTests.cs core/tests/Kairo.IntegrationTests/Catalogue/CatalogueFileTests.cs
git commit -m "Read product files in Google's Merchant Center format"
```

### Task 3: Importing a file

**Files:**
- Modify: `core/src/Kairo.SharedKernel/Files/IFileStore.cs` (the `FileBuckets` class)
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/Products/Product.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFixes.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueLiveEvents.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/CatalogueImporter.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportViews.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportEndpoints.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/Catalogues.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/CatalogueRows.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/CatalogueLocks.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/FileImportTests.cs`

**Interfaces:**
- Consumes:
  - From Task 1: `KairoDatabase.TryLockAsync`, `CatalogueDbContext` and every entity.
  - From Task 2: `CatalogueFile.Parse`, `ParsedCatalogue`, `ProductOffer`, `ImportProblem` and `CatalogueFileException`.
- Produces:
  - `FileBuckets.CatalogueImports = "catalogue-imports"`.
  - `Product.StockState`, `Product.From(ProductOffer, Guid importId, DateTimeOffset now)`, `Product.Apply(ProductOffer, Guid importId, DateTimeOffset now) → bool`, `Product.Remove(Guid importId, DateTimeOffset now)` and `Product.RefreshMissing() → bool`.
  - `ProductFixes.DropSupplied(Product) → bool`.
  - `ImportInput(string Kind, string? FileName, string? FeedUrl, Guid? StartedBy, bool AllowLargeRemoval = false)`.
  - `CatalogueImporter`:
    - `LockName(Guid brandId)` (static) and `TryLockAsync(ct) → Task<bool>`.
    - `ImportAsync(ImportInput, byte[] body, ct) → Task<CatalogueImport>`.
    - `FailAsync(ImportInput, string code, string message, ct) → Task<CatalogueImport>`.
    - `ImportAsync` clears the context's change tracker as it saves, so callers load anything else they change **after** it returns.
  - `ImportProblems.Write`, `ImportProblems.Read`.
  - `ImportView.Of(CatalogueImport)` and `ImportDetailView.Of(CatalogueImport)`.
  - `ImportEndpoints.BusyProblem()` (409) and `ImportEndpoints.AuditTarget = "catalogue_import"`.
  - `CatalogueLiveEvents.Imported(CatalogueImport)`.
  - `CatalogueSetup.AddCatalogue()` / `MapCatalogue()`, grown by later tasks.
  - Test helpers:
    - `Catalogues.Row`, `Csv`, `Many`, `File`, `PostFileAsync`, `ImportAsync`, `ErrorsAsync` and `ProblemTitleAsync`; plus `Catalogues.Base` and `Catalogues.Imports`.
    - `CatalogueRows.ProductsAsync`, `StockEventsAsync` and `ImportsAsync`.
    - `CatalogueLocks.HoldAsync`.

- [ ] **Step 1: Write the test helpers**

Create `core/tests/Kairo.IntegrationTests/Catalogue/Catalogues.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kairo.IntegrationTests.Catalogue;

internal static class Catalogues
{
    public const string Header = "id,title,link,image_link,price,availability,product_type\n";

    public static string Base(Guid brandId) => $"/api/brands/{brandId}/catalogue";

    public static string Imports(Guid brandId) => $"{Base(brandId)}/imports";

    public static string Row(string id, string availability = "in_stock", string? title = null, string price = "1499.00 INR", string category = "Shoes") =>
        $"{id},{title ?? $"Runner {id}"},https://acme.example/p/{id},https://acme.example/i/{id}.jpg,{price},{availability},{category}\n";

    public static string Csv(params string[] rows) => Header + string.Concat(rows);

    public static string Many(int count, string availability = "in_stock") =>
        Header + string.Concat(Enumerable.Range(1, count).Select(i => Row($"SKU-{i}", availability)));

    public static MultipartFormDataContent File(string text, string fileName = "products.csv", bool? allowLargeRemoval = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        if (allowLargeRemoval is { } allow)
        {
            content.Add(new StringContent(allow ? "true" : "false"), "allowLargeRemoval");
        }

        return content;
    }

    public static Task<HttpResponseMessage> PostFileAsync(HttpClient browser, Guid brandId, string text, bool? allowLargeRemoval = null) =>
        browser.PostAsync(Imports(brandId), File(text, allowLargeRemoval: allowLargeRemoval));

    public static async Task<JsonElement> ImportAsync(HttpClient admin, Guid brandId, string text, bool? allowLargeRemoval = null)
    {
        var response = await PostFileAsync(admin, brandId, text, allowLargeRemoval);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<string[]> ErrorsAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()!).ToArray();
    }

    public static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;
}
```

Create `core/tests/Kairo.IntegrationTests/Catalogue/CatalogueRows.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Catalogue;

internal sealed record ProductRow(
    Guid Id,
    string OfferId,
    string? Title,
    string? Availability,
    string Status,
    DateTimeOffset? RemovedAt,
    int Missing,
    string Fixes,
    Guid LastImportId);

internal sealed record StockRow(Guid ProductId, string? From, string? To, Guid ImportId);

internal sealed record ImportRow(Guid Id, string Status, string? StorageKey, Guid? StartedBy);

internal static class CatalogueRows
{
    public static Task<List<ProductRow>> ProductsAsync(string connectionString, Guid brandId) => ReadAsync(
        connectionString,
        brandId,
        "SELECT Id, OfferId, Title, Availability, Status, RemovedAt, Missing, Fixes, LastImportId FROM catalogue.Products WHERE BrandId = @brand ORDER BY OfferId",
        reader => new ProductRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetGuid(8)));

    public static Task<List<StockRow>> StockEventsAsync(string connectionString, Guid brandId) => ReadAsync(
        connectionString,
        brandId,
        """
        SELECT e.ProductId, e.[From], e.[To], e.ImportId
        FROM catalogue.StockEvents e JOIN catalogue.Imports i ON i.Id = e.ImportId
        WHERE e.BrandId = @brand
        ORDER BY i.Number, e.ProductId
        """,
        reader => new StockRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetGuid(3)));

    public static Task<List<ImportRow>> ImportsAsync(string connectionString, Guid brandId) => ReadAsync(
        connectionString,
        brandId,
        "SELECT Id, Status, StorageKey, StartedBy FROM catalogue.Imports WHERE BrandId = @brand ORDER BY Number",
        reader => new ImportRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3)));

    private static async Task<List<T>> ReadAsync<T>(string connectionString, Guid brandId, string sql, Func<SqlDataReader, T> map)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;\n" + sql, connection);
        command.Parameters.AddWithValue("@brand", brandId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Catalogue/CatalogueLocks.cs`:

```csharp
using Kairo.Modules.Catalogue.Imports;
using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Catalogue;

internal static class CatalogueLocks
{
    public static async Task<IAsyncDisposable> HoldAsync(string connectionString, Guid brandId)
    {
        var database = new KairoDatabase(connectionString);
        await database.BeginTransactionAsync(CancellationToken.None);
        Assert.True(await database.TryLockAsync(CatalogueImporter.LockName(brandId), CancellationToken.None));
        return database;
    }
}
```

- [ ] **Step 2: Write the failing import tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/FileImportTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.Modules.Catalogue.Format;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class FileImportTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ConnectionString => sqlServer.KairoConnectionString;

    [Fact]
    public async Task A_file_import_adds_the_products_and_is_recorded()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await Catalogues.PostFileAsync(
            admin.Browser,
            brandId,
            Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var import = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var importId = import.GetProperty("id").GetGuid();
        Assert.Equal($"/api/brands/{brandId}/catalogue/imports/{importId}", response.Headers.Location!.OriginalString);
        Assert.Equal("file", import.GetProperty("kind").GetString());
        Assert.Equal("products.csv", import.GetProperty("fileName").GetString());
        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(3, import.GetProperty("rows").GetInt32());
        Assert.Equal(3, import.GetProperty("valid").GetInt32());
        Assert.Equal(3, import.GetProperty("added").GetInt32());
        Assert.Equal(admin.UserId, import.GetProperty("startedBy").GetGuid());
        var products = await CatalogueRows.ProductsAsync(ConnectionString, brandId);
        Assert.Equal(new[] { "SKU-1", "SKU-2", "SKU-3" }, products.Select(p => p.OfferId).ToArray());
        Assert.All(products, product =>
        {
            Assert.Equal("active", product.Status);
            Assert.Equal(0, product.Missing);
            Assert.Equal(importId, product.LastImportId);
        });
        Assert.Contains("catalogue.file_imported", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Members_can_list_imports_and_read_one_with_its_problems()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var import = await Catalogues.ImportAsync(
            admin.Browser,
            brandId,
            Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2", price: "cheap"), ",no id,,,,,\n"));
        var approver = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "approver");

        var listed = await approver.Browser.GetFromJsonAsync<JsonElement>(Catalogues.Imports(brandId), Token);
        var detail = await approver.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Imports(brandId)}/{import.GetProperty("id").GetGuid()}", Token);

        Assert.False(Assert.Single(listed.EnumerateArray()).TryGetProperty("problems", out _));
        Assert.Equal(1, detail.GetProperty("invalid").GetInt32());
        Assert.Equal(
            new[]
            {
                "3 price The price isn't an amount above 0 with an optional currency, such as 1499.00 INR, so it was left empty.",
                "4 id The id is missing.",
            },
            detail.GetProperty("problems").EnumerateArray()
                .Select(p => $"{p.GetProperty("row").GetInt32()} {p.GetProperty("attribute").GetString()} {p.GetProperty("message").GetString()}")
                .ToArray());
    }

    [Fact]
    public async Task A_second_import_updates_changed_products_and_counts_the_rest()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));

        var import = await Catalogues.ImportAsync(
            admin.Browser,
            brandId,
            Catalogues.Csv(Catalogues.Row("SKU-1", title: "Runner Pro"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3"), Catalogues.Row("SKU-4")));

        Assert.Equal((1, 1, 2, 0), Counts(import));
        var products = await CatalogueRows.ProductsAsync(ConnectionString, brandId);
        Assert.Equal("Runner Pro", products.Single(p => p.OfferId == "SKU-1").Title);
        Assert.Equal(import.GetProperty("id").GetGuid(), products.Single(p => p.OfferId == "SKU-1").LastImportId);
        Assert.NotEqual(import.GetProperty("id").GetGuid(), products.Single(p => p.OfferId == "SKU-2").LastImportId);
    }

    [Fact]
    public async Task Products_left_out_are_removed_and_come_back_later()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));

        var removing = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));
        var removed = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Single(p => p.OfferId == "SKU-3");
        var returning = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"), Catalogues.Row("SKU-3")));
        var back = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Single(p => p.OfferId == "SKU-3");

        Assert.Equal((0, 0, 2, 1), Counts(removing));
        Assert.Equal("removed", removed.Status);
        Assert.NotNull(removed.RemovedAt);
        Assert.Equal((0, 1, 2, 0), Counts(returning));
        Assert.Equal("active", back.Status);
        Assert.Null(back.RemovedAt);
    }

    [Fact]
    public async Task An_id_whose_case_changes_is_the_same_product()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("sku-1", title: "Runner SKU-1")));

        Assert.Equal((0, 1, 0, 0), Counts(import));
        Assert.Equal("sku-1", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).OfferId);
    }

    [Fact]
    public async Task Stock_changes_are_recorded()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var soldOut = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "out_of_stock"), Catalogues.Row("SKU-2")));
        var dropped = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-2")));
        var back = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "preorder"), Catalogues.Row("SKU-2")));

        var productId = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Single(p => p.OfferId == "SKU-1").Id;
        var events = await CatalogueRows.StockEventsAsync(ConnectionString, brandId);
        Assert.Equal(new[] { "in_stock>out_of_stock", "out_of_stock>removed", "removed>preorder" }, events.Select(e => $"{e.From}>{e.To}").ToArray());
        Assert.All(events, e => Assert.Equal(productId, e.ProductId));
        Assert.Equal(
            new[] { soldOut, dropped, back }.Select(import => import.GetProperty("id").GetGuid()).ToArray(),
            events.Select(e => e.ImportId).ToArray());
    }

    [Fact]
    public async Task The_same_file_again_is_unchanged_and_stored_once()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var text = Catalogues.Csv(Catalogues.Row("SKU-1"));
        var first = await Catalogues.ImportAsync(admin.Browser, brandId, text);

        var second = await Catalogues.ImportAsync(admin.Browser, brandId, text);

        Assert.Equal("unchanged", second.GetProperty("status").GetString());
        Assert.Equal((0, 0, 0, 0), Counts(second));
        Assert.Equal(first.GetProperty("id").GetGuid(), Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).LastImportId);
        var imports = await CatalogueRows.ImportsAsync(ConnectionString, brandId);
        Assert.NotNull(imports[0].StorageKey);
        Assert.Null(imports[1].StorageKey);
    }

    [Fact]
    public async Task The_raw_file_is_kept_in_the_import_bucket()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var text = Catalogues.Csv(Catalogues.Row("SKU-1"));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, text);

        var key = Assert.Single(await CatalogueRows.ImportsAsync(ConnectionString, brandId)).StorageKey;
        Assert.Equal($"{brandId:N}/imports/{import.GetProperty("id").GetGuid():N}", key);
        await using var stream = await _api.Services.GetRequiredService<IFileStore>().OpenAsync(FileBuckets.CatalogueImports, key!, Token);
        using var reader = new StreamReader(stream);
        Assert.Equal(text, await reader.ReadToEndAsync(Token));
    }

    [Fact]
    public async Task A_file_that_cant_be_read_fails_and_changes_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, "title,price\nRunner,1499\n");

        Assert.Equal("failed", import.GetProperty("status").GetString());
        Assert.Equal("no_id_column", import.GetProperty("errorCode").GetString());
        Assert.Equal("The file has no id column. Every product needs an id.", import.GetProperty("errorMessage").GetString());
        Assert.Equal("active", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Status);
    }

    [Fact]
    public async Task A_file_with_no_valid_rows_is_suspicious_and_changes_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Header);

        Assert.Equal("suspicious", import.GetProperty("status").GetString());
        Assert.Equal("no_valid_rows", import.GetProperty("errorCode").GetString());
        Assert.Equal("No product in this import could be read, so Kairo changed nothing.", import.GetProperty("errorMessage").GetString());
        Assert.Equal("active", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Status);
    }

    [Fact]
    public async Task Removing_more_than_half_of_20_or_more_products_is_suspicious_unless_allowed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(20));

        var refused = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(9));
        var activeAfterRefusal = (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Count(p => p.Status == "active");
        var allowed = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(9), allowLargeRemoval: true);

        Assert.Equal("suspicious", refused.GetProperty("status").GetString());
        Assert.Equal("large_removal", refused.GetProperty("errorCode").GetString());
        Assert.Equal(
            "This import would remove 11 of your 20 products, so Kairo changed nothing. If that's right, import the file again and allow large removals.",
            refused.GetProperty("errorMessage").GetString());
        Assert.Equal(20, activeAfterRefusal);
        Assert.Equal("succeeded", allowed.GetProperty("status").GetString());
        Assert.Equal(11, allowed.GetProperty("removed").GetInt32());
    }

    [Theory]
    [InlineData(20, 10)]
    [InlineData(19, 1)]
    public async Task Smaller_removals_go_ahead(int before, int after)
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(before));

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Many(after));

        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(before - after, import.GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task Each_import_publishes_a_live_event()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var import = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var published = Assert.Single(await OutboxRows.ReadAsync(ConnectionString, brandId), row => row.Type == "catalogue.imported");
        var payload = JsonDocument.Parse(published.Payload).RootElement;
        Assert.Equal(import.GetProperty("id").GetGuid(), payload.GetProperty("importId").GetGuid());
        Assert.Equal("succeeded", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_admins_can_import()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");

        var response = await Catalogues.PostFileAsync(marketer.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Choosing_no_file_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        using var form = new MultipartFormDataContent { { new StringContent("true"), "allowLargeRemoval" } };

        var response = await admin.Browser.PostAsync(Catalogues.Imports(brandId), form, Token);

        Assert.Equal(new[] { "Choose a product file to upload." }, await Catalogues.ErrorsAsync(response, "file"));
    }

    [Fact]
    public async Task A_file_over_50_MB_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = Catalogues.Csv(Catalogues.Row("SKU-1")) + new string(' ', CatalogueFile.MaxBytes);

        var response = await Catalogues.PostFileAsync(admin.Browser, brandId, huge);

        Assert.Equal(new[] { "The file is larger than 50 MB." }, await Catalogues.ErrorsAsync(response, "file"));
        Assert.Empty(await CatalogueRows.ImportsAsync(ConnectionString, brandId));
    }

    [Fact]
    public async Task An_import_while_another_is_running_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();

        HttpResponseMessage response;
        await using (await CatalogueLocks.HoldAsync(ConnectionString, brandId))
        {
            response = await Catalogues.PostFileAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Kairo is already importing this catalogue. Try again in a minute.", await Catalogues.ProblemTitleAsync(response));
        Assert.Empty(await CatalogueRows.ImportsAsync(ConnectionString, brandId));
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_imports()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(Catalogues.Imports(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_catalogue_of_50000_products_imports_within_a_minute()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var text = Catalogues.Many(CatalogueFile.MaxProducts);

        var clock = Stopwatch.StartNew();
        var import = await Catalogues.ImportAsync(admin.Browser, brandId, text);
        clock.Stop();

        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(CatalogueFile.MaxProducts, import.GetProperty("added").GetInt32());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"The import took {clock.Elapsed}.");
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static (int Added, int Updated, int Unchanged, int Removed) Counts(JsonElement import) => (
        import.GetProperty("added").GetInt32(),
        import.GetProperty("updated").GetInt32(),
        import.GetProperty("unchanged").GetInt32(),
        import.GetProperty("removed").GetInt32());

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

- [ ] **Step 3: Run the import tests to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Catalogue.FileImportTests"`
Expected: the build fails with `CS0117: 'FileBuckets' does not contain a definition for 'CatalogueImports'` and `CS0234` for `Kairo.Modules.Catalogue.Imports.CatalogueImporter`.

- [ ] **Step 4: Add the bucket name**

In `core/src/Kairo.SharedKernel/Files/IFileStore.cs`, change `FileBuckets` to:

```csharp
public static class FileBuckets
{
    public const string BrandFiles = "brand-files";
    public const string CatalogueImports = "catalogue-imports";
}
```

- [ ] **Step 5: Teach a product to take an offer**

Replace `core/src/Modules/Kairo.Modules.Catalogue/Products/Product.cs` with:

```csharp
using Kairo.Modules.Catalogue.Format;

namespace Kairo.Modules.Catalogue.Products;

internal sealed class Product : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string OfferId { get; set; } = "";

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Link { get; set; }

    public string? ImageLink { get; set; }

    public List<string> AdditionalImageLinks { get; set; } = [];

    public decimal? Price { get; set; }

    public decimal? SalePrice { get; set; }

    public string? Currency { get; set; }

    public string? Availability { get; set; }

    public string? Category { get; set; }

    public string? GoogleCategory { get; set; }

    public string? Brand { get; set; }

    public string? Gtin { get; set; }

    public string? ItemGroupId { get; set; }

    public ProductFixes Fixes { get; set; } = new();

    public MissingDetails Missing { get; set; }

    public string Status { get; set; } = ProductStatuses.Active;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid LastImportId { get; set; }

    public string? StockState => Status == ProductStatuses.Removed ? ProductStatuses.Removed : Availability;

    public static Product From(ProductOffer offer, Guid importId, DateTimeOffset now)
    {
        var product = new Product { Id = Guid.NewGuid(), FirstSeenAt = now };
        product.Apply(offer, importId, now);
        return product;
    }

    public bool Apply(ProductOffer offer, Guid importId, DateTimeOffset now)
    {
        var changed = Status != ProductStatuses.Active
            || OfferId != offer.OfferId
            || Title != offer.Title
            || Description != offer.Description
            || Link != offer.Link
            || ImageLink != offer.ImageLink
            || !AdditionalImageLinks.SequenceEqual(offer.AdditionalImageLinks)
            || Price != offer.Price
            || SalePrice != offer.SalePrice
            || Currency != offer.Currency
            || Availability != offer.Availability
            || Category != offer.Category
            || GoogleCategory != offer.GoogleCategory
            || Brand != offer.Brand
            || Gtin != offer.Gtin
            || ItemGroupId != offer.ItemGroupId;

        OfferId = offer.OfferId;
        Title = offer.Title;
        Description = offer.Description;
        Link = offer.Link;
        ImageLink = offer.ImageLink;
        AdditionalImageLinks = [.. offer.AdditionalImageLinks];
        Price = offer.Price;
        SalePrice = offer.SalePrice;
        Currency = offer.Currency;
        Availability = offer.Availability;
        Category = offer.Category;
        GoogleCategory = offer.GoogleCategory;
        Brand = offer.Brand;
        Gtin = offer.Gtin;
        ItemGroupId = offer.ItemGroupId;
        Status = ProductStatuses.Active;
        RemovedAt = null;
        changed |= Fixes.DropSupplied(this);
        changed |= RefreshMissing();
        if (changed)
        {
            UpdatedAt = now;
            LastImportId = importId;
        }

        return changed;
    }

    public void Remove(Guid importId, DateTimeOffset now)
    {
        Status = ProductStatuses.Removed;
        RemovedAt = now;
        UpdatedAt = now;
        LastImportId = importId;
    }

    public bool RefreshMissing()
    {
        var missing = MissingDetails.None;
        if (Title is null && Fixes.Title is null)
        {
            missing |= MissingDetails.Title;
        }

        if (Price is null && Fixes.Price is null)
        {
            missing |= MissingDetails.Price;
        }

        if (Link is null && Fixes.Link is null)
        {
            missing |= MissingDetails.Link;
        }

        if (ImageLink is null && Fixes.ImageLink is null)
        {
            missing |= MissingDetails.Image;
        }

        if (Availability is null)
        {
            missing |= MissingDetails.Stock;
        }

        var changed = missing != Missing;
        Missing = missing;
        return changed;
    }
}
```

Add this method to `ProductFixes` in `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFixes.cs`, after the last property:

```csharp

    public bool DropSupplied(Product product)
    {
        var dropped = false;
        if (product.Title is not null && Title is not null)
        {
            Title = null;
            dropped = true;
        }

        if (product.Price is not null && Price is not null)
        {
            Price = null;
            Currency = null;
            dropped = true;
        }

        if (product.Link is not null && Link is not null)
        {
            Link = null;
            dropped = true;
        }

        if (product.ImageLink is not null && ImageLink is not null)
        {
            ImageLink = null;
            dropped = true;
        }

        if (product.Category is not null && Category is not null)
        {
            Category = null;
            dropped = true;
        }

        return dropped;
    }
```

- [ ] **Step 6: Write the views and the live event**

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportViews.cs`:

```csharp
using System.Text.Json;
using Kairo.Modules.Catalogue.Format;

namespace Kairo.Modules.Catalogue.Imports;

internal static class ImportProblems
{
    public static string Write(IReadOnlyList<ImportProblem> problems) => JsonSerializer.Serialize(problems, JsonSerializerOptions.Web);

    public static ImportProblem[] Read(string json) => JsonSerializer.Deserialize<ImportProblem[]>(json, JsonSerializerOptions.Web) ?? [];
}

internal sealed record ImportView(
    Guid Id,
    string Kind,
    string? FileName,
    string? FeedUrl,
    string Status,
    int Rows,
    int Valid,
    int Invalid,
    int Added,
    int Updated,
    int Removed,
    int Unchanged,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt)
{
    public static ImportView Of(CatalogueImport import) => new(
        import.Id,
        import.Kind,
        import.FileName,
        import.FeedUrl,
        import.Status,
        import.Rows,
        import.Valid,
        import.Invalid,
        import.Added,
        import.Updated,
        import.Removed,
        import.Unchanged,
        import.ErrorCode,
        import.ErrorMessage,
        import.StartedBy,
        import.StartedAt,
        import.FinishedAt);
}

internal sealed record ImportDetailView(
    Guid Id,
    string Kind,
    string? FileName,
    string? FeedUrl,
    string Status,
    int Rows,
    int Valid,
    int Invalid,
    int Added,
    int Updated,
    int Removed,
    int Unchanged,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ImportProblem[] Problems)
{
    public static ImportDetailView Of(CatalogueImport import) => new(
        import.Id,
        import.Kind,
        import.FileName,
        import.FeedUrl,
        import.Status,
        import.Rows,
        import.Valid,
        import.Invalid,
        import.Added,
        import.Updated,
        import.Removed,
        import.Unchanged,
        import.ErrorCode,
        import.ErrorMessage,
        import.StartedBy,
        import.StartedAt,
        import.FinishedAt,
        ImportProblems.Read(import.Problems));
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/CatalogueLiveEvents.cs`:

```csharp
using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue;

internal static class CatalogueLiveEvents
{
    public static LiveEvent Imported(CatalogueImport import) =>
        new("catalogue.imported", new { importId = import.Id, status = import.Status }, LiveAudience.Everyone);
}
```

- [ ] **Step 7: Write the importer**

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/CatalogueImporter.cs`:

```csharp
using System.Security.Cryptography;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Format;
using Kairo.Modules.Catalogue.Products;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record ImportInput(string Kind, string? FileName, string? FeedUrl, Guid? StartedBy, bool AllowLargeRemoval = false);

internal sealed class CatalogueImporter(
    CatalogueDbContext db,
    KairoDatabase database,
    IFileStore store,
    IBrandDirectory brands,
    ILiveUpdates live,
    TimeProvider time)
{
    public const int SaveBatch = 1000;
    public const int LargeRemovalFloor = 20;
    public const string NoValidRows = "no_valid_rows";
    public const string LargeRemoval = "large_removal";

    private Guid BrandId => database.BrandId ?? throw new InvalidOperationException("No brand is set for this import.");

    public static string LockName(Guid brandId) => $"catalogue:{brandId:N}";

    public Task<bool> TryLockAsync(CancellationToken cancellationToken) =>
        database.TryLockAsync(LockName(BrandId), cancellationToken);

    public async Task<CatalogueImport> ImportAsync(ImportInput input, byte[] body, CancellationToken cancellationToken)
    {
        var import = Start(input);
        import.ContentHash = Convert.ToHexStringLower(SHA256.HashData(body));
        var last = await db.Imports.AsNoTracking()
            .Where(i => i.Status == ImportStatuses.Succeeded)
            .OrderByDescending(i => i.StartedAt)
            .ThenByDescending(i => i.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (last is not null && last.ContentHash == import.ContentHash && last.Kind == input.Kind && last.FeedUrl == input.FeedUrl)
        {
            import.Status = ImportStatuses.Unchanged;
            return await FinishAsync(import, cancellationToken);
        }

        import.StorageKey = FileKeys.For(BrandId, "imports", import.Id);
        await store.PutAsync(FileBuckets.CatalogueImports, import.StorageKey, body, "application/octet-stream", cancellationToken);

        ParsedCatalogue parsed;
        try
        {
            var currency = (await brands.GetAsync([BrandId], cancellationToken)).Single().Currency;
            parsed = CatalogueFile.Parse(body, currency);
        }
        catch (CatalogueFileException exception)
        {
            return await FailAsync(import, exception.Code, exception.Message, cancellationToken);
        }

        import.Rows = parsed.Rows;
        import.Valid = parsed.Offers.Count;
        import.Invalid = parsed.Invalid;
        import.Problems = ImportProblems.Write(parsed.Problems);
        await ApplyAsync(import, parsed.Offers, input, cancellationToken);
        return await FinishAsync(import, cancellationToken);
    }

    public Task<CatalogueImport> FailAsync(ImportInput input, string code, string message, CancellationToken cancellationToken) =>
        FailAsync(Start(input), code, message, cancellationToken);

    private async Task ApplyAsync(CatalogueImport import, IReadOnlyList<ProductOffer> offers, ImportInput input, CancellationToken cancellationToken)
    {
        var existing = await db.Products.AsNoTracking().ToListAsync(cancellationToken);
        var byOffer = existing.ToDictionary(product => product.OfferId, StringComparer.OrdinalIgnoreCase);
        var offered = offers.Select(offer => offer.OfferId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = existing.Count(product => product.Status == ProductStatuses.Active);
        var removals = existing.Where(product => product.Status == ProductStatuses.Active && !offered.Contains(product.OfferId)).ToList();

        if (offers.Count == 0)
        {
            Suspicious(import, NoValidRows, "No product in this import could be read, so Kairo changed nothing.");
            return;
        }

        if (!input.AllowLargeRemoval && active >= LargeRemovalFloor && removals.Count * 2 > active)
        {
            Suspicious(import, LargeRemoval, LargeRemovalMessage(input.Kind, removals.Count, active));
            return;
        }

        var now = import.StartedAt;
        var pending = 0;
        foreach (var offer in offers)
        {
            if (byOffer.TryGetValue(offer.OfferId, out var product))
            {
                var before = product.StockState;
                if (!product.Apply(offer, import.Id, now))
                {
                    import.Unchanged++;
                    continue;
                }

                db.Products.Update(product);
                import.Updated++;
                RecordStock(product, before, import.Id, now);
            }
            else
            {
                db.Products.Add(Product.From(offer, import.Id, now));
                import.Added++;
            }

            pending = await SaveWhenFullAsync(pending + 1, cancellationToken);
        }

        foreach (var product in removals)
        {
            var before = product.StockState;
            product.Remove(import.Id, now);
            db.Products.Update(product);
            import.Removed++;
            RecordStock(product, before, import.Id, now);
            pending = await SaveWhenFullAsync(pending + 1, cancellationToken);
        }

        await SaveAsync(cancellationToken);
        import.Status = ImportStatuses.Succeeded;
    }

    private void RecordStock(Product product, string? before, Guid importId, DateTimeOffset now)
    {
        if (before == product.StockState)
        {
            return;
        }

        db.StockEvents.Add(new StockEvent
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            From = before,
            To = product.StockState,
            ImportId = importId,
            At = now,
        });
    }

    private async Task<int> SaveWhenFullAsync(int pending, CancellationToken cancellationToken)
    {
        if (pending < SaveBatch)
        {
            return pending;
        }

        await SaveAsync(cancellationToken);
        return 0;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private static void Suspicious(CatalogueImport import, string code, string message)
    {
        import.Status = ImportStatuses.Suspicious;
        import.ErrorCode = code;
        import.ErrorMessage = message;
    }

    private static string LargeRemovalMessage(string kind, int removals, int active) =>
        $"This import would remove {removals} of your {active} products, so Kairo changed nothing. "
        + (kind == SourceKinds.File ? "If that's right, import the file again and allow large removals." : "Check the feed, then refresh it.");

    private Task<CatalogueImport> FailAsync(CatalogueImport import, string code, string message, CancellationToken cancellationToken)
    {
        import.Status = ImportStatuses.Failed;
        import.ErrorCode = code;
        import.ErrorMessage = message;
        return FinishAsync(import, cancellationToken);
    }

    private async Task<CatalogueImport> FinishAsync(CatalogueImport import, CancellationToken cancellationToken)
    {
        import.FinishedAt = time.GetUtcNow();
        db.Imports.Add(import);
        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(CatalogueLiveEvents.Imported(import), cancellationToken);
        return import;
    }

    private CatalogueImport Start(ImportInput input) => new()
    {
        Id = Guid.NewGuid(),
        Kind = input.Kind,
        FileName = input.FileName,
        FeedUrl = input.FeedUrl,
        StartedBy = input.StartedBy,
        StartedAt = time.GetUtcNow(),
    };
}
```

- [ ] **Step 8: Write the endpoints and map them**

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Format;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record UploadSizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;

internal static class ImportEndpoints
{
    public const long MaxRequestBytes = CatalogueFile.MaxBytes + 64 * 1024;
    public const int Listed = 50;
    public const string AuditTarget = "catalogue_import";
    public const string ChooseFile = "Choose a product file to upload.";
    public const string TooLarge = "The file is larger than 50 MB.";
    public const string Busy = "Kairo is already importing this catalogue. Try again in a minute.";

    public static async Task<Results<Created<ImportView>, ValidationProblem, ProblemHttpResult>> ImportFileAsync(
        IFormFile? file,
        [FromForm] bool? allowLargeRemoval,
        CatalogueDbContext db,
        CatalogueImporter importer,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileProblem(ChooseFile);
        }

        if (file.Length > CatalogueFile.MaxBytes)
        {
            return FileProblem(TooLarge);
        }

        if (!await importer.TryLockAsync(cancellationToken))
        {
            return BusyProblem();
        }

        byte[] body;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            body = buffer.ToArray();
        }

        var import = await importer.ImportAsync(
            new ImportInput(SourceKinds.File, CleanName(file.FileName), null, member.UserId, allowLargeRemoval == true),
            body,
            cancellationToken);
        await UseFileAsync(db, member, time, cancellationToken);

        var view = ImportView.Of(import);
        await audit.RecordAsync(new AuditRecord("catalogue.file_imported", AuditTarget, import.Id.ToString(), After: view), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/catalogue/imports/{import.Id}", view);
    }

    public static async Task<Ok<ImportView[]>> ListAsync(CatalogueDbContext db, CancellationToken cancellationToken)
    {
        var imports = await db.Imports.AsNoTracking()
            .OrderByDescending(import => import.StartedAt)
            .ThenByDescending(import => import.Number)
            .Take(Listed)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(imports.Select(ImportView.Of).ToArray());
    }

    public static async Task<Results<Ok<ImportDetailView>, NotFound>> GetAsync(Guid importId, CatalogueDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Imports.AsNoTracking().SingleOrDefaultAsync(import => import.Id == importId, cancellationToken) is not { } import)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ImportDetailView.Of(import));
    }

    public static ProblemHttpResult BusyProblem() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Busy);

    private static async Task UseFileAsync(CatalogueDbContext db, CurrentMember member, TimeProvider time, CancellationToken cancellationToken)
    {
        var source = await db.Sources.SingleOrDefaultAsync(cancellationToken);
        if (source is null)
        {
            source = new CatalogueSource { BrandId = member.BrandId };
            db.Sources.Add(source);
        }

        source.Kind = SourceKinds.File;
        source.FeedUrl = null;
        source.SetBy = member.UserId;
        source.SetAt = time.GetUtcNow();
        source.LastCheckedAt = null;
        source.LastSucceededAt = null;
        source.FailuresInRow = 0;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CleanName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (name.Length == 0)
        {
            return "catalogue";
        }

        return name.Length > 200 ? name[..200] : name;
    }

    private static ValidationProblem FileProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`:

```csharp
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue;

internal static class CatalogueSetup
{
    public static void AddCatalogue(this IServiceCollection services)
    {
        services.AddScoped<CatalogueImporter>();
    }

    public static void MapCatalogue(this IEndpointRouteBuilder api)
    {
        var catalogue = api.MapBrandApi().MapGroup("/catalogue");

        var imports = catalogue.MapGroup("/imports");
        imports.MapPost("", ImportEndpoints.ImportFileAsync)
            .RequireBrandRole(BrandRoles.Admin)
            .WithMetadata(new UploadSizeLimit(ImportEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: ImportEndpoints.MaxRequestBytes)
            .DisableAntiforgery();
        imports.MapGet("", ImportEndpoints.ListAsync);
        imports.MapGet("/{importId:guid}", ImportEndpoints.GetAsync);
    }
}
```

In `core/src/Modules/Kairo.Modules.Catalogue/CatalogueModule.cs`, add `services.AddCatalogue();` as the last line of `AddServices`, and make `MapApi`'s body `api.MapCatalogue();`.

- [ ] **Step 9: Run the import tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Catalogue.FileImportTests"`
Expected: 20 passed. Read the 50,000-product test's duration in the output. If it fails on time, first check that `SaveBatch` saving and `ChangeTracker.Clear()` are happening, before touching anything else.

- [ ] **Step 10: Regenerate the API contract**

Run:

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
```

Expected: the last run passes. `git diff --stat` shows `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts` with the three `/catalogue/imports` paths.

- [ ] **Step 11: Commit**

```bash
git add core/src/Kairo.SharedKernel/Files/IFileStore.cs core/src/Modules/Kairo.Modules.Catalogue core/tests/Kairo.IntegrationTests/Catalogue/Catalogues.cs core/tests/Kairo.IntegrationTests/Catalogue/CatalogueRows.cs core/tests/Kairo.IntegrationTests/Catalogue/CatalogueLocks.cs core/tests/Kairo.IntegrationTests/Catalogue/FileImportTests.cs core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Import the catalogue from a file"
```

### Task 4: Feed links

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/FeedFetcher.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/FeedRefresher.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Imports/SourceEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportViews.cs` (add `SourceView`)
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/FeedTests.cs`

**Interfaces:**
- Consumes:
  - From Task 3: `CatalogueImporter` (`TryLockAsync`, `ImportAsync`, `FailAsync`), `ImportInput`, `ImportView`, `ImportEndpoints.BusyProblem()`, `ImportEndpoints.AuditTarget` and the test helpers.
  - The existing `SafeWebClient`, `SafeFetchOptions`, `SafeFetchException` and `SafeFetchErrors`.
  - The existing `INotifications`, `Notification`, `LiveAudience` and `IBrandDirectory`.
- Produces:
  - `FeedFetcher.FetchAsync(string url, ct) → Task<FetchedFeed>`, where `FetchedFeed(byte[]? Body, string? ErrorCode, string? ErrorMessage)`.
  - `FeedUrls.Clean(string?) → string?` and `FeedUrls.BadUrl`.
  - `FeedRefresher`:
    - `RefreshAsync(string feedUrl, Guid? startedBy, ct) → Task<CatalogueImport?>`, which returns null when the lock is busy.
    - `AlertAfter = 3`, plus the alert title and body.
    - It needs a transaction, like any lock.
  - `SourceView.Of(CatalogueSource)`.
  - `FeedSetView(SourceView Source, ImportView Import)`.

- [ ] **Step 1: Write the failing feed tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/FeedTests.cs`:

```csharp
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Notifications;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class FeedTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string FeedUrl = "https://shop.acme.example/feeds/products.csv";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ConnectionString => sqlServer.KairoConnectionString;

    [Fact]
    public async Task Setting_a_feed_imports_it_and_saves_the_source()
    {
        var (admin, brandId) = await WorkspaceAsync();
        Serve(Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        var source = body.GetProperty("source");
        Assert.Equal("feed", source.GetProperty("kind").GetString());
        Assert.Equal(FeedUrl, source.GetProperty("feedUrl").GetString());
        Assert.Equal(admin.UserId, source.GetProperty("setBy").GetGuid());
        Assert.Equal(0, source.GetProperty("failuresInRow").GetInt32());
        Assert.Equal(_api.Clock.GetUtcNow(), source.GetProperty("lastSucceededAt").GetDateTimeOffset());
        var import = body.GetProperty("import");
        Assert.Equal("feed", import.GetProperty("kind").GetString());
        Assert.Equal(FeedUrl, import.GetProperty("feedUrl").GetString());
        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(2, import.GetProperty("added").GetInt32());
        var saved = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal(FeedUrl, saved.GetProperty("feedUrl").GetString());
        Assert.Contains("catalogue.feed_set", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task A_workspace_with_no_source_says_so()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync(Source(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("shop.acme.example/feed.csv")]
    [InlineData("ftp://shop.acme.example/feed.csv")]
    [InlineData("https://shop.acme.example:8443/feed.csv")]
    [InlineData("https://user:secret@shop.acme.example/feed.csv")]
    public async Task A_feed_address_that_isnt_a_public_web_address_is_refused(string url)
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url }, Token);

        Assert.Equal(new[] { "Use a full web address starting with http:// or https://." }, await Catalogues.ErrorsAsync(response, "url"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    [Fact]
    public async Task A_feed_that_cant_be_fetched_is_refused_and_not_saved()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);

        Assert.Equal(new[] { "The feed address answered with an error." }, await Catalogues.ErrorsAsync(response, "url"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Empty(await CatalogueRows.ImportsAsync(ConnectionString, brandId));
    }

    [Fact]
    public async Task A_feed_that_isnt_a_product_file_is_refused_and_not_saved()
    {
        var (admin, brandId) = await WorkspaceAsync();
        _api.Websites.Html(FeedUrl, "<!doctype html><html><body>Acme</body></html>");

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);

        Assert.Equal(
            new[] { "Kairo couldn't read this as a product file. Use CSV, TSV or XML in Google's Merchant Center format." },
            await Catalogues.ErrorsAsync(response, "url"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    [Fact]
    public async Task A_gzipped_xml_feed_is_imported()
    {
        var (admin, brandId) = await WorkspaceAsync();
        const string rss = """
            <rss version="2.0" xmlns:g="http://base.google.com/ns/1.0"><channel>
              <item><g:id>SKU-1</g:id><title>Runner</title><g:price>1499 INR</g:price></item>
              <item><g:id>SKU-2</g:id><title>Walker</title><g:price>999 INR</g:price></item>
            </channel></rss>
            """;
        _api.Websites.File("https://shop.acme.example/feeds/products.xml.gz", "application/gzip", Gzip(rss));

        var response = await admin.Browser.PutAsJsonAsync(Feed(brandId), new { url = "https://shop.acme.example/feeds/products.xml.gz" }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("import").GetProperty("added").GetInt32());
        Assert.Equal(new[] { "Runner", "Walker" }, (await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Select(p => p.Title).ToArray());
    }

    [Fact]
    public async Task Refreshing_imports_the_feed_again()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        Serve(Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var response = await admin.Browser.PostAsync(Refresh(brandId), null, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var import = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("succeeded", import.GetProperty("status").GetString());
        Assert.Equal(1, import.GetProperty("added").GetInt32());
        Assert.Equal(admin.UserId, import.GetProperty("startedBy").GetGuid());
        Assert.Contains("catalogue.feed_refreshed", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Refreshing_without_a_feed_is_a_conflict()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var response = await admin.Browser.PostAsync(Refresh(brandId), null, Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("There's no feed link to refresh.", await Catalogues.ProblemTitleAsync(response));
    }

    [Fact]
    public async Task Two_refreshes_at_once_import_once()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        _api.Websites.Slow(FeedUrl, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")), TimeSpan.FromSeconds(2));
        var other = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "admin");

        var responses = await Task.WhenAll(
            admin.Browser.PostAsync(Refresh(brandId), null, Token),
            other.Browser.PostAsync(Refresh(brandId), null, Token));

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(2, (await CatalogueRows.ImportsAsync(ConnectionString, brandId)).Count);
    }

    [Fact]
    public async Task Removing_the_feed_keeps_the_products()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        var response = await admin.Browser.DeleteAsync(Feed(brandId), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.GetAsync(Source(brandId), Token)).StatusCode);
        Assert.Equal("active", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Status);
        Assert.Contains("catalogue.feed_removed", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Uploading_a_file_replaces_the_feed()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));

        var source = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal("file", source.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, source.GetProperty("feedUrl").ValueKind);
    }

    [Fact]
    public async Task Three_failed_refreshes_in_a_row_alert_the_admins_once()
    {
        var (admin, brandId, handle) = await WorkspaceWithHandleAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        _api.Websites.Status(FeedUrl, HttpStatusCode.InternalServerError);

        var statuses = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var import = await (await admin.Browser.PostAsync(Refresh(brandId), null, Token)).Content.ReadFromJsonAsync<JsonElement>(Token);
            statuses.Add(import.GetProperty("status").GetString()!);
        }

        Assert.All(statuses, status => Assert.Equal("failed", status));
        var source = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal(4, source.GetProperty("failuresInRow").GetInt32());
        var alert = Assert.Single(await NotificationRows.NotificationsAsync(ConnectionString, brandId), row => row.Kind == "connection.problem");
        Assert.Equal(admin.UserId, alert.UserId);
        Assert.Equal("Your product feed isn't refreshing", alert.Title);
        Assert.Equal("Kairo couldn't refresh your product feed 3 times in a row.", alert.Body);
        Assert.Equal($"/{handle}/brand-kit/catalogue", alert.Link);
    }

    [Fact]
    public async Task A_successful_refresh_clears_the_failures()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        _api.Websites.Status(FeedUrl, HttpStatusCode.InternalServerError);
        await admin.Browser.PostAsync(Refresh(brandId), null, Token);
        await admin.Browser.PostAsync(Refresh(brandId), null, Token);
        Serve(Catalogues.Csv(Catalogues.Row("SKU-1")));

        var import = await (await admin.Browser.PostAsync(Refresh(brandId), null, Token)).Content.ReadFromJsonAsync<JsonElement>(Token);

        Assert.Equal("unchanged", import.GetProperty("status").GetString());
        var source = await admin.Browser.GetFromJsonAsync<JsonElement>(Source(brandId), Token);
        Assert.Equal(0, source.GetProperty("failuresInRow").GetInt32());
    }

    [Theory]
    [InlineData("marketer")]
    [InlineData("approver")]
    public async Task Only_admins_manage_the_feed(string role)
    {
        var (admin, brandId) = await WorkspaceAsync();
        await SetFeedAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.Browser.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Browser.PostAsync(Refresh(brandId), null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Browser.DeleteAsync(Feed(brandId), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.Browser.GetAsync(Source(brandId), Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Feed(Guid brandId) => $"{Catalogues.Base(brandId)}/feed";

    private static string Refresh(Guid brandId) => $"{Catalogues.Base(brandId)}/feed/refresh";

    private static string Source(Guid brandId) => $"{Catalogues.Base(brandId)}/source";

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private void Serve(string csv) => _api.Websites.Text(FeedUrl, "text/csv", csv);

    private async Task SetFeedAsync(HttpClient admin, Guid brandId, string csv)
    {
        Serve(csv);
        var response = await admin.PutAsJsonAsync(Feed(brandId), new { url = FeedUrl }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var (admin, brandId, _) = await WorkspaceWithHandleAsync();
        return (admin, brandId);
    }

    private async Task<(Member Admin, Guid BrandId, string Handle)> WorkspaceWithHandleAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        return (admin, await Workspaces.CreateAsync(admin.Browser, handle), handle);
    }
}
```

- [ ] **Step 2: Run the feed tests to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Catalogue.FeedTests"`
Expected: 19 test cases, all failed. The feed endpoints don't exist yet, so they answer `404` or `405`, and the first `Assert.Equal` fails in each.

- [ ] **Step 3: Write the fetcher and the address check**

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/FeedFetcher.cs`:

```csharp
using Kairo.Modules.Catalogue.Format;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record FetchedFeed(byte[]? Body, string? ErrorCode, string? ErrorMessage);

internal static class FeedUrls
{
    public const int MaxLength = 2000;
    public const string BadUrl = "Use a full web address starting with http:// or https://.";

    public static string? Clean(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && SafeWebClient.IsAllowed(uri) ? uri.AbsoluteUri : null;
    }
}

internal sealed class FeedFetcher(SafeWebClient web)
{
    public static readonly SafeFetchOptions Options = new(CatalogueFile.MaxBytes, TimeSpan.FromSeconds(120));

    public async Task<FetchedFeed> FetchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await web.FetchAsync(new Uri(url), Options, cancellationToken);
            return new FetchedFeed(response.Body, null, null);
        }
        catch (SafeFetchException exception)
        {
            return new FetchedFeed(null, exception.Code, MessageFor(exception.Code));
        }
    }

    public static string MessageFor(string code) => code switch
    {
        SafeFetchErrors.TooLarge => "The feed is larger than 50 MB.",
        SafeFetchErrors.Timeout => "The feed didn't finish downloading within 120 seconds.",
        SafeFetchErrors.BlockedAddress => "Kairo can't read this address. Use a public http or https link.",
        SafeFetchErrors.BadStatus => "The feed address answered with an error.",
        _ => "Kairo couldn't reach this address.",
    };
}
```

- [ ] **Step 4: Write the refresher**

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/FeedRefresher.cs`:

```csharp
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed class FeedRefresher(
    CatalogueDbContext db,
    CatalogueImporter importer,
    FeedFetcher fetcher,
    INotifications notifications,
    IBrandDirectory brands,
    TimeProvider time)
{
    public const int AlertAfter = 3;
    public const string AlertKind = "connection.problem";
    public const string AlertTitle = "Your product feed isn't refreshing";
    public const string AlertBody = "Kairo couldn't refresh your product feed 3 times in a row.";

    public async Task<CatalogueImport?> RefreshAsync(string feedUrl, Guid? startedBy, CancellationToken cancellationToken)
    {
        if (!await importer.TryLockAsync(cancellationToken))
        {
            return null;
        }

        var input = new ImportInput(SourceKinds.Feed, null, feedUrl, startedBy);
        var fetched = await fetcher.FetchAsync(feedUrl, cancellationToken);
        var import = fetched.Body is { } body
            ? await importer.ImportAsync(input, body, cancellationToken)
            : await importer.FailAsync(input, fetched.ErrorCode!, fetched.ErrorMessage!, cancellationToken);
        await RecordAsync(import, cancellationToken);
        return import;
    }

    public static bool Succeeded(CatalogueImport import) =>
        import.Status is ImportStatuses.Succeeded or ImportStatuses.Unchanged;

    private async Task RecordAsync(CatalogueImport import, CancellationToken cancellationToken)
    {
        if (await db.Sources.SingleOrDefaultAsync(cancellationToken) is not { Kind: SourceKinds.Feed } source)
        {
            return;
        }

        var now = time.GetUtcNow();
        source.LastCheckedAt = now;
        if (Succeeded(import))
        {
            source.LastSucceededAt = now;
            source.FailuresInRow = 0;
        }
        else
        {
            source.FailuresInRow++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (source.FailuresInRow == AlertAfter)
        {
            var brand = (await brands.GetAsync([source.BrandId], cancellationToken)).Single();
            await notifications.NotifyAsync(
                new Notification(AlertKind, LiveAudience.Roles(BrandRoles.Admin), AlertTitle, AlertBody, $"/{brand.Handle}/brand-kit/catalogue"),
                cancellationToken);
        }
    }
}
```

The refresher loads the source **after** the import, because `ImportAsync` clears the change tracker as it saves.

- [ ] **Step 5: Write the source view and endpoints**

Add to the end of `core/src/Modules/Kairo.Modules.Catalogue/Imports/ImportViews.cs`:

```csharp

internal sealed record SourceView(
    string Kind,
    string? FeedUrl,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSucceededAt,
    int FailuresInRow)
{
    public static SourceView Of(CatalogueSource source) => new(
        source.Kind,
        source.FeedUrl,
        source.SetBy,
        source.SetAt,
        source.LastCheckedAt,
        source.LastSucceededAt,
        source.FailuresInRow);
}

internal sealed record FeedSetView(SourceView Source, ImportView Import);
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Imports/SourceEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record FeedRequest(string? Url);

internal static class SourceEndpoints
{
    public const string AuditTarget = "catalogue_source";
    public const string NoFeed = "There's no feed link to refresh.";

    public static async Task<Results<Ok<SourceView>, NoContent>> GetAsync(CatalogueDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Sources.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is { } source)
        {
            return TypedResults.Ok(SourceView.Of(source));
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<FeedSetView>, ValidationProblem, ProblemHttpResult>> SetFeedAsync(
        FeedRequest request,
        CatalogueDbContext db,
        CatalogueImporter importer,
        FeedFetcher fetcher,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (FeedUrls.Clean(request.Url) is not { } url)
        {
            return UrlProblem(FeedUrls.BadUrl);
        }

        if (!await importer.TryLockAsync(cancellationToken))
        {
            return ImportEndpoints.BusyProblem();
        }

        var fetched = await fetcher.FetchAsync(url, cancellationToken);
        if (fetched.Body is not { } body)
        {
            return UrlProblem(fetched.ErrorMessage!);
        }

        var import = await importer.ImportAsync(new ImportInput(SourceKinds.Feed, null, url, member.UserId), body, cancellationToken);
        if (import.Status == ImportStatuses.Failed)
        {
            return UrlProblem(import.ErrorMessage!);
        }

        var source = await db.Sources.SingleOrDefaultAsync(cancellationToken);
        var before = source is null ? null : SourceView.Of(source);
        if (source is null)
        {
            source = new CatalogueSource { BrandId = member.BrandId };
            db.Sources.Add(source);
        }

        var now = time.GetUtcNow();
        var succeeded = FeedRefresher.Succeeded(import);
        source.Kind = SourceKinds.Feed;
        source.FeedUrl = url;
        source.SetBy = member.UserId;
        source.SetAt = now;
        source.LastCheckedAt = now;
        source.LastSucceededAt = succeeded ? now : null;
        source.FailuresInRow = succeeded ? 0 : 1;
        await db.SaveChangesAsync(cancellationToken);

        var after = SourceView.Of(source);
        await audit.RecordAsync(new AuditRecord("catalogue.feed_set", AuditTarget, member.BrandId.ToString(), before, after), cancellationToken);
        return TypedResults.Ok(new FeedSetView(after, ImportView.Of(import)));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> RemoveFeedAsync(
        CatalogueDbContext db,
        CatalogueImporter importer,
        CurrentMember member,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (!await importer.TryLockAsync(cancellationToken))
        {
            return ImportEndpoints.BusyProblem();
        }

        if (await db.Sources.SingleOrDefaultAsync(cancellationToken) is { Kind: SourceKinds.Feed } source)
        {
            var before = SourceView.Of(source);
            db.Sources.Remove(source);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditRecord("catalogue.feed_removed", AuditTarget, member.BrandId.ToString(), before), cancellationToken);
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<ImportView>, ProblemHttpResult>> RefreshAsync(
        CatalogueDbContext db,
        FeedRefresher refresher,
        CurrentMember member,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (await db.Sources.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { Kind: SourceKinds.Feed, FeedUrl: { } url })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: NoFeed);
        }

        if (await refresher.RefreshAsync(url, member.UserId, cancellationToken) is not { } import)
        {
            return ImportEndpoints.BusyProblem();
        }

        var view = ImportView.Of(import);
        await audit.RecordAsync(new AuditRecord("catalogue.feed_refreshed", ImportEndpoints.AuditTarget, import.Id.ToString(), After: view), cancellationToken);
        return TypedResults.Ok(view);
    }

    private static ValidationProblem UrlProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [message] });
}
```

- [ ] **Step 6: Register and map them**

In `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`, add to `AddCatalogue`:

```csharp
        services.AddScoped<FeedFetcher>();
        services.AddScoped<FeedRefresher>();
```

and to the end of `MapCatalogue`:

```csharp

        catalogue.MapGet("/source", SourceEndpoints.GetAsync);
        var feed = catalogue.MapGroup("/feed").RequireBrandRole(BrandRoles.Admin);
        feed.MapPut("", SourceEndpoints.SetFeedAsync);
        feed.MapDelete("", SourceEndpoints.RemoveFeedAsync);
        feed.MapPost("/refresh", SourceEndpoints.RefreshAsync);
```

- [ ] **Step 7: Run the feed tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Catalogue.FeedTests"`
Expected: 19 passed.

- [ ] **Step 8: Run all the catalogue tests**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"`
Expected: all passed.

- [ ] **Step 9: Regenerate the API contract**

Run the three commands from Task 3, Step 10.
Expected: the last run passes, and the contract gains `/catalogue/source`, `/catalogue/feed` and `/catalogue/feed/refresh`.

- [ ] **Step 10: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Catalogue core/tests/Kairo.IntegrationTests/Catalogue/FeedTests.cs core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Import the catalogue from a feed link and alert when it keeps failing"
```

### Task 5: Browsing products and fixing missing details

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFields.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductViews.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFixRequest.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductEndpoints.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Products/SummaryEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueLiveEvents.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/ProductBrowseTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/ProductFixTests.cs`

**Interfaces:**
- Consumes:
  - From Task 1: `Product`, `ProductFixes`, `MissingDetails`, `ProductStatuses`, `Availabilities` and `StockEvent`.
  - From Task 2: `ProductValues` (limits, `IsWebAddress`, `MaxAmount`).
  - From Task 3: `Product.RefreshMissing()`, `CatalogueImporter.TryLockAsync`, `ImportEndpoints.BusyProblem()` and `ImportView`.
  - From Task 4: `SourceView` and `FeedUrls.BadUrl`.
- Produces:
  - `ProductFields`: field names, `Fixed()`, `Missing()`, `ParseMissing()` and `Plain(decimal?)`.
  - Views: `ProductView`, `FixesView`, `StockEventView`, `ProductDetailView`, `ProductPage` and `CatalogueSummary`.
  - `CatalogueLiveEvents.ProductChanged(Guid)`.

- [ ] **Step 1: Write the failing browse tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/ProductBrowseTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class ProductBrowseTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_active_products_by_title_a_page_at_a_time()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(
            Catalogues.Row("SKU-1", title: "Echo"),
            Catalogues.Row("SKU-2", title: "Alpha"),
            Catalogues.Row("SKU-3", title: "Delta"),
            Catalogues.Row("SKU-4", title: "Bravo"),
            Catalogues.Row("SKU-5", title: "Charlie")));

        var page = await ListAsync(admin.Browser, brandId, "?page=2&pageSize=2");

        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, page.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Charlie", "Delta" }, page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToArray());
    }

    [Fact]
    public async Task A_product_shows_its_details_with_plain_prices()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", price: "\"1,499.50 INR\"")));

        var item = Assert.Single((await ListAsync(admin.Browser, brandId)).GetProperty("items").EnumerateArray());

        Assert.Equal("SKU-1", item.GetProperty("offerId").GetString());
        Assert.Equal("Runner SKU-1", item.GetProperty("title").GetString());
        Assert.Equal("https://acme.example/p/SKU-1", item.GetProperty("link").GetString());
        Assert.Equal("https://acme.example/i/SKU-1.jpg", item.GetProperty("imageLink").GetString());
        Assert.Equal("1499.5", item.GetProperty("price").GetRawText());
        Assert.Equal("INR", item.GetProperty("currency").GetString());
        Assert.Equal("in_stock", item.GetProperty("availability").GetString());
        Assert.Equal("Shoes", item.GetProperty("category").GetString());
        Assert.Empty(item.GetProperty("fixed").EnumerateArray());
        Assert.Empty(item.GetProperty("missing").EnumerateArray());
        Assert.Equal("active", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Filters_by_search_category_availability_and_missing_details()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(
            Catalogues.Row("SKU-1", title: "Trail Runner"),
            Catalogues.Row("SKU-2", "out_of_stock", title: "Road Runner"),
            Catalogues.Row("SKU-3", title: "Wool Socks", category: "Socks"),
            "SKU-4,,https://acme.example/p/SKU-4,,,,Socks\n"));

        Assert.Equal(new[] { "SKU-2", "SKU-1" }, await OfferIdsAsync(admin.Browser, brandId, "?search=runner"));
        Assert.Equal(new[] { "SKU-3" }, await OfferIdsAsync(admin.Browser, brandId, "?search=SKU-3"));
        Assert.Equal(new[] { "SKU-4", "SKU-3" }, await OfferIdsAsync(admin.Browser, brandId, "?category=Socks"));
        Assert.Equal(new[] { "SKU-2" }, await OfferIdsAsync(admin.Browser, brandId, "?availability=out_of_stock"));
        Assert.Equal(new[] { "SKU-4" }, await OfferIdsAsync(admin.Browser, brandId, "?missing=any"));
        Assert.Equal(new[] { "SKU-4" }, await OfferIdsAsync(admin.Browser, brandId, "?missing=stock"));
        Assert.Empty(await OfferIdsAsync(admin.Browser, brandId, "?missing=link"));
        var incomplete = Assert.Single((await ListAsync(admin.Browser, brandId, "?missing=image")).GetProperty("items").EnumerateArray());
        Assert.Equal(
            new[] { "title", "price", "image", "stock" },
            incomplete.GetProperty("missing").EnumerateArray().Select(name => name.GetString()).ToArray());
    }

    [Fact]
    public async Task Lists_removed_products_on_request()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2")));
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));

        Assert.Equal(new[] { "SKU-2" }, await OfferIdsAsync(admin.Browser, brandId, "?status=removed"));
        Assert.Equal(new[] { "SKU-1" }, await OfferIdsAsync(admin.Browser, brandId));
    }

    [Theory]
    [InlineData("?page=0", "page", "Use a page number from 1.")]
    [InlineData("?pageSize=201", "pageSize", "Use a page size from 1 to 200.")]
    [InlineData("?status=gone", "status", "Use active or removed.")]
    [InlineData("?availability=maybe", "availability", "Use in_stock, out_of_stock, preorder or backorder.")]
    [InlineData("?missing=colour", "missing", "Use any, title, price, link, image or stock.")]
    public async Task Refuses_a_bad_filter(string query, string field, string message)
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync(Products(brandId) + query, Token);

        Assert.Equal(new[] { message }, await Catalogues.ErrorsAsync(response, field));
    }

    [Fact]
    public async Task Shows_one_product_with_its_fixes_and_stock_events()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1", "out_of_stock")));
        var productId = Assert.Single((await ListAsync(admin.Browser, brandId)).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();

        var detail = await admin.Browser.GetFromJsonAsync<JsonElement>($"{Products(brandId)}/{productId}", Token);

        Assert.Equal("SKU-1", detail.GetProperty("product").GetProperty("offerId").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("fixes").GetProperty("title").ValueKind);
        var stock = Assert.Single(detail.GetProperty("stockEvents").EnumerateArray());
        Assert.Equal("in_stock", stock.GetProperty("from").GetString());
        Assert.Equal("out_of_stock", stock.GetProperty("to").GetString());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.GetAsync($"{Products(brandId)}/{Guid.NewGuid()}", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Summarises_the_catalogue()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var products = new[]
        {
            Catalogues.Row("SKU-1"),
            Catalogues.Row("SKU-2", "out_of_stock"),
            Catalogues.Row("SKU-3", "preorder", category: "Socks"),
            "SKU-4,,https://acme.example/p/SKU-4,,,,Socks\n",
        };
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv([.. products, Catalogues.Row("SKU-5")]));
        var last = await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(products));

        var summary = await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/summary", Token);

        Assert.Equal(5, summary.GetProperty("products").GetInt32());
        Assert.Equal(4, summary.GetProperty("active").GetInt32());
        Assert.Equal(1, summary.GetProperty("inStock").GetInt32());
        Assert.Equal(1, summary.GetProperty("outOfStock").GetInt32());
        var missing = summary.GetProperty("missing");
        Assert.Equal(
            new[] { 1, 1, 0, 1, 1, 1 },
            new[] { "title", "price", "link", "image", "stock", "any" }.Select(name => missing.GetProperty(name).GetInt32()).ToArray());
        Assert.Equal(
            new[] { "Shoes 2", "Socks 2" },
            summary.GetProperty("categories").EnumerateArray().Select(c => $"{c.GetProperty("name").GetString()} {c.GetProperty("products").GetInt32()}").ToArray());
        Assert.Equal(last.GetProperty("id").GetGuid(), summary.GetProperty("lastImport").GetProperty("id").GetGuid());
        Assert.Equal("file", summary.GetProperty("source").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task An_empty_catalogue_has_an_empty_summary()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var summary = await admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/summary", Token);

        Assert.Equal(0, summary.GetProperty("products").GetInt32());
        Assert.Empty(summary.GetProperty("categories").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("lastImport").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("source").ValueKind);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_products()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Catalogues.Row("SKU-1")));
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(Products(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Products(Guid brandId) => $"{Catalogues.Base(brandId)}/products";

    private static async Task<JsonElement> ListAsync(HttpClient browser, Guid brandId, string query = "") =>
        await browser.GetFromJsonAsync<JsonElement>(Products(brandId) + query, Token);

    private static async Task<string[]> OfferIdsAsync(HttpClient browser, Guid brandId, string query = "") =>
        (await ListAsync(browser, brandId, query)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("offerId").GetString()!).ToArray();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

`Catalogues.Row` writes the price into a CSV field, so a price with a comma (`1,499.50 INR`) is passed in quotes.

- [ ] **Step 2: Write the failing fix tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/ProductFixTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class ProductFixTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string Untitled = "SKU-1,,https://acme.example/p/SKU-1,,,in_stock,\n";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ConnectionString => sqlServer.KairoConnectionString;

    [Fact]
    public async Task A_fix_fills_missing_details()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var response = await admin.Browser.PatchAsJsonAsync(
            Fixes(brandId, productId),
            new { title = "Trail Runner", imageLink = "https://acme.example/i/1.jpg", category = "Shoes" },
            Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("Trail Runner", product.GetProperty("title").GetString());
        Assert.Equal("https://acme.example/i/1.jpg", product.GetProperty("imageLink").GetString());
        Assert.Equal("Shoes", product.GetProperty("category").GetString());
        Assert.Equal(new[] { "title", "imageLink", "category" }, Names(product, "fixed"));
        Assert.Equal(new[] { "price" }, Names(product, "missing"));
        Assert.Contains("catalogue.product_fixed", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
        var published = Assert.Single(await OutboxRows.ReadAsync(ConnectionString, brandId), row => row.Type == "catalogue.product_changed");
        Assert.Equal(productId, JsonDocument.Parse(published.Payload).RootElement.GetProperty("productId").GetGuid());
    }

    [Fact]
    public async Task A_price_fix_uses_the_brands_currency_unless_given()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var inBrandCurrency = await FixAsync(admin.Browser, brandId, productId, new { price = 1299.5m });
        var inDollars = await FixAsync(admin.Browser, brandId, productId, new { price = 15, currency = "usd" });

        Assert.Equal(1299.5m, inBrandCurrency.GetProperty("price").GetDecimal());
        Assert.Equal("INR", inBrandCurrency.GetProperty("currency").GetString());
        Assert.Equal(15m, inDollars.GetProperty("price").GetDecimal());
        Assert.Equal("USD", inDollars.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_fix_lasts_while_the_import_leaves_the_field_empty()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        await FixAsync(admin.Browser, brandId, productId, new { title = "Trail Runner" });

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Untitled.Replace("in_stock", "out_of_stock")));

        var product = await GetAsync(admin.Browser, brandId, productId);
        Assert.Equal("Trail Runner", product.GetProperty("title").GetString());
        Assert.Equal(new[] { "title" }, Names(product, "fixed"));
    }

    [Fact]
    public async Task A_fix_is_dropped_once_the_import_supplies_the_field()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        await FixAsync(admin.Browser, brandId, productId, new { title = "Trail Runner" });

        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(Untitled.Replace("SKU-1,,", "SKU-1,Runner Pro,")));

        var product = await GetAsync(admin.Browser, brandId, productId);
        Assert.Equal("Runner Pro", product.GetProperty("title").GetString());
        Assert.Empty(Names(product, "fixed"));
        Assert.DoesNotContain("Trail Runner", Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Fixes);
    }

    [Fact]
    public async Task Null_clears_a_fix()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        await FixAsync(admin.Browser, brandId, productId, new { title = "Trail Runner" });

        var product = await FixAsync(admin.Browser, brandId, productId, new { title = (string?)null });

        Assert.Equal(JsonValueKind.Null, product.GetProperty("title").ValueKind);
        Assert.Empty(Names(product, "fixed"));
        Assert.Contains("title", Names(product, "missing"));
    }

    [Fact]
    public async Task A_field_the_import_supplies_cant_be_fixed()
    {
        var (admin, brandId, productId) = await ProductAsync(Catalogues.Row("SKU-1"));

        var response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = "Other", imageLink = "https://acme.example/i/2.jpg" }, Token);

        Assert.Equal(new[] { "The import already supplies the title, so it can't be fixed here." }, await Catalogues.ErrorsAsync(response, "title"));
        Assert.Equal(new[] { "The import already supplies the image link, so it can't be fixed here." }, await Catalogues.ErrorsAsync(response, "imageLink"));
    }

    [Theory]
    [InlineData("{\"stock\":\"in_stock\"}", "stock", "Stock can't be fixed here. Change it in the file or feed.")]
    [InlineData("{\"availability\":\"in_stock\"}", "availability", "Stock can't be fixed here. Change it in the file or feed.")]
    [InlineData("{\"colour\":\"red\"}", "colour", "Kairo can't fix this field.")]
    [InlineData("{\"title\":5}", "title", "Use a title of up to 150 characters.")]
    [InlineData("{\"price\":-1}", "price", "Use a price above 0.")]
    [InlineData("{\"price\":\"cheap\"}", "price", "Use a price above 0.")]
    [InlineData("{\"price\":10,\"currency\":\"rupees\"}", "currency", "Use a three-letter currency code, such as INR.")]
    [InlineData("{\"currency\":\"INR\"}", "currency", "Send the currency with a price.")]
    [InlineData("{\"link\":\"acme.example/p/1\"}", "link", "Use a full web address starting with http:// or https://.")]
    public async Task Refuses_a_bad_fix(string json, string field, string message)
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        using var request = new HttpRequestMessage(HttpMethod.Patch, Fixes(brandId, productId))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        var response = await admin.Browser.SendAsync(request, Token);

        Assert.Equal(new[] { message }, await Catalogues.ErrorsAsync(response, field));
        Assert.Empty(Names(await GetAsync(admin.Browser, brandId, productId), "fixed"));
    }

    [Fact]
    public async Task A_title_over_150_characters_is_refused()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        var response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = new string('t', 151) }, Token);

        Assert.Equal(new[] { "Use a title of up to 150 characters." }, await Catalogues.ErrorsAsync(response, "title"));
    }

    [Theory]
    [InlineData("marketer", HttpStatusCode.OK)]
    [InlineData("approver", HttpStatusCode.Forbidden)]
    public async Task Admins_and_marketers_can_fix_but_approvers_cannot(string role, HttpStatusCode expected)
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var response = await member.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = "Trail Runner" }, Token);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_fix_while_an_import_is_running_is_refused()
    {
        var (admin, brandId, productId) = await ProductAsync(Untitled);

        HttpResponseMessage response;
        await using (await CatalogueLocks.HoldAsync(ConnectionString, brandId))
        {
            response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, productId), new { title = "Trail Runner" }, Token);
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Kairo is already importing this catalogue. Try again in a minute.", await Catalogues.ProblemTitleAsync(response));
    }

    [Fact]
    public async Task An_unknown_product_cant_be_fixed()
    {
        var (admin, brandId, _) = await ProductAsync(Untitled);

        var response = await admin.Browser.PatchAsJsonAsync(Fixes(brandId, Guid.NewGuid()), new { title = "Trail Runner" }, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Fixes(Guid brandId, Guid productId) => $"{Catalogues.Base(brandId)}/products/{productId}/fixes";

    private static string[] Names(JsonElement product, string list) =>
        product.GetProperty(list).EnumerateArray().Select(name => name.GetString()!).ToArray();

    private static async Task<JsonElement> FixAsync(HttpClient browser, Guid brandId, Guid productId, object fixes)
    {
        var response = await browser.PatchAsJsonAsync(Fixes(brandId, productId), fixes, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private static async Task<JsonElement> GetAsync(HttpClient browser, Guid brandId, Guid productId) =>
        (await browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brandId)}/products/{productId}", Token)).GetProperty("product");

    private async Task<(Member Admin, Guid BrandId, Guid ProductId)> ProductAsync(string row)
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Catalogues.ImportAsync(admin.Browser, brandId, Catalogues.Csv(row));
        var productId = Assert.Single(await CatalogueRows.ProductsAsync(ConnectionString, brandId)).Id;
        return (admin, brandId, productId);
    }
}
```

- [ ] **Step 3: Run both classes to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"`
Expected: the new `ProductBrowseTests` and `ProductFixTests` cases fail, because `/products`, `/summary` and `/fixes` answer `404` or `405`. The earlier catalogue tests still pass.

- [ ] **Step 4: Write the field names and views**

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFields.cs`:

```csharp
using System.Globalization;

namespace Kairo.Modules.Catalogue.Products;

internal static class ProductFields
{
    public const string Title = "title";
    public const string Price = "price";
    public const string Currency = "currency";
    public const string Link = "link";
    public const string ImageLink = "imageLink";
    public const string Category = "category";
    public const string AnyMissing = "any";

    private static readonly (MissingDetails Flag, string Name)[] MissingNames =
    [
        (MissingDetails.Title, "title"),
        (MissingDetails.Price, "price"),
        (MissingDetails.Link, "link"),
        (MissingDetails.Image, "image"),
        (MissingDetails.Stock, "stock"),
    ];

    public static string[] Fixed(ProductFixes fixes)
    {
        var fields = new List<string>();
        if (fixes.Title is not null)
        {
            fields.Add(Title);
        }

        if (fixes.Price is not null)
        {
            fields.Add(Price);
        }

        if (fixes.Link is not null)
        {
            fields.Add(Link);
        }

        if (fixes.ImageLink is not null)
        {
            fields.Add(ImageLink);
        }

        if (fixes.Category is not null)
        {
            fields.Add(Category);
        }

        return [.. fields];
    }

    public static string[] Missing(MissingDetails missing) =>
        MissingNames.Where(entry => (missing & entry.Flag) != MissingDetails.None).Select(entry => entry.Name).ToArray();

    public static MissingDetails? ParseMissing(string value) =>
        value == AnyMissing
            ? MissingDetails.All
            : MissingNames.Where(entry => entry.Name == value).Select(entry => (MissingDetails?)entry.Flag).FirstOrDefault();

    public static decimal? Plain(decimal? amount) =>
        amount is { } value
            ? decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductViews.cs`:

```csharp
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal sealed record ProductView(
    Guid Id,
    string OfferId,
    string? Title,
    string? Description,
    string? Link,
    string? ImageLink,
    string[] AdditionalImageLinks,
    decimal? Price,
    decimal? SalePrice,
    string? Currency,
    string? Availability,
    string? Category,
    string? GoogleCategory,
    string? Brand,
    string? Gtin,
    string? ItemGroupId,
    string[] Fixed,
    string[] Missing,
    string Status,
    DateTimeOffset UpdatedAt)
{
    public static ProductView Of(Product product) => new(
        product.Id,
        product.OfferId,
        product.Title ?? product.Fixes.Title,
        product.Description,
        product.Link ?? product.Fixes.Link,
        product.ImageLink ?? product.Fixes.ImageLink,
        [.. product.AdditionalImageLinks],
        ProductFields.Plain(product.Price ?? product.Fixes.Price),
        ProductFields.Plain(product.SalePrice),
        product.Price is null ? product.Fixes.Currency : product.Currency,
        product.Availability,
        product.Category ?? product.Fixes.Category,
        product.GoogleCategory,
        product.Brand,
        product.Gtin,
        product.ItemGroupId,
        ProductFields.Fixed(product.Fixes),
        ProductFields.Missing(product.Missing),
        product.Status,
        product.UpdatedAt);
}

internal sealed record FixesView(string? Title, decimal? Price, string? Currency, string? Link, string? ImageLink, string? Category)
{
    public static FixesView Of(ProductFixes fixes) =>
        new(fixes.Title, ProductFields.Plain(fixes.Price), fixes.Currency, fixes.Link, fixes.ImageLink, fixes.Category);
}

internal sealed record StockEventView(string? From, string? To, Guid ImportId, DateTimeOffset At);

internal sealed record ProductDetailView(ProductView Product, FixesView Fixes, StockEventView[] StockEvents);

internal sealed record ProductPage(ProductView[] Items, int Page, int PageSize, int Total);

internal sealed record MissingCounts(int Title, int Price, int Link, int Image, int Stock, int Any);

internal sealed record CategoryCount(string Name, int Products);

internal sealed record CatalogueSummary(
    int Products,
    int Active,
    int InStock,
    int OutOfStock,
    MissingCounts Missing,
    CategoryCount[] Categories,
    ImportView? LastImport,
    SourceView? Source);
```

- [ ] **Step 5: Write the fix request rules**

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductFixRequest.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Modules.Catalogue.Format;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal static class ProductFixRequest
{
    public const string CantFixStock = "Stock can't be fixed here. Change it in the file or feed.";
    public const string UnknownField = "Kairo can't fix this field.";
    public const string BadTitle = "Use a title of up to 150 characters.";
    public const string BadPrice = "Use a price above 0.";
    public const string BadCurrency = "Use a three-letter currency code, such as INR.";
    public const string CurrencyWithoutPrice = "Send the currency with a price.";
    public const string BadCategory = "Use a category of up to 750 characters.";

    public static string Supplied(string field) =>
        $"The import already supplies the {(field == ProductFields.ImageLink ? "image link" : field)}, so it can't be fixed here.";

    public static Dictionary<string, string[]> Apply(JsonObject body, Product product, string brandCurrency)
    {
        var errors = new Dictionary<string, string[]>();
        var changes = new List<Action<ProductFixes>>();

        void Text(string field, JsonNode? node, int maxLength, string invalid, bool supplied, Action<ProductFixes, string?> set)
        {
            if (!TryReadText(node, out var text) || text?.Length > maxLength)
            {
                errors[field] = [invalid];
            }
            else if (text is not null && supplied)
            {
                errors[field] = [Supplied(field)];
            }
            else
            {
                changes.Add(fixes => set(fixes, text));
            }
        }

        void Address(string field, JsonNode? node, bool supplied, Action<ProductFixes, string?> set)
        {
            if (!TryReadText(node, out var text) || (text is not null && !ProductValues.IsWebAddress(text)))
            {
                errors[field] = [FeedUrls.BadUrl];
            }
            else if (text is not null && supplied)
            {
                errors[field] = [Supplied(field)];
            }
            else
            {
                changes.Add(fixes => set(fixes, text));
            }
        }

        void Price(JsonNode? node, JsonNode? currencyNode)
        {
            if (node is null)
            {
                changes.Add(fixes =>
                {
                    fixes.Price = null;
                    fixes.Currency = null;
                });
                return;
            }

            if (node is not JsonValue value
                || value.GetValueKind() != JsonValueKind.Number
                || !value.TryGetValue<decimal>(out var amount)
                || amount <= 0
                || amount > ProductValues.MaxAmount)
            {
                errors[ProductFields.Price] = [BadPrice];
                return;
            }

            var currency = brandCurrency;
            if (currencyNode is not null)
            {
                if (!TryReadText(currencyNode, out var code) || code is null || code.Length != 3 || !code.All(char.IsAsciiLetter))
                {
                    errors[ProductFields.Currency] = [BadCurrency];
                    return;
                }

                currency = code.ToUpperInvariant();
            }

            if (product.Price is not null)
            {
                errors[ProductFields.Price] = [Supplied(ProductFields.Price)];
                return;
            }

            var rounded = Math.Round(amount, 4, MidpointRounding.AwayFromZero);
            changes.Add(fixes =>
            {
                fixes.Price = rounded;
                fixes.Currency = currency;
            });
        }

        foreach (var (field, node) in body)
        {
            switch (field)
            {
                case ProductFields.Title:
                    Text(field, node, ProductValues.MaxTitleLength, BadTitle, product.Title is not null, (fixes, text) => fixes.Title = text);
                    break;
                case ProductFields.Link:
                    Address(field, node, product.Link is not null, (fixes, text) => fixes.Link = text);
                    break;
                case ProductFields.ImageLink:
                    Address(field, node, product.ImageLink is not null, (fixes, text) => fixes.ImageLink = text);
                    break;
                case ProductFields.Category:
                    Text(field, node, ProductValues.MaxCategoryLength, BadCategory, product.Category is not null, (fixes, text) => fixes.Category = text);
                    break;
                case ProductFields.Price:
                    Price(node, body[ProductFields.Currency]);
                    break;
                case ProductFields.Currency:
                    if (!body.ContainsKey(ProductFields.Price))
                    {
                        errors[field] = [CurrencyWithoutPrice];
                    }

                    break;
                case "stock" or "availability":
                    errors[field] = [CantFixStock];
                    break;
                default:
                    errors[field] = [UnknownField];
                    break;
            }
        }

        if (errors.Count == 0)
        {
            foreach (var change in changes)
            {
                change(product.Fixes);
            }
        }

        return errors;
    }

    private static bool TryReadText(JsonNode? node, out string? text)
    {
        text = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        var trimmed = value.GetValue<string>().Trim();
        text = trimmed.Length == 0 ? null : trimmed;
        return true;
    }
}
```

A fix changes nothing unless every field in the request is valid.

- [ ] **Step 6: Write the endpoints**

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/ProductEndpoints.cs`:

```csharp
using System.Text.Json.Nodes;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal static class ProductEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int ShownStockEvents = 20;
    public const string AuditTarget = "catalogue_product";

    public static async Task<Results<Ok<ProductPage>, ValidationProblem>> ListAsync(
        CatalogueDbContext db,
        int? page,
        int? pageSize,
        string? search,
        string? category,
        string? availability,
        string? missing,
        string? status,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = page ?? 1;
        if (pageNumber < 1)
        {
            errors["page"] = ["Use a page number from 1."];
        }

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["Use a page size from 1 to 200."];
        }

        var wanted = status ?? ProductStatuses.Active;
        if (wanted is not (ProductStatuses.Active or ProductStatuses.Removed))
        {
            errors["status"] = ["Use active or removed."];
        }

        if (availability is not null && !Availabilities.All.Contains(availability))
        {
            errors["availability"] = ["Use in_stock, out_of_stock, preorder or backorder."];
        }

        var gaps = missing is null ? null : ProductFields.ParseMissing(missing);
        if (missing is not null && gaps is null)
        {
            errors["missing"] = ["Use any, title, price, link, image or stock."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = db.Products.AsNoTracking().Where(p => p.Status == wanted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.OfferId.Contains(term) || (p.Title ?? p.Fixes.Title)!.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var name = category.Trim();
            query = query.Where(p => (p.Category ?? p.Fixes.Category) == name);
        }

        if (availability is not null)
        {
            query = query.Where(p => p.Availability == availability);
        }

        if (gaps is { } flags)
        {
            query = query.Where(p => (p.Missing & flags) != MissingDetails.None);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Title ?? p.Fixes.Title)
            .ThenBy(p => p.OfferId)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new ProductPage(items.Select(ProductView.Of).ToArray(), pageNumber, size, total));
    }

    public static async Task<Results<Ok<ProductDetailView>, NotFound>> GetAsync(
        Guid productId,
        CatalogueDbContext db,
        CancellationToken cancellationToken)
    {
        if (await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken) is not { } product)
        {
            return TypedResults.NotFound();
        }

        var events = await db.StockEvents.AsNoTracking()
            .Where(stock => stock.ProductId == productId)
            .OrderByDescending(stock => stock.At)
            .Take(ShownStockEvents)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new ProductDetailView(
            ProductView.Of(product),
            FixesView.Of(product.Fixes),
            events.Select(stock => new StockEventView(stock.From, stock.To, stock.ImportId, stock.At)).ToArray()));
    }

    public static async Task<Results<Ok<ProductView>, NotFound, ValidationProblem, ProblemHttpResult>> FixAsync(
        Guid productId,
        JsonObject body,
        CatalogueDbContext db,
        CatalogueImporter importer,
        IBrandDirectory brands,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!await importer.TryLockAsync(cancellationToken))
        {
            return ImportEndpoints.BusyProblem();
        }

        if (await db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken) is not { } product)
        {
            return TypedResults.NotFound();
        }

        var currency = (await brands.GetAsync([member.BrandId], cancellationToken)).Single().Currency;
        var before = FixesView.Of(product.Fixes);
        var errors = ProductFixRequest.Apply(body, product, currency);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        product.RefreshMissing();
        product.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var after = FixesView.Of(product.Fixes);
        await audit.RecordAsync(new AuditRecord("catalogue.product_fixed", AuditTarget, product.Id.ToString(), before, after), cancellationToken);
        await live.PublishAsync(CatalogueLiveEvents.ProductChanged(product.Id), cancellationToken);
        return TypedResults.Ok(ProductView.Of(product));
    }
}
```

Create `core/src/Modules/Kairo.Modules.Catalogue/Products/SummaryEndpoints.cs`:

```csharp
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue.Products;

internal static class SummaryEndpoints
{
    public const int ShownCategories = 100;

    public static async Task<Ok<CatalogueSummary>> GetAsync(CatalogueDbContext db, CancellationToken cancellationToken)
    {
        var all = db.Products.AsNoTracking();
        var active = all.Where(p => p.Status == ProductStatuses.Active);
        var missing = new MissingCounts(
            await CountMissingAsync(active, MissingDetails.Title, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Price, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Link, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Image, cancellationToken),
            await CountMissingAsync(active, MissingDetails.Stock, cancellationToken),
            await CountMissingAsync(active, MissingDetails.All, cancellationToken));
        var categories = await active
            .Select(p => p.Category ?? p.Fixes.Category)
            .Where(name => name != null)
            .GroupBy(name => name)
            .Select(group => new { Name = group.Key, Products = group.Count() })
            .OrderByDescending(entry => entry.Products)
            .ThenBy(entry => entry.Name)
            .Take(ShownCategories)
            .ToListAsync(cancellationToken);
        var lastImport = await db.Imports.AsNoTracking()
            .OrderByDescending(import => import.StartedAt)
            .ThenByDescending(import => import.Number)
            .FirstOrDefaultAsync(cancellationToken);
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        return TypedResults.Ok(new CatalogueSummary(
            await all.CountAsync(cancellationToken),
            await active.CountAsync(cancellationToken),
            await active.CountAsync(p => p.Availability == Availabilities.InStock, cancellationToken),
            await active.CountAsync(p => p.Availability == Availabilities.OutOfStock, cancellationToken),
            missing,
            categories.Select(entry => new CategoryCount(entry.Name!, entry.Products)).ToArray(),
            lastImport is null ? null : ImportView.Of(lastImport),
            source is null ? null : SourceView.Of(source)));
    }

    private static Task<int> CountMissingAsync(IQueryable<Product> products, MissingDetails flags, CancellationToken cancellationToken) =>
        products.CountAsync(p => (p.Missing & flags) != MissingDetails.None, cancellationToken);
}
```

If EF Core can't translate the category `GroupBy` over `Category ?? Fixes.Category` (the test fails with `could not be translated`), project the effective category with `.Select(p => p.Category ?? p.Fixes.Category).ToListAsync()` and group in memory. That's at most 50,000 short strings per brand. Record this as a ruling.

- [ ] **Step 7: Add the live event, and map the endpoints**

In `core/src/Modules/Kairo.Modules.Catalogue/CatalogueLiveEvents.cs`, add after `Imported`:

```csharp

    public static LiveEvent ProductChanged(Guid productId) =>
        new("catalogue.product_changed", new { productId }, LiveAudience.Everyone);
```

In `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`, add `using Kairo.Modules.Catalogue.Products;` at the top, and to the end of `MapCatalogue`:

```csharp

        catalogue.MapGet("/summary", SummaryEndpoints.GetAsync);
        var products = catalogue.MapGroup("/products");
        products.MapGet("", ProductEndpoints.ListAsync);
        products.MapGet("/{productId:guid}", ProductEndpoints.GetAsync);
        products.MapPatch("/{productId:guid}/fixes", ProductEndpoints.FixAsync).RequireBrandRole(BrandRoles.Admin | BrandRoles.Marketer);
```

- [ ] **Step 8: Run all the catalogue tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"`
Expected: all passed, including the 14 `ProductBrowseTests` cases and the 20 `ProductFixTests` cases.

- [ ] **Step 9: Regenerate the API contract**

Run the three commands from Task 3, Step 10.
Expected: the last run passes, and the contract gains `/catalogue/summary`, `/catalogue/products`, `/catalogue/products/{productId}` and `/catalogue/products/{productId}/fixes`.

- [ ] **Step 10: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Catalogue core/tests/Kairo.IntegrationTests/Catalogue/ProductBrowseTests.cs core/tests/Kairo.IntegrationTests/Catalogue/ProductFixTests.cs core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Browse the catalogue and fix missing product details"
```

### Task 6: The hourly refresh job

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Refresh/RefreshRunner.cs`
- Create: `core/src/Modules/Kairo.Modules.Catalogue/Refresh/RefreshJobs.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`
- Modify: `core/src/Modules/Kairo.Modules.Catalogue/CatalogueModule.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/KairoWorkerFactory.cs`
- Test: `core/tests/Kairo.IntegrationTests/Catalogue/HourlyRefreshTests.cs`

**Interfaces:**
- Consumes:
  - From Task 4: `FeedRefresher.RefreshAsync(string feedUrl, Guid? startedBy, ct)` (null when the lock is busy) and `SourceKinds`.
  - From Task 3: `CatalogueRows`, `CatalogueLocks` and `Catalogues`.
  - Existing: `KairoDatabase.UseBrandAsync`, `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync` and `EnterSystemScopeAsync`; `IsolatedDatabase.CreateAsync`; `FakeWebsites`.
- Produces:
  - `POST /jobs/catalogue/refresh?brandId=` on the worker. It answers `200` with `{ brands, results: [{ brandId, outcome, importId }] }`, where `outcome` is an import status, `skipped` or `error`.
  - In the test worker factory, `KairoWorkerFactory.Websites` and `KairoWorkerFactory.RefreshCatalogueAsync(Guid? brandId = null)`.

- [ ] **Step 1: Give the test worker fake websites**

In `core/tests/Kairo.IntegrationTests/Api/KairoWorkerFactory.cs`:
- Add `using Kairo.IntegrationTests.Web;` and `using Kairo.SharedKernel.Http;` with the other usings.
- Add this property after `AnalyticsProject`:

```csharp
    public FakeWebsites Websites { get; } = new();
```

- Add this method after `RunDailyAsync`:

```csharp
    public async Task<HttpResponseMessage> RefreshCatalogueAsync(Guid? brandId = null)
    {
        using var client = CreateClient();
        return await client.PostAsync(brandId is { } id ? $"/jobs/catalogue/refresh?brandId={id}" : "/jobs/catalogue/refresh", null);
    }
```

- Add this line inside `ConfigureTestServices`, after the `kairo-measurement-google` line:

```csharp
            services.AddHttpClient(SafeWebClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => Websites);
```

- [ ] **Step 2: Write the failing job tests**

Create `core/tests/Kairo.IntegrationTests/Catalogue/HourlyRefreshTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;
using Kairo.IntegrationTests.Notifications;

namespace Kairo.IntegrationTests.Catalogue;

public sealed class HourlyRefreshTests(SqlServer sqlServer) : IAsyncLifetime
{
    private const string FirstFeed = "https://one.acme.example/feed.csv";
    private const string SecondFeed = "https://two.acme.example/feed.csv";

    private static readonly string OneProduct = Catalogues.Csv(Catalogues.Row("SKU-1"));
    private static readonly string TwoProducts = Catalogues.Csv(Catalogues.Row("SKU-1"), Catalogues.Row("SKU-2"));

    private string _database = "";
    private KairoApiFactory _api = null!;
    private KairoWorkerFactory _worker = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await IsolatedDatabase.CreateAsync(sqlServer);
        _api = new KairoApiFactory(_database);
        _worker = new KairoWorkerFactory(_database, _api.GoogleApis, _api.Clock.GetUtcNow().AddHours(1));
    }

    [Fact]
    public async Task A_run_refreshes_every_brand_with_a_feed()
    {
        var first = await BrandWithFeedAsync(FirstFeed);
        var second = await BrandWithFeedAsync(SecondFeed);
        var fileAdmin = await Workspaces.SignInAsync(_api);
        var fileBrand = await Workspaces.CreateAsync(fileAdmin.Browser);
        await Catalogues.ImportAsync(fileAdmin.Browser, fileBrand, OneProduct);
        _worker.Websites.Text(FirstFeed, "text/csv", TwoProducts);
        _worker.Websites.Text(SecondFeed, "text/csv", TwoProducts);

        var run = await RunAsync();

        Assert.Equal(2, run.GetProperty("brands").GetInt32());
        foreach (var brandId in new[] { first.BrandId, second.BrandId })
        {
            Assert.Equal("succeeded", OutcomeOf(run, brandId));
            var imports = await CatalogueRows.ImportsAsync(_database, brandId);
            Assert.Equal(2, imports.Count);
            Assert.Null(imports[1].StartedBy);
            Assert.Equal(2, (await CatalogueRows.ProductsAsync(_database, brandId)).Count);
        }

        Assert.Single(await CatalogueRows.ImportsAsync(_database, fileBrand));
    }

    [Fact]
    public async Task A_run_for_one_brand_refreshes_only_that_brand()
    {
        var first = await BrandWithFeedAsync(FirstFeed);
        var second = await BrandWithFeedAsync(SecondFeed);
        _worker.Websites.Text(FirstFeed, "text/csv", TwoProducts);
        _worker.Websites.Text(SecondFeed, "text/csv", TwoProducts);

        var run = await RunAsync(first.BrandId);

        Assert.Equal(1, run.GetProperty("brands").GetInt32());
        Assert.Equal(2, (await CatalogueRows.ImportsAsync(_database, first.BrandId)).Count);
        Assert.Single(await CatalogueRows.ImportsAsync(_database, second.BrandId));
    }

    [Fact]
    public async Task One_brands_failure_doesnt_stop_the_others()
    {
        var first = await BrandWithFeedAsync(FirstFeed);
        var second = await BrandWithFeedAsync(SecondFeed);
        _worker.Websites.Text(SecondFeed, "text/csv", TwoProducts);

        var run = await RunAsync();

        Assert.Equal("failed", OutcomeOf(run, first.BrandId));
        Assert.Equal("succeeded", OutcomeOf(run, second.BrandId));
        Assert.Equal("bad_status", (await LatestImportAsync(first)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_brand_already_importing_is_skipped()
    {
        var brand = await BrandWithFeedAsync(FirstFeed);
        _worker.Websites.Text(FirstFeed, "text/csv", TwoProducts);

        JsonElement run;
        await using (await CatalogueLocks.HoldAsync(_database, brand.BrandId))
        {
            run = await RunAsync();
        }

        Assert.Equal("skipped", OutcomeOf(run, brand.BrandId));
        Assert.Single(await CatalogueRows.ImportsAsync(_database, brand.BrandId));
    }

    [Fact]
    public async Task Three_failed_runs_in_a_row_alert_the_admins_once_until_a_refresh_succeeds()
    {
        var brand = await BrandWithFeedAsync(FirstFeed);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("failed", OutcomeOf(await RunAsync(), brand.BrandId));
        }

        var checkedAt = _worker.Clock.GetUtcNow();
        var afterFailures = await SourceAsync(brand);
        var alertsAfterFailures = await AlertsAsync(brand.BrandId);
        _worker.Websites.Text(FirstFeed, "text/csv", OneProduct);
        var recovered = OutcomeOf(await RunAsync(), brand.BrandId);
        var afterSuccess = await SourceAsync(brand);
        _worker.Websites.Status(FirstFeed, HttpStatusCode.InternalServerError);
        for (var i = 0; i < 3; i++)
        {
            await RunAsync();
        }

        Assert.Equal(4, afterFailures.GetProperty("failuresInRow").GetInt32());
        Assert.Equal(checkedAt, afterFailures.GetProperty("lastCheckedAt").GetDateTimeOffset());
        var alert = Assert.Single(alertsAfterFailures);
        Assert.Equal(brand.Admin.UserId, alert.UserId);
        Assert.Equal("Your product feed isn't refreshing", alert.Title);
        Assert.Equal("Kairo couldn't refresh your product feed 3 times in a row.", alert.Body);
        Assert.Equal($"/{brand.Handle}/brand-kit/catalogue", alert.Link);
        Assert.Equal("unchanged", recovered);
        Assert.Equal(0, afterSuccess.GetProperty("failuresInRow").GetInt32());
        Assert.Equal(2, (await AlertsAsync(brand.BrandId)).Count);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static string OutcomeOf(JsonElement run, Guid brandId) =>
        run.GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("brandId").GetGuid() == brandId)
            .GetProperty("outcome").GetString()!;

    private async Task<JsonElement> RunAsync(Guid? brandId = null)
    {
        _worker.Clock.Advance(TimeSpan.FromSeconds(1));
        var response = await _worker.RefreshCatalogueAsync(brandId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    private async Task<List<NotificationRow>> AlertsAsync(Guid brandId) =>
        (await NotificationRows.NotificationsAsync(_database, brandId)).Where(row => row.Kind == "connection.problem").ToList();

    private static async Task<JsonElement> SourceAsync((Member Admin, Guid BrandId, string Handle) brand) =>
        await brand.Admin.Browser.GetFromJsonAsync<JsonElement>($"{Catalogues.Base(brand.BrandId)}/source", Token);

    private static async Task<JsonElement> LatestImportAsync((Member Admin, Guid BrandId, string Handle) brand) =>
        (await brand.Admin.Browser.GetFromJsonAsync<JsonElement>(Catalogues.Imports(brand.BrandId), Token))[0];

    private async Task<(Member Admin, Guid BrandId, string Handle)> BrandWithFeedAsync(string url)
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        _api.Websites.Text(url, "text/csv", OneProduct);
        var response = await admin.Browser.PutAsJsonAsync($"{Catalogues.Base(brandId)}/feed", new { url }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (admin, brandId, handle);
    }
}
```

The worker's clock starts an hour after the API's, so the worker's imports come after the API's in `StartedAt` order. Each run moves the worker's clock on by one second, as the nightly analytics tests do.

- [ ] **Step 3: Run the job tests to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Catalogue.HourlyRefreshTests"`
Expected: 5 failed. `RunAsync` gets `404` from the worker, because `/jobs/catalogue/refresh` isn't mapped.

- [ ] **Step 4: Write the runner and the job endpoint**

Create `core/src/Modules/Kairo.Modules.Catalogue/Refresh/RefreshRunner.cs`:

```csharp
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Imports;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Catalogue.Refresh;

internal sealed record BrandRefresh(Guid BrandId, string Outcome, Guid? ImportId);

internal sealed record RefreshRun(int Brands, BrandRefresh[] Results);

internal static class RefreshOutcomes
{
    public const string Skipped = "skipped";
    public const string Error = "error";
}

internal sealed class RefreshRunner(IServiceScopeFactory scopes, ILogger<RefreshRunner> logger)
{
    public const int BrandsAtOnce = 4;

    public async Task<RefreshRun> RunAsync(Guid? brandId, CancellationToken cancellationToken)
    {
        var brandIds = await FeedBrandsAsync(brandId, cancellationToken);
        var results = new BrandRefresh[brandIds.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, brandIds.Count),
            new ParallelOptions { MaxDegreeOfParallelism = BrandsAtOnce, CancellationToken = cancellationToken },
            async (index, token) => results[index] = await RefreshBrandAsync(brandIds[index], token));
        return new RefreshRun(brandIds.Count, results);
    }

    private async Task<List<Guid>> FeedBrandsAsync(Guid? brandId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await scope.ServiceProvider.GetRequiredService<CatalogueDbContext>().Sources
                .Where(source => source.Kind == SourceKinds.Feed && (brandId == null || source.BrandId == brandId))
                .Select(source => source.BrandId)
                .ToListAsync(cancellationToken);
        }
    }

    private async Task<BrandRefresh> RefreshBrandAsync(Guid brandId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, cancellationToken);
            await database.BeginTransactionAsync(cancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<CatalogueDbContext>();
            if (await db.Sources.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { Kind: SourceKinds.Feed, FeedUrl: { } url })
            {
                await database.RollbackAsync(cancellationToken);
                return new BrandRefresh(brandId, RefreshOutcomes.Skipped, null);
            }

            var import = await scope.ServiceProvider.GetRequiredService<FeedRefresher>().RefreshAsync(url, null, cancellationToken);
            await database.CommitAsync(cancellationToken);
            return import is null
                ? new BrandRefresh(brandId, RefreshOutcomes.Skipped, null)
                : new BrandRefresh(brandId, import.Status, import.Id);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The catalogue refresh failed for brand {BrandId}.", brandId);
            return new BrandRefresh(brandId, RefreshOutcomes.Error, null);
        }
    }
}
```

An exception leaves the transaction uncommitted, and disposing the scope rolls it back. So a brand that errors keeps its products, and its lock is released.

Create `core/src/Modules/Kairo.Modules.Catalogue/Refresh/RefreshJobs.cs`:

```csharp
using Microsoft.Extensions.Hosting;

namespace Kairo.Modules.Catalogue.Refresh;

internal static class RefreshJobs
{
    public static void MapCatalogueJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/catalogue/refresh", RefreshAsync);

    private static async Task<Ok<RefreshRun>> RefreshAsync(Guid? brandId, RefreshRunner runner, IHostApplicationLifetime lifetime) =>
        TypedResults.Ok(await runner.RunAsync(brandId, lifetime.ApplicationStopping));
}
```

- [ ] **Step 5: Register and map it**

In `core/src/Modules/Kairo.Modules.Catalogue/CatalogueSetup.cs`, add `using Kairo.Modules.Catalogue.Refresh;` and this line to `AddCatalogue`:

```csharp
        services.AddSingleton<RefreshRunner>();
```

In `core/src/Modules/Kairo.Modules.Catalogue/CatalogueModule.cs`, add `using Kairo.Modules.Catalogue.Refresh;`, and make `MapJobs`'s body `jobs.MapCatalogueJobs();`.

- [ ] **Step 6: Run the job tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Catalogue.HourlyRefreshTests"`
Expected: 5 passed.

- [ ] **Step 7: Run the worker host tests and every catalogue test**

Run:

```bash
dotnet test --project core/tests/Kairo.Worker.Tests
dotnet test --project core/tests/Kairo.IntegrationTests --filter-namespace "Kairo.IntegrationTests.Catalogue"
```

Expected: both pass.

- [ ] **Step 8: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Catalogue core/tests/Kairo.IntegrationTests/Api/KairoWorkerFactory.cs core/tests/Kairo.IntegrationTests/Catalogue/HourlyRefreshTests.cs
git commit -m "Refresh every catalogue feed from an hourly worker job"
```

### Task 7: Deployment and documentation

**Files:**
- Create: `deploy/Kairo.Deploy/catalogue-imports-lifecycle.json`
- Modify: `deploy/Kairo.Deploy/Setup.ps1` (`$script:FileBuckets`, `Initialize-KairoBuckets`)
- Modify: `deploy/Kairo.Deploy/Common.ps1` (scheduler job names)
- Modify: `deploy/Kairo.Deploy/Release.ps1` (`Publish-KairoSchedule`)
- Modify: `deploy/tests/Setup.Tests.ps1`
- Modify: `deploy/tests/Release.Tests.ps1`
- Modify: `docs/Kairo-SAD.md` (§2 jobs row, §6 Catalogue row, §6 file buckets paragraph)
- Modify: `README.md` (new section after "Draft a Brand Kit locally")

**Interfaces:**
- Consumes: the worker route `POST /jobs/catalogue/refresh` (Task 6) and the bucket name `catalogue-imports` (Task 3).
- Produces: `setup.ps1` creates `{project}-catalogue-imports` with a 30-day delete rule; `deploy.ps1` creates or updates `kairo-catalogue-hourly`.

- [ ] **Step 1: Write the failing Pester tests**

In `deploy/tests/Setup.Tests.ps1`, add this test right after `'creates a private bucket for brand files that only the API and the worker can use'`:

```powershell
        It 'creates a private bucket for catalogue imports that deletes files after 30 days' {
            $calls | Should -Contain 'storage buckets create gs://kairo-test-catalogue-imports --location asia-south1 --uniform-bucket-level-access --public-access-prevention --project kairo-test'
            foreach ($name in 'kairo-api', 'kairo-worker') {
                $calls | Should -Contain "storage buckets add-iam-policy-binding gs://kairo-test-catalogue-imports --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/storage.objectAdmin --project kairo-test"
            }

            $update = @($calls | Where-Object { $_ -like 'storage buckets update gs://kairo-test-catalogue-imports --lifecycle-file=* --project kairo-test' })
            $update.Count | Should -Be 1
            $file = $update[0] -replace '^.*--lifecycle-file=(\S+) .*$', '$1'
            $rules = Get-Content -Raw -Path $file | ConvertFrom-Json
            $rules.rule[0].action.type | Should -Be 'Delete'
            $rules.rule[0].condition.age | Should -Be 30
            ($calls | Where-Object { $_ -like 'storage buckets update gs://kairo-test-brand-files *' }) | Should -BeNullOrEmpty
        }
```

In `deploy/tests/Release.Tests.ps1`, add these two tests right after `'updates the nightly analytics job when it already exists'`:

```powershell
    It 'creates the hourly catalogue job when it is missing' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $calls | Should -Contain ('scheduler jobs create http kairo-catalogue-hourly --schedule 0 * * * * --time-zone Asia/Kolkata ' +
            '--uri https://kairo-worker-abc123-el.a.run.app/jobs/catalogue/refresh --http-method POST ' +
            '--oidc-service-account-email kairo-scheduler@kairo-test.iam.gserviceaccount.com ' +
            '--oidc-token-audience https://kairo-worker-abc123-el.a.run.app --attempt-deadline 1800s --max-retry-attempts 0 ' +
            '--location asia-south1 --project kairo-test')
    }

    It 'updates the hourly catalogue job when it already exists' {
        $script:scheduleExists = $true

        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        @($calls | Where-Object { $_ -like 'scheduler jobs create http kairo-catalogue-hourly *' }).Count | Should -Be 0
        @($calls | Where-Object { $_ -like 'scheduler jobs update http kairo-catalogue-hourly *' }).Count | Should -Be 1
    }
```

- [ ] **Step 2: Run the Pester tests to see them fail**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: 3 failed (the bucket test and the two catalogue job tests); every other test passes.

- [ ] **Step 3: Add the bucket and its lifecycle rule**

Create `deploy/Kairo.Deploy/catalogue-imports-lifecycle.json`:

```json
{
  "rule": [
    {
      "action": { "type": "Delete" },
      "condition": { "age": 30 }
    }
  ]
}
```

In `deploy/Kairo.Deploy/Setup.ps1`, replace `$script:FileBuckets = @('brand-files')` with:

```powershell
$script:FileBuckets = @('brand-files', 'catalogue-imports')
$script:BucketLifecycles = @{ 'catalogue-imports' = 'catalogue-imports-lifecycle.json' }
```

and in `Initialize-KairoBuckets`, add this block inside the `foreach ($name in $script:FileBuckets)` loop, after the inner `foreach ($service ...)` loop:

```powershell

        if ($script:BucketLifecycles.ContainsKey($name)) {
            $rules = Join-Path $PSScriptRoot $script:BucketLifecycles[$name]
            Invoke-Gcloud -ArgumentList (@('storage', 'buckets', 'update', $bucket, "--lifecycle-file=$rules") + $scope)
        }
```

- [ ] **Step 4: Schedule the hourly job**

In `deploy/Kairo.Deploy/Common.ps1`, add after `$script:AnalyticsJob = 'kairo-analytics-daily'`:

```powershell
$script:CatalogueJob = 'kairo-catalogue-hourly'
```

In `deploy/Kairo.Deploy/Release.ps1`, replace the whole `Publish-KairoSchedule` function with:

```powershell
function Publish-KairoSchedule {
    param(
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][string]$WorkerUrl
    )

    $jobs = @(
        @{ Name = $script:AnalyticsJob; Schedule = '0 5 * * *'; Path = '/jobs/analytics/daily' },
        @{ Name = $script:CatalogueJob; Schedule = '0 * * * *'; Path = '/jobs/catalogue/refresh' }
    )
    $scope = @('--location', $Environment.Region, '--project', $Environment.Project)
    foreach ($job in $jobs) {
        $verb = 'create'
        if (Test-Gcloud -ArgumentList (@('scheduler', 'jobs', 'describe', $job.Name) + $scope)) {
            $verb = 'update'
        }

        Write-Host "Scheduling $($job.Name)"
        Invoke-Gcloud -ArgumentList (@(
                'scheduler', 'jobs', $verb, 'http', $job.Name,
                '--schedule', $job.Schedule,
                '--time-zone', 'Asia/Kolkata',
                '--uri', "$WorkerUrl$($job.Path)",
                '--http-method', 'POST',
                '--oidc-service-account-email', (Get-KairoServiceAccount -Environment $Environment -Name 'kairo-scheduler'),
                '--oidc-token-audience', $WorkerUrl,
                '--attempt-deadline', '1800s',
                '--max-retry-attempts', '0'
            ) + $scope)
    }
}
```

- [ ] **Step 5: Run the Pester tests to see them pass**

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: all passed, 0 failed (3 more than before this task).

- [ ] **Step 6: Update the SAD**

In `docs/Kairo-SAD.md`:
- In the §2 table, change the "Jobs and events" row's middle cell to: ``Cloud Scheduler (including the nightly `kairo-analytics-daily` run and the hourly `kairo-catalogue-hourly` feed refresh), Cloud Tasks, Pub/Sub``.
- In the §6 table, replace the row `| Catalogue | Product, StockEvent, FeedImport | Current stock on Product; full history in BigQuery |` with:

```markdown
| Catalogue | Product, Source, Import, StockEvent | Current details, stock and fixes on Product; every stock change in StockEvent; each import's counts and first 50 problems in Import |
```

- In §6, change the sentence ``Separate buckets hold brand files (`{project}-brand-files`), catalogue imports, generated media and reports.`` to: ``Separate buckets hold brand files (`{project}-brand-files`), catalogue imports (`{project}-catalogue-imports`, deleted after 30 days), generated media and reports.``

- [ ] **Step 7: Document trying the catalogue locally**

In `README.md`, add this section right before `## Run everything locally`:

````markdown
## Try the catalogue locally

The catalogue takes product data in Google's Merchant Center format: CSV, TSV, or RSS and Atom XML, optionally gzipped, up to 50 MB and 50,000 products.

- **From a file.** Post it as the `file` field to `/api/brands/{brandId}/catalogue/imports`. Locally, the raw files are kept in the `kairo-files` folder in your temp folder.
- **From a feed link.** Kairo reads only public `http` and `https` addresses on the usual ports, so a file served from `localhost` is refused. To try a feed, use a file hosted somewhere public.
- **The hourly refresh.** In Cloud Run, Cloud Scheduler calls the worker every hour. Locally, call it yourself, for every brand with a feed or with `?brandId=<id>` for one:

  ```bash
  curl -X POST http://localhost:5200/jobs/catalogue/refresh
  ```

Three failed refreshes in a row send admins a "Your product feed isn't refreshing" notification.
````

- [ ] **Step 8: Run the whole core suite**

Run: `dotnet test --solution core/Kairo.slnx > .superpowers/sdd/catalogue-core-tests.log 2>&1; tail -20 .superpowers/sdd/catalogue-core-tests.log`
Expected: every test project passes, with 0 failed. If a test outside the catalogue fails, read its output. Rerun it once on its own to tell an existing flaky test from a regression, and report it by name either way.

- [ ] **Step 9: Commit**

```bash
git add deploy/Kairo.Deploy/catalogue-imports-lifecycle.json deploy/Kairo.Deploy/Setup.ps1 deploy/Kairo.Deploy/Common.ps1 deploy/Kairo.Deploy/Release.ps1 deploy/tests/Setup.Tests.ps1 deploy/tests/Release.Tests.ps1 docs/Kairo-SAD.md README.md
git commit -m "Deploy the catalogue bucket and hourly refresh, and document the catalogue"
```

---

## After the tasks: checking it live

The spec's **Done when** needs the development project:
- The demo brand's catalogue imports from a file and from a feed link.
- The hourly refresh runs.
- Missing details are listed and fixable.
- Stock changes are recorded.

Once the branch is reviewed and merged, that check needs a deploy (`deploy.bat development`) and a public feed link. Ask the user before deploying; don't mark the task `[x]` in `docs/task.md` until it's done.
