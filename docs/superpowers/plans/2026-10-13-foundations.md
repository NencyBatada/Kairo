# Kairo Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Google sign-in, workspaces, team roles, approval settings, brand-separated data and the audit log, so a person can sign in, create a workspace, invite the team, assign roles and approvers, and see every change in the audit log.

**Architecture:**
- **Core.** The .NET core gains shared database plumbing in `Kairo.SharedKernel`:
  - one SQL connection and one transaction per request, shared by every module
  - EF Core query filters, backed by SQL Server row-level security
  - filters for the `/api` and `/api/brands/{brandId}` route groups
- **Modules.**
  - Identity owns sign-in, sessions, memberships, invitations and approval settings.
  - Brands owns workspaces.
  - Audit owns the append-only audit log.
  - A new `Kairo.Migrator` applies every module's migrations.
- **Web app.** The Next.js app moves its screens under `/[workspace]`. Server components and Server Actions call the core through a typed client generated from the core's committed OpenAPI document.
- **Local stack.** A YARP gateway in Aspire puts the web app and the core on one address.

**Tech Stack:**
- **Core:** .NET 10 and ASP.NET Core 10, EF Core 10 on SQL Server 2022, OpenID Connect against Google, Aspire 13.6.1 with YARP, xUnit v3 with Testcontainers.
- **Web:** Next.js 16.4, React 19.3, `openapi-fetch` and `openapi-typescript`, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-13-foundations-design.md`

## Global Constraints

- **Shell and working directory.** Run every command in Git Bash (the Bash tool) from the repository root `/c/Kairo`, unless a step says otherwise. Paths in this plan are relative to the root.
- **Branch.** Work on `feature/foundations`. It already exists and holds the spec.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user.
  - Stage files by explicit path, never with `git add -A` or `git add .`.
  - Never stage `CLAUDE.md` or `.claude/`.
- **No comments in code** (`CLAUDE.md` §3). This means no `//`, `#`, `/* */` or `<!-- -->` comments, no C# XML doc comments, no JSDoc and no shebang lines. Generated files are cleaned up as well:
  - EF Core migrations go through `core/scripts/tidy-migrations.sh`.
  - The TypeScript API types are generated with comments removed.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build in this plan must finish with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container. Start Docker Desktop first if `docker info` fails.
- **Running tests.**
  - Whole core: `dotnet test --solution core/Kairo.slnx`.
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`.
  - Web: `npm --prefix web test`.
- **Versions.**
  - `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` and the `dotnet-ef` tool: 10.0.12
  - `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` and `Microsoft.AspNetCore.OpenApi`: 10.0.12
  - `Aspire.Hosting.Yarp`: 13.6.1
  - `Testcontainers.MsSql`: 4.16.0
  - `Microsoft.Extensions.TimeProvider.Testing`: 10.10.0
  - `openapi-typescript`: 7.13.0
  - `openapi-fetch`: 0.17.0
  - `server-only`: 0.0.1
  - SQL Server test image: `mcr.microsoft.com/mssql/server:2022-latest`, the same image Aspire uses
- **Names.**
  - Database schema per module: the module name in snake case (`identity`, `brands`, `audit`).
  - Session cookie: `__Host-kairo-session`.
  - Request header on every write: `X-Kairo-Request: 1`.
  - Session context keys: `kairo.brand_id` and `kairo.all_brands`.
  - Google callback path: `/api/auth/callback/google`.
- **Configuration.**
  - Core: `ConnectionStrings:kairo`, `Kairo:PublicOrigin`, `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret`.
  - Optional core settings: `Kairo:Sessions:IdleTimeout` (default 3 days) and `Kairo:Sessions:AbsoluteTimeout` (default 14 days).
  - Web: `KAIRO_API_URL`.
- **Module rules stay true.** Each module exposes exactly one public type, and adds no `ProjectReference` of its own. Every migration class must be made `internal` by the tidy script.

## Review Focus

These are the inputs most likely to hurt a real user. Each one is pinned by a test in the task named in brackets.

1. **A signed-in person opens another workspace's page or API path.** They must get 404 and never any of its data. (Task 6: `Someone_outside_the_workspace_gets_not_found`.)
2. **Someone accepts an invitation that was sent to a different email.** It must answer 404 and must not make them a member. (Task 8: `Someone_else_cannot_accept_an_invitation_meant_for_another_email`.)
3. **The last admin, or the only person on an approval duty, is demoted, removed or leaves.** This must be refused with a reason. Otherwise the workspace is left with nobody who can administer it or approve spend. (Task 10: `The_last_admin_cannot_be_demoted_removed_or_leave` and `The_only_person_on_a_duty_keeps_the_approver_role`.)
4. **A session cookie copied before sign-out is used again.** It must be rejected. (Task 5: `Signing_out_ends_the_session_for_every_copy_of_the_cookie`.)
5. **A sign-in link carries a return address on another site**, such as `//evil.example`, `/\evil.example` or `https://evil.example`. The user must land on Kairo's home page instead. (Task 5: `Return_addresses_outside_Kairo_fall_back_to_the_home_page`.)

## Where this plan differs from the spec

- **Separate project for database tests.** Tests that need a database live in a new `Kairo.IntegrationTests` project, not `Kairo.Api.Tests`. The existing host tests therefore still run without Docker.
- **A fake Google instead of a test-only sign-in endpoint.** Tests sign in by driving the real OpenID Connect handler against a fake Google: static metadata, a test signing key and a fake token endpoint. No test-only code ships in the API, and the real sign-in path is what gets tested.
- **The Migrator builds a `WebApplication`.** It never starts a server. The modules register ASP.NET Core authorization, which needs routing services, so a plain generic host fails dependency validation in Development.
- **Sign-in services are registered in every host.** The Identity module registers them like any other module service, but only the API adds the authentication middleware and the sign-in endpoints.
- **A 403 on a page read shows the error page, not a "no access" page.** Next.js's `forbidden()` still needs an experimental flag. Pages check roles before calling admin-only endpoints, and Server Actions show "You don't have permission to do that." for a 403.

## How the work is ordered

| Task | Delivers |
| --- | --- |
| 1 | Shared database plumbing: connection, transaction, brand separation, the integration test project |
| 2 | The `/api` request pipeline: request header, transactions, workspace membership and roles |
| 3 | `Kairo.Migrator` and the `dotnet-ef` tooling |
| 4 | Audit log storage |
| 5 | Google sign-in and sessions |
| 6 | Workspaces |
| 7 | Audit log endpoint |
| 8 | Invitations |
| 9 | Approval settings |
| 10 | Team management and the permission matrix |
| 11 | OpenAPI contract |
| 12 | Web: workspace routes, sign-in, proxy and API client |
| 13 | Web: welcome page |
| 14 | Web: workspace settings and audit log |
| 15 | Web: team and approval settings |
| 16 | Local stack: gateway, Migrator and Google sign-in |
| 17 | Documentation and CI |

---

### Task 1: Shared database plumbing

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/Kairo.slnx`
- Modify: `core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj`
- Modify: `core/src/Kairo.SharedKernel/IModule.cs`
- Modify: all 14 `core/src/Modules/Kairo.Modules.*/*Module.cs`
- Create: `core/src/Kairo.SharedKernel/KairoCoreServices.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/KairoDatabase.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/IBrandOwned.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/ModuleDbContext.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/SessionContextInterceptor.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/KairoDbContextOptions.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/BrandSeparation.cs`
- Create: `core/src/Kairo.SharedKernel/Persistence/ModuleMigrations.cs`
- Create: `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`
- Create: `core/tests/Kairo.IntegrationTests/SqlServer.cs`
- Create: `core/tests/Kairo.IntegrationTests/Persistence/PlumbingDatabase.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/TransactionTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/BrandSeparationTests.cs`

**Interfaces:**
- Consumes: `IModule` from the project-structure work.
- Produces (namespace `Kairo.SharedKernel.Persistence` unless stated):
  - `KairoDatabase` (scoped, one per request or job), which has:
    - members `SqlConnection Connection`, `Guid? BrandId`, `bool AllBrands` and `bool HasTransaction`
    - `Task UseBrandAsync(Guid brandId, CancellationToken)`
    - `Task<IAsyncDisposable> EnterSystemScopeAsync(CancellationToken)`
    - `Task BeginTransactionAsync(CancellationToken)`, `Task CommitAsync(CancellationToken)` and `Task RollbackAsync(CancellationToken)`
  - `IBrandOwned { Guid BrandId { get; set; } }`.
  - `abstract class ModuleDbContext(DbContextOptions, KairoDatabase) : DbContext`:
    - Subclasses override `string Schema` and `void ConfigureModel(ModelBuilder)`.
    - It exposes `protected Guid? CurrentBrandId` and `protected bool AllBrands` for custom filters.
    - It filters every `IBrandOwned` entity by the current brand and stamps the brand on new rows.
  - `KairoDbContextOptions`:
    - `UseKairoDatabase(this DbContextOptionsBuilder, IServiceProvider, string schema)`
    - `UseKairoDatabase(this DbContextOptionsBuilder, KairoDatabase, string schema)`
    - `CreateForDesignTime<TContext>(string schema, Func<DbContextOptions<TContext>, KairoDatabase, TContext>)`
  - `BrandSeparation`, with:
    - `CreatePredicateSql(schema)`, `AddTableSql(schema, table, column = "BrandId")`, `RemoveTableSql(schema, table)` and `DropSql(schema)`
    - `MigrationBuilder` extensions `CreateBrandAccessPredicate`, `AddBrandSeparation`, `RemoveBrandSeparation` and `DropBrandSeparation`
  - `ModuleMigrations.ApplyAsync(IServiceProvider, CancellationToken)`.
  - `Kairo.SharedKernel.KairoCoreServices.AddKairoCore(this IServiceCollection)`.
  - `IModule.MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)`.
  - Test fixture `Kairo.IntegrationTests.SqlServer` with `Task<string> CreateDatabaseAsync(string prefix)`.

- [ ] **Step 1: Add the package versions**

In `core/Directory.Packages.props`, add a new item group after the `Service defaults` group:

```xml
  <ItemGroup Label="Data">
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.12" />
  </ItemGroup>
```

In the `Tests` item group, add:

```xml
    <PackageVersion Include="Testcontainers.MsSql" Version="4.16.0" />
```

- [ ] **Step 2: Create the integration test project**

Create `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`. `core/tests/Directory.Build.props` already makes it an xUnit v3 executable and links the shared test helpers.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Testcontainers.MsSql" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

In `core/Kairo.slnx`, add the project to the `/tests/` folder, keeping the entries sorted:

```xml
    <Project Path="tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj" />
```

Create `core/tests/Kairo.IntegrationTests/SqlServer.cs`. It starts one SQL Server container for the whole test run and hands out empty databases:

```csharp
using Kairo.IntegrationTests;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServer))]

namespace Kairo.IntegrationTests;

public sealed class SqlServer : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
        return new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString;
    }
}
```

- [ ] **Step 3: Write the test database helper**

Create `core/tests/Kairo.IntegrationTests/Persistence/PlumbingDatabase.cs`. It sets up two small module-like contexts in a fresh database:
- `NotesDbContext` has the brand-owned tables `Notes` and `Pins`, both under row-level security.
- `TagsDbContext` has `Tags`, which belongs to no brand.

```csharp
using Kairo.SharedKernel;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

internal sealed class Note : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Text { get; set; } = "";
}

internal sealed class Pin : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }
}

internal sealed class Tag
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}

internal sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Pin> Pins => Set<Pin>();

    protected override string Schema => "notes";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().ToTable("Notes");
        modelBuilder.Entity<Pin>().ToTable("Pins");
    }
}

internal sealed class TagsDbContext(DbContextOptions<TagsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public DbSet<Tag> Tags => Set<Tag>();

    protected override string Schema => "tags";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Tag>().ToTable("Tags");
}

internal sealed class PlumbingDatabase(ServiceProvider services, string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<PlumbingDatabase> CreateAsync(SqlServer sqlServer, int? maxPoolSize = null)
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("plumbing");
        if (maxPoolSize is { } size)
        {
            connectionString = new SqlConnectionStringBuilder(connectionString) { MaxPoolSize = size }.ConnectionString;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kairo"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddKairoCore();
        services.AddDbContext<NotesDbContext>((provider, options) => options.UseKairoDatabase(provider, "notes"));
        services.AddDbContext<TagsDbContext>((provider, options) => options.UseKairoDatabase(provider, "tags"));

        var database = new PlumbingDatabase(services.BuildServiceProvider(), connectionString);
        await using var scope = database.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
        await notes.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await tags.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.CreatePredicateSql("notes"));
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.AddTableSql("notes", "Notes"));
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.AddTableSql("notes", "Pins"));
        return database;
    }

    public AsyncServiceScope CreateScope() => services.CreateAsyncScope();

    public async Task<Guid> AddNoteAsync(Guid brandId, string text)
    {
        await using var scope = CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await database.UseBrandAsync(brandId, CancellationToken.None);
        var note = new Note { Text = text };
        notes.Notes.Add(note);
        await notes.SaveChangesAsync();
        return note.Id;
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
```

- [ ] **Step 4: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Persistence/TransactionTests.cs`:

```csharp
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

public sealed class TransactionTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Changes_in_two_contexts_commit_together()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            notes.Notes.Add(new Note { Text = "first" });
            await notes.SaveChangesAsync(Token);
            var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
            tags.Tags.Add(new Tag { Name = "first" });
            await tags.SaveChangesAsync(Token);
            await database.CommitAsync(Token);
        }

        Assert.Equal((1, 1), await CountAsync(plumbing, brandId));
    }

    [Fact]
    public async Task Rolling_back_discards_changes_in_every_context()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            notes.Notes.Add(new Note { Text = "first" });
            await notes.SaveChangesAsync(Token);
            var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
            tags.Tags.Add(new Tag { Name = "first" });
            await tags.SaveChangesAsync(Token);
            await database.RollbackAsync(Token);
        }

        Assert.Equal((0, 0), await CountAsync(plumbing, brandId));
    }

    [Fact]
    public async Task A_context_created_during_the_transaction_joins_it()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            await database.UseBrandAsync(brandId, Token);
            await database.BeginTransactionAsync(Token);
            var tags = scope.ServiceProvider.GetRequiredService<TagsDbContext>();
            tags.Tags.Add(new Tag { Name = "late" });
            await tags.SaveChangesAsync(Token);
            await database.RollbackAsync(Token);
        }

        Assert.Equal((0, 0), await CountAsync(plumbing, brandId));
    }

    [Fact]
    public async Task A_scope_can_run_a_second_transaction_after_the_first_ends()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        await using (var scope = plumbing.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
            var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            await database.UseBrandAsync(brandId, Token);

            await database.BeginTransactionAsync(Token);
            notes.Notes.Add(new Note { Text = "discarded" });
            await notes.SaveChangesAsync(Token);
            await database.RollbackAsync(Token);
            notes.ChangeTracker.Clear();

            await database.BeginTransactionAsync(Token);
            notes.Notes.Add(new Note { Text = "kept" });
            await notes.SaveChangesAsync(Token);
            await database.CommitAsync(Token);
        }

        Assert.Equal((1, 0), await CountAsync(plumbing, brandId));
    }

    private static async Task<(int Notes, int Tags)> CountAsync(PlumbingDatabase plumbing, Guid brandId)
    {
        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        var notes = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.CountAsync(Token);
        var tags = await scope.ServiceProvider.GetRequiredService<TagsDbContext>().Tags.CountAsync(Token);
        return (notes, tags);
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Persistence/BrandSeparationTests.cs`. Each layer is tested on its own:
- Tests that use `IgnoreQueryFilters()` switch the EF Core filter off, so only row-level security is left.
- The raw SQL test bypasses EF Core entirely.

```csharp
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Persistence;

public sealed class BrandSeparationTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queries_see_only_the_current_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        await plumbing.AddNoteAsync(brandA, "a");
        await plumbing.AddNoteAsync(brandB, "b");

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandA, Token);
        var texts = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.Select(note => note.Text).ToListAsync(Token);

        Assert.Equal(["a"], texts);
    }

    [Fact]
    public async Task Row_level_security_hides_other_brands_without_the_query_filter()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        await plumbing.AddNoteAsync(brandA, "a");
        await plumbing.AddNoteAsync(brandB, "b");

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandA, Token);
        var texts = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes
            .IgnoreQueryFilters()
            .Select(note => note.Text)
            .ToListAsync(Token);

        Assert.Equal(["a"], texts);
    }

    [Fact]
    public async Task Raw_sql_sees_nothing_when_no_brand_is_set()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        await plumbing.AddNoteAsync(Guid.NewGuid(), "a");

        await using var scope = plumbing.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        var count = await notes.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [notes].[Notes]").SingleAsync(Token);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Rows_cannot_be_written_for_another_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        notes.Notes.Add(new Note { BrandId = Guid.NewGuid(), Text = "elsewhere" });

        await Assert.ThrowsAsync<DbUpdateException>(() => notes.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task New_rows_take_the_current_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var brandId = Guid.NewGuid();

        var noteId = await plumbing.AddNoteAsync(brandId, "a");

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        var note = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.SingleAsync(Token);
        Assert.Equal((noteId, brandId), (note.Id, note.BrandId));
    }

    [Fact]
    public async Task Saving_a_brand_owned_row_without_a_brand_fails()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);

        await using var scope = plumbing.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        notes.Notes.Add(new Note { Text = "orphan" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => notes.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task The_system_scope_sees_every_brand_until_it_ends()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        await plumbing.AddNoteAsync(Guid.NewGuid(), "a");
        await plumbing.AddNoteAsync(Guid.NewGuid(), "b");

        await using var scope = plumbing.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();

        int inside;
        await using (await database.EnterSystemScopeAsync(Token))
        {
            inside = await notes.Notes.CountAsync(Token);
        }

        var after = await notes.Notes.IgnoreQueryFilters().CountAsync(Token);
        Assert.Equal((2, 0), (inside, after));
    }

    [Fact]
    public async Task A_pooled_connection_does_not_keep_an_earlier_brand()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer, maxPoolSize: 1);
        var brandId = Guid.NewGuid();
        await plumbing.AddNoteAsync(brandId, "a");

        await using (var first = plumbing.CreateScope())
        {
            await first.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
            Assert.Equal(1, await first.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.CountAsync(Token));
        }

        await using var second = plumbing.CreateScope();
        var count = await second.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.IgnoreQueryFilters().CountAsync(Token);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_second_table_joins_the_existing_policy()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        foreach (var brand in new[] { brandA, brandB })
        {
            await using var writer = plumbing.CreateScope();
            await writer.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand, Token);
            var context = writer.ServiceProvider.GetRequiredService<NotesDbContext>();
            context.Pins.Add(new Pin());
            await context.SaveChangesAsync(Token);
        }

        await using var scope = plumbing.CreateScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandA, Token);
        var pins = await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Pins.IgnoreQueryFilters().ToListAsync(Token);

        Assert.Equal(brandA, Assert.Single(pins).BrandId);
    }

    [Fact]
    public async Task Removing_a_table_from_the_policy_stops_filtering_it()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        await using (var writer = plumbing.CreateScope())
        {
            await writer.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(Guid.NewGuid(), Token);
            var context = writer.ServiceProvider.GetRequiredService<NotesDbContext>();
            context.Pins.Add(new Pin());
            await context.SaveChangesAsync(Token);
        }

        await using var scope = plumbing.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await notes.Database.ExecuteSqlRawAsync(BrandSeparation.RemoveTableSql("notes", "Pins"), Token);

        Assert.Equal(1, await notes.Pins.IgnoreQueryFilters().CountAsync(Token));
    }

    [Fact]
    public async Task Switching_brand_inside_a_transaction_applies_immediately()
    {
        await using var plumbing = await PlumbingDatabase.CreateAsync(sqlServer);
        var (brandA, brandB) = (Guid.NewGuid(), Guid.NewGuid());
        await plumbing.AddNoteAsync(brandA, "a");
        await plumbing.AddNoteAsync(brandB, "b");

        await using var scope = plumbing.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<KairoDatabase>();
        var notes = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        await database.UseBrandAsync(brandA, Token);
        await database.BeginTransactionAsync(Token);
        var first = await notes.Notes.IgnoreQueryFilters().Select(note => note.Text).SingleAsync(Token);
        await database.UseBrandAsync(brandB, Token);
        var second = await notes.Notes.IgnoreQueryFilters().Select(note => note.Text).SingleAsync(Token);
        await database.RollbackAsync(Token);

        Assert.Equal(("a", "b"), (first, second));
    }
}
```

- [ ] **Step 5: Run the tests to see them fail**

Make sure Docker is running (`docker info`). Then run:

```bash
dotnet build core/tests/Kairo.IntegrationTests
```

Expected: the build fails with CS0246 errors, because `KairoDatabase`, `ModuleDbContext`, `IBrandOwned`, `BrandSeparation` and `AddKairoCore` don't exist yet.

- [ ] **Step 6: Give SharedKernel EF Core and the migration hook**

Replace `core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
  </ItemGroup>

</Project>
```

Replace `core/src/Kairo.SharedKernel/IModule.cs`:

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

    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);
}
```

Give every one of the 14 module classes the new method. Modules without a database return a completed task:

```bash
for file in core/src/Modules/Kairo.Modules.*/*Module.cs; do
  perl -0pi -e 's/(    public void MapJobs\(IEndpointRouteBuilder jobs\)\n    \{\n    \}\n)/$1\n    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;\n/' "$file"
done
grep -l "MigrateAsync" core/src/Modules/Kairo.Modules.*/*Module.cs | wc -l
```

Expected: `14`. Each module class now looks like `core/src/Modules/Kairo.Modules.Approvals/ApprovalsModule.cs`:

```csharp
namespace Kairo.Modules.Approvals;

public sealed class ApprovalsModule : IModule
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

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **Step 7: Write the shared connection and transaction**

Create `core/src/Kairo.SharedKernel/Persistence/KairoDatabase.cs`. It owns:
- the one connection every module context in a scope uses
- the transaction that all of them join
- the brand and system-scope state written to the connection's session context

The state is written whenever the connection opens and whenever it changes.

```csharp
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Kairo.SharedKernel.Persistence;

public sealed class KairoDatabase(string connectionString) : IDisposable, IAsyncDisposable
{
    private const string SessionContextSql = """
        EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @brand_id;
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = @all_brands;
        """;

    private readonly List<DbContext> _contexts = [];
    private SqlTransaction? _transaction;
    private int _systemScopes;

    public SqlConnection Connection { get; } = new(connectionString);

    public Guid? BrandId { get; private set; }

    public bool AllBrands => _systemScopes > 0;

    public bool HasTransaction => _transaction is not null;

    public async Task UseBrandAsync(Guid brandId, CancellationToken cancellationToken)
    {
        BrandId = brandId;
        await RefreshSessionContextAsync(cancellationToken);
    }

    public async Task<IAsyncDisposable> EnterSystemScopeAsync(CancellationToken cancellationToken)
    {
        _systemScopes++;
        await RefreshSessionContextAsync(cancellationToken);
        return new SystemScope(this);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
            await ApplySessionContextAsync(Connection, cancellationToken);
        }

        _transaction = (SqlTransaction)await Connection.BeginTransactionAsync(cancellationToken);
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(_transaction, cancellationToken);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("No transaction is in progress.");
        await transaction.CommitAsync(cancellationToken);
        await EndTransactionAsync(transaction);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not { } transaction)
        {
            return;
        }

        if (transaction.Connection is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        await EndTransactionAsync(transaction);
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        Connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        await Connection.DisposeAsync();
    }

    internal void Enlist(DbContext context)
    {
        _contexts.Add(context);
        if (_transaction is not null)
        {
            context.Database.UseTransaction(_transaction);
        }
    }

    internal void ApplySessionContext(DbConnection connection)
    {
        using var command = CreateSessionContextCommand(connection);
        command.ExecuteNonQuery();
    }

    internal async Task ApplySessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = CreateSessionContextCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateSessionContextCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SessionContextSql;
        command.Transaction = _transaction;

        var brand = command.CreateParameter();
        brand.ParameterName = "@brand_id";
        brand.DbType = DbType.Guid;
        brand.Value = BrandId.HasValue ? BrandId.Value : DBNull.Value;
        command.Parameters.Add(brand);

        var allBrands = command.CreateParameter();
        allBrands.ParameterName = "@all_brands";
        allBrands.DbType = DbType.Boolean;
        allBrands.Value = AllBrands;
        command.Parameters.Add(allBrands);

        return command;
    }

    private async Task RefreshSessionContextAsync(CancellationToken cancellationToken)
    {
        if (Connection.State == ConnectionState.Open)
        {
            await ApplySessionContextAsync(Connection, cancellationToken);
        }
    }

    private async Task EndTransactionAsync(SqlTransaction transaction)
    {
        _transaction = null;
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(null);
        }

        await transaction.DisposeAsync();
    }

    private sealed class SystemScope(KairoDatabase database) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            database._systemScopes--;
            await database.RefreshSessionContextAsync(CancellationToken.None);
        }
    }
}
```

Create `core/src/Kairo.SharedKernel/Persistence/IBrandOwned.cs`:

```csharp
namespace Kairo.SharedKernel.Persistence;

public interface IBrandOwned
{
    Guid BrandId { get; set; }
}
```

Create `core/src/Kairo.SharedKernel/Persistence/SessionContextInterceptor.cs`. Whenever EF Core opens the shared connection, the interceptor writes the session context:

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kairo.SharedKernel.Persistence;

internal sealed class SessionContextInterceptor : DbConnectionInterceptor
{
    public static readonly SessionContextInterceptor Instance = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (eventData.Context is ModuleDbContext context)
        {
            context.KairoDatabase.ApplySessionContext(connection);
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ModuleDbContext context)
        {
            await context.KairoDatabase.ApplySessionContextAsync(connection, cancellationToken);
        }
    }
}
```

- [ ] **Step 8: Write the module DbContext base class and options**

Create `core/src/Kairo.SharedKernel/Persistence/ModuleDbContext.cs`:

```csharp
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Kairo.SharedKernel.Persistence;

public abstract class ModuleDbContext : DbContext
{
    private static readonly MethodInfo ApplyBrandFilterMethod =
        typeof(ModuleDbContext).GetMethod(nameof(ApplyBrandFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected ModuleDbContext(DbContextOptions options, KairoDatabase database)
        : base(options)
    {
        KairoDatabase = database;
        database.Enlist(this);
    }

    internal KairoDatabase KairoDatabase { get; }

    protected Guid? CurrentBrandId => KairoDatabase.BrandId;

    protected bool AllBrands => KairoDatabase.AllBrands;

    protected abstract string Schema { get; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampBrand();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampBrand();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);

        var brandOwnedTypes = modelBuilder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(type => typeof(IBrandOwned).IsAssignableFrom(type))
            .ToList();

        foreach (var type in brandOwnedTypes)
        {
            ApplyBrandFilterMethod.MakeGenericMethod(type).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyBrandFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IBrandOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => AllBrands || entity.BrandId == CurrentBrandId);

    private void StampBrand()
    {
        foreach (var entry in ChangeTracker.Entries<IBrandOwned>().Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity.BrandId == Guid.Empty)
            {
                entry.Entity.BrandId = KairoDatabase.BrandId
                    ?? throw new InvalidOperationException("No brand is set for this request.");
            }
        }
    }
}
```

Create `core/src/Kairo.SharedKernel/Persistence/KairoDbContextOptions.cs`. The design-time helper is for `dotnet ef`, which builds a context without connecting to a database:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.SharedKernel.Persistence;

public static class KairoDbContextOptions
{
    public const string DesignTimeConnectionString = "Server=localhost;Database=kairo;Integrated Security=true;TrustServerCertificate=true";

    public static DbContextOptionsBuilder UseKairoDatabase(
        this DbContextOptionsBuilder options,
        IServiceProvider services,
        string schema) =>
        options.UseKairoDatabase(services.GetRequiredService<KairoDatabase>(), schema);

    public static DbContextOptionsBuilder UseKairoDatabase(
        this DbContextOptionsBuilder options,
        KairoDatabase database,
        string schema) =>
        options
            .UseSqlServer(database.Connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .AddInterceptors(SessionContextInterceptor.Instance);

    public static TContext CreateForDesignTime<TContext>(
        string schema,
        Func<DbContextOptions<TContext>, KairoDatabase, TContext> create)
        where TContext : DbContext
    {
        var database = new KairoDatabase(DesignTimeConnectionString);
        var options = new DbContextOptionsBuilder<TContext>();
        options.UseKairoDatabase(database, schema);
        return create(options.Options, database);
    }
}
```

- [ ] **Step 9: Write the row-level security helpers, the migration runner and the service registration**

Create `core/src/Kairo.SharedKernel/Persistence/BrandSeparation.cs`:
- The predicate returns a row only when its brand matches `kairo.brand_id`, or when `kairo.all_brands` is on.
- Adding a table creates the schema's policy the first time, and alters it after that.

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kairo.SharedKernel.Persistence;

public static class BrandSeparation
{
    public static string CreatePredicateSql(string schema) => $"""
        CREATE FUNCTION [{schema}].[BrandAccess](@BrandId uniqueidentifier)
        RETURNS TABLE
        WITH SCHEMABINDING
        AS RETURN
            SELECT 1 AS Allowed
            WHERE @BrandId = CAST(SESSION_CONTEXT(N'kairo.brand_id') AS uniqueidentifier)
               OR CAST(SESSION_CONTEXT(N'kairo.all_brands') AS bit) = 1
        """;

    public static string AddTableSql(string schema, string table, string column = "BrandId")
    {
        var predicates = $"ADD FILTER PREDICATE [{schema}].[BrandAccess]([{column}]) ON [{schema}].[{table}], "
            + $"ADD BLOCK PREDICATE [{schema}].[BrandAccess]([{column}]) ON [{schema}].[{table}] AFTER INSERT, "
            + $"ADD BLOCK PREDICATE [{schema}].[BrandAccess]([{column}]) ON [{schema}].[{table}] AFTER UPDATE";

        return $"""
            IF OBJECT_ID(N'[{schema}].[BrandSeparation]', N'SP') IS NULL
                EXEC(N'CREATE SECURITY POLICY [{schema}].[BrandSeparation] {predicates} WITH (STATE = ON)');
            ELSE
                EXEC(N'ALTER SECURITY POLICY [{schema}].[BrandSeparation] {predicates}');
            """;
    }

    public static string RemoveTableSql(string schema, string table) =>
        $"ALTER SECURITY POLICY [{schema}].[BrandSeparation] "
        + $"DROP FILTER PREDICATE ON [{schema}].[{table}], "
        + $"DROP BLOCK PREDICATE ON [{schema}].[{table}] AFTER INSERT, "
        + $"DROP BLOCK PREDICATE ON [{schema}].[{table}] AFTER UPDATE;";

    public static string DropSql(string schema) => $"""
        DROP SECURITY POLICY IF EXISTS [{schema}].[BrandSeparation];
        DROP FUNCTION IF EXISTS [{schema}].[BrandAccess];
        """;

    public static void CreateBrandAccessPredicate(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(CreatePredicateSql(schema));

    public static void AddBrandSeparation(this MigrationBuilder migrationBuilder, string schema, string table, string column = "BrandId") =>
        migrationBuilder.Sql(AddTableSql(schema, table, column));

    public static void RemoveBrandSeparation(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql(RemoveTableSql(schema, table));

    public static void DropBrandSeparation(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql(DropSql(schema));
}
```

Create `core/src/Kairo.SharedKernel/Persistence/ModuleMigrations.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.SharedKernel.Persistence;

public static class ModuleMigrations
{
    public static async Task ApplyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (var module in services.GetServices<IModule>())
        {
            await using var scope = services.CreateAsyncScope();
            await module.MigrateAsync(scope.ServiceProvider, cancellationToken);
        }
    }
}
```

Create `core/src/Kairo.SharedKernel/KairoCoreServices.cs`. The connection string is read when the first `KairoDatabase` is created, not at registration, so hosts start even before their configuration is complete:

```csharp
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.SharedKernel;

public static class KairoCoreServices
{
    public static IServiceCollection AddKairoCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped(provider => new KairoDatabase(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("kairo")
                ?? throw new InvalidOperationException("The 'kairo' connection string is missing.")));
        return services;
    }
}
```

- [ ] **Step 10: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests
```

Expected: PASS, 15 tests (4 transaction tests and 11 brand-separation tests). The first run pulls the SQL Server image if Docker doesn't already have it.

- [ ] **Step 11: Check that nothing else broke**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: the build succeeds with 0 warnings, and every test passes, including the existing architecture and host tests.

- [ ] **Step 12: Commit**

```bash
git add core/Directory.Packages.props core/Kairo.slnx core/src/Kairo.SharedKernel core/src/Modules/Kairo.Modules.*/*Module.cs core/tests/Kairo.IntegrationTests
git commit -m "Add shared database connection, transactions and brand separation"
```

### Task 2: API request pipeline

**Files:**
- Create: `core/src/Kairo.SharedKernel/Access/BrandRoles.cs`
- Create: `core/src/Kairo.SharedKernel/Access/KairoClaims.cs`
- Create: `core/src/Kairo.SharedKernel/Access/IBrandAccess.cs`
- Create: `core/src/Kairo.SharedKernel/Access/CurrentMember.cs`
- Create: `core/src/Kairo.SharedKernel/Http/KairoApi.cs`
- Create: `core/src/Kairo.SharedKernel/Http/RequestHeaderFilter.cs`
- Create: `core/src/Kairo.SharedKernel/Http/TransactionFilter.cs`
- Create: `core/src/Kairo.SharedKernel/Http/BrandAccessFilter.cs`
- Modify: `core/src/Kairo.SharedKernel/KairoCoreServices.cs`
- Modify: `core/src/Modules/Directory.Build.props`
- Modify: `core/src/Kairo.Api/Program.cs`
- Modify: `core/src/Kairo.Worker/Program.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`
- Create: `core/tests/Kairo.IntegrationTests/Pipeline/PipelineApp.cs`
- Test: `core/tests/Kairo.IntegrationTests/Pipeline/PipelineTests.cs`

**Interfaces:**
- Consumes: from Task 1, `KairoDatabase`, `AddKairoCore()`, `UseKairoDatabase(...)`, and the test helpers `PlumbingDatabase` and `NotesDbContext`.
- Produces in namespace `Kairo.SharedKernel.Access`:
  - `[Flags] enum BrandRoles { None = 0, Admin = 1, Marketer = 2, Approver = 4, All = 7 }`
  - `enum BrandRole { Admin, Marketer, Approver }`
  - Extensions `BrandRole.ToFlag()`, `IEnumerable<BrandRole>.ToFlags()` and `BrandRoles.ToList()`. `ToList()` returns `BrandRole[]` in the order admin, marketer, approver.
  - `KairoClaims`, with constants `UserId = "kairo_user_id"`, `Name = "name"` and `Email = "email"`, plus `ClaimsPrincipal.FindUserId()`, which returns `Guid?`.
  - `IBrandAccess.FindRolesAsync(Guid brandId, Guid userId, CancellationToken)`, which returns `Task<BrandRoles?>`. A null result means the user is not a member.
  - `CurrentMember` (scoped), with `BrandId`, `UserId`, `Roles` and `bool Has(BrandRoles role)`.
- Produces in namespace `Kairo.SharedKernel.Http`:
  - `KairoApi.RequestHeader = "X-Kairo-Request"`.
  - `IEndpointRouteBuilder.MapKairoApi()`, which creates the `/api` group with the request-header and transaction filters.
  - `IEndpointRouteBuilder.MapBrandApi()`, which creates `/brands/{brandId:guid}` under the given group. Its endpoints require sign-in and membership, and they set `CurrentMember` and the brand.
  - `RequireBrandRole(BrandRoles roles)` on any endpoint or group.
  - `sealed record BrandRoleRequirement(BrandRoles Roles)`.

- [ ] **Step 1: Write the test host**

In `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`, add `Microsoft.AspNetCore.Mvc.Testing`, which brings the test server. Its version is already in `core/Directory.Packages.props`.

```xml
  <ItemGroup>
    <PackageReference Include="Testcontainers.MsSql" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  </ItemGroup>
```

Create `core/tests/Kairo.IntegrationTests/Pipeline/PipelineApp.cs`. It is a minimal web app built only from the SharedKernel pieces:
- a fake membership lookup
- a test sign-in that trusts an `X-Test-User` header
- a few endpoints that write notes and then succeed, refuse or throw

```csharp
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kairo.IntegrationTests.Persistence;
using Kairo.SharedKernel;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Http;
using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kairo.IntegrationTests.Pipeline;

internal sealed class PipelineApp(WebApplication app) : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";

    public HttpClient Client { get; } = app.GetTestClient();

    public static async Task<PipelineApp> StartAsync(string connectionString, FakeBrandAccess access)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["ConnectionStrings:kairo"] = connectionString;
        builder.Services.AddKairoCore();
        builder.Services.AddProblemDetails();
        builder.Services.AddDbContext<NotesDbContext>((provider, options) => options.UseKairoDatabase(provider, "notes"));
        builder.Services.AddSingleton<IBrandAccess>(access);
        builder.Services.AddAuthentication(TestUser.SchemeName).AddScheme<AuthenticationSchemeOptions, TestUser>(TestUser.SchemeName, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        var api = app.MapKairoApi();
        api.MapGet("/ping", () => TypedResults.Ok());
        api.MapPost("/ping", () => TypedResults.Ok());

        var brand = api.MapBrandApi();
        brand.MapGet("/notes", async (NotesDbContext db) =>
            TypedResults.Ok(await db.Notes.OrderBy(note => note.Text).Select(note => note.Text).ToListAsync()));
        brand.MapPost("/notes", async (NoteRequest request, NotesDbContext db) =>
        {
            db.Notes.Add(new Note { Text = request.Text });
            await db.SaveChangesAsync();
            return TypedResults.Ok();
        });
        brand.MapPost("/notes/then-refuse", async (NoteRequest request, NotesDbContext db) =>
        {
            db.Notes.Add(new Note { Text = request.Text });
            await db.SaveChangesAsync();
            return TypedResults.Conflict();
        });
        brand.MapPost("/notes/then-throw", async (NoteRequest request, NotesDbContext db) =>
        {
            db.Notes.Add(new Note { Text = request.Text });
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Something broke after saving.");
        });
        brand.MapGet("/admin-only", () => TypedResults.Ok()).RequireBrandRole(BrandRoles.Admin);
        brand.MapGet("/whoami", (CurrentMember member) =>
            TypedResults.Ok(new MemberEcho(member.BrandId, member.UserId, member.Roles.ToString())));

        await app.StartAsync();
        return new PipelineApp(app);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid? user = null, object? body = null, bool withHeader = true)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (user is { } id)
        {
            request.Headers.Add(UserHeader, id.ToString());
        }

        if (withHeader)
        {
            request.Headers.Add(KairoApi.RequestHeader, "1");
        }

        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }
}

internal sealed record NoteRequest(string Text);

internal sealed record MemberEcho(Guid BrandId, Guid UserId, string Roles);

internal sealed class FakeBrandAccess : IBrandAccess
{
    private readonly ConcurrentDictionary<(Guid BrandId, Guid UserId), BrandRoles> _members = new();

    public void Add(Guid brandId, Guid userId, BrandRoles roles) => _members[(brandId, userId)] = roles;

    public Task<BrandRoles?> FindRolesAsync(Guid brandId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_members.TryGetValue((brandId, userId), out var roles) ? roles : (BrandRoles?)null);
}

internal sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PipelineApp.UserHeader, out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(KairoClaims.UserId, userId.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Pipeline/PipelineTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kairo.IntegrationTests.Persistence;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Pipeline;

public sealed class PipelineTests(SqlServer sqlServer) : IAsyncLifetime
{
    private readonly FakeBrandAccess _access = new();
    private readonly Guid _brand = Guid.NewGuid();
    private readonly Guid _otherBrand = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _marketer = Guid.NewGuid();
    private PlumbingDatabase _database = null!;
    private PipelineApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await PlumbingDatabase.CreateAsync(sqlServer);
        _access.Add(_brand, _admin, BrandRoles.Admin | BrandRoles.Marketer);
        _access.Add(_otherBrand, _admin, BrandRoles.Admin);
        _access.Add(_brand, _marketer, BrandRoles.Marketer);
        _app = await PipelineApp.StartAsync(_database.ConnectionString, _access);
    }

    [Fact]
    public async Task A_change_without_the_request_header_is_refused()
    {
        var without = await _app.SendAsync(HttpMethod.Post, "/api/ping", withHeader: false);
        var with = await _app.SendAsync(HttpMethod.Post, "/api/ping");

        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);
        Assert.Equal(HttpStatusCode.OK, with.StatusCode);
    }

    [Fact]
    public async Task Reads_do_not_need_the_request_header()
    {
        var response = await _app.SendAsync(HttpMethod.Get, "/api/ping", withHeader: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_successful_change_is_saved()
    {
        var saved = await _app.SendAsync(HttpMethod.Post, Notes(_brand), _admin, new { text = "first" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["first"], await NotesAsync(_brand));
    }

    [Fact]
    public async Task A_change_that_ends_in_an_error_status_is_rolled_back()
    {
        var refused = await _app.SendAsync(HttpMethod.Post, $"{Notes(_brand)}/then-refuse", _admin, new { text = "discarded" });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Empty(await NotesAsync(_brand));
    }

    [Fact]
    public async Task A_change_that_throws_is_rolled_back()
    {
        var failed = await _app.SendAsync(HttpMethod.Post, $"{Notes(_brand)}/then-throw", _admin, new { text = "discarded" });

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Empty(await NotesAsync(_brand));
    }

    [Fact]
    public async Task Workspace_endpoints_need_a_signed_in_user()
    {
        var response = await _app.SendAsync(HttpMethod.Get, Notes(_brand));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task People_outside_the_workspace_get_not_found()
    {
        var stranger = await _app.SendAsync(HttpMethod.Get, Notes(_brand), Guid.NewGuid());
        var unknown = await _app.SendAsync(HttpMethod.Get, Notes(Guid.NewGuid()), _admin);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Members_without_the_role_an_endpoint_needs_are_forbidden()
    {
        var marketer = await _app.SendAsync(HttpMethod.Get, $"/api/brands/{_brand}/admin-only", _marketer);
        var admin = await _app.SendAsync(HttpMethod.Get, $"/api/brands/{_brand}/admin-only", _admin);

        Assert.Equal(HttpStatusCode.Forbidden, marketer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task The_workspace_in_the_route_scopes_the_data()
    {
        await _app.SendAsync(HttpMethod.Post, Notes(_brand), _admin, new { text = "mine" });
        await _app.SendAsync(HttpMethod.Post, Notes(_otherBrand), _admin, new { text = "theirs" });

        Assert.Equal(["mine"], await NotesAsync(_brand));
        Assert.Equal(["theirs"], await NotesAsync(_otherBrand));
    }

    [Fact]
    public async Task Endpoints_can_see_the_current_member()
    {
        var response = await _app.SendAsync(HttpMethod.Get, $"/api/brands/{_brand}/whoami", _marketer);

        var member = await response.Content.ReadFromJsonAsync<MemberEcho>(TestContext.Current.CancellationToken);
        Assert.Equal(new MemberEcho(_brand, _marketer, "Marketer"), member);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    private static string Notes(Guid brandId) => $"/api/brands/{brandId}/notes";

    private async Task<string[]> NotesAsync(Guid brandId)
    {
        var response = await _app.SendAsync(HttpMethod.Get, Notes(brandId), _admin);
        return (await response.Content.ReadFromJsonAsync<string[]>(TestContext.Current.CancellationToken))!;
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet build core/tests/Kairo.IntegrationTests
```

Expected: the build fails with CS0246 errors, because `Kairo.SharedKernel.Access`, `Kairo.SharedKernel.Http`, `BrandRoles`, `KairoApi`, `IBrandAccess` and `CurrentMember` don't exist yet.

- [ ] **Step 4: Write the access types**

Create `core/src/Kairo.SharedKernel/Access/BrandRoles.cs`:

```csharp
namespace Kairo.SharedKernel.Access;

[Flags]
public enum BrandRoles
{
    None = 0,
    Admin = 1,
    Marketer = 2,
    Approver = 4,
    All = Admin | Marketer | Approver,
}

public enum BrandRole
{
    Admin,
    Marketer,
    Approver,
}

public static class BrandRoleConversions
{
    public static BrandRoles ToFlag(this BrandRole role) => role switch
    {
        BrandRole.Admin => BrandRoles.Admin,
        BrandRole.Marketer => BrandRoles.Marketer,
        BrandRole.Approver => BrandRoles.Approver,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static BrandRoles ToFlags(this IEnumerable<BrandRole> roles) =>
        roles.Aggregate(BrandRoles.None, (all, role) => all | role.ToFlag());

    public static BrandRole[] ToList(this BrandRoles roles) =>
        Enum.GetValues<BrandRole>().Where(role => roles.HasFlag(role.ToFlag())).ToArray();
}
```

Create `core/src/Kairo.SharedKernel/Access/KairoClaims.cs`:

```csharp
using System.Security.Claims;

namespace Kairo.SharedKernel.Access;

public static class KairoClaims
{
    public const string UserId = "kairo_user_id";
    public const string Name = "name";
    public const string Email = "email";

    public static Guid? FindUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(UserId), out var id) ? id : null;
}
```

Create `core/src/Kairo.SharedKernel/Access/IBrandAccess.cs`:

```csharp
namespace Kairo.SharedKernel.Access;

public interface IBrandAccess
{
    Task<BrandRoles?> FindRolesAsync(Guid brandId, Guid userId, CancellationToken cancellationToken);
}
```

Create `core/src/Kairo.SharedKernel/Access/CurrentMember.cs`:

```csharp
namespace Kairo.SharedKernel.Access;

public sealed class CurrentMember
{
    public Guid BrandId { get; private set; }

    public Guid UserId { get; private set; }

    public BrandRoles Roles { get; private set; }

    public bool Has(BrandRoles role) => (Roles & role) != BrandRoles.None;

    internal void Set(Guid brandId, Guid userId, BrandRoles roles)
    {
        BrandId = brandId;
        UserId = userId;
        Roles = roles;
    }
}
```

In `core/src/Kairo.SharedKernel/KairoCoreServices.cs`, add `using Kairo.SharedKernel.Access;` and register the current member after the `KairoDatabase` line:

```csharp
        services.AddScoped<CurrentMember>();
```

- [ ] **Step 5: Write the route groups and filters**

Create `core/src/Kairo.SharedKernel/Http/KairoApi.cs`:

```csharp
using Kairo.SharedKernel.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kairo.SharedKernel.Http;

public static class KairoApi
{
    public const string RequestHeader = "X-Kairo-Request";

    public static RouteGroupBuilder MapKairoApi(this IEndpointRouteBuilder app) =>
        app.MapGroup("/api")
            .AddEndpointFilter<RequestHeaderFilter>()
            .AddEndpointFilter<TransactionFilter>();

    public static RouteGroupBuilder MapBrandApi(this IEndpointRouteBuilder api) =>
        api.MapGroup("/brands/{brandId:guid}")
            .RequireAuthorization()
            .AddEndpointFilter<BrandAccessFilter>();

    public static TBuilder RequireBrandRole<TBuilder>(this TBuilder builder, BrandRoles roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new BrandRoleRequirement(roles));
}

public sealed record BrandRoleRequirement(BrandRoles Roles);
```

Create `core/src/Kairo.SharedKernel/Http/RequestHeaderFilter.cs`. A page on another origin can't add a custom header without a CORS preflight, and the core allows none:

```csharp
using Microsoft.AspNetCore.Http;

namespace Kairo.SharedKernel.Http;

internal sealed class RequestHeaderFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || request.Headers.ContainsKey(KairoApi.RequestHeader))
        {
            return next(context);
        }

        return ValueTask.FromResult<object?>(TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: $"The {KairoApi.RequestHeader} header is required."));
    }
}
```

Create `core/src/Kairo.SharedKernel/Http/TransactionFilter.cs`. Every write runs in one transaction. It commits when the endpoint returns a success status, and rolls back on an error status or an exception:

```csharp
using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;

namespace Kairo.SharedKernel.Http;

internal sealed class TransactionFilter(KairoDatabase database) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        var cancellationToken = context.HttpContext.RequestAborted;
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await next(context);
            if (IsSuccess(result))
            {
                await database.CommitAsync(cancellationToken);
            }
            else
            {
                await database.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static bool IsSuccess(object? result) =>
        result is not IStatusCodeHttpResult { StatusCode: >= 400 };
}
```

Create `core/src/Kairo.SharedKernel/Http/BrandAccessFilter.cs`. Non-members get 404, so nobody can probe which workspace IDs exist:

```csharp
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.AspNetCore.Http;

namespace Kairo.SharedKernel.Http;

internal sealed class BrandAccessFilter(IBrandAccess access, KairoDatabase database, CurrentMember member) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.FindUserId() is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!Guid.TryParse(http.Request.RouteValues["brandId"]?.ToString(), out var brandId))
        {
            return TypedResults.NotFound();
        }

        if (await access.FindRolesAsync(brandId, userId, http.RequestAborted) is not { } roles)
        {
            return TypedResults.NotFound();
        }

        var requirement = http.GetEndpoint()?.Metadata.GetMetadata<BrandRoleRequirement>();
        if (requirement is not null && (roles & requirement.Roles) == BrandRoles.None)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You don't have the role this needs.");
        }

        member.Set(brandId, userId, roles);
        await database.UseBrandAsync(brandId, http.RequestAborted);
        return await next(context);
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Pipeline.PipelineTests"
```

Expected: PASS, 10 tests.

- [ ] **Step 7: Use the pipeline in the hosts and modules**

Replace the `ItemGroup` of `Using` items in `core/src/Modules/Directory.Build.props`, so every module sees the shared types:

```xml
  <ItemGroup>
    <Using Include="Kairo.SharedKernel" />
    <Using Include="Kairo.SharedKernel.Access" />
    <Using Include="Kairo.SharedKernel.Http" />
    <Using Include="Kairo.SharedKernel.Persistence" />
    <Using Include="Microsoft.AspNetCore.Builder" />
    <Using Include="Microsoft.AspNetCore.Http" />
    <Using Include="Microsoft.AspNetCore.Http.HttpResults" />
    <Using Include="Microsoft.AspNetCore.Routing" />
    <Using Include="Microsoft.EntityFrameworkCore" />
    <Using Include="Microsoft.Extensions.Configuration" />
    <Using Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
```

Replace `core/src/Kairo.Api/Program.cs`. Enums travel as camelCase strings, errors are problem details, and the module endpoints sit in the `/api` group:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Kairo.SharedKernel.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerClient("kairo");
builder.AddRedisClient("redis");
builder.Services.AddKairoCore();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

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

app.UseExceptionHandler();

app.MapDefaultEndpoints();

var api = app.MapKairoApi();
foreach (var module in modules)
{
    module.MapApi(api);
}

app.Run();
```

In `core/src/Kairo.Worker/Program.cs`, register the shared services right after `builder.AddSqlServerClient("kairo");`. Module database contexts need them:

```csharp
builder.Services.AddKairoCore();
```

- [ ] **Step 8: Check everything still builds and passes**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: the build has 0 warnings and every test passes, including the API and worker host tests.

- [ ] **Step 9: Commit**

```bash
git add core/src/Kairo.SharedKernel core/src/Modules/Directory.Build.props core/src/Kairo.Api/Program.cs core/src/Kairo.Worker/Program.cs core/tests/Kairo.IntegrationTests
git commit -m "Add the API pipeline: request header, transactions, workspace access and roles"
```

### Task 3: Migrator and EF Core tooling

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/Kairo.slnx`
- Create: `core/src/Kairo.Migrator/Kairo.Migrator.csproj`
- Create: `core/src/Kairo.Migrator/MigratorHost.cs`
- Create: `core/src/Kairo.Migrator/Program.cs`
- Create: `dotnet-tools.json`, generated by `dotnet new tool-manifest`
- Create: `core/scripts/tidy-migrations.sh`
- Modify: `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`
- Modify: `core/tests/Kairo.IntegrationTests/SqlServer.cs`
- Test: `core/tests/Kairo.IntegrationTests/MigratorTests.cs`

**Interfaces:**
- Consumes: from Task 1, `IModule.MigrateAsync`, `ModuleMigrations.ApplyAsync` and `AddKairoCore()`.
- Produces:
  - `Kairo.Migrator.MigratorHost.Build(string[] args)`, which returns a `WebApplication` with every module registered. It is never run as a server.
  - The `dotnet ef` local tool.
  - `bash core/scripts/tidy-migrations.sh <files>`, which removes the BOM and comments from generated migrations and makes their classes `internal`.
  - `SqlServer.KairoConnectionString`, a shared database migrated once per test run.

- [ ] **Step 1: Write the failing tests**

In `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`, reference the Migrator:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Migrator\Kairo.Migrator.csproj" />
  </ItemGroup>
```

Create `core/tests/Kairo.IntegrationTests/MigratorTests.cs`:

```csharp
using Kairo.Migrator;
using Kairo.SharedKernel;
using Kairo.SharedKernel.Persistence;
using Kairo.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kairo.IntegrationTests;

public sealed class MigratorTests(SqlServer sqlServer)
{
    [Fact]
    public async Task The_migrator_registers_every_module()
    {
        await using var host = MigratorHost.Build([]);

        var registered = host.Services.GetServices<IModule>()
            .Select(module => module.GetType().Assembly.GetName().Name!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ModuleFolders.Names, registered);
    }

    [Fact]
    public async Task Every_registration_resolves_in_development()
    {
        await using var host = MigratorHost.Build(["--environment", "Development"]);

        Assert.True(host.Environment.IsDevelopment());
    }

    [Fact]
    public async Task Migrating_a_database_twice_is_harmless()
    {
        var connectionString = await sqlServer.CreateDatabaseAsync("migrate");

        for (var run = 0; run < 2; run++)
        {
            await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={connectionString}"]);
            await ModuleMigrations.ApplyAsync(host.Services, TestContext.Current.CancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet build core/tests/Kairo.IntegrationTests
```

Expected: the build fails, because `core/src/Kairo.Migrator/Kairo.Migrator.csproj` doesn't exist.

- [ ] **Step 3: Create the Migrator**

In `core/Directory.Packages.props`, add to the `Data` item group:

```xml
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
```

Create `core/src/Kairo.Migrator/Kairo.Migrator.csproj`. It is also the startup project for `dotnet ef`, so it carries the design package:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kairo.ServiceDefaults\Kairo.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="../Modules/*/Kairo.Modules.*.csproj" />
  </ItemGroup>

</Project>
```

Create `core/src/Kairo.Migrator/MigratorHost.cs`. It builds a `WebApplication` rather than a generic host. The modules register ASP.NET Core authorization, which needs routing services; a generic host fails dependency validation in Development.

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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kairo.Migrator;

public static class MigratorHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddKairoCore();

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

        return builder.Build();
    }
}
```

Create `core/src/Kairo.Migrator/Program.cs`. `Program` is an explicit internal class, so it doesn't clash with the API's public `Program` in test projects that reference both:

```csharp
using Kairo.SharedKernel.Persistence;

namespace Kairo.Migrator;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        await using var host = MigratorHost.Build(args);
        await ModuleMigrations.ApplyAsync(host.Services, CancellationToken.None);
    }
}
```

In `core/Kairo.slnx`, add the project to the `/src/` folder, keeping the entries sorted:

```xml
    <Project Path="src/Kairo.Migrator/Kairo.Migrator.csproj" />
```

- [ ] **Step 4: Migrate the shared test database**

Replace `core/tests/Kairo.IntegrationTests/SqlServer.cs`. The fixture now also creates the `kairo` test database and migrates it once, using the Migrator's own code:

```csharp
using Kairo.IntegrationTests;
using Kairo.Migrator;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServer))]

namespace Kairo.IntegrationTests;

public sealed class SqlServer : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string KairoConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        KairoConnectionString = await CreateDatabaseAsync("kairo");
        await using var migrator = MigratorHost.Build([$"--ConnectionStrings:kairo={KairoConnectionString}"]);
        await ModuleMigrations.ApplyAsync(migrator.Services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
        return new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString;
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests
```

Expected: PASS, 28 tests: 15 from Task 1, 10 from Task 2 and 3 Migrator tests.

- [ ] **Step 6: Add the `dotnet ef` tool and the migration tidy script**

From the repository root:

```bash
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.12
dotnet ef --version
```

Expected: the manifest is written to `dotnet-tools.json` in the repository root, and the last command prints `10.0.12`.

Create `core/scripts/tidy-migrations.sh`. EF Core writes a BOM, two kinds of comment and `public` migration classes into generated files. The script strips the BOM and the comments to follow `CLAUDE.md` §3, and makes the classes `internal` so each module keeps exactly one public type. It has no shebang line; always run it with `bash`.

```bash
set -euo pipefail

for file in "$@"; do
  sed -i \
    -e '1s/^\xEF\xBB\xBF//' \
    -e '/^[[:space:]]*\/\/ <auto-generated \/>[[:space:]]*$/d' \
    -e '/^[[:space:]]*\/\/\/ <inheritdoc \/>[[:space:]]*$/d' \
    -e 's/^    public partial class /    internal partial class /' \
    "$file"
done
```

- [ ] **Step 7: Check everything still builds and passes**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: 0 warnings, and every test passes.

- [ ] **Step 8: Commit**

```bash
git add core/Directory.Packages.props core/Kairo.slnx core/src/Kairo.Migrator dotnet-tools.json core/scripts/tidy-migrations.sh core/tests/Kairo.IntegrationTests
git commit -m "Add the Migrator, the dotnet-ef tool and a migration tidy script"
```

### Task 4: Audit log storage

**Files:**
- Create: `core/src/Kairo.Contracts/Audit/IAuditLog.cs`
- Create: `core/src/Modules/Kairo.Modules.Audit/Data/AuditEntry.cs`
- Create: `core/src/Modules/Kairo.Modules.Audit/Data/AuditDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Audit/Data/AuditDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Audit/Data/Migrations/*`, generated
- Create: `core/src/Modules/Kairo.Modules.Audit/AuditLog.cs`
- Modify: `core/src/Modules/Kairo.Modules.Audit/AuditModule.cs`
- Modify: `core/tests/Kairo.ArchitectureTests/ModuleRulesTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Audit/AuditStorageTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/SecurityPolicyCoverageTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `ModuleDbContext`, `UseKairoDatabase`, `CreateForDesignTime`, `IBrandOwned` and the `BrandSeparation` migration helpers
  - from Task 2: `KairoClaims`
  - from Task 3: `dotnet ef`, `tidy-migrations.sh` and `MigratorHost`
- Produces:
  - `Kairo.Contracts.Audit.IAuditLog.RecordAsync(AuditRecord record, CancellationToken)`.
  - `sealed record AuditRecord(string Action, string TargetType, string TargetId, object? Before = null, object? After = null)`.
  - An entry needs a brand set on `KairoDatabase`. The actor is taken from the signed-in user, or is the system ("Kairo") when there is no user.
  - Table `audit.Entries`:
    - columns `Id` (bigint identity), `BrandId`, `OccurredAt`, `ActorType`, `ActorId`, `ActorName`, `Action`, `TargetType`, `TargetId`, `Before` (JSON), `After` (JSON) and `TraceId`
    - updates and deletes are rejected by the trigger `audit.Entries_PreventChanges`

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Audit/AuditStorageTests.cs`. It uses the Migrator's container, which has every module registered, so it can call `IAuditLog` without the API:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Migrator;
using Kairo.SharedKernel.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Audit;

public sealed class AuditStorageTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recording_an_entry_keeps_who_did_what_to_which_target()
    {
        var brandId = Guid.NewGuid();
        await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={sqlServer.KairoConnectionString}"]);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(
                new AuditRecord("test.recorded", "thing", "42", Before: new { Name = "Old" }, After: new { Name = "New" }),
                Token);
        }

        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.brand_id', @value = @brand;
            SELECT [ActorType], [ActorName], [Action], [TargetType], [TargetId], [Before], [After] FROM [audit].[Entries];
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(
            ("system", "Kairo", "test.recorded", "thing", "42", """{"name":"Old"}""", """{"name":"New"}"""),
            (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        Assert.False(await reader.ReadAsync(Token));
    }

    [Fact]
    public async Task Recording_needs_a_brand()
    {
        await using var host = MigratorHost.Build([$"--ConnectionStrings:kairo={sqlServer.KairoConnectionString}"]);
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(new AuditRecord("test.recorded", "thing", "1"), Token));
    }

    [Fact]
    public async Task Audit_entries_cannot_be_changed_or_deleted()
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);

        foreach (var statement in new[] { "UPDATE [audit].[Entries] SET [Action] = N'tampered'", "DELETE FROM [audit].[Entries]" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1; {statement};";
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync(Token));
            Assert.Contains("cannot be changed or deleted", error.Message);
        }
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Persistence/SecurityPolicyCoverageTests.cs`. It fails the build as soon as any module adds a workspace-owned table without putting it under its schema's security policy.

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class SecurityPolicyCoverageTests(SqlServer sqlServer)
{
    [Fact]
    public async Task Every_table_with_a_brand_column_has_row_level_security()
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name + N'.' + t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE (EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name = N'BrandId')
                   OR t.object_id = OBJECT_ID(N'brands.Brands'))
              AND NOT EXISTS (SELECT 1 FROM sys.security_predicates p WHERE p.target_object_id = t.object_id AND p.predicate_type_desc = N'FILTER')
            """;
        var uncovered = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            uncovered.Add(reader.GetString(0));
        }

        Assert.Empty(uncovered);
    }
}
```

In `core/tests/Kairo.ArchitectureTests/ModuleRulesTests.cs`, add these `using` directives:

```csharp
using System.Text.RegularExpressions;
using Kairo.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
```

Then add this test after `Module_project_adds_no_project_references`. Every module DbContext must have a design-time factory, and must map tables only into its own schema, which is the module name in snake case:

```csharp
    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_tables_live_in_the_module_schema(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var schema = Regex.Replace(assemblyName["Kairo.Modules.".Length..], "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
        var contextTypes = assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ModuleDbContext)) && !type.IsAbstract);

        foreach (var contextType in contextTypes)
        {
            var factoryInterface = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(contextType);
            var factoryType = Assert.Single(assembly.GetTypes(), type => type.GetInterfaces().Contains(factoryInterface));
            var factory = Activator.CreateInstance(factoryType)!;
            using var context = (DbContext)factoryInterface
                .GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!
                .Invoke(factory, [Array.Empty<string>()])!;

            Assert.All(context.Model.GetEntityTypes(), entityType => Assert.Equal(schema, entityType.GetSchema()));
        }
    }
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet build core/tests/Kairo.IntegrationTests
```

Expected: the build fails with CS0234, because `Kairo.Contracts.Audit` doesn't exist.

- [ ] **Step 3: Write the contract**

Create `core/src/Kairo.Contracts/Audit/IAuditLog.cs`:

```csharp
namespace Kairo.Contracts.Audit;

public interface IAuditLog
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

public sealed record AuditRecord(string Action, string TargetType, string TargetId, object? Before = null, object? After = null);
```

- [ ] **Step 4: Write the Audit module's data**

Create `core/src/Modules/Kairo.Modules.Audit/Data/AuditEntry.cs`:

```csharp
namespace Kairo.Modules.Audit.Data;

internal sealed class AuditEntry : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string ActorType { get; set; } = "";

    public string? ActorId { get; set; }

    public string ActorName { get; set; } = "";

    public string Action { get; set; } = "";

    public string TargetType { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string? Before { get; set; }

    public string? After { get; set; }

    public string? TraceId { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Audit/Data/AuditDbContext.cs`. `HasTrigger` tells EF Core the table has a trigger, so it saves rows in a way SQL Server allows on such tables:

```csharp
namespace Kairo.Modules.Audit.Data;

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable("Entries", table => table.HasTrigger("Entries_PreventChanges"));
            entry.HasIndex(e => new { e.BrandId, e.Id });
            entry.Property(e => e.ActorType).HasMaxLength(20);
            entry.Property(e => e.ActorId).HasMaxLength(64);
            entry.Property(e => e.ActorName).HasMaxLength(200);
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.Property(e => e.TargetType).HasMaxLength(50);
            entry.Property(e => e.TargetId).HasMaxLength(64);
            entry.Property(e => e.TraceId).HasMaxLength(32);
        });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Audit/Data/AuditDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Audit.Data;

internal sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<AuditDbContext>(
            AuditDbContext.SchemaName,
            (options, database) => new AuditDbContext(options, database));
}
```

Create `core/src/Modules/Kairo.Modules.Audit/AuditLog.cs`:

```csharp
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal sealed class AuditLog(AuditDbContext db, IHttpContextAccessor http, TimeProvider time) : IAuditLog
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var principal = http.HttpContext?.User;
        var userId = principal?.FindUserId();
        db.Entries.Add(new AuditEntry
        {
            OccurredAt = time.GetUtcNow(),
            ActorType = userId is null ? "system" : "user",
            ActorId = userId?.ToString(),
            ActorName = userId is null ? "Kairo" : principal!.FindFirstValue(KairoClaims.Name) ?? "",
            Action = record.Action,
            TargetType = record.TargetType,
            TargetId = record.TargetId,
            Before = Serialize(record.Before),
            After = Serialize(record.After),
            TraceId = Activity.Current?.TraceId.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
```

Replace `core/src/Modules/Kairo.Modules.Audit/AuditModule.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

public sealed class AuditModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, AuditDbContext.SchemaName));
        services.AddScoped<IAuditLog, AuditLog>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<AuditDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add Entries --project core/src/Modules/Kairo.Modules.Audit --startup-project core/src/Kairo.Migrator --context AuditDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Audit/Data/Migrations/*.cs
```

This creates three files in `core/src/Modules/Kairo.Modules.Audit/Data/Migrations/`: a timestamped `*_Entries.cs`, its `*_Entries.Designer.cs`, and `AuditDbContextModelSnapshot.cs`.

Edit `*_Entries.cs` by hand to add the trigger and the security policy. After the edit, the file must read exactly as follows, except that `Entries` stays the class name EF generated:

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kairo.Modules.Audit.Data.Migrations
{
    internal partial class Entries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_BrandId_Id",
                schema: "audit",
                table: "Entries",
                columns: new[] { "BrandId", "Id" });

            migrationBuilder.Sql("""
                CREATE TRIGGER [audit].[Entries_PreventChanges] ON [audit].[Entries]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Audit entries cannot be changed or deleted.', 1;
                END
                """);

            migrationBuilder.CreateBrandAccessPredicate("audit");
            migrationBuilder.AddBrandSeparation("audit", "Entries");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropBrandSeparation("audit");

            migrationBuilder.DropTable(
                name: "Entries",
                schema: "audit");
        }
    }
}
```

Check that the tidy script did its job:

```bash
grep -rn "//\|public partial" core/src/Modules/Kairo.Modules.Audit/Data/Migrations/ || echo clean
```

Expected: `clean`.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: PASS for both. The integration tests are now 32: the 28 from before, 3 audit storage tests and the coverage test. The architecture tests gain one schema case per module.

- [ ] **Step 7: Check everything still builds and passes**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: 0 warnings, and every test passes.

- [ ] **Step 8: Commit**

```bash
git add core/src/Kairo.Contracts/Audit core/src/Modules/Kairo.Modules.Audit core/tests/Kairo.ArchitectureTests/ModuleRulesTests.cs core/tests/Kairo.IntegrationTests
git commit -m "Store an append-only audit log, kept apart per brand"
```

### Task 5: Google sign-in and sessions

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Kairo.Modules.Identity.csproj`
- Create: `core/src/Modules/Kairo.Modules.Identity/Users/User.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Sessions/Session.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Sessions/SessionTimeouts.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Sessions/DatabaseTicketStore.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/SignIn/IdentitySchemes.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/SignIn/GoogleAccount.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/SignIn/GoogleAccountSignIn.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/SignIn/ReturnUrls.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/SignIn/SignInSetup.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/SignIn/SignInEndpoints.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*`, generated
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Modify: `core/src/Kairo.Api/Program.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`
- Create: `core/tests/Kairo.IntegrationTests/Api/FakeGoogle.cs`
- Create: `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/SignInTests.cs`

**Interfaces:**
- Consumes:
  - from Task 1: `ModuleDbContext` and the related helpers
  - from Task 2: `KairoClaims` and `MapKairoApi`, since endpoints are added to the `/api` group
  - from Task 3: `dotnet ef` and `tidy-migrations.sh`
- Produces:
  - **Endpoints.**
    - `GET /api/auth/sign-in?returnUrl=` sends the browser to Google.
    - `GET /api/auth/callback/google` is handled by the OpenID Connect handler.
    - `POST /api/auth/sign-out` returns 204.
    - `GET /api/me` returns 200 with `MeResponse(Guid Id, string Name, string Email, string? PictureUrl)`, or 401.
  - **Authentication.** The cookie scheme `Kairo.Session` is the default, and the cookie is named `__Host-kairo-session`. The signed-in principal carries the claims `KairoClaims.UserId`, `Name` and `Email`.
  - **Data.** `IdentityDbContext` (schema `identity`) has the tables `Users`, `Sessions` and `DataProtectionKeys`.
  - **Test helpers** in `Kairo.IntegrationTests.Api`:
    - `KairoApiFactory(string connectionString, Action<IServiceCollection>? configureServices = null)`, which has:
      - members `Clock`, `Google`, `CreateBrowser(bool withRequestHeader = true)` and `CookieHeader(HttpClient)`
      - `Task<HttpClient> SignInAsync(GoogleUser)`
      - `Task<string> SignInAsync(HttpClient browser, GoogleUser user, string? returnUrl)`, which returns the final redirect target
    - `GoogleUser.Create(string name = "Test User", bool emailVerified = true)`.

- [ ] **Step 1: Add the packages**

In `core/Directory.Packages.props`, add a new item group after `Data`:

```xml
  <ItemGroup Label="Web">
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="10.0.12" />
  </ItemGroup>
```

In the `Tests` item group, add:

```xml
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
```

Replace `core/src/Modules/Kairo.Modules.Identity/Kairo.Modules.Identity.csproj`. Package references are allowed; project references are not.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" />
  </ItemGroup>

</Project>
```

In `core/tests/Kairo.IntegrationTests/Kairo.IntegrationTests.csproj`, add the fake clock package and the API project:

```xml
  <ItemGroup>
    <PackageReference Include="Testcontainers.MsSql" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Migrator\Kairo.Migrator.csproj" />
    <ProjectReference Include="..\..\src\Kairo.Api\Kairo.Api.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the fake Google and the API test factory**

Create `core/tests/Kairo.IntegrationTests/Api/FakeGoogle.cs`. It replaces Google's metadata and token endpoint for the real OpenID Connect handler. It signs ID tokens with its own key and echoes the nonce from the sign-in redirect, so the handler's own checks run unchanged.

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Kairo.IntegrationTests.Api;

public sealed record GoogleUser(string Subject, string Email, string Name, bool EmailVerified = true)
{
    public static GoogleUser Create(string name = "Test User", bool emailVerified = true)
    {
        var id = Guid.NewGuid().ToString("N");
        return new GoogleUser(id, $"user-{id}@example.com", name, emailVerified);
    }
}

public sealed class FakeGoogle
{
    public const string ClientId = "kairo-test-client";
    public const string Issuer = "https://accounts.google.com";

    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "kairo-test-key" };
    private readonly ConcurrentDictionary<string, (GoogleUser User, string Nonce)> _codes = new();

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> TokenRequests { get; } = new();

    public void Configure(OpenIdConnectOptions options)
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
        };
        configuration.SigningKeys.Add(_key);
        options.Configuration = configuration;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        options.Backchannel = new HttpClient(new TokenEndpoint(this));
    }

    public string IssueCode(GoogleUser user, string nonce)
    {
        var code = Guid.NewGuid().ToString("N");
        _codes[code] = (user, nonce);
        return code;
    }

    private string CreateIdToken(GoogleUser user, string nonce)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Subject,
                ["email"] = user.Email,
                ["email_verified"] = user.EmailVerified,
                ["name"] = user.Name,
                ["nonce"] = nonce,
            },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class TokenEndpoint(FakeGoogle google) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
                .Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
            google.TokenRequests.Enqueue(form);

            if (!google._codes.TryRemove(form["code"], out var issued))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Dictionary<string, object>
                {
                    ["access_token"] = "fake-access-token",
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 3600,
                    ["id_token"] = google.CreateIdToken(issued.User, issued.Nonce),
                }),
            };
        }
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`:
- It runs the real API against the shared test database, with a fake clock and the fake Google.
- Browsers are `https://kairo.test` clients that keep cookies. They don't follow redirects, so tests can read each `Location`.
- Logging is set to warnings only, to keep test output short.

```csharp
using System.Net;
using System.Runtime.CompilerServices;
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

    public async Task<string> SignInAsync(HttpClient browser, GoogleUser user, string? returnUrl)
    {
        var challenge = await browser.GetAsync($"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
        var authorize = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var code = Google.IssueCode(user, authorize["nonce"]!);
        var callbackPath = new Uri(authorize["redirect_uri"]!).AbsolutePath;
        var callback = await browser.GetAsync($"{callbackPath}?code={code}&state={Uri.EscapeDataString(authorize["state"]!)}");
        return callback.Headers.Location!.OriginalString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:kairo", connectionString);
        builder.UseSetting("ConnectionStrings:redis", "127.0.0.1:1,abortConnect=false,connectRetry=0,connectTimeout=1000");
        builder.UseSetting("Authentication:Google:ClientId", FakeGoogle.ClientId);
        builder.UseSetting("Authentication:Google:ClientSecret", "kairo-test-secret");
        builder.UseSetting("Kairo:PublicOrigin", PublicOrigin);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigureAll<OpenIdConnectOptions>(Google.Configure);
            configureServices?.Invoke(services);
        });
    }
}
```

- [ ] **Step 3: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/SignInTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Api;

public sealed class SignInTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signing_in_with_Google_starts_a_session()
    {
        var user = GoogleUser.Create("Priya Shah");

        var browser = await _api.SignInAsync(user);
        var me = await browser.GetFromJsonAsync<Me>("/api/me", Token);

        Assert.Equal(("Priya Shah", user.Email), (me!.Name, me.Email));
    }

    [Fact]
    public async Task Emails_are_stored_in_lowercase()
    {
        var user = GoogleUser.Create() with { Email = $"Mixed.Case-{Guid.NewGuid():N}@Example.com" };

        var browser = await _api.SignInAsync(user);
        var me = await browser.GetFromJsonAsync<Me>("/api/me", Token);

        Assert.Equal(user.Email.ToLowerInvariant(), me!.Email);
    }

    [Fact]
    public async Task The_challenge_asks_Google_for_a_code_with_pkce_and_the_public_redirect_uri()
    {
        var browser = _api.CreateBrowser();

        var response = await browser.GetAsync("/api/auth/sign-in?returnUrl=/acme/today", Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("accounts.google.com", location.Host);
        Assert.Equal(FakeGoogle.ClientId, query["client_id"]);
        Assert.Equal($"{KairoApiFactory.PublicOrigin}/api/auth/callback/google", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
    }

    [Fact]
    public async Task The_code_is_redeemed_with_the_public_redirect_uri()
    {
        await _api.SignInAsync(GoogleUser.Create());

        var request = Assert.Single(_api.Google.TokenRequests);
        Assert.Equal($"{KairoApiFactory.PublicOrigin}/api/auth/callback/google", request["redirect_uri"]);
        Assert.True(request.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task Signing_in_returns_to_the_page_the_user_started_from()
    {
        var location = await _api.SignInAsync(_api.CreateBrowser(), GoogleUser.Create(), "/acme/today?tab=open");

        Assert.Equal("/acme/today?tab=open", location);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("evil")]
    [InlineData("")]
    public async Task Return_addresses_outside_Kairo_fall_back_to_the_home_page(string returnUrl)
    {
        var location = await _api.SignInAsync(_api.CreateBrowser(), GoogleUser.Create(), returnUrl);

        Assert.Equal("/", location);
    }

    [Fact]
    public async Task An_account_with_an_unverified_email_cannot_sign_in()
    {
        var browser = _api.CreateBrowser();

        var location = await _api.SignInAsync(browser, GoogleUser.Create(emailVerified: false), "/");

        Assert.Equal("/sign-in?error=unverified_email", location);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_updates_the_same_user()
    {
        var user = GoogleUser.Create("Old Name");
        var first = await (await _api.SignInAsync(user)).GetFromJsonAsync<Me>("/api/me", Token);

        var renamed = user with { Name = "New Name", Email = $"new-{Guid.NewGuid():N}@example.com" };
        var second = await (await _api.SignInAsync(renamed)).GetFromJsonAsync<Me>("/api/me", Token);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(("New Name", renamed.Email), (second.Name, second.Email));
    }

    [Fact]
    public async Task Me_needs_a_session()
    {
        var response = await _api.CreateBrowser().GetAsync("/api/me", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_for_every_copy_of_the_cookie()
    {
        var browser = await _api.SignInAsync(GoogleUser.Create());
        var copied = _api.CookieHeader(browser);

        var signOut = await browser.PostAsync("/api/auth/sign-out", null, Token);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        var thief = _api.CreateBrowser();
        thief.DefaultRequestHeaders.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/api/me", Token)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_needs_the_request_header()
    {
        var browser = _api.CreateBrowser(withRequestHeader: false);

        var response = await browser.PostAsync("/api/auth/sign-out", null, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_session_ends_after_three_idle_days()
    {
        var browser = await _api.SignInAsync(GoogleUser.Create());

        _api.Clock.Advance(TimeSpan.FromDays(3) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me", Token)).StatusCode);
    }

    [Fact]
    public async Task An_active_session_ends_fourteen_days_after_sign_in()
    {
        var browser = await _api.SignInAsync(GoogleUser.Create());

        for (var day = 2; day <= 12; day += 2)
        {
            _api.Clock.Advance(TimeSpan.FromDays(2));
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/me", Token)).StatusCode);
        }

        _api.Clock.Advance(TimeSpan.FromDays(2) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me", Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private sealed record Me(Guid Id, string Name, string Email, string? PictureUrl);
}
```

- [ ] **Step 4: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.SignInTests"
```

Expected: FAIL. `/api/auth/sign-in` and `/api/me` don't exist yet, so the requests get 404 and the sign-in helper throws while reading the redirect.

- [ ] **Step 5: Write the users and sessions**

Create `core/src/Modules/Kairo.Modules.Identity/Users/User.cs`:

```csharp
namespace Kairo.Modules.Identity.Users;

internal sealed class User
{
    public Guid Id { get; set; }

    public string GoogleSubject { get; set; } = "";

    public string Email { get; set; } = "";

    public string Name { get; set; } = "";

    public string? PictureUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSignedInAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Sessions/Session.cs`:

```csharp
namespace Kairo.Modules.Identity.Sessions;

internal sealed class Session
{
    public string Key { get; set; } = "";

    public Guid UserId { get; set; }

    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Sessions/SessionTimeouts.cs`. It isn't called `SessionOptions`, which would clash with ASP.NET Core's type of that name.

```csharp
namespace Kairo.Modules.Identity.Sessions;

internal sealed class SessionTimeouts
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromDays(3);

    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromDays(14);
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Sessions/DatabaseTicketStore.cs`:
- The cookie holds only a random key. The session itself lives in SQL Server, so deleting it ends every copy of the cookie.
- The idle limit comes from the cookie handler's sliding expiration, and this store enforces the absolute limit.
- Each call uses its own scope, so a session survives even when the request's own transaction rolls back.

```csharp
using System.Security.Cryptography;
using Kairo.Modules.Identity.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Kairo.Modules.Identity.Sessions;

internal sealed class DatabaseTicketStore(IServiceScopeFactory scopes, TimeProvider time, IOptions<SessionTimeouts> options)
    : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Sessions.Add(new Session
        {
            Key = key,
            UserId = ticket.Principal.FindUserId() ?? throw new InvalidOperationException("The ticket has no user."),
            Ticket = TicketSerializer.Default.Serialize(ticket),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + options.Value.AbsoluteTimeout,
        });
        await db.SaveChangesAsync();
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Key == key);
        if (session is null)
        {
            return;
        }

        session.Ticket = TicketSerializer.Default.Serialize(ticket);
        session.LastSeenAt = time.GetUtcNow();
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key);
        if (session is null)
        {
            return null;
        }

        if (time.GetUtcNow() >= session.ExpiresAt)
        {
            await db.Sessions.Where(s => s.Key == key).ExecuteDeleteAsync();
            return null;
        }

        return TicketSerializer.Default.Deserialize(session.Ticket);
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Sessions.Where(s => s.Key == key).ExecuteDeleteAsync();
    }
}
```

- [ ] **Step 6: Write the Identity DbContext**

Create `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContext.cs`. It also stores the Data Protection keys, so every API instance shares the keys that encrypt cookies.

```csharp
using Kairo.Modules.Identity.Sessions;
using Kairo.Modules.Identity.Users;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Kairo.Modules.Identity.Data;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database), IDataProtectionKeyContext
{
    public const string SchemaName = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasIndex(u => u.GoogleSubject).IsUnique();
            user.HasIndex(u => u.Email);
            user.Property(u => u.GoogleSubject).HasMaxLength(255);
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Name).HasMaxLength(200);
            user.Property(u => u.PictureUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("Sessions");
            session.HasKey(s => s.Key);
            session.Property(s => s.Key).HasMaxLength(64);
            session.HasIndex(s => s.UserId);
            session.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Identity.Data;

internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<IdentityDbContext>(
            IdentityDbContext.SchemaName,
            (options, database) => new IdentityDbContext(options, database));
}
```

- [ ] **Step 7: Write the Google sign-in**

Create `core/src/Modules/Kairo.Modules.Identity/SignIn/IdentitySchemes.cs`:

```csharp
namespace Kairo.Modules.Identity.SignIn;

internal static class IdentitySchemes
{
    public const string Session = "Kairo.Session";
    public const string Google = "Google";
    public const string SessionCookie = "__Host-kairo-session";
    public const string CallbackPath = "/api/auth/callback/google";
}
```

Create `core/src/Modules/Kairo.Modules.Identity/SignIn/GoogleAccount.cs`. Inbound claim mapping is off, so the claims keep Google's names:

```csharp
using System.Security.Claims;

namespace Kairo.Modules.Identity.SignIn;

internal sealed record GoogleAccount(string Subject, string Email, bool EmailVerified, string Name, string? PictureUrl)
{
    public static GoogleAccount From(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue("email") ?? "";
        return new GoogleAccount(
            principal.FindFirstValue("sub") ?? throw new InvalidOperationException("Google returned no account ID."),
            email,
            bool.TryParse(principal.FindFirstValue("email_verified"), out var verified) && verified,
            principal.FindFirstValue("name") ?? email,
            principal.FindFirstValue("picture"));
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/SignIn/GoogleAccountSignIn.cs`. Users are keyed on Google's account ID, emails are stored in lowercase, and an unverified email is turned away:

```csharp
using System.Security.Claims;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Users;

namespace Kairo.Modules.Identity.SignIn;

internal sealed class GoogleAccountSignIn(IdentityDbContext db, TimeProvider time)
{
    public const string UnverifiedEmail = "unverified_email";

    public async Task<ClaimsPrincipal?> SignInAsync(GoogleAccount account, CancellationToken cancellationToken)
    {
        if (!account.EmailVerified)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleSubject == account.Subject, cancellationToken);
        if (user is null)
        {
            user = new User { GoogleSubject = account.Subject, CreatedAt = now };
            db.Users.Add(user);
        }

        user.Email = account.Email.Trim().ToLowerInvariant();
        user.Name = account.Name;
        user.PictureUrl = account.PictureUrl;
        user.LastSignedInAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var identity = new ClaimsIdentity(
            [
                new Claim(KairoClaims.UserId, user.Id.ToString()),
                new Claim(KairoClaims.Name, user.Name),
                new Claim(KairoClaims.Email, user.Email),
            ],
            IdentitySchemes.Session,
            KairoClaims.Name,
            null);
        return new ClaimsPrincipal(identity);
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/SignIn/ReturnUrls.cs`. Only paths inside Kairo are allowed:

```csharp
namespace Kairo.Modules.Identity.SignIn;

internal static class ReturnUrls
{
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }

        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/";
        }

        return returnUrl.Any(char.IsControl) ? "/" : returnUrl;
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/SignIn/SignInSetup.cs`. Notes on the settings:
- Google returns the code in the query string (`ResponseMode.Query`). The correlation and nonce cookies can therefore be `SameSite=Lax`, which also works on `http://localhost`.
- Google issues tokens under either issuer name.
- The redirect URI is always built from `Kairo:PublicOrigin`, because the API runs behind the gateway.
- The cookie handler answers API calls with 401 or 403 rather than redirecting.

```csharp
using Kairo.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Kairo.Modules.Identity.SignIn;

internal static class SignInSetup
{
    public static void AddKairoSignIn(this IServiceCollection services)
    {
        services.AddOptions<SessionTimeouts>().BindConfiguration("Kairo:Sessions");
        services.AddSingleton<DatabaseTicketStore>();
        services.AddScoped<GoogleAccountSignIn>();
        services.AddAuthorization();

        services.AddAuthentication(IdentitySchemes.Session)
            .AddCookie(IdentitySchemes.Session, options =>
            {
                options.Cookie.Name = IdentitySchemes.SessionCookie;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(IdentitySchemes.Google, options =>
            {
                options.Authority = "https://accounts.google.com";
                options.CallbackPath = IdentitySchemes.CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.Prompt = "select_account";
                options.SignInScheme = IdentitySchemes.Session;
                options.MapInboundClaims = false;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.TokenValidationParameters.ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];
                options.Events.OnTokenValidated = async context =>
                {
                    var signIn = context.HttpContext.RequestServices.GetRequiredService<GoogleAccountSignIn>();
                    var principal = await signIn.SignInAsync(GoogleAccount.From(context.Principal!), context.HttpContext.RequestAborted);
                    if (principal is null)
                    {
                        context.Fail(GoogleAccountSignIn.UnverifiedEmail);
                        return;
                    }

                    context.Principal = principal;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    var error = context.Failure?.Message == GoogleAccountSignIn.UnverifiedEmail ? "unverified_email" : "sign_in_failed";
                    context.Response.Redirect($"/sign-in?error={error}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddOptions<CookieAuthenticationOptions>(IdentitySchemes.Session)
            .Configure<DatabaseTicketStore, IOptions<SessionTimeouts>, TimeProvider>((options, store, sessions, time) =>
            {
                options.SessionStore = store;
                options.ExpireTimeSpan = sessions.Value.IdleTimeout;
                options.TimeProvider = time;
            });

        services.AddOptions<OpenIdConnectOptions>(IdentitySchemes.Google)
            .Configure<IConfiguration, TimeProvider>((options, configuration, time) =>
            {
                options.ClientId = configuration["Authentication:Google:ClientId"];
                options.ClientSecret = configuration["Authentication:Google:ClientSecret"];
                options.TimeProvider = time;
                var origin = (configuration["Kairo:PublicOrigin"] ?? "").TrimEnd('/');
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    if (origin.Length > 0)
                    {
                        context.ProtocolMessage.RedirectUri = origin + IdentitySchemes.CallbackPath;
                    }

                    return Task.CompletedTask;
                };
            });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/SignIn/SignInEndpoints.cs`:

```csharp
using System.Security.Claims;
using Kairo.Modules.Identity.Data;
using Microsoft.AspNetCore.Authentication;

namespace Kairo.Modules.Identity.SignIn;

internal static class SignInEndpoints
{
    public static void MapSignIn(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapGet("/sign-in", (string? returnUrl) =>
            TypedResults.Challenge(
                new AuthenticationProperties { RedirectUri = ReturnUrls.Sanitize(returnUrl) },
                [IdentitySchemes.Google]));

        auth.MapPost("/sign-out", async (HttpContext http) =>
        {
            await http.SignOutAsync(IdentitySchemes.Session);
            return TypedResults.NoContent();
        });

        api.MapGet("/me", async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> (
            ClaimsPrincipal principal,
            IdentityDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.FindUserId();
            var me = await db.Users
                .Where(user => user.Id == userId)
                .Select(user => new MeResponse(user.Id, user.Name, user.Email, user.PictureUrl))
                .SingleOrDefaultAsync(cancellationToken);
            return me is null ? TypedResults.Unauthorized() : TypedResults.Ok(me);
        }).RequireAuthorization();
    }
}

internal sealed record MeResponse(Guid Id, string Name, string Email, string? PictureUrl);
```

Replace `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`:

```csharp
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.SignIn;
using Microsoft.AspNetCore.DataProtection;

namespace Kairo.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, IdentityDbContext.SchemaName));
        services.AddDataProtection()
            .SetApplicationName("Kairo")
            .PersistKeysToDbContext<IdentityDbContext>();
        services.AddKairoSignIn();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapSignIn();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
}
```

In `core/src/Kairo.Api/Program.cs`, add authentication and authorization right after `app.UseExceptionHandler();`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 8: Generate the migration**

```bash
dotnet ef migrations add SignIn --project core/src/Modules/Kairo.Modules.Identity --startup-project core/src/Kairo.Migrator --context IdentityDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*.cs
grep -rn "//\|public partial" core/src/Modules/Kairo.Modules.Identity/Data/Migrations/ || echo clean
```

Expected: `clean`. This migration needs no hand edits. It creates `identity.DataProtectionKeys`, `identity.Users` (unique on `GoogleSubject`, indexed on `Email`) and `identity.Sessions` (cascade-deleted with the user). None of these tables belongs to a workspace.

- [ ] **Step 9: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.SignInTests"
```

Expected: PASS, 17 test cases.

- [ ] **Step 10: Check everything still builds and passes**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: 0 warnings, and every test passes. That includes `Kairo.Api.Tests`, whose `/alive` and `/health` checks still run with the database unreachable, and `MigratorTests.Every_registration_resolves_in_development`.

- [ ] **Step 11: Commit**

```bash
git add core/Directory.Packages.props core/src/Modules/Kairo.Modules.Identity core/src/Kairo.Api/Program.cs core/tests/Kairo.IntegrationTests
git commit -m "Sign in with Google and keep sessions in SQL Server"
```

### Task 6: Workspaces

**Files:**
- Create: `core/src/Kairo.Contracts/Identity/IMembershipDirectory.cs`
- Create: `core/src/Kairo.Contracts/Identity/IWorkspaceFounding.cs`
- Create: `core/src/Kairo.Contracts/Brands/IBrandDirectory.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/Membership.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/ApprovalDuty.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/BudgetLimit.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/BrandAccess.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/MembershipDirectory.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/WorkspaceFounding.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContext.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*_Members.*`, generated
- Create: `core/src/Modules/Kairo.Modules.Brands/Data/Brand.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContextFactory.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Data/Migrations/*`, generated
- Create: `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceRules.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Workspaces/BrandDirectory.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/BrandsModule.cs`
- Create: `core/tests/Kairo.IntegrationTests/Api/Workspaces.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/WorkspaceTests.cs`

**Interfaces:**
- Consumes:
  - Tasks 1 and 2: the persistence plumbing, `MapBrandApi`, `RequireBrandRole`, `CurrentMember` and `IBrandAccess`.
  - Task 4: `IAuditLog`.
  - Task 5: `IdentityDbContext`, the `users` table, `KairoApiFactory` and `GoogleUser`.
- Produces:
  - **Contracts.**
    - `Kairo.Contracts.Identity.IMembershipDirectory.GetForUserAsync(Guid userId, CancellationToken)`, which returns `IReadOnlyList<BrandMembership>`, where `BrandMembership(Guid BrandId, BrandRoles Roles)`.
    - `Kairo.Contracts.Identity.IWorkspaceFounding.FoundAsync(Guid brandId, Guid founderId, string currency, CancellationToken)`. It needs the brand already set on `KairoDatabase`.
    - `Kairo.Contracts.Brands.IBrandDirectory.GetAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken)`, which returns `IReadOnlyList<BrandSummary>`, where `BrandSummary(Guid Id, string Handle, string Name)`.
  - **Identity types.**
    - `Membership`, `DutyAssignment` (table `identity.ApprovalDuties`) and `BudgetLimit`.
    - `enum ApprovalDuty { Brief, Assets, Launch, BudgetOwner }`, with an extension that returns the plain-words name, such as "brief approver".
    - `IBrandAccess` is implemented by `BrandAccess`.
  - **Endpoints.**
    - `POST /api/brands` with `CreateWorkspaceRequest(Name, Handle, TimeZone, Currency)` returns 201 `CreatedWorkspace(Id, Handle)`, or 400 with field errors.
    - `GET /api/brands` returns `WorkspaceSummary[]`, each `(Id, Handle, Name, Roles)`.
    - `GET /api/brands/{brandId}` returns `WorkspaceDetails(Id, Handle, Name, TimeZone, Currency, Roles)`. The roles are the caller's.
    - `PATCH /api/brands/{brandId}` with `UpdateWorkspaceRequest(Name?, TimeZone?)` returns `WorkspaceDetails`. Admins only.
  - **Audit actions:** `brand.created` and `brand.updated`.
  - **Test helpers** in `Workspaces`: `Member(Browser, UserId, Account)`, `NewHandle()`, `NewWorkspace(handle, name)`, `SignInAsync(api, name)`, `UserIdAsync(browser)` and `CreateAsync(browser, handle?)`.

- [ ] **Step 1: Write the test helpers and the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/Workspaces.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed record Member(HttpClient Browser, Guid UserId, GoogleUser Account);

internal static class Workspaces
{
    public static string NewHandle() => $"w{Guid.NewGuid():N}"[..20];

    public static object NewWorkspace(string handle, string name = "Acme Shoes") =>
        new { name, handle, timeZone = "Asia/Kolkata", currency = "INR" };

    public static async Task<Member> SignInAsync(KairoApiFactory api, string name = "Test User")
    {
        var account = GoogleUser.Create(name);
        var browser = await api.SignInAsync(account);
        return new Member(browser, await UserIdAsync(browser), account);
    }

    public static async Task<Guid> UserIdAsync(HttpClient browser) =>
        (await browser.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("id").GetGuid();

    public static async Task<Guid> CreateAsync(HttpClient browser, string? handle = null)
    {
        var response = await browser.PostAsJsonAsync("/api/brands", NewWorkspace(handle ?? NewHandle()));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Api/WorkspaceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.IntegrationTests.Api;

public sealed class WorkspaceTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_workspace_returns_where_to_find_it()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();

        var response = await owner.Browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(handle, created.GetProperty("handle").GetString());
        Assert.Equal($"/api/brands/{created.GetProperty("id").GetGuid()}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_creator_holds_every_role()
    {
        var owner = await Workspaces.SignInAsync(_api);
        await Workspaces.CreateAsync(owner.Browser);

        var mine = await owner.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token);

        var roles = Assert.Single(mine.EnumerateArray()).GetProperty("roles").EnumerateArray().Select(role => role.GetString());
        Assert.Equal(["admin", "marketer", "approver"], roles);
    }

    [Fact]
    public async Task People_see_only_their_own_workspaces()
    {
        var first = await Workspaces.SignInAsync(_api);
        var second = await Workspaces.SignInAsync(_api);
        var firstId = await Workspaces.CreateAsync(first.Browser);
        await Workspaces.CreateAsync(second.Browser);

        var mine = await first.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token);

        Assert.Equal(firstId, Assert.Single(mine.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Someone_outside_the_workspace_gets_not_found()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var stranger = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Browser.GetAsync($"/api/brands/{brandId}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Mine now" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Browser.GetAsync($"/api/brands/{Guid.NewGuid()}", Token)).StatusCode);
    }

    [Fact]
    public async Task Details_include_the_time_zone_currency_and_my_roles()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var details = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}", Token);

        Assert.Equal("Acme Shoes", details.GetProperty("name").GetString());
        Assert.Equal("Asia/Kolkata", details.GetProperty("timeZone").GetString());
        Assert.Equal("INR", details.GetProperty("currency").GetString());
        Assert.Equal(3, details.GetProperty("roles").GetArrayLength());
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("1acme")]
    [InlineData("acme--shoes")]
    [InlineData("acme-")]
    [InlineData("Acme")]
    [InlineData("acme_shoes")]
    [InlineData("sign-in")]
    [InlineData("api")]
    [InlineData("welcome")]
    [InlineData("settings")]
    public async Task Handles_must_follow_the_rules(string handle)
    {
        var owner = await Workspaces.SignInAsync(_api);

        var response = await owner.Browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("handle", out _));
    }

    [Fact]
    public async Task A_taken_handle_is_refused()
    {
        var first = await Workspaces.SignInAsync(_api);
        var second = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        await Workspaces.CreateAsync(first.Browser, handle);

        var response = await second.Browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal("That handle is taken.", errors.GetProperty("handle")[0].GetString());
    }

    [Theory]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("timeZone", "India Standard Time")]
    [InlineData("currency", "XYZ")]
    [InlineData("currency", "inr")]
    [InlineData("name", "")]
    public async Task Names_time_zones_and_currencies_are_checked(string field, string value)
    {
        var owner = await Workspaces.SignInAsync(_api);
        var request = new Dictionary<string, string>
        {
            ["name"] = "Acme Shoes",
            ["handle"] = Workspaces.NewHandle(),
            ["timeZone"] = "Asia/Kolkata",
            ["currency"] = "INR",
            [field] = value,
        };

        var response = await owner.Browser.PostAsJsonAsync("/api/brands", request, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task An_admin_can_rename_the_workspace_and_change_its_time_zone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear", timeZone = "Europe/London" }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}", Token);
        Assert.Equal(("Acme Footwear", "Europe/London"), (details.GetProperty("name").GetString(), details.GetProperty("timeZone").GetString()));
    }

    [Fact]
    public async Task Updates_are_checked_too()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { timeZone = "Nowhere/Special" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_failure_part_way_through_leaves_nothing_behind()
    {
        await using var failing = new KairoApiFactory(
            sqlServer.KairoConnectionString,
            services => services.Replace(ServiceDescriptor.Scoped<IAuditLog, FailingAuditLog>()));
        var account = GoogleUser.Create();
        var handle = Workspaces.NewHandle();

        var failed = await (await failing.SignInAsync(account)).PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var browser = await _api.SignInAsync(account);
        Assert.Empty((await browser.GetFromJsonAsync<JsonElement>("/api/brands", Token)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsJsonAsync("/api/brands", Workspaces.NewWorkspace(handle), Token)).StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private sealed class FailingAuditLog : IAuditLog
    {
        public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The audit log is down.");
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.WorkspaceTests"
```

Expected: FAIL. `POST /api/brands` doesn't exist yet, so creating a workspace gets 404 or 405.

- [ ] **Step 3: Write the contracts**

Create `core/src/Kairo.Contracts/Identity/IMembershipDirectory.cs`:

```csharp
using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Identity;

public interface IMembershipDirectory
{
    Task<IReadOnlyList<BrandMembership>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record BrandMembership(Guid BrandId, BrandRoles Roles);
```

Create `core/src/Kairo.Contracts/Identity/IWorkspaceFounding.cs`:

```csharp
namespace Kairo.Contracts.Identity;

public interface IWorkspaceFounding
{
    Task FoundAsync(Guid brandId, Guid founderId, string currency, CancellationToken cancellationToken);
}
```

Create `core/src/Kairo.Contracts/Brands/IBrandDirectory.cs`:

```csharp
namespace Kairo.Contracts.Brands;

public interface IBrandDirectory
{
    Task<IReadOnlyList<BrandSummary>> GetAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken);
}

public sealed record BrandSummary(Guid Id, string Handle, string Name);
```

- [ ] **Step 4: Write Identity's memberships, duties and budget limit**

Create `core/src/Modules/Kairo.Modules.Identity/Members/Membership.cs`:

```csharp
namespace Kairo.Modules.Identity.Members;

internal sealed class Membership : IBrandOwned
{
    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public BrandRoles Roles { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Members/ApprovalDuty.cs`:

```csharp
namespace Kairo.Modules.Identity.Members;

internal enum ApprovalDuty
{
    Brief,
    Assets,
    Launch,
    BudgetOwner,
}

internal sealed class DutyAssignment : IBrandOwned
{
    public Guid BrandId { get; set; }

    public ApprovalDuty Duty { get; set; }

    public Guid UserId { get; set; }
}

internal static class ApprovalDuties
{
    public static string Describe(this ApprovalDuty duty) => duty switch
    {
        ApprovalDuty.Brief => "brief approver",
        ApprovalDuty.Assets => "asset approver",
        ApprovalDuty.Launch => "launch approver",
        ApprovalDuty.BudgetOwner => "budget owner",
        _ => throw new ArgumentOutOfRangeException(nameof(duty), duty, null),
    };
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Members/BudgetLimit.cs`:

```csharp
namespace Kairo.Modules.Identity.Members;

internal sealed class BudgetLimit : IBrandOwned
{
    public Guid BrandId { get; set; }

    public decimal? Amount { get; set; }

    public string Currency { get; set; } = "";
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Members/BrandAccess.cs`. The membership check has to look across workspaces before it knows which brand to set, so it runs in the system scope:

```csharp
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class BrandAccess(IdentityDbContext db, KairoDatabase database) : IBrandAccess
{
    public async Task<BrandRoles?> FindRolesAsync(Guid brandId, Guid userId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Memberships
                .Where(membership => membership.BrandId == brandId && membership.UserId == userId)
                .Select(membership => (BrandRoles?)membership.Roles)
                .SingleOrDefaultAsync(cancellationToken);
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Members/MembershipDirectory.cs`:

```csharp
using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class MembershipDirectory(IdentityDbContext db, KairoDatabase database) : IMembershipDirectory
{
    public async Task<IReadOnlyList<BrandMembership>> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Memberships
                .Where(membership => membership.UserId == userId)
                .Select(membership => new BrandMembership(membership.BrandId, membership.Roles))
                .ToListAsync(cancellationToken);
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Members/WorkspaceFounding.cs`. It gives the creator every role and puts them on every duty, so a one-person workspace works straight away:

```csharp
using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal sealed class WorkspaceFounding(IdentityDbContext db, TimeProvider time) : IWorkspaceFounding
{
    public async Task FoundAsync(Guid brandId, Guid founderId, string currency, CancellationToken cancellationToken)
    {
        db.Memberships.Add(new Membership
        {
            BrandId = brandId,
            UserId = founderId,
            Roles = BrandRoles.All,
            JoinedAt = time.GetUtcNow(),
        });

        foreach (var duty in Enum.GetValues<ApprovalDuty>())
        {
            db.DutyAssignments.Add(new DutyAssignment { BrandId = brandId, Duty = duty, UserId = founderId });
        }

        db.BudgetLimits.Add(new BudgetLimit { BrandId = brandId, Currency = currency });
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

Replace `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContext.cs`:
- A duty assignment points at a membership, so removing a member also removes their duties.
- Duties are stored as strings, which keeps the table readable.

```csharp
using Kairo.Modules.Identity.Members;
using Kairo.Modules.Identity.Sessions;
using Kairo.Modules.Identity.Users;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Kairo.Modules.Identity.Data;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database), IDataProtectionKeyContext
{
    public const string SchemaName = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<DutyAssignment> DutyAssignments => Set<DutyAssignment>();

    public DbSet<BudgetLimit> BudgetLimits => Set<BudgetLimit>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasIndex(u => u.GoogleSubject).IsUnique();
            user.HasIndex(u => u.Email);
            user.Property(u => u.GoogleSubject).HasMaxLength(255);
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Name).HasMaxLength(200);
            user.Property(u => u.PictureUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.ToTable("Sessions");
            session.HasKey(s => s.Key);
            session.Property(s => s.Key).HasMaxLength(64);
            session.HasIndex(s => s.UserId);
            session.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("Memberships");
            membership.HasKey(m => new { m.BrandId, m.UserId });
            membership.HasIndex(m => m.UserId);
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DutyAssignment>(assignment =>
        {
            assignment.ToTable("ApprovalDuties");
            assignment.HasKey(a => new { a.BrandId, a.Duty, a.UserId });
            assignment.Property(a => a.Duty).HasConversion<string>().HasMaxLength(20);
            assignment.HasOne<Membership>().WithMany()
                .HasForeignKey(a => new { a.BrandId, a.UserId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BudgetLimit>(limit =>
        {
            limit.ToTable("BudgetLimits");
            limit.HasKey(l => l.BrandId);
            limit.Property(l => l.Amount).HasPrecision(19, 4);
            limit.Property(l => l.Currency).HasMaxLength(3).IsFixedLength();
        });
    }
}
```

Replace `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`:

```csharp
using Kairo.Contracts.Identity;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Members;
using Kairo.Modules.Identity.SignIn;
using Microsoft.AspNetCore.DataProtection;

namespace Kairo.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, IdentityDbContext.SchemaName));
        services.AddDataProtection()
            .SetApplicationName("Kairo")
            .PersistKeysToDbContext<IdentityDbContext>();
        services.AddKairoSignIn();
        services.AddScoped<IBrandAccess, BrandAccess>();
        services.AddScoped<IMembershipDirectory, MembershipDirectory>();
        services.AddScoped<IWorkspaceFounding, WorkspaceFounding>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapSignIn();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 5: Write the Brands module**

Create `core/src/Modules/Kairo.Modules.Brands/Data/Brand.cs`:

```csharp
namespace Kairo.Modules.Brands.Data;

internal sealed class Brand
{
    public Guid Id { get; set; }

    public string Handle { get; set; } = "";

    public string Name { get; set; } = "";

    public string TimeZone { get; set; } = "";

    public string Currency { get; set; } = "";

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContext.cs`. A brand's own `Id` is its brand, so the table gets its own filter in place of the `IBrandOwned` one:

```csharp
namespace Kairo.Modules.Brands.Data;

internal sealed class BrandsDbContext(DbContextOptions<BrandsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "brands";

    public DbSet<Brand> Brands => Set<Brand>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>(brand =>
        {
            brand.ToTable("Brands");
            brand.HasIndex(b => b.Handle).IsUnique();
            brand.Property(b => b.Handle).HasMaxLength(40);
            brand.Property(b => b.Name).HasMaxLength(100);
            brand.Property(b => b.TimeZone).HasMaxLength(64);
            brand.Property(b => b.Currency).HasMaxLength(3).IsFixedLength();
            brand.HasQueryFilter(b => AllBrands || b.Id == CurrentBrandId);
        });
    }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.Brands.Data;

internal sealed class BrandsDbContextFactory : IDesignTimeDbContextFactory<BrandsDbContext>
{
    public BrandsDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<BrandsDbContext>(
            BrandsDbContext.SchemaName,
            (options, database) => new BrandsDbContext(options, database));
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceRules.cs`. The rules are:
- **Handles** are 3 to 40 characters of lowercase letters, digits and single hyphens. They start with a letter and don't end with a hyphen.
- **Reserved words** that clash with top-level web routes can't be used as handles.
- **Time zones** must be IANA IDs.
- **Currencies** must be ISO 4217 codes.

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Brands.Workspaces;

internal static partial class WorkspaceRules
{
    private static readonly HashSet<string> ReservedHandles =
        ["api", "health", "sign-in", "sign-out", "welcome", "settings", "new", "admin", "app", "help"];

    private static readonly HashSet<string> Currencies = LoadCurrencies();

    public static string? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter the brand's name.";
        }

        return name.Trim().Length > 100 ? "Use 100 characters or fewer." : null;
    }

    public static string? CheckHandle(string? handle)
    {
        if (string.IsNullOrEmpty(handle))
        {
            return "Choose a handle.";
        }

        if (!HandlePattern().IsMatch(handle))
        {
            return "Use 3 to 40 lowercase letters, digits and single hyphens, starting with a letter.";
        }

        return ReservedHandles.Contains(handle) ? "That handle is reserved." : null;
    }

    public static string? CheckTimeZone(string? timeZone) =>
        timeZone is not null
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _)
            ? null
            : "Choose a time zone from the list.";

    public static string? CheckCurrency(string? currency) =>
        currency is not null && Currencies.Contains(currency) ? null : "Choose a currency from the list.";

    public static Dictionary<string, string[]> Errors(params (string Field, string? Error)[] checks) =>
        checks
            .Where(check => check.Error is not null)
            .ToDictionary(check => check.Field, check => new[] { check.Error! });

    private static HashSet<string> LoadCurrencies()
    {
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                currencies.Add(new RegionInfo(culture.Name).ISOCurrencySymbol);
            }
            catch (ArgumentException)
            {
            }
        }

        return currencies;
    }

    [GeneratedRegex("^[a-z](?:[a-z0-9]|-(?=[a-z0-9])){2,39}$")]
    private static partial Regex HandlePattern();
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Workspaces/BrandDirectory.cs`:

```csharp
using Kairo.Contracts.Brands;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Workspaces;

internal sealed class BrandDirectory(BrandsDbContext db, KairoDatabase database) : IBrandDirectory
{
    public async Task<IReadOnlyList<BrandSummary>> GetAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken)
    {
        var ids = brandIds.ToList();
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Brands
                .Where(brand => ids.Contains(brand.Id))
                .OrderBy(brand => brand.Name)
                .Select(brand => new BrandSummary(brand.Id, brand.Handle, brand.Name))
                .ToListAsync(cancellationToken);
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Workspaces/WorkspaceEndpoints.cs`. Creating a workspace goes through these steps, all in the request's single transaction:
1. Add the brand row, which gives it its ID.
2. Set that ID as the request's brand.
3. Save the brand.
4. Set up the founder in Identity.
5. Write the audit entry.

A race on the handle surfaces as SQL Server's unique-key error, which is reported as a field error.

```csharp
using System.Security.Claims;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Modules.Brands.Data;
using Microsoft.Data.SqlClient;

namespace Kairo.Modules.Brands.Workspaces;

internal static class WorkspaceEndpoints
{
    public static void MapWorkspaces(this IEndpointRouteBuilder api)
    {
        var workspaces = api.MapGroup("/brands").RequireAuthorization();
        workspaces.MapPost("", CreateAsync);
        workspaces.MapGet("", ListMineAsync);

        var workspace = api.MapBrandApi();
        workspace.MapGet("", GetAsync);
        workspace.MapPatch("", UpdateAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Results<Created<CreatedWorkspace>, ValidationProblem>> CreateAsync(
        CreateWorkspaceRequest request,
        ClaimsPrincipal principal,
        BrandsDbContext db,
        KairoDatabase database,
        IWorkspaceFounding founding,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = WorkspaceRules.Errors(
            ("name", WorkspaceRules.CheckName(request.Name)),
            ("handle", WorkspaceRules.CheckHandle(request.Handle)),
            ("timeZone", WorkspaceRules.CheckTimeZone(request.TimeZone)),
            ("currency", WorkspaceRules.CheckCurrency(request.Currency)));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            if (await db.Brands.AnyAsync(brand => brand.Handle == request.Handle, cancellationToken))
            {
                return HandleTaken();
            }
        }

        var userId = principal.FindUserId()!.Value;
        var brand = new Brand
        {
            Handle = request.Handle,
            Name = request.Name.Trim(),
            TimeZone = request.TimeZone,
            Currency = request.Currency,
            CreatedBy = userId,
            CreatedAt = time.GetUtcNow(),
        };
        db.Brands.Add(brand);
        await database.UseBrandAsync(brand.Id, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return HandleTaken();
        }

        await founding.FoundAsync(brand.Id, userId, brand.Currency, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand.created", "brand", brand.Id.ToString(), After: Snapshot(brand)),
            cancellationToken);
        return TypedResults.Created($"/api/brands/{brand.Id}", new CreatedWorkspace(brand.Id, brand.Handle));
    }

    private static async Task<Ok<WorkspaceSummary[]>> ListMineAsync(
        ClaimsPrincipal principal,
        IMembershipDirectory memberships,
        IBrandDirectory brands,
        CancellationToken cancellationToken)
    {
        var mine = await memberships.GetForUserAsync(principal.FindUserId()!.Value, cancellationToken);
        var roles = mine.ToDictionary(membership => membership.BrandId, membership => membership.Roles);
        var found = await brands.GetAsync(roles.Keys, cancellationToken);
        return TypedResults.Ok(found
            .Select(brand => new WorkspaceSummary(brand.Id, brand.Handle, brand.Name, roles[brand.Id].ToList()))
            .ToArray());
    }

    private static async Task<Results<Ok<WorkspaceDetails>, NotFound>> GetAsync(
        BrandsDbContext db,
        CurrentMember member,
        CancellationToken cancellationToken)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(cancellationToken);
        return brand is null ? TypedResults.NotFound() : TypedResults.Ok(Details(brand, member));
    }

    private static async Task<Results<Ok<WorkspaceDetails>, ValidationProblem, NotFound>> UpdateAsync(
        UpdateWorkspaceRequest request,
        BrandsDbContext db,
        CurrentMember member,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = WorkspaceRules.Errors(
            ("name", request.Name is null ? null : WorkspaceRules.CheckName(request.Name)),
            ("timeZone", request.TimeZone is null ? null : WorkspaceRules.CheckTimeZone(request.TimeZone)));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var brand = await db.Brands.SingleOrDefaultAsync(cancellationToken);
        if (brand is null)
        {
            return TypedResults.NotFound();
        }

        var before = Snapshot(brand);
        brand.Name = request.Name?.Trim() ?? brand.Name;
        brand.TimeZone = request.TimeZone ?? brand.TimeZone;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand.updated", "brand", brand.Id.ToString(), before, Snapshot(brand)),
            cancellationToken);
        return TypedResults.Ok(Details(brand, member));
    }

    private static ValidationProblem HandleTaken() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["handle"] = ["That handle is taken."] });

    private static object Snapshot(Brand brand) =>
        new { brand.Name, brand.Handle, brand.TimeZone, brand.Currency };

    private static WorkspaceDetails Details(Brand brand, CurrentMember member) =>
        new(brand.Id, brand.Handle, brand.Name, brand.TimeZone, brand.Currency, member.Roles.ToList());
}

internal sealed record CreateWorkspaceRequest(string Name, string Handle, string TimeZone, string Currency);

internal sealed record UpdateWorkspaceRequest(string? Name, string? TimeZone);

internal sealed record CreatedWorkspace(Guid Id, string Handle);

internal sealed record WorkspaceSummary(Guid Id, string Handle, string Name, BrandRole[] Roles);

internal sealed record WorkspaceDetails(Guid Id, string Handle, string Name, string TimeZone, string Currency, BrandRole[] Roles);
```

Replace `core/src/Modules/Kairo.Modules.Brands/BrandsModule.cs`:

```csharp
using Kairo.Contracts.Brands;
using Kairo.Modules.Brands.Data;
using Kairo.Modules.Brands.Workspaces;

namespace Kairo.Modules.Brands;

public sealed class BrandsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BrandsDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, BrandsDbContext.SchemaName));
        services.AddScoped<IBrandDirectory, BrandDirectory>();
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapWorkspaces();
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BrandsDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 6: Generate the two migrations and add their security policies**

```bash
dotnet ef migrations add Members --project core/src/Modules/Kairo.Modules.Identity --startup-project core/src/Kairo.Migrator --context IdentityDbContext --output-dir Data/Migrations
dotnet ef migrations add Workspaces --project core/src/Modules/Kairo.Modules.Brands --startup-project core/src/Kairo.Migrator --context BrandsDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*.cs core/src/Modules/Kairo.Modules.Brands/Data/Migrations/*.cs
```

Edit `core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*_Members.cs`. EF creates `BudgetLimits`, `Memberships` and `ApprovalDuties`, plus the indexes `IX_ApprovalDuties_BrandId_UserId` and `IX_Memberships_UserId`.

At the end of `Up`, after the last `CreateIndex` call, add:

```csharp

            migrationBuilder.CreateBrandAccessPredicate("identity");
            migrationBuilder.AddBrandSeparation("identity", "Memberships");
            migrationBuilder.AddBrandSeparation("identity", "ApprovalDuties");
            migrationBuilder.AddBrandSeparation("identity", "BudgetLimits");
```

At the start of `Down`, before the first `DropTable` call, add:

```csharp
            migrationBuilder.DropBrandSeparation("identity");

```

Edit `core/src/Modules/Kairo.Modules.Brands/Data/Migrations/*_Workspaces.cs`. EF creates `brands.Brands` and the unique index `IX_Brands_Handle`. The brand's own `Id` is the column the policy checks.

At the end of `Up`, after the `CreateIndex` call, add:

```csharp

            migrationBuilder.CreateBrandAccessPredicate("brands");
            migrationBuilder.AddBrandSeparation("brands", "Brands", "Id");
```

At the start of `Down`, add:

```csharp
            migrationBuilder.DropBrandSeparation("brands");

```

Check the tidy script's work:

```bash
grep -rn "//\|public partial" core/src/Modules/Kairo.Modules.Identity/Data/Migrations/ core/src/Modules/Kairo.Modules.Brands/Data/Migrations/ || echo clean
```

Expected: `clean`.

- [ ] **Step 7: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.WorkspaceTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.SecurityPolicyCoverageTests"
```

Expected: PASS. `WorkspaceTests` has 24 test cases. The coverage test now also checks the Identity and Brands tables.

- [ ] **Step 8: Check everything still builds and passes**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
```

Expected: 0 warnings, and every test passes. The architecture tests include the new `brands` and `identity` schema checks.

- [ ] **Step 9: Commit**

```bash
git add core/src/Kairo.Contracts core/src/Modules/Kairo.Modules.Identity core/src/Modules/Kairo.Modules.Brands core/tests/Kairo.IntegrationTests
git commit -m "Create workspaces whose founder holds every role and duty"
```

### Task 7: Audit log endpoint

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Audit/AuditLogEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Audit/AuditModule.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/Workspaces.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/AuditLogTests.cs`

**Interfaces:**
- Consumes:
  - from Task 4: `AuditDbContext`
  - from Task 2: `MapBrandApi` and `RequireBrandRole`
  - from Task 6: workspace creation and update, which write `brand.created` and `brand.updated`
- Produces:
  - `GET /api/brands/{brandId}/audit-log?before=&limit=`, for admins only. It returns `AuditLogPage(AuditEntryResponse[] Entries, string? NextBefore)`, newest first.
    - `limit` defaults to 50 and is clamped to between 1 and 200.
    - `NextBefore` is null on the last page.
  - `AuditEntryResponse(long Id, DateTimeOffset OccurredAt, string ActorType, string? ActorId, string ActorName, string Action, string TargetType, string TargetId, JsonNode? Before, JsonNode? After)`.
  - Test helpers `Workspaces.AuditActionsAsync(admin, brandId)`, which returns `string[]`, and `Workspaces.LatestAuditEntryAsync(admin, brandId)`, which returns `JsonElement`.

- [ ] **Step 1: Add the audit helpers and write the failing tests**

In `core/tests/Kairo.IntegrationTests/Api/Workspaces.cs`, add these two methods to the `Workspaces` class, after `CreateAsync`:

```csharp
    public static async Task<string[]> AuditActionsAsync(HttpClient admin, Guid brandId)
    {
        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=200");
        return page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()!).ToArray();
    }

    public static async Task<JsonElement> LatestAuditEntryAsync(HttpClient admin, Guid brandId)
    {
        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=1");
        return page.GetProperty("entries")[0];
    }
```

Create `core/tests/Kairo.IntegrationTests/Api/AuditLogTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class AuditLogTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admins_see_who_did_what_newest_first()
    {
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token);

        var page = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log", Token);

        var entries = page.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["brand.updated", "brand.created"], entries.Select(entry => entry.GetProperty("action").GetString()));
        Assert.All(entries, entry => Assert.Equal("Neha Rao", entry.GetProperty("actorName").GetString()));
        Assert.Equal("Acme Shoes", entries[0].GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Acme Footwear", entries[0].GetProperty("after").GetProperty("name").GetString());
        Assert.Equal("INR", entries[1].GetProperty("after").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task The_log_pages_with_a_cursor()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name }, Token);
        }

        var first = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=3", Token);
        var before = first.GetProperty("nextBefore").GetString();
        var second = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=3&before={before}", Token);

        Assert.Equal(3, first.GetProperty("entries").GetArrayLength());
        Assert.Equal("brand.created", Assert.Single(second.GetProperty("entries").EnumerateArray()).GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task Each_workspace_sees_only_its_own_entries()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{second}", new { name = "Second" }, Token);

        Assert.Equal(["brand.created"], await Workspaces.AuditActionsAsync(owner.Browser, first));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.AuditLogTests"
```

Expected: FAIL. `/api/brands/{brandId}/audit-log` answers 404.

- [ ] **Step 3: Write the endpoint**

Create `core/src/Modules/Kairo.Modules.Audit/AuditLogEndpoints.cs`. The cursor is the last entry's `Id`. Entries are numbered in the order they were written, so `Id` doubles as their order.

```csharp
using System.Text.Json.Nodes;
using Kairo.Modules.Audit.Data;

namespace Kairo.Modules.Audit;

internal static class AuditLogEndpoints
{
    public static void MapAuditLog(this IEndpointRouteBuilder api)
    {
        api.MapBrandApi().MapGet("/audit-log", ListAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Ok<AuditLogPage>> ListAsync(
        long? before,
        int? limit,
        AuditDbContext db,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Entries.AsNoTracking();
        if (before is { } cursor)
        {
            query = query.Where(entry => entry.Id < cursor);
        }

        var entries = await query
            .OrderByDescending(entry => entry.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var page = entries.Take(take).Select(ToResponse).ToList();
        var nextBefore = entries.Count > take ? page[^1].Id.ToString() : null;
        return TypedResults.Ok(new AuditLogPage(page, nextBefore));
    }

    private static AuditEntryResponse ToResponse(AuditEntry entry) => new(
        entry.Id,
        entry.OccurredAt,
        entry.ActorType,
        entry.ActorId,
        entry.ActorName,
        entry.Action,
        entry.TargetType,
        entry.TargetId,
        entry.Before is null ? null : JsonNode.Parse(entry.Before),
        entry.After is null ? null : JsonNode.Parse(entry.After));
}

internal sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string ActorType,
    string? ActorId,
    string ActorName,
    string Action,
    string TargetType,
    string TargetId,
    JsonNode? Before,
    JsonNode? After);

internal sealed record AuditLogPage(IReadOnlyList<AuditEntryResponse> Entries, string? NextBefore);
```

In `core/src/Modules/Kairo.Modules.Audit/AuditModule.cs`, map it:

```csharp
    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapAuditLog();
    }
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.AuditLogTests"
```

Expected: PASS, 3 tests.

- [ ] **Step 5: Check everything still builds and passes, then commit**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
git add core/src/Modules/Kairo.Modules.Audit core/tests/Kairo.IntegrationTests
git commit -m "Let admins page through their workspace's audit log"
```

Expected: 0 warnings, and every test passes before the commit.

### Task 8: Invitations

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Identity/Invitations/Invitation.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Invitations/InvitationEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContext.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*_Invitations.*`, generated
- Modify: `core/tests/Kairo.IntegrationTests/Api/Workspaces.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/InvitationTests.cs`

**Interfaces:**
- Consumes:
  - from Task 6: `Membership`, `IBrandDirectory` and `CurrentMember`
  - from Task 4: `IAuditLog`
  - from Task 5: `User`
- Produces:
  - **For admins:**
    - `GET /api/brands/{brandId}/invitations` returns `PendingInvitation[]`, each `(Id, Email, Roles, InvitedBy, SentAt, ExpiresAt)`.
    - `POST /api/brands/{brandId}/invitations` with `InviteRequest(Email, Roles)` returns 201 `PendingInvitation`, or 400.
    - `POST .../invitations/{invitationId}/revoke` returns 204 or 404.
  - **For the invited person:**
    - `GET /api/me/invitations` returns `MyInvitation[]`, each `(Id, WorkspaceName, WorkspaceHandle, Roles, InvitedBy, ExpiresAt)`.
    - `POST /api/me/invitations/{invitationId}/accept` returns 200 `JoinedWorkspace(Id, Handle)`, or 404.
    - `POST /api/me/invitations/{invitationId}/decline` returns 204 or 404.
  - **Audit actions:** `invitation.sent`, `invitation.revoked`, `invitation.accepted` and `invitation.declined`.
  - **Test helpers:** `Workspaces.InviteAsync(admin, brandId, email, params string[] roles)`, which returns the invitation ID, and `Workspaces.JoinAsync(api, admin, brandId, params string[] roles)`, which returns a `Member`.

- [ ] **Step 1: Add the invitation helpers and write the failing tests**

In `core/tests/Kairo.IntegrationTests/Api/Workspaces.cs`, add these two methods to the `Workspaces` class:

```csharp
    public static async Task<Guid> InviteAsync(HttpClient admin, Guid brandId, string email, params string[] roles)
    {
        var response = await admin.PostAsJsonAsync($"/api/brands/{brandId}/invitations", new { email, roles });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Member> JoinAsync(KairoApiFactory api, HttpClient admin, Guid brandId, params string[] roles)
    {
        var member = await SignInAsync(api, $"Member {Guid.NewGuid():N}"[..15]);
        var invitationId = await InviteAsync(admin, brandId, member.Account.Email, roles);
        (await member.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null)).EnsureSuccessStatusCode();
        return member;
    }
```

Create `core/tests/Kairo.IntegrationTests/Api/InvitationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class InvitationTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_invited_person_sees_the_invitation_when_they_sign_in()
    {
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);

        await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer", "approver");

        var mine = await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token);
        var invitation = Assert.Single(mine.EnumerateArray());
        Assert.Equal("Acme Shoes", invitation.GetProperty("workspaceName").GetString());
        Assert.Equal("Neha Rao", invitation.GetProperty("invitedBy").GetString());
        Assert.Equal(["marketer", "approver"], invitation.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Equal("invitation.sent", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Accepting_makes_them_a_member_with_the_invited_roles()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        var accepted = await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(brandId, (await accepted.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid());
        var mine = await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token);
        Assert.Equal(["marketer"], Assert.Single(mine.EnumerateArray()).GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Empty((await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
        Assert.Equal("invitation.accepted", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Someone_else_cannot_accept_an_invitation_meant_for_another_email()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, $"priya-{Guid.NewGuid():N}@example.com", "admin");
        var intruder = await Workspaces.SignInAsync(_api);

        var response = await intruder.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await intruder.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token)).EnumerateArray());
        Assert.Empty((await intruder.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
    }

    [Fact]
    public async Task Emails_match_whatever_their_case_and_spacing()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);

        await Workspaces.InviteAsync(owner.Browser, brandId, $"  {invitee.Account.Email.ToUpperInvariant()} ", "marketer");

        Assert.Single((await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
    }

    [Fact]
    public async Task An_invitation_expires_after_fourteen_days()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        _api.Clock.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        var signedInAgain = await _api.SignInAsync(invitee.Account);

        Assert.Empty((await signedInAgain.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await signedInAgain.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token)).StatusCode);
    }

    [Fact]
    public async Task A_revoked_invitation_cannot_be_accepted()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        var revoked = await owner.Browser.PostAsync($"/api/brands/{brandId}/invitations/{invitationId}/revoke", null, Token);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token)).StatusCode);
        Assert.Equal("invitation.revoked", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task A_declined_invitation_is_gone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        var declined = await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/decline", null, Token);

        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token)).StatusCode);
        Assert.Empty((await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/invitations", Token)).EnumerateArray());
        Assert.Equal("invitation.declined", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Members_cannot_be_invited_again()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var member = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await owner.Browser.PostAsJsonAsync(
            $"/api/brands/{brandId}/invitations",
            new { email = member.Account.Email, roles = new[] { "approver" } },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal("That person is already a member.", errors.GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task Inviting_again_replaces_the_pending_invitation()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var email = $"priya-{Guid.NewGuid():N}@example.com";

        var first = await Workspaces.InviteAsync(owner.Browser, brandId, email, "marketer");
        var second = await Workspaces.InviteAsync(owner.Browser, brandId, email, "approver");

        Assert.Equal(first, second);
        var pending = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/invitations", Token);
        Assert.Equal(["approver"], Assert.Single(pending.EnumerateArray()).GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Theory]
    [InlineData("not-an-email", "marketer")]
    [InlineData("Priya <priya@example.com>", "marketer")]
    [InlineData("priya@example.com", null)]
    public async Task Invitations_need_a_valid_email_and_a_role(string email, string? role)
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PostAsJsonAsync(
            $"/api/brands/{brandId}/invitations",
            new { email, roles = role is null ? Array.Empty<string>() : [role] },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_invite()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await marketer.Browser.PostAsJsonAsync(
            $"/api/brands/{brandId}/invitations",
            new { email = "someone@example.com", roles = new[] { "marketer" } },
            Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.InvitationTests"
```

Expected: FAIL. The invitation endpoints answer 404, so `InviteAsync` throws on `EnsureSuccessStatusCode`.

- [ ] **Step 3: Write the invitation and its endpoints**

Create `core/src/Modules/Kairo.Modules.Identity/Invitations/Invitation.cs`:

```csharp
namespace Kairo.Modules.Identity.Invitations;

internal enum InvitationStatus
{
    Pending,
    Accepted,
    Declined,
    Revoked,
}

internal sealed class Invitation : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Email { get; set; } = "";

    public BrandRoles Roles { get; set; }

    public Guid InvitedBy { get; set; }

    public DateTimeOffset SentAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public InvitationStatus Status { get; set; }

    public DateTimeOffset? RespondedAt { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Invitations/InvitationEndpoints.cs`:
- The invited person's own invitations span workspaces, so those queries run in the system scope, matched on their verified email from `identity.Users`.
- Accepting switches the request to the invitation's workspace before writing the membership and the audit entry.
- An invitation for anyone else's email answers 404, exactly like one that doesn't exist.

```csharp
using System.Net.Mail;
using System.Security.Claims;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Members;

namespace Kairo.Modules.Identity.Invitations;

internal static class InvitationEndpoints
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public static void MapInvitations(this IEndpointRouteBuilder api)
    {
        var sent = api.MapBrandApi().MapGroup("/invitations").RequireBrandRole(BrandRoles.Admin);
        sent.MapGet("", ListPendingAsync);
        sent.MapPost("", InviteAsync);
        sent.MapPost("/{invitationId:guid}/revoke", RevokeAsync);

        var mine = api.MapGroup("/me/invitations").RequireAuthorization();
        mine.MapGet("", ListMineAsync);
        mine.MapPost("/{invitationId:guid}/accept", AcceptAsync);
        mine.MapPost("/{invitationId:guid}/decline", DeclineAsync);
    }

    private static async Task<Ok<PendingInvitation[]>> ListPendingAsync(
        IdentityDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var rows = await db.Invitations
            .Where(invitation => invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt > now)
            .Join(db.Users, invitation => invitation.InvitedBy, user => user.Id, (invitation, user) => new
            {
                invitation.Id,
                invitation.Email,
                invitation.Roles,
                InvitedBy = user.Name,
                invitation.SentAt,
                invitation.ExpiresAt,
            })
            .OrderBy(row => row.Email)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(row => new PendingInvitation(row.Id, row.Email, row.Roles.ToList(), row.InvitedBy, row.SentAt, row.ExpiresAt))
            .ToArray());
    }

    private static async Task<Results<Created<PendingInvitation>, ValidationProblem>> InviteAsync(
        InviteRequest request,
        CurrentMember member,
        IdentityDbContext db,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var errors = new Dictionary<string, string[]>();
        if (email is null)
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (request.Roles is not { Length: > 0 })
        {
            errors["roles"] = ["Choose at least one role."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var alreadyMember = await db.Memberships
            .Join(db.Users, membership => membership.UserId, user => user.Id, (_, user) => user.Email)
            .AnyAsync(memberEmail => memberEmail == email, cancellationToken);
        if (alreadyMember)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["That person is already a member."] });
        }

        var now = time.GetUtcNow();
        var invitation = await db.Invitations.SingleOrDefaultAsync(
            existing => existing.Email == email && existing.Status == InvitationStatus.Pending,
            cancellationToken);
        if (invitation is null)
        {
            invitation = new Invitation { Email = email!, Status = InvitationStatus.Pending };
            db.Invitations.Add(invitation);
        }

        invitation.Roles = request.Roles.ToFlags();
        invitation.InvitedBy = member.UserId;
        invitation.SentAt = now;
        invitation.ExpiresAt = now + Lifetime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.sent", "invitation", invitation.Id.ToString(), After: new { invitation.Email, Roles = invitation.Roles.ToList() }),
            cancellationToken);

        var inviter = await db.Users.Where(user => user.Id == member.UserId).Select(user => user.Name).SingleAsync(cancellationToken);
        return TypedResults.Created(
            $"/api/brands/{member.BrandId}/invitations/{invitation.Id}",
            new PendingInvitation(invitation.Id, invitation.Email, invitation.Roles.ToList(), inviter, invitation.SentAt, invitation.ExpiresAt));
    }

    private static async Task<Results<NoContent, NotFound>> RevokeAsync(
        Guid invitationId,
        IdentityDbContext db,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(
            pending => pending.Id == invitationId && pending.Status == InvitationStatus.Pending,
            cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        invitation.Status = InvitationStatus.Revoked;
        invitation.RespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.revoked", "invitation", invitation.Id.ToString(), Before: new { invitation.Email }),
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<MyInvitation[]>> ListMineAsync(
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        IBrandDirectory brands,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = await EmailOfAsync(principal, db, cancellationToken);
        var now = time.GetUtcNow();
        List<InvitationRow> rows;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            rows = await db.Invitations
                .Where(invitation => invitation.Email == email && invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt > now)
                .Join(db.Users, invitation => invitation.InvitedBy, user => user.Id, (invitation, user) => new InvitationRow(invitation.Id, invitation.BrandId, invitation.Roles, user.Name, invitation.ExpiresAt))
                .ToListAsync(cancellationToken);
        }

        var workspaces = (await brands.GetAsync(rows.Select(row => row.BrandId).Distinct().ToList(), cancellationToken))
            .ToDictionary(workspace => workspace.Id);
        return TypedResults.Ok(rows
            .Where(row => workspaces.ContainsKey(row.BrandId))
            .Select(row => new MyInvitation(
                row.Id,
                workspaces[row.BrandId].Name,
                workspaces[row.BrandId].Handle,
                row.Roles.ToList(),
                row.InvitedBy,
                row.ExpiresAt))
            .ToArray());
    }

    private static async Task<Results<Ok<JoinedWorkspace>, NotFound>> AcceptAsync(
        Guid invitationId,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        IBrandDirectory brands,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invitation = await FindMineAsync(invitationId, principal, db, database, time, cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        var userId = principal.FindUserId()!.Value;
        var now = time.GetUtcNow();
        await database.UseBrandAsync(invitation.BrandId, cancellationToken);
        if (!await db.Memberships.AnyAsync(membership => membership.UserId == userId, cancellationToken))
        {
            db.Memberships.Add(new Membership
            {
                BrandId = invitation.BrandId,
                UserId = userId,
                Roles = invitation.Roles,
                JoinedAt = now,
            });
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.RespondedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.accepted", "invitation", invitation.Id.ToString(), After: new { invitation.Email, Roles = invitation.Roles.ToList() }),
            cancellationToken);

        var workspace = (await brands.GetAsync([invitation.BrandId], cancellationToken)).Single();
        return TypedResults.Ok(new JoinedWorkspace(workspace.Id, workspace.Handle));
    }

    private static async Task<Results<NoContent, NotFound>> DeclineAsync(
        Guid invitationId,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var invitation = await FindMineAsync(invitationId, principal, db, database, time, cancellationToken);
        if (invitation is null)
        {
            return TypedResults.NotFound();
        }

        await database.UseBrandAsync(invitation.BrandId, cancellationToken);
        invitation.Status = InvitationStatus.Declined;
        invitation.RespondedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("invitation.declined", "invitation", invitation.Id.ToString(), Before: new { invitation.Email }),
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Invitation?> FindMineAsync(
        Guid invitationId,
        ClaimsPrincipal principal,
        IdentityDbContext db,
        KairoDatabase database,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = await EmailOfAsync(principal, db, cancellationToken);
        var now = time.GetUtcNow();
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            return await db.Invitations.SingleOrDefaultAsync(
                invitation => invitation.Id == invitationId
                    && invitation.Email == email
                    && invitation.Status == InvitationStatus.Pending
                    && invitation.ExpiresAt > now,
                cancellationToken);
        }
    }

    private static Task<string> EmailOfAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken cancellationToken)
    {
        var userId = principal.FindUserId()!.Value;
        return db.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync(cancellationToken);
    }

    private static string? NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        return email is { Length: > 0 and <= 320 } && MailAddress.TryCreate(email, out var address) && address.Address == email
            ? email
            : null;
    }
}

internal sealed record InviteRequest(string Email, BrandRole[] Roles);

internal sealed record PendingInvitation(Guid Id, string Email, BrandRole[] Roles, string InvitedBy, DateTimeOffset SentAt, DateTimeOffset ExpiresAt);

internal sealed record MyInvitation(Guid Id, string WorkspaceName, string WorkspaceHandle, BrandRole[] Roles, string InvitedBy, DateTimeOffset ExpiresAt);

internal sealed record JoinedWorkspace(Guid Id, string Handle);

internal sealed record InvitationRow(Guid Id, Guid BrandId, BrandRoles Roles, string InvitedBy, DateTimeOffset ExpiresAt);
```

In `core/src/Modules/Kairo.Modules.Identity/Data/IdentityDbContext.cs`:

1. Add `using Kairo.Modules.Identity.Invitations;` to the `using` directives.
2. Add the set after `BudgetLimits`:

   ```csharp
       public DbSet<Invitation> Invitations => Set<Invitation>();
   ```

3. Add the mapping at the end of `ConfigureModel`. Only one invitation per email can be pending in a workspace at a time:

   ```csharp

           modelBuilder.Entity<Invitation>(invitation =>
           {
               invitation.ToTable("Invitations");
               invitation.Property(i => i.Email).HasMaxLength(320);
               invitation.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
               invitation.HasIndex(i => new { i.BrandId, i.Email })
                   .IsUnique()
                   .HasFilter("[Status] = N'Pending'");
               invitation.HasIndex(i => i.Email);
               invitation.HasOne<User>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Restrict);
           });
   ```

In `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`:

1. Add `using Kairo.Modules.Identity.Invitations;` to the `using` directives.
2. Map the endpoints:

   ```csharp
       public void MapApi(IEndpointRouteBuilder api)
       {
           api.MapSignIn();
           api.MapInvitations();
       }
   ```

- [ ] **Step 4: Generate the migration and add the table to the policy**

```bash
dotnet ef migrations add Invitations --project core/src/Modules/Kairo.Modules.Identity --startup-project core/src/Kairo.Migrator --context IdentityDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*.cs
```

Edit `core/src/Modules/Kairo.Modules.Identity/Data/Migrations/*_Invitations.cs`. EF creates `identity.Invitations` with the indexes `IX_Invitations_BrandId_Email` (unique, filtered on `[Status] = N'Pending'`), `IX_Invitations_Email` and `IX_Invitations_InvitedBy`. The schema's policy already exists, so this adds one table to it.

At the end of `Up`, after the last `CreateIndex` call, add:

```csharp

            migrationBuilder.AddBrandSeparation("identity", "Invitations");
```

At the start of `Down`, add:

```csharp
            migrationBuilder.RemoveBrandSeparation("identity", "Invitations");

```

```bash
grep -rn "//\|public partial" core/src/Modules/Kairo.Modules.Identity/Data/Migrations/ || echo clean
```

Expected: `clean`.

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.InvitationTests"
```

Expected: PASS, 13 test cases.

- [ ] **Step 6: Check everything still builds and passes, then commit**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
git add core/src/Modules/Kairo.Modules.Identity core/tests/Kairo.IntegrationTests
git commit -m "Invite people to a workspace and let them accept or decline"
```

Expected: 0 warnings, and every test passes before the commit. The coverage test now includes `identity.Invitations`.

### Task 9: Approval settings

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Identity/Approvals/ApprovalSettingsEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/ApprovalSettingsTests.cs`

**Interfaces:**
- Consumes:
  - from Task 6: `DutyAssignment`, `BudgetLimit`, `ApprovalDuty` and `Membership`
  - from Task 8: `Workspaces.JoinAsync`
  - from Task 7: `Workspaces.LatestAuditEntryAsync`
- Produces:
  - `GET /api/brands/{brandId}/approval-settings`, for any member. It returns `ApprovalSettings(Guid[] BriefApprovers, Guid[] AssetApprovers, Guid[] LaunchApprovers, Guid[] BudgetOwners, decimal? BudgetLimit, string Currency)`.
  - `PUT /api/brands/{brandId}/approval-settings`, for admins. It takes `SaveApprovalSettingsRequest(Guid[] BriefApprovers, Guid[] AssetApprovers, Guid[] LaunchApprovers, Guid[] BudgetOwners, decimal? BudgetLimit)` and returns `ApprovalSettings`, or 400 with field errors keyed `briefApprovers`, `assetApprovers`, `launchApprovers`, `budgetOwners` and `budgetLimit`.
  - Audit action `approval_settings.changed`, with the settings before and after.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/ApprovalSettingsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ApprovalSettingsTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_puts_its_creator_on_every_duty_with_no_budget_limit()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);

        foreach (var duty in new[] { "briefApprovers", "assetApprovers", "launchApprovers", "budgetOwners" })
        {
            Assert.Equal(owner.UserId, Assert.Single(settings.GetProperty(duty).EnumerateArray()).GetGuid());
        }

        Assert.Equal(JsonValueKind.Null, settings.GetProperty("budgetLimit").ValueKind);
        Assert.Equal("INR", settings.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task An_admin_can_share_duties_and_set_the_budget_limit()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver");

        var response = await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = new[] { owner.UserId, approver.UserId },
            assetApprovers = new[] { approver.UserId },
            launchApprovers = new[] { owner.UserId },
            budgetOwners = new[] { owner.UserId },
            budgetLimit = 200000m,
        }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await approver.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(2, settings.GetProperty("briefApprovers").GetArrayLength());
        Assert.Equal(approver.UserId, Assert.Single(settings.GetProperty("assetApprovers").EnumerateArray()).GetGuid());
        Assert.Equal(200000m, settings.GetProperty("budgetLimit").GetDecimal());
        Assert.Equal("approval_settings.changed", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Every_duty_needs_someone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await SaveAsync(owner, brandId, brief: [], budgetLimit: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("briefApprovers", out _));
    }

    [Fact]
    public async Task Only_members_with_the_approver_role_can_hold_a_duty()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        var stranger = await Workspaces.SignInAsync(_api);

        var withMarketer = await SaveAsync(owner, brandId, brief: [marketer.UserId], budgetLimit: null);
        var withStranger = await SaveAsync(owner, brandId, brief: [stranger.UserId], budgetLimit: null);

        Assert.Equal(HttpStatusCode.BadRequest, withMarketer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withStranger.StatusCode);
    }

    [Fact]
    public async Task The_budget_limit_cannot_be_negative()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await SaveAsync(owner, brandId, brief: [owner.UserId], budgetLimit: -1m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors").TryGetProperty("budgetLimit", out _));
    }

    [Fact]
    public async Task The_budget_limit_can_be_cleared()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await SaveAsync(owner, brandId, brief: [owner.UserId], budgetLimit: 5000m);

        await SaveAsync(owner, brandId, brief: [owner.UserId], budgetLimit: null);

        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("budgetLimit").ValueKind);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static Task<HttpResponseMessage> SaveAsync(Member owner, Guid brandId, Guid[] brief, decimal? budgetLimit) =>
        owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = brief,
            assetApprovers = new[] { owner.UserId },
            launchApprovers = new[] { owner.UserId },
            budgetOwners = new[] { owner.UserId },
            budgetLimit,
        }, Token);
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ApprovalSettingsTests"
```

Expected: FAIL. `/api/brands/{brandId}/approval-settings` answers 404.

- [ ] **Step 3: Write the endpoints**

Create `core/src/Modules/Kairo.Modules.Identity/Approvals/ApprovalSettingsEndpoints.cs`:
- Saving replaces the people on every duty and the budget limit together.
- Only the differences are written, so an unchanged assignment is never deleted and re-added within the same save.

```csharp
using Kairo.Contracts.Audit;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Members;

namespace Kairo.Modules.Identity.Approvals;

internal static class ApprovalSettingsEndpoints
{
    private const decimal LargestBudgetLimit = 999_999_999_999_999.9999m;

    public static void MapApprovalSettings(this IEndpointRouteBuilder api)
    {
        var settings = api.MapBrandApi().MapGroup("/approval-settings");
        settings.MapGet("", GetAsync);
        settings.MapPut("", SaveAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Ok<ApprovalSettings>> GetAsync(IdentityDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await LoadAsync(db, cancellationToken));

    private static async Task<Results<Ok<ApprovalSettings>, ValidationProblem>> SaveAsync(
        SaveApprovalSettingsRequest request,
        CurrentMember member,
        IdentityDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        (string Field, ApprovalDuty Duty, Guid[]? People)[] duties =
        [
            ("briefApprovers", ApprovalDuty.Brief, request.BriefApprovers),
            ("assetApprovers", ApprovalDuty.Assets, request.AssetApprovers),
            ("launchApprovers", ApprovalDuty.Launch, request.LaunchApprovers),
            ("budgetOwners", ApprovalDuty.BudgetOwner, request.BudgetOwners),
        ];

        var approvers = (await db.Memberships
                .Where(membership => (membership.Roles & BrandRoles.Approver) == BrandRoles.Approver)
                .Select(membership => membership.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var errors = new Dictionary<string, string[]>();
        foreach (var (field, _, people) in duties)
        {
            if (people is not { Length: > 0 })
            {
                errors[field] = ["Choose at least one person."];
            }
            else if (people.Any(person => !approvers.Contains(person)))
            {
                errors[field] = ["Only members with the approver role can hold this duty."];
            }
        }

        if (request.BudgetLimit is < 0 or > LargestBudgetLimit)
        {
            errors["budgetLimit"] = ["Enter an amount of zero or more, or leave it empty."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var before = await LoadAsync(db, cancellationToken);
        var desired = duties
            .SelectMany(duty => duty.People!.Distinct().Select(person => (duty.Duty, UserId: person)))
            .ToHashSet();
        var current = await db.DutyAssignments.ToListAsync(cancellationToken);
        db.DutyAssignments.RemoveRange(current.Where(assignment => !desired.Contains((assignment.Duty, assignment.UserId))));
        foreach (var (duty, userId) in desired.Where(wanted => !current.Any(a => a.Duty == wanted.Duty && a.UserId == wanted.UserId)))
        {
            db.DutyAssignments.Add(new DutyAssignment { Duty = duty, UserId = userId });
        }

        var limit = await db.BudgetLimits.SingleAsync(cancellationToken);
        limit.Amount = request.BudgetLimit;
        await db.SaveChangesAsync(cancellationToken);

        var after = await LoadAsync(db, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("approval_settings.changed", "brand", member.BrandId.ToString(), before, after),
            cancellationToken);
        return TypedResults.Ok(after);
    }

    private static async Task<ApprovalSettings> LoadAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        var assignments = await db.DutyAssignments.AsNoTracking().ToListAsync(cancellationToken);
        var limit = await db.BudgetLimits.AsNoTracking().SingleAsync(cancellationToken);

        Guid[] On(ApprovalDuty duty) =>
            assignments.Where(assignment => assignment.Duty == duty).Select(assignment => assignment.UserId).Order().ToArray();

        return new ApprovalSettings(
            On(ApprovalDuty.Brief),
            On(ApprovalDuty.Assets),
            On(ApprovalDuty.Launch),
            On(ApprovalDuty.BudgetOwner),
            limit.Amount,
            limit.Currency);
    }
}

internal sealed record ApprovalSettings(
    Guid[] BriefApprovers,
    Guid[] AssetApprovers,
    Guid[] LaunchApprovers,
    Guid[] BudgetOwners,
    decimal? BudgetLimit,
    string Currency);

internal sealed record SaveApprovalSettingsRequest(
    Guid[] BriefApprovers,
    Guid[] AssetApprovers,
    Guid[] LaunchApprovers,
    Guid[] BudgetOwners,
    decimal? BudgetLimit);
```

In `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`:

1. Add `using Kairo.Modules.Identity.Approvals;` to the `using` directives.
2. Map the endpoints:

   ```csharp
       public void MapApi(IEndpointRouteBuilder api)
       {
           api.MapSignIn();
           api.MapInvitations();
           api.MapApprovalSettings();
       }
   ```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ApprovalSettingsTests"
```

Expected: PASS, 6 tests.

- [ ] **Step 5: Check everything still builds and passes, then commit**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
git add core/src/Modules/Kairo.Modules.Identity core/tests/Kairo.IntegrationTests
git commit -m "Let admins choose approvers per duty and set the budget limit"
```

Expected: 0 warnings, and every test passes before the commit.

### Task 10: Team management and the permission matrix

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/TeamRules.cs`
- Create: `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/TeamTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs`

**Interfaces:**
- Consumes:
  - from Task 6: `Membership`, `DutyAssignment` and `ApprovalDuty.Describe()`
  - from Task 8: `Workspaces.JoinAsync`
  - from Task 9: the approval settings endpoint, used to arrange duties in tests
- Produces:
  - `GET /api/brands/{brandId}/members`, for any member. It returns `MemberResponse[]`, each `(UserId, Name, Email, PictureUrl, Roles, JoinedAt)`, sorted by name.
  - `PUT /api/brands/{brandId}/members/{userId}/roles`, for admins. It takes `ChangeRolesRequest(BrandRole[] Roles)` and returns 200 `MemberResponse`, 400, 404, or 409 with a `title` that explains the reason.
  - `DELETE /api/brands/{brandId}/members/{userId}`, for an admin, or for members removing themselves. It returns 204, 403, 404 or 409.
  - Audit actions `member.roles_changed`, `member.removed` and `member.left`.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/TeamTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class TeamTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_member_can_see_the_team()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var members = await marketer.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/members", Token);

        Assert.Equal(
            new[] { owner.UserId, marketer.UserId }.Order(),
            members.EnumerateArray().Select(member => member.GetProperty("userId").GetGuid()).Order());
    }

    [Fact]
    public async Task An_admin_can_change_someones_roles()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await ChangeRolesAsync(owner, brandId, marketer.UserId, "marketer", "approver");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId);
        Assert.Equal("member.roles_changed", entry.GetProperty("action").GetString());
        Assert.Equal(2, entry.GetProperty("after").GetProperty("roles").GetArrayLength());
    }

    [Fact]
    public async Task Every_member_keeps_at_least_one_role()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await ChangeRolesAsync(owner, brandId, marketer.UserId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_demoted_removed_or_leave()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var demote = await ChangeRolesAsync(owner, brandId, owner.UserId, "marketer", "approver");
        var leave = await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{owner.UserId}", Token);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        Assert.Contains("at least one admin", (await demote.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_only_person_on_a_duty_keeps_the_approver_role()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var secondAdmin = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");

        var response = await ChangeRolesAsync(secondAdmin, brandId, owner.UserId, "admin", "marketer");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("only brief approver", (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Taking_away_the_approver_role_takes_them_off_their_duties()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var approver = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "approver", "marketer");
        await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = new[] { owner.UserId, approver.UserId },
            assetApprovers = new[] { owner.UserId },
            launchApprovers = new[] { owner.UserId },
            budgetOwners = new[] { owner.UserId },
            budgetLimit = (decimal?)null,
        }, Token);

        var response = await ChangeRolesAsync(owner, brandId, approver.UserId, "marketer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/approval-settings", Token);
        Assert.Equal(owner.UserId, Assert.Single(settings.GetProperty("briefApprovers").EnumerateArray()).GetGuid());
    }

    [Fact]
    public async Task An_admin_can_leave_once_someone_else_can_take_over()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var successor = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin", "approver");
        await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/approval-settings", new
        {
            briefApprovers = new[] { successor.UserId },
            assetApprovers = new[] { successor.UserId },
            launchApprovers = new[] { successor.UserId },
            budgetOwners = new[] { successor.UserId },
            budgetLimit = (decimal?)null,
        }, Token);

        var leave = await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{owner.UserId}", Token);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Browser.GetAsync($"/api/brands/{brandId}", Token)).StatusCode);
        Assert.Equal("member.left", (await Workspaces.LatestAuditEntryAsync(successor.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Members_who_are_not_admins_can_leave_but_cannot_remove_others()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var removeOwner = await marketer.Browser.DeleteAsync($"/api/brands/{brandId}/members/{owner.UserId}", Token);
        var leave = await marketer.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token);

        Assert.Equal(HttpStatusCode.Forbidden, removeOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }

    [Fact]
    public async Task A_removed_member_loses_access_straight_away()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var removed = await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{marketer.UserId}", Token);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await marketer.Browser.GetAsync($"/api/brands/{brandId}", Token)).StatusCode);
        Assert.Equal("member.removed", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static Task<HttpResponseMessage> ChangeRolesAsync(Member admin, Guid brandId, Guid userId, params string[] roles) =>
        admin.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{userId}/roles", new { roles }, Token);
}
```

Create `core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs`. It checks PRD §8 against every workspace endpoint for three people: an admin, a marketer only and an approver only.
- A role that isn't allowed must get 403.
- A role that is allowed must get past the role check. The requests use harmless payloads, such as an unknown ID or an invalid body, so they change nothing.

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class PermissionTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    [Fact]
    public async Task Each_role_can_do_only_what_the_PRD_allows()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "approver");
        var someone = Guid.NewGuid();
        var brand = $"/api/brands/{brandId}";
        var emptyDuties = new { briefApprovers = Array.Empty<Guid>(), assetApprovers = Array.Empty<Guid>(), launchApprovers = Array.Empty<Guid>(), budgetOwners = Array.Empty<Guid>() };

        (HttpMethod Method, string Path, object? Body, bool OnlyAdmins)[] endpoints =
        [
            (HttpMethod.Get, brand, null, false),
            (HttpMethod.Patch, brand, new { }, true),
            (HttpMethod.Get, $"{brand}/members", null, false),
            (HttpMethod.Put, $"{brand}/members/{someone}/roles", new { roles = new[] { "marketer" } }, true),
            (HttpMethod.Delete, $"{brand}/members/{someone}", null, true),
            (HttpMethod.Get, $"{brand}/invitations", null, true),
            (HttpMethod.Post, $"{brand}/invitations", new { email = "not-an-email", roles = Array.Empty<string>() }, true),
            (HttpMethod.Post, $"{brand}/invitations/{someone}/revoke", null, true),
            (HttpMethod.Get, $"{brand}/approval-settings", null, false),
            (HttpMethod.Put, $"{brand}/approval-settings", emptyDuties, true),
            (HttpMethod.Get, $"{brand}/audit-log", null, true),
        ];

        var wrong = new List<string>();
        foreach (var (method, path, body, onlyAdmins) in endpoints)
        {
            foreach (var (name, member, isAdmin) in new[] { ("admin", admin, true), ("marketer", marketer, false), ("approver", approver, false) })
            {
                var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
                var status = (await member.Browser.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
                var forbidden = status == HttpStatusCode.Forbidden;
                if (forbidden != (onlyAdmins && !isAdmin))
                {
                    wrong.Add($"{method} {path} as {name} returned {(int)status}");
                }
            }
        }

        Assert.Empty(wrong);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.TeamTests"
```

Expected: FAIL. The members endpoints answer 404 or 405.

- [ ] **Step 3: Write the team rules and endpoints**

Create `core/src/Modules/Kairo.Modules.Identity/Members/TeamRules.cs`. Duties are checked in their declared order, so the reason given is predictable:

```csharp
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal static class TeamRules
{
    public const string LastAdmin = "A workspace needs at least one admin. Make someone else an admin first.";

    public static async Task<bool> IsLastAdminAsync(IdentityDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var admins = await db.Memberships
            .Where(membership => (membership.Roles & BrandRoles.Admin) == BrandRoles.Admin)
            .Select(membership => membership.UserId)
            .ToListAsync(cancellationToken);
        return admins is [var only] && only == userId;
    }

    public static async Task<string?> SoleDutyAsync(IdentityDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var assignments = await db.DutyAssignments.ToListAsync(cancellationToken);
        foreach (var duty in Enum.GetValues<ApprovalDuty>())
        {
            var holders = assignments.Where(assignment => assignment.Duty == duty).Select(assignment => assignment.UserId).ToList();
            if (holders is [var only] && only == userId)
            {
                return $"This member is the only {duty.Describe()}. Give that duty to someone else first.";
            }
        }

        return null;
    }
}
```

Create `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal static class MemberEndpoints
{
    public static void MapMembers(this IEndpointRouteBuilder api)
    {
        var members = api.MapBrandApi().MapGroup("/members");
        members.MapGet("", ListAsync);
        members.MapPut("/{userId:guid}/roles", ChangeRolesAsync).RequireBrandRole(BrandRoles.Admin);
        members.MapDelete("/{userId:guid}", RemoveAsync);
    }

    private static async Task<Ok<MemberResponse[]>> ListAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Memberships
            .Join(db.Users, membership => membership.UserId, user => user.Id, (membership, user) => new
            {
                membership.UserId,
                user.Name,
                user.Email,
                user.PictureUrl,
                membership.Roles,
                membership.JoinedAt,
            })
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(row => new MemberResponse(row.UserId, row.Name, row.Email, row.PictureUrl, row.Roles.ToList(), row.JoinedAt))
            .ToArray());
    }

    private static async Task<Results<Ok<MemberResponse>, ValidationProblem, NotFound, ProblemHttpResult>> ChangeRolesAsync(
        Guid userId,
        ChangeRolesRequest request,
        IdentityDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (request.Roles is not { Length: > 0 })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["Choose at least one role."] });
        }

        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        var before = membership.Roles;
        var after = request.Roles.ToFlags();
        if (before.HasFlag(BrandRoles.Admin) && !after.HasFlag(BrandRoles.Admin)
            && await TeamRules.IsLastAdminAsync(db, userId, cancellationToken))
        {
            return Conflict(TeamRules.LastAdmin);
        }

        if (before.HasFlag(BrandRoles.Approver) && !after.HasFlag(BrandRoles.Approver))
        {
            if (await TeamRules.SoleDutyAsync(db, userId, cancellationToken) is { } sole)
            {
                return Conflict(sole);
            }

            db.DutyAssignments.RemoveRange(await db.DutyAssignments.Where(a => a.UserId == userId).ToListAsync(cancellationToken));
        }

        membership.Roles = after;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("member.roles_changed", "member", userId.ToString(), new { Roles = before.ToList() }, new { Roles = after.ToList() }),
            cancellationToken);

        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        return TypedResults.Ok(new MemberResponse(user.Id, user.Name, user.Email, user.PictureUrl, after.ToList(), membership.JoinedAt));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid userId,
        CurrentMember member,
        IdentityDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var leaving = userId == member.UserId;
        if (!leaving && !member.Has(BrandRoles.Admin))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only admins can remove other members.");
        }

        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (membership is null)
        {
            return TypedResults.NotFound();
        }

        if (membership.Roles.HasFlag(BrandRoles.Admin) && await TeamRules.IsLastAdminAsync(db, userId, cancellationToken))
        {
            return Conflict(TeamRules.LastAdmin);
        }

        if (await TeamRules.SoleDutyAsync(db, userId, cancellationToken) is { } sole)
        {
            return Conflict(sole);
        }

        db.DutyAssignments.RemoveRange(await db.DutyAssignments.Where(a => a.UserId == userId).ToListAsync(cancellationToken));
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(leaving ? "member.left" : "member.removed", "member", userId.ToString(), new { Roles = membership.Roles.ToList() }),
            cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Conflict(string message) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: message);
}

internal sealed record MemberResponse(Guid UserId, string Name, string Email, string? PictureUrl, BrandRole[] Roles, DateTimeOffset JoinedAt);

internal sealed record ChangeRolesRequest(BrandRole[] Roles);
```

In `core/src/Modules/Kairo.Modules.Identity/IdentityModule.cs`, map the endpoints. The `Members` namespace is already imported.

```csharp
    public void MapApi(IEndpointRouteBuilder api)
    {
        api.MapSignIn();
        api.MapMembers();
        api.MapInvitations();
        api.MapApprovalSettings();
    }
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.TeamTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.PermissionTests"
```

Expected: PASS, 9 team tests and 1 permission test.

- [ ] **Step 5: Check everything still builds and passes, then commit**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
git add core/src/Modules/Kairo.Modules.Identity core/tests/Kairo.IntegrationTests
git commit -m "Manage the team's roles and members, and test every role against PRD section 8"
```

Expected: 0 warnings, and every test passes before the commit.

### Task 11: OpenAPI contract

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/src/Kairo.Api/Kairo.Api.csproj`
- Create: `core/src/Kairo.Api/KairoOpenApi.cs`
- Modify: `core/src/Kairo.Api/Program.cs`
- Create: `core/src/Kairo.Api/openapi.json`, generated by the test below
- Test: `core/tests/Kairo.IntegrationTests/Api/OpenApiDocumentTests.cs`

**Interfaces:**
- Consumes: every endpoint from Tasks 5 to 10.
- Produces:
  - `GET /openapi/v1.json`, in Development only.
  - `core/src/Kairo.Api/openapi.json`, the committed contract that the web app generates its types from in Task 12.
  - `KAIRO_UPDATE_OPENAPI=1`. When this is set, the drift test rewrites the committed file instead of comparing.

- [ ] **Step 1: Write the failing test**

Create `core/tests/Kairo.IntegrationTests/Api/OpenApiDocumentTests.cs`. Both sides are normalised to the same indentation and line endings, so the check is about content only:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kairo.IntegrationTests.Api;

public sealed class OpenApiDocumentTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    [Fact]
    public async Task The_committed_document_matches_the_api()
    {
        var actual = Normalize(await _api.CreateBrowser().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        var path = Path.Combine(FindCoreDirectory(), "src", "Kairo.Api", "openapi.json");

        if (Environment.GetEnvironmentVariable("KAIRO_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run the tests with KAIRO_UPDATE_OPENAPI=1 to write it.");
        Assert.Equal(Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)), actual);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

    private static string FindCoreDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kairo.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the core folder.");
    }
}
```

- [ ] **Step 2: Run the test to see it fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
```

Expected: FAIL with `HttpRequestException`, because `/openapi/v1.json` answers 404.

- [ ] **Step 3: Generate the document from the API**

In `core/Directory.Packages.props`, add to the `Web` item group:

```xml
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
```

Replace `core/src/Kairo.Api/Kairo.Api.csproj`. The committed document is for the web app, not for the API's published output.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Aspire.Microsoft.Data.SqlClient" />
    <PackageReference Include="Aspire.StackExchange.Redis" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  </ItemGroup>

  <ItemGroup>
    <Content Remove="openapi.json" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kairo.ServiceDefaults\Kairo.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Kairo.SharedKernel\Kairo.SharedKernel.csproj" />
    <ProjectReference Include="../Modules/*/Kairo.Modules.*.csproj" />
  </ItemGroup>

</Project>
```

Create `core/src/Kairo.Api/KairoOpenApi.cs`. It makes two changes to the generated document:
- It drops the `servers` list, which would otherwise depend on whichever host and port served the request.
- It declares route parameters such as `{brandId}` that no handler binds. The workspace filter reads `brandId`, not the endpoint, and the generated web client needs to know about it.

```csharp
using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace Kairo.Api;

public static partial class KairoOpenApi
{
    public static IServiceCollection AddKairoOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Servers = [];
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                foreach (Match match in RouteParameter().Matches(context.Description.RelativePath ?? ""))
                {
                    var name = match.Groups["name"].Value;
                    operation.Parameters ??= [];
                    if (operation.Parameters.Any(parameter => parameter.Name == name && parameter.In == ParameterLocation.Path))
                    {
                        continue;
                    }

                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = name,
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    });
                }

                return Task.CompletedTask;
            });
        });

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)")]
    private static partial Regex RouteParameter();
}
```

In `core/src/Kairo.Api/Program.cs`, make three changes:
1. Add `using Kairo.Api;` to the `using` directives.
2. Register the document after the `ConfigureHttpJsonOptions` call:

   ```csharp
   builder.Services.AddKairoOpenApi();
   ```

3. Serve it in Development only, right after `app.MapDefaultEndpoints();`:

   ```csharp

   if (app.Environment.IsDevelopment())
   {
       app.MapOpenApi();
   }
   ```

- [ ] **Step 4: Write the committed document**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
grep -c '"/api/' core/src/Kairo.Api/openapi.json
grep -c '"servers"' core/src/Kairo.Api/openapi.json || true
```

Expected:
- the test passes after writing the file
- the first `grep` prints `15`, the number of distinct API paths
- the second prints `0`

Open the file and check two things. `BrandRole` is an `enum` of `"admin"`, `"marketer"` and `"approver"`. Every `/api/brands/{brandId}...` operation lists a required `brandId` path parameter.

- [ ] **Step 5: Run the test to see it pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
```

Expected: PASS.

- [ ] **Step 6: Check everything still builds and passes, then commit**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
git add core/Directory.Packages.props core/src/Kairo.Api core/tests/Kairo.IntegrationTests
git commit -m "Publish the core's OpenAPI document and keep the committed copy current"
```

Expected: 0 warnings, and every test passes before the commit.

### Task 12: Web: workspace routes, sign-in, proxy and API client

**Files:**
- Modify: `web/package.json` and `web/package-lock.json`, through `npm install`
- Create: `web/scripts/generate-api.mjs`
- Create: `web/src/lib/core-api/schema.ts`, generated
- Modify: `web/eslint.config.mjs`
- Create: `web/src/lib/http-names.ts`
- Create: `web/src/lib/core-api/client.ts`
- Create: `web/src/lib/core-api/queries.ts`
- Create: `web/src/lib/core-api/types.ts`
- Create: `web/src/proxy.ts`
- Create: `web/src/app/route.ts`
- Delete: `web/src/app/page.tsx`
- Modify: `web/src/app/layout.tsx`
- Create: `web/src/app/sign-in/page.tsx`
- Create: `web/src/app/sign-in/_components/sign-in-panel.tsx`
- Move: `web/src/app/{today,opportunities,campaigns,triggers,impact-lab,brand-kit,settings}` to `web/src/app/[workspace]/`
- Create: `web/src/app/[workspace]/layout.tsx`
- Create: `web/src/app/[workspace]/_components/workspace-sidebar.tsx`
- Modify: `web/src/app/[workspace]/campaigns/[id]/layout.tsx`
- Modify: `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.tsx`
- Modify: `web/src/app/[workspace]/campaigns/[id]/_components/room-tabs.tsx`
- Modify: `web/src/components/nav-menu.tsx`
- Create: `web/src/components/workspace-switcher.tsx`
- Create: `web/src/components/user-menu.tsx`
- Create: `web/src/components/sign-out-button.tsx`
- Test: `web/src/proxy.test.ts`
- Test: `web/src/lib/core-api/client.test.ts`
- Test: `web/src/app/sign-in/_components/sign-in-panel.test.tsx`
- Test: `web/src/components/nav-menu.test.tsx`
- Test: `web/src/components/workspace-switcher.test.tsx`
- Test: `web/src/components/sign-out-button.test.tsx`
- Test: `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.test.tsx`
- Test: `web/src/app/[workspace]/campaigns/[id]/_components/room-tabs.test.tsx`

**Interfaces:**
- Consumes:
  - from Task 11: `core/src/Kairo.Api/openapi.json`
  - from Tasks 5 to 10: the core endpoints and the `__Host-kairo-session` cookie
- Produces:
  - **Cookie and header names** in `@/lib/http-names`: `SESSION_COOKIE`, `LAST_WORKSPACE_COOKIE`, `REQUEST_HEADER` and `PATH_HEADER`.
  - **Server-only client** in `@/lib/core-api/client`:
    - `coreApi()`, a typed `openapi-fetch` client that forwards the session cookie and sends `X-Kairo-Request`.
    - `unwrap(result)`, which sends a 401 to sign-in, renders not-found for a 404, and throws on any other failure.
    - `signInAgain()`.
  - **Server-only queries** in `@/lib/core-api/queries`, cached per request: `getMe()`, `getMyWorkspaces()`, `getMyInvitations()` and `getWorkspace(handle)`. `getWorkspace` renders not-found when the user isn't a member.
  - **Types** in `@/lib/core-api/types`: `Role`, `Workspace`, `WorkspaceDetails`, `MyInvitation`, `Member`, `PendingInvitation`, `ApprovalSettings`, `AuditLogPage`, `AuditEntry` and `roleLabels`.
  - **Components:**
    - `NavMenu({ workspace })`.
    - `WorkspaceSwitcher({ current, workspaces, pendingInvitations })`.
    - `UserMenu({ name, email })`.
    - `SignOutButton`.
  - **`npm run generate:api`.**

- [ ] **Step 1: Add the packages and generate the API types**

```bash
npm --prefix web install openapi-fetch@0.17.0 server-only@0.0.1
npm --prefix web install --save-dev openapi-typescript@7.13.0
```

Create `web/scripts/generate-api.mjs`. It uses `openapi-typescript`'s Node API, which writes no banner, and prints the result with comments removed, to follow `CLAUDE.md` §3:

```js
import { readFile, writeFile } from "node:fs/promises";

import openapiTS, { astToString } from "openapi-typescript";

const [input, output] = process.argv.slice(2);
const schema = JSON.parse(await readFile(input, "utf8"));
const ast = await openapiTS(schema);
await writeFile(output, astToString(ast, { formatOptions: { removeComments: true } }));
```

In `web/package.json`, add a script after `"test"`:

```json
    "generate:api": "node scripts/generate-api.mjs ../core/src/Kairo.Api/openapi.json src/lib/core-api/schema.ts"
```

Then generate the file:

```bash
mkdir -p web/src/lib/core-api
npm --prefix web run generate:api
grep -c "/\*\|//" web/src/lib/core-api/schema.ts || true
```

Expected: `0` comment lines. `schema.ts` exports `paths` and `components`.

In `web/eslint.config.mjs`, leave the generated file out of linting:

```js
  globalIgnores([".next/**", "out/**", "build/**", "next-env.d.ts", "src/lib/core-api/schema.ts"]),
```

- [ ] **Step 2: Move the screens under the workspace segment**

```bash
mkdir -p "web/src/app/[workspace]"
for area in today opportunities campaigns triggers impact-lab brand-kit settings; do
  git mv "web/src/app/$area" "web/src/app/[workspace]/$area"
done
git rm -q web/src/app/page.tsx
```

- [ ] **Step 3: Write the failing tests**

Create `web/src/proxy.test.ts`:

```ts
import { NextRequest } from "next/server";
import { expect, test } from "vitest";

import { proxy } from "@/proxy";

type WithCookies = Response & { cookies: { get(name: string): { value: string } | undefined } };

function request(path: string, session = false) {
  const req = new NextRequest(new URL(path, "http://localhost:8000"));
  if (session) {
    req.cookies.set("__Host-kairo-session", "abc");
  }

  return req;
}

test("sends visitors without a session to sign-in, remembering where they were going", () => {
  const response = proxy(request("/acme/today?tab=open"));

  expect(response.status).toBe(307);
  expect(response.headers.get("location")).toBe("/sign-in?returnUrl=%2Facme%2Ftoday%3Ftab%3Dopen");
});

test("lets the sign-in page through without a session", () => {
  const response = proxy(request("/sign-in"));

  expect(response.status).toBe(200);
});

test("remembers the workspace a signed-in user opened", () => {
  const response = proxy(request("/acme/settings/team", true)) as WithCookies;

  expect(response.cookies.get("kairo-last-workspace")?.value).toBe("acme");
});

test("does not treat top-level pages as workspaces", () => {
  const response = proxy(request("/welcome", true)) as WithCookies;

  expect(response.cookies.get("kairo-last-workspace")).toBeUndefined();
});
```

Create `web/src/lib/core-api/client.test.ts`. `server-only` and the Next.js request APIs are replaced with stand-ins, so the client's handling of each answer can be tested directly:

```ts
import { expect, test, vi } from "vitest";

import { unwrap } from "./client";

vi.mock("server-only", () => ({}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`redirect:${url}`);
  },
  notFound: () => {
    throw new Error("not-found");
  },
}));

vi.mock("next/headers", () => ({
  headers: async () => new Headers({ "x-kairo-path": "/acme/today?tab=open" }),
  cookies: async () => ({ get: () => undefined }),
}));

test("sends the user to sign-in, and back to the same page, when the session has ended", async () => {
  await expect(unwrap({ response: new Response(null, { status: 401 }) })).rejects.toThrow(
    "redirect:/sign-in?returnUrl=%2Facme%2Ftoday%3Ftab%3Dopen",
  );
});

test("renders not found for a 404", async () => {
  await expect(unwrap({ response: new Response(null, { status: 404 }) })).rejects.toThrow("not-found");
});

test("returns the data of a successful answer", async () => {
  expect(await unwrap({ data: { id: 1 }, response: new Response(null, { status: 200 }) })).toEqual({ id: 1 });
});

test("fails loudly on any other answer", async () => {
  await expect(unwrap({ response: new Response(null, { status: 500 }) })).rejects.toThrow("The core API answered 500.");
});
```

Create `web/src/app/sign-in/_components/sign-in-panel.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { SignInPanel } from "./sign-in-panel";

async function renderPanel(searchParams: Record<string, string>) {
  render(await SignInPanel({ searchParams: Promise.resolve(searchParams) }));
}

test("sends the user to Google with the page they asked for", async () => {
  await renderPanel({ returnUrl: "/acme/today?tab=open" });

  expect(screen.getByRole("link", { name: "Sign in with Google" }).getAttribute("href")).toBe(
    "/api/auth/sign-in?returnUrl=%2Facme%2Ftoday%3Ftab%3Dopen",
  );
  expect(screen.queryByRole("alert")).toBeNull();
});

test("explains why an unverified account was turned away", async () => {
  await renderPanel({ error: "unverified_email" });

  expect(screen.getByRole("alert").textContent).toContain("hasn't verified the email");
});

test("treats an unknown error as a failed sign-in", async () => {
  await renderPanel({ error: "something_else" });

  expect(screen.getByRole("alert").textContent).toBe("Sign-in didn't finish. Please try again.");
});
```

Replace `web/src/components/nav-menu.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { NavMenu } from "@/components/nav-menu";

test("lists the seven areas in order, each linking into the workspace", () => {
  render(<NavMenu workspace="acme" />);

  const nav = screen.getByRole("navigation", { name: "Main" });
  const links = within(nav).getAllByRole("link");

  expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Today", "/acme/today"],
    ["Opportunities", "/acme/opportunities"],
    ["Campaigns", "/acme/campaigns"],
    ["Triggers", "/acme/triggers"],
    ["Impact Lab", "/acme/impact-lab"],
    ["Brand Kit", "/acme/brand-kit"],
    ["Settings", "/acme/settings"],
  ]);
});
```

Create `web/src/components/workspace-switcher.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { WorkspaceSwitcher } from "@/components/workspace-switcher";

const acme = { id: "1", handle: "acme", name: "Acme Shoes" };
const beta = { id: "2", handle: "beta", name: "Beta Bags" };

test("links to every workspace and marks the current one", () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme, beta]} pendingInvitations={0} />);

  const nav = screen.getByRole("navigation", { name: "Workspaces" });
  const current = within(nav).getByRole("link", { name: "Acme Shoes" });
  expect(current.getAttribute("href")).toBe("/acme/today");
  expect(current.getAttribute("aria-current")).toBe("page");
  expect(within(nav).getByRole("link", { name: "Beta Bags" }).getAttribute("aria-current")).toBeNull();
});

test("shows how many invitations are waiting", () => {
  render(<WorkspaceSwitcher current={acme} workspaces={[acme]} pendingInvitations={2} />);

  expect(screen.getByRole("link", { name: "Invitations (2) and new workspaces" }).getAttribute("href")).toBe("/welcome");
});
```

Create `web/src/components/sign-out-button.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import { SignOutButton } from "@/components/sign-out-button";

const router = vi.hoisted(() => ({ replace: vi.fn(), refresh: vi.fn() }));

vi.mock("next/navigation", () => ({ useRouter: () => router }));

afterEach(() => {
  vi.unstubAllGlobals();
});

test("ends the session with the request header, then goes to sign-in", async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal("fetch", fetchMock);

  render(<SignOutButton />);
  fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

  await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/sign-in"));
  expect(fetchMock).toHaveBeenCalledWith("/api/auth/sign-out", {
    method: "POST",
    headers: { "X-Kairo-Request": "1" },
  });
  expect(router.refresh).toHaveBeenCalled();
});
```

Replace `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.test.tsx`:

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

test("links to the six tabs of the room inside its workspace", () => {
  render(<CampaignRoomTabs workspace="acme" roomId="42" />);

  expect(tabLinks()).toEqual([
    ["Evidence", "/acme/campaigns/42/evidence"],
    ["Brief", "/acme/campaigns/42/brief"],
    ["Assets", "/acme/campaigns/42/assets"],
    ["Launch", "/acme/campaigns/42/launch"],
    ["Performance", "/acme/campaigns/42/performance"],
    ["Results", "/acme/campaigns/42/results"],
  ]);
});

test("encodes room ids that contain URL characters", () => {
  render(<CampaignRoomTabs workspace="acme" roomId="a/b?c" />);

  expect(tabLinks()[0]).toEqual(["Evidence", "/acme/campaigns/a%2Fb%3Fc/evidence"]);
});
```

Replace `web/src/app/[workspace]/campaigns/[id]/_components/room-tabs.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { RoomTabs } from "./room-tabs";

async function evidenceHref(id: string) {
  render(await RoomTabs({ params: Promise.resolve({ workspace: "acme", id }) }));
  const nav = screen.getByRole("navigation", { name: "Campaign room" });
  return within(nav).getByRole("link", { name: "Evidence" }).getAttribute("href");
}

test("keeps a room id that arrives percent-encoded from the URL", async () => {
  expect(await evidenceHref("a%2Fb%3Fc")).toBe("/acme/campaigns/a%2Fb%3Fc/evidence");
});

test("keeps an encoded space in a room id", async () => {
  expect(await evidenceHref("hello%20world")).toBe("/acme/campaigns/hello%20world/evidence");
});

test("keeps a room id that is not valid percent-encoding", async () => {
  expect(await evidenceHref("100%")).toBe("/acme/campaigns/100%25/evidence");
});
```

- [ ] **Step 4: Run the tests to see them fail**

```bash
npm --prefix web test
```

Expected: FAIL.
- The new test files can't import `@/proxy`, `./client`, `./sign-in-panel`, `@/components/workspace-switcher` or `@/components/sign-out-button`.
- The nav menu and room tab tests fail, because the links don't include the workspace yet.

- [ ] **Step 5: Write the names, the client and the queries**

Create `web/src/lib/http-names.ts`:

```ts
export const SESSION_COOKIE = "__Host-kairo-session";
export const LAST_WORKSPACE_COOKIE = "kairo-last-workspace";
export const REQUEST_HEADER = "X-Kairo-Request";
export const PATH_HEADER = "x-kairo-path";
```

Create `web/src/lib/core-api/client.ts`. Notes on the client:
- It reads the cookie before anything else, which marks every caller as request-time work under Cache Components.
- It only forwards Kairo's session cookie.
- A 401 sends the user to sign-in and back to the page the proxy recorded.

```ts
import "server-only";

import { cookies, headers } from "next/headers";
import { notFound, redirect } from "next/navigation";
import createClient from "openapi-fetch";

import type { paths } from "./schema";

import { PATH_HEADER, REQUEST_HEADER, SESSION_COOKIE } from "@/lib/http-names";

export async function coreApi() {
  const session = (await cookies()).get(SESSION_COOKIE);
  const baseUrl = process.env.KAIRO_API_URL;
  if (!baseUrl) {
    throw new Error("KAIRO_API_URL is not set.");
  }

  return createClient<paths>({
    baseUrl,
    headers: {
      [REQUEST_HEADER]: "1",
      ...(session ? { cookie: `${SESSION_COOKIE}=${session.value}` } : {}),
    },
  });
}

export async function signInAgain(): Promise<never> {
  const path = (await headers()).get(PATH_HEADER) ?? "/";
  redirect(`/sign-in?returnUrl=${encodeURIComponent(path)}`);
}

export async function unwrap<T>(result: { data?: T; response: Response }): Promise<T> {
  if (result.response.status === 401) {
    return signInAgain();
  }

  if (result.response.status === 404) {
    notFound();
  }

  if (result.data === undefined) {
    throw new Error(`The core API answered ${result.response.status}.`);
  }

  return result.data;
}
```

Create `web/src/lib/core-api/queries.ts`:

```ts
import "server-only";

import { notFound } from "next/navigation";
import { cache } from "react";

import { coreApi, unwrap } from "./client";

export const getMe = cache(async () => unwrap(await (await coreApi()).GET("/api/me")));

export const getMyWorkspaces = cache(async () => unwrap(await (await coreApi()).GET("/api/brands")));

export const getMyInvitations = cache(async () => unwrap(await (await coreApi()).GET("/api/me/invitations")));

export const getWorkspace = cache(async (handle: string) => {
  const workspace = (await getMyWorkspaces()).find((candidate) => candidate.handle === handle);
  if (!workspace) {
    notFound();
  }

  return workspace;
});
```

Create `web/src/lib/core-api/types.ts`. It is safe to import from client components:

```ts
import type { components } from "./schema";

type Schemas = components["schemas"];

export type Role = Schemas["BrandRole"];
export type Workspace = Schemas["WorkspaceSummary"];
export type WorkspaceDetails = Schemas["WorkspaceDetails"];
export type MyInvitation = Schemas["MyInvitation"];
export type Member = Schemas["MemberResponse"];
export type PendingInvitation = Schemas["PendingInvitation"];
export type ApprovalSettings = Schemas["ApprovalSettings"];
export type AuditLogPage = Schemas["AuditLogPage"];
export type AuditEntry = Schemas["AuditEntryResponse"];

export const roleLabels: Record<Role, string> = {
  admin: "Admin",
  marketer: "Marketer",
  approver: "Approver",
};
```

- [ ] **Step 6: Write the proxy, the root redirect and the sign-in page**

Create `web/src/proxy.ts`:
- Visitors without a session cookie go to sign-in. This is only a fast path; the core still checks every session.
- It passes the requested path on to server components, for the return address.
- It remembers the last workspace the user opened.
- Redirects use a relative `Location`, so they stay on the gateway's address.

```ts
import { NextResponse, type NextRequest } from "next/server";

import { LAST_WORKSPACE_COOKIE, PATH_HEADER, SESSION_COOKIE } from "@/lib/http-names";

const TOP_LEVEL_ROUTES = new Set(["sign-in", "welcome", "health"]);

export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const path = pathname + search;

  if (pathname !== "/sign-in" && !request.cookies.has(SESSION_COOKIE)) {
    return new Response(null, {
      status: 307,
      headers: { Location: `/sign-in?returnUrl=${encodeURIComponent(path)}` },
    });
  }

  const headers = new Headers(request.headers);
  headers.set(PATH_HEADER, path);
  const response = NextResponse.next({ request: { headers } });

  const handle = pathname.split("/")[1];
  if (handle && !TOP_LEVEL_ROUTES.has(handle)) {
    response.cookies.set(LAST_WORKSPACE_COOKIE, handle, {
      path: "/",
      httpOnly: true,
      sameSite: "lax",
      maxAge: 60 * 60 * 24 * 365,
    });
  }

  return response;
}

export const config = {
  matcher: ["/((?!api|_next|favicon.ico|robots.txt|health).*)"],
};
```

Create `web/src/app/route.ts`. `/` sends signed-in users to the workspace they used last, if they still belong to it. Otherwise it sends them to their first workspace, or to `/welcome` if they have none.

```ts
import { cookies } from "next/headers";

import { coreApi } from "@/lib/core-api/client";
import { LAST_WORKSPACE_COOKIE } from "@/lib/http-names";

export async function GET() {
  const { data, response } = await (await coreApi()).GET("/api/brands");
  if (response.status === 401 || !data) {
    return redirectTo("/sign-in");
  }

  const last = (await cookies()).get(LAST_WORKSPACE_COOKIE)?.value;
  const target = data.find((workspace) => workspace.handle === last) ?? data[0];
  return redirectTo(target ? `/${target.handle}/today` : "/welcome");
}

function redirectTo(location: string) {
  return new Response(null, { status: 307, headers: { Location: location } });
}
```

Replace `web/src/app/layout.tsx`. The shell moves to the workspace layout:

```tsx
import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

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
      <body className="min-h-full">{children}</body>
    </html>
  );
}
```

Create `web/src/app/sign-in/page.tsx`:

```tsx
import { Suspense } from "react";

import { SignInPanel } from "./_components/sign-in-panel";

export default function SignInPage({ searchParams }: PageProps<"/sign-in">) {
  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <section className="w-full max-w-sm space-y-6 text-center">
        <h1 className="text-2xl font-semibold">Sign in to Kairo</h1>
        <Suspense fallback={null}>
          <SignInPanel searchParams={searchParams} />
        </Suspense>
      </section>
    </main>
  );
}
```

Create `web/src/app/sign-in/_components/sign-in-panel.tsx`. The core checks the return address, so the panel only passes it on:

```tsx
const errors: Record<string, string> = {
  unverified_email: "Google hasn't verified the email on that account, so it can't be used to sign in.",
  sign_in_failed: "Sign-in didn't finish. Please try again.",
};

type SignInPanelProps = {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

export async function SignInPanel({ searchParams }: SignInPanelProps) {
  const { returnUrl, error } = await searchParams;
  const target = typeof returnUrl === "string" ? returnUrl : "/";
  const message = typeof error === "string" ? (errors[error] ?? errors.sign_in_failed) : null;

  return (
    <div className="space-y-4">
      {message && (
        <p role="alert" className="rounded-md bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950 dark:text-red-200">
          {message}
        </p>
      )}
      <a
        href={`/api/auth/sign-in?returnUrl=${encodeURIComponent(target)}`}
        className="block rounded-md bg-black px-4 py-2 text-sm font-medium text-white hover:bg-black/80 dark:bg-white dark:text-black"
      >
        Sign in with Google
      </a>
    </div>
  );
}
```

- [ ] **Step 7: Write the workspace shell**

Create `web/src/app/[workspace]/layout.tsx`. The workspace and the user are request-time data, so the sidebar and the page each sit inside `<Suspense>`:

```tsx
import { Suspense } from "react";

import { WorkspaceSidebar } from "./_components/workspace-sidebar";

export default function WorkspaceLayout({ children, params }: LayoutProps<"/[workspace]">) {
  return (
    <div className="flex min-h-screen">
      <aside className="w-60 shrink-0 border-r border-black/10 p-4 dark:border-white/15">
        <p className="mb-6 px-3 text-lg font-semibold">Kairo</p>
        <Suspense fallback={null}>
          <WorkspaceSidebar params={params} />
        </Suspense>
      </aside>
      <main className="flex-1 p-8">
        <Suspense fallback={null}>{children}</Suspense>
      </main>
    </div>
  );
}
```

Create `web/src/app/[workspace]/_components/workspace-sidebar.tsx`. A handle the user doesn't belong to renders not-found:

```tsx
import { notFound } from "next/navigation";

import { NavMenu } from "@/components/nav-menu";
import { UserMenu } from "@/components/user-menu";
import { WorkspaceSwitcher } from "@/components/workspace-switcher";
import { getMe, getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";

export async function WorkspaceSidebar({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  const [workspaces, me, invitations] = await Promise.all([getMyWorkspaces(), getMe(), getMyInvitations()]);
  const current = workspaces.find((candidate) => candidate.handle === workspace);
  if (!current) {
    notFound();
  }

  return (
    <div className="space-y-6">
      <WorkspaceSwitcher current={current} workspaces={workspaces} pendingInvitations={invitations.length} />
      <NavMenu workspace={current.handle} />
      <UserMenu name={me.name} email={me.email} />
    </div>
  );
}
```

Replace `web/src/components/nav-menu.tsx`:

```tsx
import Link from "next/link";

const areas = [
  { segment: "today", label: "Today" },
  { segment: "opportunities", label: "Opportunities" },
  { segment: "campaigns", label: "Campaigns" },
  { segment: "triggers", label: "Triggers" },
  { segment: "impact-lab", label: "Impact Lab" },
  { segment: "brand-kit", label: "Brand Kit" },
  { segment: "settings", label: "Settings" },
];

export function NavMenu({ workspace }: { workspace: string }) {
  return (
    <nav aria-label="Main">
      <ul className="space-y-1">
        {areas.map((area) => (
          <li key={area.segment}>
            <Link
              href={`/${workspace}/${area.segment}`}
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

Create `web/src/components/workspace-switcher.tsx`. It is a native `<details>` element, which needs no client JavaScript:

```tsx
import Link from "next/link";

type WorkspaceLink = { id: string; handle: string; name: string };

type WorkspaceSwitcherProps = {
  current: WorkspaceLink;
  workspaces: WorkspaceLink[];
  pendingInvitations: number;
};

export function WorkspaceSwitcher({ current, workspaces, pendingInvitations }: WorkspaceSwitcherProps) {
  return (
    <details className="rounded-md border border-black/10 dark:border-white/15">
      <summary className="cursor-pointer px-3 py-2 text-sm font-medium">{current.name}</summary>
      <nav aria-label="Workspaces" className="border-t border-black/10 p-1 dark:border-white/15">
        <ul className="space-y-1">
          {workspaces.map((workspace) => (
            <li key={workspace.id}>
              <Link
                href={`/${workspace.handle}/today`}
                aria-current={workspace.id === current.id ? "page" : undefined}
                className="block rounded-md px-2 py-1 text-sm hover:bg-black/5 aria-[current=page]:font-semibold dark:hover:bg-white/10"
              >
                {workspace.name}
              </Link>
            </li>
          ))}
        </ul>
        <Link href="/welcome" className="mt-1 block rounded-md px-2 py-1 text-sm hover:bg-black/5 dark:hover:bg-white/10">
          {pendingInvitations > 0
            ? `Invitations (${pendingInvitations}) and new workspaces`
            : "Create a workspace"}
        </Link>
      </nav>
    </details>
  );
}
```

Create `web/src/components/user-menu.tsx`:

```tsx
import { SignOutButton } from "./sign-out-button";

export function UserMenu({ name, email }: { name: string; email: string }) {
  return (
    <div className="space-y-2 border-t border-black/10 px-3 pt-4 text-sm dark:border-white/15">
      <p className="font-medium">{name}</p>
      <p className="truncate text-zinc-600 dark:text-zinc-400">{email}</p>
      <SignOutButton />
    </div>
  );
}
```

Create `web/src/components/sign-out-button.tsx`. The browser calls the core directly, through the gateway, so the core can delete the session and the cookie:

```tsx
"use client";

import { useRouter } from "next/navigation";

import { REQUEST_HEADER } from "@/lib/http-names";

export function SignOutButton() {
  const router = useRouter();

  async function signOut() {
    await fetch("/api/auth/sign-out", { method: "POST", headers: { [REQUEST_HEADER]: "1" } });
    router.replace("/sign-in");
    router.refresh();
  }

  return (
    <button type="button" onClick={signOut} className="text-sm underline-offset-2 hover:underline">
      Sign out
    </button>
  );
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/_components/campaign-room-tabs.tsx`:

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

export function CampaignRoomTabs({ workspace, roomId }: { workspace: string; roomId: string }) {
  const base = `/${workspace}/campaigns/${encodeURIComponent(roomId)}`;

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

Replace `web/src/app/[workspace]/campaigns/[id]/_components/room-tabs.tsx`:

```tsx
import { CampaignRoomTabs } from "./campaign-room-tabs";

export async function RoomTabs({ params }: { params: Promise<{ workspace: string; id: string }> }) {
  const { workspace, id } = await params;
  return <CampaignRoomTabs workspace={workspace} roomId={decodeRoomId(id)} />;
}

function decodeRoomId(id: string) {
  try {
    return decodeURIComponent(id);
  } catch {
    return id;
  }
}
```

Replace `web/src/app/[workspace]/campaigns/[id]/layout.tsx`:

```tsx
import { Suspense } from "react";

import { RoomTabs } from "./_components/room-tabs";

export default function CampaignRoomLayout({ children, params }: LayoutProps<"/[workspace]/campaigns/[id]">) {
  return (
    <div className="space-y-6">
      <Suspense fallback={null}>
        <RoomTabs params={params} />
      </Suspense>
      {children}
    </div>
  );
}
```

- [ ] **Step 8: Run the tests, lint and build**

```bash
npm --prefix web test
npm --prefix web run lint
npm --prefix web run build
```

Expected:
- **Tests:** 21 pass. That is 4 proxy, 4 API client, 3 sign-in panel, 1 nav menu, 2 switcher, 1 sign-out, 2 campaign room tabs, 3 room tabs and 1 health.
- **Lint:** no problems.
- **Build:** succeeds. The route list shows `/[workspace]/...` routes as partially prerendered and `/sign-in` as partially prerendered. It also shows `/health`, and the `ƒ Proxy` line.

- [ ] **Step 9: Commit**

```bash
git add web/package.json web/package-lock.json web/scripts web/eslint.config.mjs web/src
git commit -m "Put the web screens inside a workspace, add Google sign-in and a typed core API client"
```

### Task 13: Web: welcome page

**Files:**
- Create: `web/src/lib/forms.ts`
- Create: `web/src/components/field-error.tsx`
- Create: `web/src/app/welcome/actions.ts`
- Create: `web/src/app/welcome/_components/create-workspace-form.tsx`
- Create: `web/src/app/welcome/_components/invitation-list.tsx`
- Create: `web/src/app/welcome/page.tsx`
- Test: `web/src/lib/forms.test.ts`
- Test: `web/src/app/welcome/_components/create-workspace-form.test.tsx`
- Test: `web/src/app/welcome/_components/invitation-list.test.tsx`

**Interfaces:**
- Consumes: from Task 12, `coreApi()`, `signInAgain()`, `getMyInvitations()`, `getMyWorkspaces()` and the types `MyInvitation` and `roleLabels`.
- Produces:
  - **Form helpers** in `@/lib/forms`:
    - `type FormState = { status: "idle" | "saved" | "error"; message?: string; fieldErrors?: Record<string, string[]> }`.
    - `idle`.
    - `problemToFormState(problem: unknown, status: number)`.
    - `text(formData, name)`.
  - **Component** `FieldError({ id, errors })`.
  - **Server Actions:**
    - `createWorkspace(state, formData)`, which redirects to `/{handle}/today` on success.
    - `acceptInvitation(invitationId)`, which redirects to the workspace.
    - `declineInvitation(invitationId)`.
  - **Page** `/welcome`.

- [ ] **Step 1: Write the failing tests**

Create `web/src/lib/forms.test.ts`:

```ts
import { expect, test } from "vitest";

import { problemToFormState, text } from "./forms";

test("turns validation errors into field errors", () => {
  expect(problemToFormState({ title: "One or more validation errors occurred.", errors: { handle: ["That handle is taken."] } }, 400)).toEqual({
    status: "error",
    fieldErrors: { handle: ["That handle is taken."] },
  });
});

test("uses the problem title as the message", () => {
  expect(problemToFormState({ title: "A workspace needs at least one admin." }, 409)).toEqual({
    status: "error",
    message: "A workspace needs at least one admin.",
  });
});

test("falls back to a message that fits the status", () => {
  expect(problemToFormState(undefined, 403).message).toBe("You don't have permission to do that.");
  expect(problemToFormState(undefined, 404).message).toBe("That no longer exists.");
  expect(problemToFormState(undefined, 500).message).toBe("Something went wrong. Please try again.");
});

test("reads trimmed text from form data", () => {
  const formData = new FormData();
  formData.set("name", "  Acme Shoes ");

  expect(text(formData, "name")).toBe("Acme Shoes");
  expect(text(formData, "missing")).toBe("");
});
```

Create `web/src/app/welcome/_components/create-workspace-form.test.tsx`. Forms receive their Server Action as a prop, so tests pass a fake one:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { CreateWorkspaceForm } from "./create-workspace-form";

test("sends the form fields to the action", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(Object.fromEntries(formData)).toEqual({ name: "Acme Shoes", handle: "acme", timeZone: "Asia/Kolkata", currency: "INR" });
    return { status: "saved" };
  });
  render(<CreateWorkspaceForm action={action} timeZones={["Asia/Kolkata", "Europe/London"]} currencies={["INR", "USD"]} />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Shoes" } });
  fireEvent.change(screen.getByLabelText("Handle"), { target: { value: "acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

  await vi.waitFor(() => expect(action).toHaveBeenCalledOnce());
});

test("shows field errors from the core next to their fields", async () => {
  const action = vi.fn(async (): Promise<FormState> => ({ status: "error", fieldErrors: { handle: ["That handle is taken."] } }));
  render(<CreateWorkspaceForm action={action} timeZones={["Asia/Kolkata"]} currencies={["INR"]} />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Shoes" } });
  fireEvent.change(screen.getByLabelText("Handle"), { target: { value: "acme" } });
  fireEvent.click(screen.getByRole("button", { name: "Create workspace" }));

  expect(await screen.findByText("That handle is taken.")).toBeTruthy();
});
```

Create `web/src/app/welcome/_components/invitation-list.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { InvitationList } from "./invitation-list";

const invitation = {
  id: "inv-1",
  workspaceName: "Acme Shoes",
  workspaceHandle: "acme",
  roles: ["marketer" as const, "approver" as const],
  invitedBy: "Neha Rao",
  expiresAt: "2026-10-27T00:00:00Z",
};

test("says who invited you and with which roles", () => {
  render(<InvitationList invitations={[invitation]} accept={vi.fn()} decline={vi.fn()} />);

  expect(screen.getByText("Acme Shoes")).toBeTruthy();
  expect(screen.getByText("Neha Rao invited you as Marketer, Approver.")).toBeTruthy();
});

test("accepts or declines the chosen invitation", async () => {
  const accept = vi.fn(async () => {});
  const decline = vi.fn(async () => {});
  render(<InvitationList invitations={[invitation]} accept={accept} decline={decline} />);

  fireEvent.click(screen.getByRole("button", { name: "Accept" }));
  fireEvent.click(screen.getByRole("button", { name: "Decline" }));

  await vi.waitFor(() => expect(accept).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
  await vi.waitFor(() => expect(decline).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
});
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
npm --prefix web test
```

Expected: FAIL. `./forms`, `./create-workspace-form` and `./invitation-list` can't be found.

- [ ] **Step 3: Write the form helpers**

Create `web/src/lib/forms.ts`:

```ts
export type FormState = {
  status: "idle" | "saved" | "error";
  message?: string;
  fieldErrors?: Record<string, string[]>;
};

export const idle: FormState = { status: "idle" };

type Problem = {
  title?: string | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
};

export function problemToFormState(problem: unknown, status: number): FormState {
  const details = (problem ?? {}) as Problem;
  if (details.errors && Object.keys(details.errors).length > 0) {
    return { status: "error", fieldErrors: details.errors };
  }

  return { status: "error", message: details.detail ?? details.title ?? fallbackMessage(status) };
}

export function text(formData: FormData, name: string) {
  const value = formData.get(name);
  return typeof value === "string" ? value.trim() : "";
}

function fallbackMessage(status: number) {
  if (status === 403) {
    return "You don't have permission to do that.";
  }

  if (status === 404) {
    return "That no longer exists.";
  }

  return "Something went wrong. Please try again.";
}
```

Create `web/src/components/field-error.tsx`:

```tsx
export function FieldError({ id, errors }: { id: string; errors?: string[] }) {
  if (!errors?.length) {
    return null;
  }

  return (
    <p id={id} className="text-sm text-red-700 dark:text-red-300">
      {errors.join(" ")}
    </p>
  );
}
```

- [ ] **Step 4: Write the actions, the components and the page**

Create `web/src/app/welcome/actions.ts`:

```ts
"use server";

import { refresh } from "next/cache";
import { redirect } from "next/navigation";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function createWorkspace(_: FormState, formData: FormData): Promise<FormState> {
  const { data, error, response } = await (await coreApi()).POST("/api/brands", {
    body: {
      name: text(formData, "name"),
      handle: text(formData, "handle").toLowerCase(),
      timeZone: text(formData, "timeZone"),
      currency: text(formData, "currency"),
    },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  if (!data) {
    return problemToFormState(error, response.status);
  }

  redirect(`/${data.handle}/today`);
}

export async function acceptInvitation(invitationId: string) {
  const { data, response } = await (await coreApi()).POST("/api/me/invitations/{invitationId}/accept", {
    params: { path: { invitationId } },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  if (!data) {
    refresh();
    return;
  }

  redirect(`/${data.handle}/today`);
}

export async function declineInvitation(invitationId: string) {
  const { response } = await (await coreApi()).POST("/api/me/invitations/{invitationId}/decline", {
    params: { path: { invitationId } },
  });

  if (response.status === 401) {
    return signInAgain();
  }

  refresh();
}
```

Create `web/src/app/welcome/_components/create-workspace-form.tsx`. The time zone defaults to India, the pilot market. Every choice is still offered.

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { idle, type FormState } from "@/lib/forms";

type CreateWorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  timeZones: string[];
  currencies: string[];
};

export function CreateWorkspaceForm({ action, timeZones, currencies }: CreateWorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="space-y-4">
      <div className="space-y-1">
        <label htmlFor="name" className="block text-sm font-medium">Brand name</label>
        <input id="name" name="name" required maxLength={100} aria-describedby="name-error" className="w-full rounded-md border px-3 py-2" />
        <FieldError id="name-error" errors={state.fieldErrors?.name} />
      </div>
      <div className="space-y-1">
        <label htmlFor="handle" className="block text-sm font-medium">Handle</label>
        <input id="handle" name="handle" required minLength={3} maxLength={40} aria-describedby="handle-error" className="w-full rounded-md border px-3 py-2" />
        <FieldError id="handle-error" errors={state.fieldErrors?.handle} />
      </div>
      <div className="space-y-1">
        <label htmlFor="timeZone" className="block text-sm font-medium">Time zone</label>
        <select id="timeZone" name="timeZone" defaultValue="Asia/Kolkata" className="w-full rounded-md border px-3 py-2">
          {timeZones.map((zone) => (
            <option key={zone} value={zone}>{zone}</option>
          ))}
        </select>
        <FieldError id="timeZone-error" errors={state.fieldErrors?.timeZone} />
      </div>
      <div className="space-y-1">
        <label htmlFor="currency" className="block text-sm font-medium">Currency</label>
        <select id="currency" name="currency" defaultValue="INR" className="w-full rounded-md border px-3 py-2">
          {currencies.map((currency) => (
            <option key={currency} value={currency}>{currency}</option>
          ))}
        </select>
        <FieldError id="currency-error" errors={state.fieldErrors?.currency} />
      </div>
      {state.message && <p role="alert" className="text-sm text-red-700 dark:text-red-300">{state.message}</p>}
      <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
        Create workspace
      </button>
    </form>
  );
}
```

Create `web/src/app/welcome/_components/invitation-list.tsx`. It is a server component. Each button is a form whose action is the Server Action bound to that invitation's ID.

```tsx
import { roleLabels, type MyInvitation } from "@/lib/core-api/types";

type InvitationListProps = {
  invitations: MyInvitation[];
  accept: (invitationId: string) => Promise<void>;
  decline: (invitationId: string) => Promise<void>;
};

export function InvitationList({ invitations, accept, decline }: InvitationListProps) {
  return (
    <ul className="divide-y divide-black/10 dark:divide-white/15">
      {invitations.map((invitation) => (
        <li key={invitation.id} className="flex items-center justify-between gap-4 py-3">
          <div>
            <p className="font-medium">{invitation.workspaceName}</p>
            <p className="text-sm text-zinc-600 dark:text-zinc-400">
              {invitation.invitedBy} invited you as {invitation.roles.map((role) => roleLabels[role]).join(", ")}.
            </p>
          </div>
          <div className="flex gap-2">
            <form action={accept.bind(null, invitation.id)}>
              <button type="submit" className="rounded-md bg-black px-3 py-1 text-sm text-white dark:bg-white dark:text-black">
                Accept
              </button>
            </form>
            <form action={decline.bind(null, invitation.id)}>
              <button type="submit" className="rounded-md border px-3 py-1 text-sm">
                Decline
              </button>
            </form>
          </div>
        </li>
      ))}
    </ul>
  );
}
```

Create `web/src/app/welcome/page.tsx`. The form is static; the invitations and the user's workspaces load at request time inside `<Suspense>`.

```tsx
import Link from "next/link";
import { Suspense } from "react";

import { getMyInvitations, getMyWorkspaces } from "@/lib/core-api/queries";

import { acceptInvitation, createWorkspace, declineInvitation } from "./actions";
import { CreateWorkspaceForm } from "./_components/create-workspace-form";
import { InvitationList } from "./_components/invitation-list";

const currencies = ["INR", "USD", "EUR", "GBP", "AED", "SGD", "AUD", "CAD", "JPY"];

export default function WelcomePage() {
  return (
    <main className="mx-auto max-w-xl space-y-10 p-8">
      <h1 className="text-2xl font-semibold">Welcome to Kairo</h1>
      <Suspense fallback={null}>
        <YourWorkspaces />
      </Suspense>
      <section className="space-y-4">
        <h2 className="text-lg font-semibold">Create a workspace</h2>
        <CreateWorkspaceForm
          action={createWorkspace}
          timeZones={Intl.supportedValuesOf("timeZone")}
          currencies={currencies}
        />
      </section>
    </main>
  );
}

async function YourWorkspaces() {
  const [invitations, workspaces] = await Promise.all([getMyInvitations(), getMyWorkspaces()]);

  return (
    <>
      {invitations.length > 0 && (
        <section className="space-y-4">
          <h2 className="text-lg font-semibold">Invitations</h2>
          <InvitationList invitations={invitations} accept={acceptInvitation} decline={declineInvitation} />
        </section>
      )}
      {workspaces.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-lg font-semibold">Your workspaces</h2>
          <ul className="space-y-1">
            {workspaces.map((workspace) => (
              <li key={workspace.id}>
                <Link href={`/${workspace.handle}/today`} className="underline-offset-2 hover:underline">
                  {workspace.name}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
```

- [ ] **Step 5: Run the tests, lint and build**

```bash
npm --prefix web test
npm --prefix web run lint
npm --prefix web run build
```

Expected:
- **Tests:** 29 pass, the 21 from Task 12 plus 8 new ones.
- **Lint:** no problems.
- **Build:** succeeds, with `/welcome` listed as partially prerendered.

- [ ] **Step 6: Commit**

```bash
git add web/src
git commit -m "Add the welcome page to create a workspace or answer invitations"
```

### Task 14: Web: workspace settings and audit log

**Files:**
- Create: `web/src/components/form-status.tsx`
- Create: `web/src/app/[workspace]/settings/layout.tsx`
- Create: `web/src/app/[workspace]/settings/_components/settings-nav.tsx`
- Create: `web/src/app/[workspace]/settings/actions.ts`
- Create: `web/src/app/[workspace]/settings/_components/workspace-form.tsx`
- Modify: `web/src/app/[workspace]/settings/page.tsx`, which replaces the placeholder
- Create: `web/src/app/[workspace]/settings/audit-log/page.tsx`
- Create: `web/src/app/[workspace]/settings/audit-log/_components/audit-log-table.tsx`
- Test: `web/src/app/[workspace]/settings/_components/settings-nav.test.tsx`
- Test: `web/src/app/[workspace]/settings/_components/workspace-form.test.tsx`
- Test: `web/src/app/[workspace]/settings/audit-log/_components/audit-log-table.test.tsx`

**Interfaces:**
- Consumes:
  - from Task 12: `coreApi()`, `unwrap()`, `getWorkspace()` and the types `WorkspaceDetails` and `AuditLogPage`
  - from Task 13: `FormState`, `idle`, `problemToFormState`, `text` and `FieldError`
- Produces:
  - **Component** `FormStatus({ state })`.
  - **Settings actions** in `web/src/app/[workspace]/settings/actions.ts`:
    - `updateWorkspace(brandId, state, formData)`.
    - The private helper `finish(error, response, message)`. Task 15 adds more actions to this file.
  - **Pages:** `/[workspace]/settings` and `/[workspace]/settings/audit-log`.

- [ ] **Step 1: Write the failing tests**

Create `web/src/app/[workspace]/settings/_components/settings-nav.test.tsx`:

```tsx
import { render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";

import { SettingsNav } from "./settings-nav";

test("links to the four settings pages of the workspace", () => {
  render(<SettingsNav workspace="acme" />);

  const nav = screen.getByRole("navigation", { name: "Settings" });
  expect(within(nav).getAllByRole("link").map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
    ["Workspace", "/acme/settings"],
    ["Team", "/acme/settings/team"],
    ["Approvals", "/acme/settings/approvals"],
    ["Audit log", "/acme/settings/audit-log"],
  ]);
});
```

Create `web/src/app/[workspace]/settings/_components/workspace-form.test.tsx`. A control inside a disabled `<fieldset>` is disabled even though its own `disabled` property stays false, so the test checks `:disabled`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { WorkspaceForm } from "./workspace-form";

const workspace = {
  id: "b1",
  handle: "acme",
  name: "Acme Shoes",
  timeZone: "Asia/Kolkata",
  currency: "INR",
  roles: ["admin" as const],
};

test("lets an admin rename the workspace", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.get("name")).toBe("Acme Footwear");
    return { status: "saved", message: "Saved." };
  });
  render(<WorkspaceForm action={action} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit />);

  fireEvent.change(screen.getByLabelText("Brand name"), { target: { value: "Acme Footwear" } });
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

  expect((await screen.findByRole("status")).textContent).toBe("Saved.");
});

test("shows the settings read-only to everyone else", () => {
  render(<WorkspaceForm action={vi.fn()} workspace={workspace} timeZones={["Asia/Kolkata"]} canEdit={false} />);

  expect(screen.getByLabelText("Brand name").matches(":disabled")).toBe(true);
  expect(screen.queryByRole("button", { name: "Save changes" })).toBeNull();
  expect(screen.getByText("INR")).toBeTruthy();
});
```

Create `web/src/app/[workspace]/settings/audit-log/_components/audit-log-table.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";

import { AuditLogTable } from "./audit-log-table";

const entry = {
  id: 7,
  occurredAt: "2026-10-13T04:30:00Z",
  actorType: "user",
  actorId: "u1",
  actorName: "Neha Rao",
  action: "brand.created",
  targetType: "brand",
  targetId: "b1",
  before: null,
  after: null,
};

test("lists who did what and when, in the workspace's time zone", () => {
  render(<AuditLogTable page={{ entries: [entry], nextBefore: null }} timeZone="Asia/Kolkata" olderHref={(before) => `?before=${before}`} />);

  expect(screen.getByText("Neha Rao")).toBeTruthy();
  expect(screen.getByText("Created the workspace")).toBeTruthy();
  expect(screen.getByText(/10:00/)).toBeTruthy();
  expect(screen.queryByRole("link", { name: "Older entries" })).toBeNull();
});

test("links to older entries when there are more", () => {
  render(<AuditLogTable page={{ entries: [entry], nextBefore: "7" }} timeZone="Asia/Kolkata" olderHref={(before) => `/acme/settings/audit-log?before=${before}`} />);

  expect(screen.getByRole("link", { name: "Older entries" }).getAttribute("href")).toBe("/acme/settings/audit-log?before=7");
});

test("shows unknown actions as they are", () => {
  render(<AuditLogTable page={{ entries: [{ ...entry, action: "launch.started" }], nextBefore: null }} timeZone="UTC" olderHref={(before) => before} />);

  expect(screen.getByText("launch.started")).toBeTruthy();
});
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
npm --prefix web test
```

Expected: FAIL. `./settings-nav`, `./workspace-form` and `./audit-log-table` can't be found.

- [ ] **Step 3: Write the settings shell, action and workspace page**

Create `web/src/components/form-status.tsx`:

```tsx
import type { FormState } from "@/lib/forms";

export function FormStatus({ state }: { state: FormState }) {
  if (!state.message) {
    return null;
  }

  return state.status === "error" ? (
    <p role="alert" className="text-sm text-red-700 dark:text-red-300">
      {state.message}
    </p>
  ) : (
    <p role="status" className="text-sm text-green-700 dark:text-green-300">
      {state.message}
    </p>
  );
}
```

Create `web/src/app/[workspace]/settings/_components/settings-nav.tsx`:

```tsx
import Link from "next/link";

const sections = [
  { segment: "", label: "Workspace" },
  { segment: "/team", label: "Team" },
  { segment: "/approvals", label: "Approvals" },
  { segment: "/audit-log", label: "Audit log" },
];

export function SettingsNav({ workspace }: { workspace: string }) {
  return (
    <nav aria-label="Settings">
      <ul className="flex gap-6 border-b border-black/10 text-sm dark:border-white/15">
        {sections.map((section) => (
          <li key={section.label}>
            <Link href={`/${workspace}/settings${section.segment}`} className="block pb-2 hover:underline">
              {section.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
```

Create `web/src/app/[workspace]/settings/layout.tsx`:

```tsx
import { Suspense } from "react";

import { SettingsNav } from "./_components/settings-nav";

export default function SettingsLayout({ children, params }: LayoutProps<"/[workspace]/settings">) {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Settings</h1>
      <Suspense fallback={null}>
        <SettingsNavFor params={params} />
      </Suspense>
      {children}
    </div>
  );
}

async function SettingsNavFor({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace } = await params;
  return <SettingsNav workspace={workspace} />;
}
```

Create `web/src/app/[workspace]/settings/actions.ts`. `finish` turns the core's answer into form state, and refreshes the page after a change:

```ts
"use server";

import { refresh } from "next/cache";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function updateWorkspace(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).PATCH("/api/brands/{brandId}", {
    params: { path: { brandId } },
    body: { name: text(formData, "name"), timeZone: text(formData, "timeZone") },
  });
  return finish(error, response, "Saved.");
}

async function finish(error: unknown, response: Response, message: string): Promise<FormState> {
  if (response.status === 401) {
    return signInAgain();
  }

  if (!response.ok) {
    return problemToFormState(error, response.status);
  }

  refresh();
  return { status: "saved", message };
}
```

Create `web/src/app/[workspace]/settings/_components/workspace-form.tsx`. The handle and the currency can't be changed, so they are shown as text:

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import type { WorkspaceDetails } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type WorkspaceFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  workspace: WorkspaceDetails;
  timeZones: string[];
  canEdit: boolean;
};

export function WorkspaceForm({ action, workspace, timeZones, canEdit }: WorkspaceFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="max-w-md space-y-4">
      <fieldset disabled={!canEdit || pending} className="space-y-4">
        <div className="space-y-1">
          <label htmlFor="name" className="block text-sm font-medium">Brand name</label>
          <input id="name" name="name" defaultValue={workspace.name} required maxLength={100} className="w-full rounded-md border px-3 py-2" />
          <FieldError id="name-error" errors={state.fieldErrors?.name} />
        </div>
        <div className="space-y-1">
          <label htmlFor="timeZone" className="block text-sm font-medium">Time zone</label>
          <select id="timeZone" name="timeZone" defaultValue={workspace.timeZone} className="w-full rounded-md border px-3 py-2">
            {timeZones.map((zone) => (
              <option key={zone} value={zone}>{zone}</option>
            ))}
          </select>
          <FieldError id="timeZone-error" errors={state.fieldErrors?.timeZone} />
        </div>
      </fieldset>
      <dl className="grid grid-cols-2 gap-2 text-sm">
        <dt className="text-zinc-600 dark:text-zinc-400">Handle</dt>
        <dd>{workspace.handle}</dd>
        <dt className="text-zinc-600 dark:text-zinc-400">Currency</dt>
        <dd>{workspace.currency}</dd>
      </dl>
      {canEdit && (
        <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
          Save changes
        </button>
      )}
      <FormStatus state={state} />
    </form>
  );
}
```

Replace `web/src/app/[workspace]/settings/page.tsx`:

```tsx
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { updateWorkspace } from "./actions";
import { WorkspaceForm } from "./_components/workspace-form";

export default async function WorkspaceSettingsPage({ params }: PageProps<"/[workspace]/settings">) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const details = await unwrap(
    await (await coreApi()).GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }),
  );

  return (
    <WorkspaceForm
      action={updateWorkspace.bind(null, workspace.id)}
      workspace={details}
      timeZones={Intl.supportedValuesOf("timeZone")}
      canEdit={workspace.roles.includes("admin")}
    />
  );
}
```

- [ ] **Step 4: Write the audit log page**

Create `web/src/app/[workspace]/settings/audit-log/_components/audit-log-table.tsx`:

```tsx
import Link from "next/link";

import type { AuditLogPage } from "@/lib/core-api/types";

const actions: Record<string, string> = {
  "brand.created": "Created the workspace",
  "brand.updated": "Changed the workspace details",
  "invitation.sent": "Sent an invitation",
  "invitation.revoked": "Revoked an invitation",
  "invitation.accepted": "Accepted an invitation",
  "invitation.declined": "Declined an invitation",
  "member.roles_changed": "Changed a member's roles",
  "member.removed": "Removed a member",
  "member.left": "Left the workspace",
  "approval_settings.changed": "Changed the approval settings",
};

type AuditLogTableProps = {
  page: AuditLogPage;
  timeZone: string;
  olderHref: (before: string) => string;
};

export function AuditLogTable({ page, timeZone, olderHref }: AuditLogTableProps) {
  const format = new Intl.DateTimeFormat("en-IN", { dateStyle: "medium", timeStyle: "short", timeZone });

  if (page.entries.length === 0) {
    return <p className="text-sm text-zinc-600 dark:text-zinc-400">Nothing has been recorded yet.</p>;
  }

  return (
    <div className="space-y-4">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-black/10 dark:border-white/15">
            <th className="py-2 font-medium">When</th>
            <th className="py-2 font-medium">Who</th>
            <th className="py-2 font-medium">What</th>
          </tr>
        </thead>
        <tbody>
          {page.entries.map((entry) => (
            <tr key={String(entry.id)} className="border-b border-black/5 dark:border-white/10">
              <td className="py-2">{format.format(new Date(entry.occurredAt))}</td>
              <td className="py-2">{entry.actorName}</td>
              <td className="py-2">{actions[entry.action] ?? entry.action}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {page.nextBefore && (
        <Link href={olderHref(page.nextBefore)} className="text-sm underline-offset-2 hover:underline">
          Older entries
        </Link>
      )}
    </div>
  );
}
```

Create `web/src/app/[workspace]/settings/audit-log/page.tsx`. Members who aren't admins see a sentence instead of a 403 error:

```tsx
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { AuditLogTable } from "./_components/audit-log-table";

export default async function AuditLogPage({ params, searchParams }: PageProps<"/[workspace]/settings/audit-log">) {
  const [{ workspace: handle }, { before }] = await Promise.all([params, searchParams]);
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) {
    return <p className="text-sm text-zinc-600 dark:text-zinc-400">Only admins can see the audit log.</p>;
  }

  const api = await coreApi();
  const brandId = workspace.id;
  const [page, details] = await Promise.all([
    unwrap(await api.GET("/api/brands/{brandId}/audit-log", {
      params: { path: { brandId }, query: { before: typeof before === "string" ? before : undefined } },
    })),
    unwrap(await api.GET("/api/brands/{brandId}", { params: { path: { brandId } } })),
  ]);

  return (
    <AuditLogTable
      page={page}
      timeZone={details.timeZone}
      olderHref={(cursor) => `/${handle}/settings/audit-log?before=${encodeURIComponent(cursor)}`}
    />
  );
}
```

- [ ] **Step 5: Run the tests, lint and build**

```bash
npm --prefix web test
npm --prefix web run lint
npm --prefix web run build
```

Expected:
- **Tests:** 35 pass.
- **Lint:** no problems.
- **Build:** succeeds, with `/[workspace]/settings` and `/[workspace]/settings/audit-log` listed.

- [ ] **Step 6: Commit**

```bash
git add web/src
git commit -m "Add workspace settings and the audit log to the web app"
```

### Task 15: Web: team and approval settings

**Files:**
- Create: `web/src/components/role-checkboxes.tsx`
- Modify: `web/src/app/[workspace]/settings/actions.ts`
- Create: `web/src/app/[workspace]/settings/team/page.tsx`
- Create: `web/src/app/[workspace]/settings/team/_components/member-row.tsx`
- Create: `web/src/app/[workspace]/settings/team/_components/invite-form.tsx`
- Create: `web/src/app/[workspace]/settings/team/_components/pending-invitations.tsx`
- Create: `web/src/app/[workspace]/settings/approvals/page.tsx`
- Create: `web/src/app/[workspace]/settings/approvals/_components/approvals-form.tsx`
- Test: `web/src/app/[workspace]/settings/team/_components/member-row.test.tsx`
- Test: `web/src/app/[workspace]/settings/team/_components/invite-form.test.tsx`
- Test: `web/src/app/[workspace]/settings/team/_components/pending-invitations.test.tsx`
- Test: `web/src/app/[workspace]/settings/approvals/_components/approvals-form.test.tsx`

**Interfaces:**
- Consumes:
  - from Task 14: `finish` in `settings/actions.ts`, `FormStatus` and the settings layout
  - from Task 13: `FieldError`, `FormState` and `idle`
  - from Task 12: `getMe()`, `getWorkspace()` and the types `Member`, `PendingInvitation`, `ApprovalSettings` and `roleLabels`
- Produces:
  - **Server Actions:**
    - `inviteMember(brandId, state, formData)`.
    - `revokeInvitation(brandId, invitationId)`.
    - `changeRoles(brandId, userId, state, formData)`.
    - `removeMember(brandId, userId)`. If the member removed is the signed-in user, it redirects to `/`.
    - `saveApprovalSettings(brandId, state, formData)`.
  - **Pages:** `/[workspace]/settings/team` and `/[workspace]/settings/approvals`.

- [ ] **Step 1: Write the failing tests**

Create `web/src/app/[workspace]/settings/team/_components/member-row.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { MemberRow } from "./member-row";

const priya = {
  userId: "u2",
  name: "Priya Shah",
  email: "priya@example.com",
  pictureUrl: null,
  roles: ["marketer" as const],
  joinedAt: "2026-10-13T00:00:00Z",
};

afterEach(() => {
  vi.unstubAllGlobals();
});

test("lets an admin change a member's roles", async () => {
  const changeRoles = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.getAll("roles")).toEqual(["marketer", "approver"]);
    return { status: "saved", message: "Roles saved." };
  });
  render(<MemberRow member={priya} isSelf={false} canManage changeRoles={changeRoles} remove={vi.fn()} />);

  fireEvent.click(screen.getByLabelText("Approver"));
  fireEvent.click(screen.getByRole("button", { name: "Save roles" }));

  expect((await screen.findByRole("status")).textContent).toBe("Roles saved.");
});

test("shows the reason when a change is refused", async () => {
  const remove = vi.fn(async (): Promise<FormState> => ({ status: "error", message: "A workspace needs at least one admin." }));
  vi.stubGlobal("confirm", () => true);
  render(<MemberRow member={priya} isSelf canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Leave workspace" }));

  expect((await screen.findByRole("alert")).textContent).toBe("A workspace needs at least one admin.");
});

test("asks before removing someone and does nothing if cancelled", () => {
  const remove = vi.fn();
  vi.stubGlobal("confirm", () => false);
  render(<MemberRow member={priya} isSelf={false} canManage remove={remove} changeRoles={vi.fn()} />);

  fireEvent.click(screen.getByRole("button", { name: "Remove" }));

  expect(remove).not.toHaveBeenCalled();
});

test("shows roles as text to members who are not admins", () => {
  render(<MemberRow member={priya} isSelf={false} canManage={false} remove={vi.fn()} changeRoles={vi.fn()} />);

  expect(screen.getByText("Marketer")).toBeTruthy();
  expect(screen.queryByRole("button")).toBeNull();
});
```

Create `web/src/app/[workspace]/settings/team/_components/invite-form.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { InviteForm } from "./invite-form";

test("sends the email and the chosen roles", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.get("email")).toBe("priya@example.com");
    expect(formData.getAll("roles")).toEqual(["marketer"]);
    return { status: "saved", message: "Invitation sent." };
  });
  render(<InviteForm action={action} />);

  fireEvent.change(screen.getByLabelText("Email"), { target: { value: "priya@example.com" } });
  fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

  expect((await screen.findByRole("status")).textContent).toBe("Invitation sent.");
});

test("shows why the core refused the invitation", async () => {
  const action = vi.fn(async (): Promise<FormState> => ({ status: "error", fieldErrors: { email: ["That person is already a member."] } }));
  render(<InviteForm action={action} />);

  fireEvent.change(screen.getByLabelText("Email"), { target: { value: "priya@example.com" } });
  fireEvent.click(screen.getByRole("button", { name: "Send invitation" }));

  expect(await screen.findByText("That person is already a member.")).toBeTruthy();
});
```

Create `web/src/app/[workspace]/settings/team/_components/pending-invitations.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import { PendingInvitations } from "./pending-invitations";

const invitation = {
  id: "inv-1",
  email: "priya@example.com",
  roles: ["marketer" as const],
  invitedBy: "Neha Rao",
  sentAt: "2026-10-13T00:00:00Z",
  expiresAt: "2026-10-27T00:00:00Z",
};

test("revokes the chosen invitation", async () => {
  const revoke = vi.fn(async () => {});
  render(<PendingInvitations invitations={[invitation]} revoke={revoke} />);

  fireEvent.click(screen.getByRole("button", { name: "Revoke" }));

  await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith("inv-1", expect.any(FormData)));
});

test("says so when nobody is waiting", () => {
  render(<PendingInvitations invitations={[]} revoke={vi.fn()} />);

  expect(screen.getByText("No invitations are waiting.")).toBeTruthy();
});
```

Create `web/src/app/[workspace]/settings/approvals/_components/approvals-form.test.tsx`:

```tsx
import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";

import type { FormState } from "@/lib/forms";

import { ApprovalsForm } from "./approvals-form";

const settings = {
  briefApprovers: ["u1"],
  assetApprovers: ["u1"],
  launchApprovers: ["u1"],
  budgetOwners: ["u1"],
  budgetLimit: null,
  currency: "INR",
};

const approvers = [
  { userId: "u1", name: "Neha Rao" },
  { userId: "u2", name: "Arjun Mehta" },
];

test("sends each duty's people and the budget limit", async () => {
  const action = vi.fn(async (_: FormState, formData: FormData): Promise<FormState> => {
    expect(formData.getAll("assetApprovers")).toEqual(["u1", "u2"]);
    expect(formData.getAll("briefApprovers")).toEqual(["u1"]);
    expect(formData.get("budgetLimit")).toBe("200000");
    return { status: "saved", message: "Approval settings saved." };
  });
  render(<ApprovalsForm action={action} settings={settings} approvers={approvers} canEdit />);

  const assets = screen.getByRole("group", { name: "Asset approvers" });
  fireEvent.click(within(assets).getByLabelText("Arjun Mehta"));
  fireEvent.change(screen.getByLabelText("Per-campaign budget limit (INR)"), { target: { value: "200000" } });
  fireEvent.click(screen.getByRole("button", { name: "Save approval settings" }));

  expect((await screen.findByRole("status")).textContent).toBe("Approval settings saved.");
});

test("explains an empty budget limit", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit />);

  expect(screen.getByText("Leave it empty and every brief and launch goes to a budget owner.")).toBeTruthy();
});

test("is read-only for members who are not admins", () => {
  render(<ApprovalsForm action={vi.fn()} settings={settings} approvers={approvers} canEdit={false} />);

  expect(screen.queryByRole("button", { name: "Save approval settings" })).toBeNull();
  expect(screen.getByLabelText("Per-campaign budget limit (INR)").matches(":disabled")).toBe(true);
});
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
npm --prefix web test
```

Expected: FAIL. The four new components can't be found.

- [ ] **Step 3: Add the team and approval actions**

Replace `web/src/app/[workspace]/settings/actions.ts`:

```ts
"use server";

import { refresh } from "next/cache";
import { redirect } from "next/navigation";

import { coreApi, signInAgain } from "@/lib/core-api/client";
import { getMe } from "@/lib/core-api/queries";
import type { Role } from "@/lib/core-api/types";
import { problemToFormState, text, type FormState } from "@/lib/forms";

export async function updateWorkspace(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).PATCH("/api/brands/{brandId}", {
    params: { path: { brandId } },
    body: { name: text(formData, "name"), timeZone: text(formData, "timeZone") },
  });
  return finish(error, response, "Saved.");
}

export async function inviteMember(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).POST("/api/brands/{brandId}/invitations", {
    params: { path: { brandId } },
    body: { email: text(formData, "email"), roles: roles(formData) },
  });
  return finish(error, response, "Invitation sent. Ask them to sign in to Kairo with that email.");
}

export async function revokeInvitation(brandId: string, invitationId: string) {
  const { response } = await (await coreApi()).POST("/api/brands/{brandId}/invitations/{invitationId}/revoke", {
    params: { path: { brandId, invitationId } },
  });
  if (response.status === 401) {
    return signInAgain();
  }

  refresh();
}

export async function changeRoles(brandId: string, userId: string, _: FormState, formData: FormData): Promise<FormState> {
  const { error, response } = await (await coreApi()).PUT("/api/brands/{brandId}/members/{userId}/roles", {
    params: { path: { brandId, userId } },
    body: { roles: roles(formData) },
  });
  return finish(error, response, "Roles saved.");
}

export async function removeMember(brandId: string, userId: string): Promise<FormState> {
  const { error, response } = await (await coreApi()).DELETE("/api/brands/{brandId}/members/{userId}", {
    params: { path: { brandId, userId } },
  });
  if (response.ok && (await getMe()).id === userId) {
    redirect("/");
  }

  return finish(error, response, "Removed.");
}

export async function saveApprovalSettings(brandId: string, _: FormState, formData: FormData): Promise<FormState> {
  const limit = text(formData, "budgetLimit");
  if (limit !== "" && !Number.isFinite(Number(limit))) {
    return { status: "error", fieldErrors: { budgetLimit: ["Enter a number, or leave it empty."] } };
  }

  const { error, response } = await (await coreApi()).PUT("/api/brands/{brandId}/approval-settings", {
    params: { path: { brandId } },
    body: {
      briefApprovers: ids(formData, "briefApprovers"),
      assetApprovers: ids(formData, "assetApprovers"),
      launchApprovers: ids(formData, "launchApprovers"),
      budgetOwners: ids(formData, "budgetOwners"),
      budgetLimit: limit === "" ? null : Number(limit),
    },
  });
  return finish(error, response, "Approval settings saved.");
}

async function finish(error: unknown, response: Response, message: string): Promise<FormState> {
  if (response.status === 401) {
    return signInAgain();
  }

  if (!response.ok) {
    return problemToFormState(error, response.status);
  }

  refresh();
  return { status: "saved", message };
}

function roles(formData: FormData) {
  return formData.getAll("roles").map(String) as Role[];
}

function ids(formData: FormData, name: string) {
  return formData.getAll(name).map(String);
}
```

- [ ] **Step 4: Write the team components and page**

Create `web/src/components/role-checkboxes.tsx`:

```tsx
import { roleLabels, type Role } from "@/lib/core-api/types";

const roles: Role[] = ["admin", "marketer", "approver"];

type RoleCheckboxesProps = {
  legend: string;
  defaultValue?: Role[];
};

export function RoleCheckboxes({ legend, defaultValue = [] }: RoleCheckboxesProps) {
  return (
    <fieldset className="flex flex-wrap gap-4">
      <legend className="sr-only">{legend}</legend>
      {roles.map((role) => (
        <label key={role} className="flex items-center gap-1 text-sm">
          <input type="checkbox" name="roles" value={role} defaultChecked={defaultValue.includes(role)} />
          {roleLabels[role]}
        </label>
      ))}
    </fieldset>
  );
}
```

Create `web/src/app/[workspace]/settings/team/_components/member-row.tsx`. Removing someone, or leaving, asks for confirmation first:

```tsx
"use client";

import { useActionState } from "react";

import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { roleLabels, type Member } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type MemberRowProps = {
  member: Member;
  isSelf: boolean;
  canManage: boolean;
  changeRoles: (state: FormState, formData: FormData) => Promise<FormState>;
  remove: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function MemberRow({ member, isSelf, canManage, changeRoles, remove }: MemberRowProps) {
  const [rolesState, rolesAction, savingRoles] = useActionState(changeRoles, idle);
  const [removeState, removeAction, removing] = useActionState(remove, idle);
  const removeLabel = isSelf ? "Leave workspace" : "Remove";

  return (
    <li className="space-y-2 py-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="font-medium">
            {member.name}
            {isSelf && " (you)"}
          </p>
          <p className="text-sm text-zinc-600 dark:text-zinc-400">{member.email}</p>
        </div>
        {(canManage || isSelf) && (
          <form
            action={removeAction}
            onSubmit={(event) => {
              if (!window.confirm(isSelf ? "Leave this workspace?" : `Remove ${member.name} from this workspace?`)) {
                event.preventDefault();
              }
            }}
          >
            <button type="submit" disabled={removing} className="text-sm text-red-700 hover:underline dark:text-red-300">
              {removeLabel}
            </button>
          </form>
        )}
      </div>
      {canManage ? (
        <form action={rolesAction} className="flex flex-wrap items-center gap-4">
          <RoleCheckboxes legend={`Roles for ${member.name}`} defaultValue={member.roles} />
          <button type="submit" disabled={savingRoles} className="rounded-md border px-3 py-1 text-sm">
            Save roles
          </button>
        </form>
      ) : (
        <p className="text-sm">{member.roles.map((role) => roleLabels[role]).join(", ")}</p>
      )}
      <FormStatus state={rolesState} />
      <FormStatus state={removeState} />
    </li>
  );
}
```

Create `web/src/app/[workspace]/settings/team/_components/invite-form.tsx`:

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { idle, type FormState } from "@/lib/forms";

type InviteFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function InviteForm({ action }: InviteFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="max-w-md space-y-3">
      <div className="space-y-1">
        <label htmlFor="invite-email" className="block text-sm font-medium">Email</label>
        <input id="invite-email" name="email" type="email" required className="w-full rounded-md border px-3 py-2" />
        <FieldError id="invite-email-error" errors={state.fieldErrors?.email} />
      </div>
      <RoleCheckboxes legend="Roles for the new member" defaultValue={["marketer"]} />
      <FieldError id="invite-roles-error" errors={state.fieldErrors?.roles} />
      <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
        Send invitation
      </button>
      <FormStatus state={state} />
    </form>
  );
}
```

Create `web/src/app/[workspace]/settings/team/_components/pending-invitations.tsx`:

```tsx
import { roleLabels, type PendingInvitation } from "@/lib/core-api/types";

type PendingInvitationsProps = {
  invitations: PendingInvitation[];
  revoke: (invitationId: string) => Promise<void>;
};

export function PendingInvitations({ invitations, revoke }: PendingInvitationsProps) {
  if (invitations.length === 0) {
    return <p className="text-sm text-zinc-600 dark:text-zinc-400">No invitations are waiting.</p>;
  }

  return (
    <ul className="divide-y divide-black/10 dark:divide-white/15">
      {invitations.map((invitation) => (
        <li key={invitation.id} className="flex items-center justify-between gap-4 py-3">
          <div>
            <p className="font-medium">{invitation.email}</p>
            <p className="text-sm text-zinc-600 dark:text-zinc-400">
              {invitation.roles.map((role) => roleLabels[role]).join(", ")} · invited by {invitation.invitedBy}
            </p>
          </div>
          <form action={revoke.bind(null, invitation.id)}>
            <button type="submit" className="text-sm text-red-700 hover:underline dark:text-red-300">
              Revoke
            </button>
          </form>
        </li>
      ))}
    </ul>
  );
}
```

Create `web/src/app/[workspace]/settings/team/page.tsx`. Everyone sees the team; only admins see the controls, the invite form and the waiting invitations.

```tsx
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getMe, getWorkspace } from "@/lib/core-api/queries";

import { changeRoles, inviteMember, removeMember, revokeInvitation } from "../actions";
import { InviteForm } from "./_components/invite-form";
import { MemberRow } from "./_components/member-row";
import { PendingInvitations } from "./_components/pending-invitations";

export default async function TeamPage({ params }: PageProps<"/[workspace]/settings/team">) {
  const { workspace: handle } = await params;
  const [workspace, me] = await Promise.all([getWorkspace(handle), getMe()]);
  const api = await coreApi();
  const brandId = workspace.id;
  const isAdmin = workspace.roles.includes("admin");
  const members = await unwrap(await api.GET("/api/brands/{brandId}/members", { params: { path: { brandId } } }));
  const invitations = isAdmin
    ? await unwrap(await api.GET("/api/brands/{brandId}/invitations", { params: { path: { brandId } } }))
    : [];

  return (
    <div className="space-y-10">
      <section className="space-y-2">
        <h2 className="text-lg font-semibold">Members</h2>
        <ul className="divide-y divide-black/10 dark:divide-white/15">
          {members.map((member) => (
            <MemberRow
              key={member.userId}
              member={member}
              isSelf={member.userId === me.id}
              canManage={isAdmin}
              changeRoles={changeRoles.bind(null, brandId, member.userId)}
              remove={removeMember.bind(null, brandId, member.userId)}
            />
          ))}
        </ul>
      </section>
      {isAdmin && (
        <>
          <section className="space-y-4">
            <h2 className="text-lg font-semibold">Invite someone</h2>
            <InviteForm action={inviteMember.bind(null, brandId)} />
          </section>
          <section className="space-y-2">
            <h2 className="text-lg font-semibold">Waiting invitations</h2>
            <PendingInvitations invitations={invitations} revoke={revokeInvitation.bind(null, brandId)} />
          </section>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Write the approvals form and page**

Create `web/src/app/[workspace]/settings/approvals/_components/approvals-form.tsx`:

```tsx
"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import type { ApprovalSettings } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type Duty = "briefApprovers" | "assetApprovers" | "launchApprovers" | "budgetOwners";

const duties: { field: Duty; label: string; help: string }[] = [
  { field: "briefApprovers", label: "Brief approvers", help: "Approve briefs, gate 1." },
  { field: "assetApprovers", label: "Asset approvers", help: "Approve the asset set, gate 2." },
  { field: "launchApprovers", label: "Launch approvers", help: "Approve launches, gate 3." },
  { field: "budgetOwners", label: "Budget owners", help: "Approve briefs and launches above the budget limit." },
];

type ApprovalsFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  settings: ApprovalSettings;
  approvers: { userId: string; name: string }[];
  canEdit: boolean;
};

export function ApprovalsForm({ action, settings, approvers, canEdit }: ApprovalsFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);

  return (
    <form action={formAction} className="max-w-xl space-y-6">
      <fieldset disabled={!canEdit || pending} className="space-y-6">
        {duties.map((duty) => (
          <fieldset key={duty.field} className="space-y-2">
            <legend className="font-medium">{duty.label}</legend>
            <p className="text-sm text-zinc-600 dark:text-zinc-400">{duty.help}</p>
            {approvers.map((approver) => (
              <label key={approver.userId} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  name={duty.field}
                  value={approver.userId}
                  defaultChecked={settings[duty.field].includes(approver.userId)}
                />
                {approver.name}
              </label>
            ))}
            <FieldError id={`${duty.field}-error`} errors={state.fieldErrors?.[duty.field]} />
          </fieldset>
        ))}
        <div className="space-y-1">
          <label htmlFor="budgetLimit" className="block font-medium">
            Per-campaign budget limit ({settings.currency})
          </label>
          <input
            id="budgetLimit"
            name="budgetLimit"
            type="number"
            min="0"
            step="0.01"
            defaultValue={settings.budgetLimit ?? ""}
            aria-describedby="budgetLimit-help"
            className="w-full rounded-md border px-3 py-2"
          />
          <p id="budgetLimit-help" className="text-sm text-zinc-600 dark:text-zinc-400">
            Leave it empty and every brief and launch goes to a budget owner.
          </p>
          <FieldError id="budgetLimit-error" errors={state.fieldErrors?.budgetLimit} />
        </div>
      </fieldset>
      {canEdit && (
        <button type="submit" disabled={pending} className="rounded-md bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50 dark:bg-white dark:text-black">
          Save approval settings
        </button>
      )}
      <FormStatus state={state} />
    </form>
  );
}
```

Create `web/src/app/[workspace]/settings/approvals/page.tsx`. Only members with the approver role can be offered for a duty:

```tsx
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { saveApprovalSettings } from "../actions";
import { ApprovalsForm } from "./_components/approvals-form";

export default async function ApprovalsPage({ params }: PageProps<"/[workspace]/settings/approvals">) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const brandId = workspace.id;
  const [settings, members] = await Promise.all([
    unwrap(await api.GET("/api/brands/{brandId}/approval-settings", { params: { path: { brandId } } })),
    unwrap(await api.GET("/api/brands/{brandId}/members", { params: { path: { brandId } } })),
  ]);

  return (
    <ApprovalsForm
      action={saveApprovalSettings.bind(null, brandId)}
      settings={settings}
      approvers={members.filter((member) => member.roles.includes("approver"))}
      canEdit={workspace.roles.includes("admin")}
    />
  );
}
```

- [ ] **Step 6: Run the tests, lint and build**

```bash
npm --prefix web test
npm --prefix web run lint
npm --prefix web run build
```

Expected:
- **Tests:** 46 pass.
- **Lint:** no problems.
- **Build:** succeeds, with `/[workspace]/settings/team` and `/[workspace]/settings/approvals` listed.

- [ ] **Step 7: Commit**

```bash
git add web/src
git commit -m "Add team management and approval settings to the web app"
```

### Task 16: Local stack: gateway, Migrator and Google sign-in

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/src/Kairo.AppHost/Kairo.AppHost.csproj`
- Modify: `core/src/Kairo.AppHost/AppHost.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes:
  - from Task 3: `Kairo.Migrator`
  - from Task 5: the configuration keys `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret` and `Kairo:PublicOrigin`
  - from Task 12: `KAIRO_API_URL`
- Produces:
  - **Gateway:** `http://localhost:8000`, which sends `/api/*` to the API and everything else to the web app.
  - **AppHost parameters:** `google-client-id`, and `google-client-secret`, which is marked secret. Both are read from the AppHost's user secrets.

- [ ] **Step 1: Wire the gateway, the Migrator and the Google settings into the AppHost**

In `core/Directory.Packages.props`, add to the `Aspire` item group:

```xml
    <PackageVersion Include="Aspire.Hosting.Yarp" Version="13.6.1" />
```

Replace `core/src/Kairo.AppHost/Kairo.AppHost.csproj`:

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
    <PackageReference Include="Aspire.Hosting.Yarp" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kairo.Api\Kairo.Api.csproj" />
    <ProjectReference Include="..\Kairo.Migrator\Kairo.Migrator.csproj" />
    <ProjectReference Include="..\Kairo.Worker\Kairo.Worker.csproj" />
  </ItemGroup>

</Project>
```

Replace `core/src/Kairo.AppHost/AppHost.cs`:
- The Migrator runs to completion before the API and the worker start.
- The API learns its public address, so Google sends users back through the gateway.
- The web app's server code calls the API directly.

```csharp
#pragma warning disable ASPIREJAVASCRIPT001

var builder = DistributedApplication.CreateBuilder(args);

var googleClientId = builder.AddParameter("google-client-id");
var googleClientSecret = builder.AddParameter("google-client-secret", secret: true);

var sql = builder.AddSqlServer("sql")
    .WithDataVolume()
    .AddDatabase("kairo");

var redis = builder.AddRedis("redis");

var agents = builder.AddUvicornApp("agents", "../../../agents", "kairo_agents.main:app")
    .WithUv()
    .WithHttpHealthCheck("/health");

var migrator = builder.AddProject<Projects.Kairo_Migrator>("migrator")
    .WithReference(sql).WaitFor(sql);

var api = builder.AddProject<Projects.Kairo_Api>("api")
    .WithReference(sql).WaitFor(sql)
    .WithReference(redis).WaitFor(redis)
    .WithReference(agents)
    .WaitForCompletion(migrator)
    .WithEnvironment("Authentication__Google__ClientId", googleClientId)
    .WithEnvironment("Authentication__Google__ClientSecret", googleClientSecret)
    .WithEnvironment("Kairo__PublicOrigin", "http://localhost:8000")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Kairo_Worker>("worker")
    .WithReference(sql).WaitFor(sql)
    .WithReference(agents)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health");

var web = builder.AddNextJsApp("web", "../../../web")
    .WithEnvironment("KAIRO_API_URL", api.GetEndpoint("http"))
    .WithHttpHealthCheck("/health");

builder.AddYarp("gateway")
    .WithHostPort(8000)
    .WithConfiguration(yarp =>
    {
        yarp.AddRoute("/api/{**catch-all}", api);
        yarp.AddRoute("/{**catch-all}", web);
    });

builder.Build().Run();
```

```bash
dotnet build core/src/Kairo.AppHost
```

Expected: the build succeeds with 0 warnings.

- [ ] **Step 2: Document the Google set-up and the new way to run Kairo**

Replace `README.md`:

````markdown
# Kairo

Kairo takes a marketer from a rising demand signal to a live, on-brand Google Ads campaign in under an hour, then proves the extra sales it caused. The [BRD](docs/Kairo-BRD.md), [PRD](docs/Kairo-PRD.md) and [SAD](docs/Kairo-SAD.md) describe the product and its architecture.

## Repository layout

| Folder | What it holds |
| --- | --- |
| `core/` | The ASP.NET Core API and worker, the 14 modules, the Migrator, and the Aspire AppHost for local development |
| `agents/` | The Python agent service, built with Google's Agent Development Kit and FastAPI |
| `web/` | The Next.js web app |
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

The core tests need Docker running, because `Kairo.IntegrationTests` starts SQL Server in a container. To run one test class, use `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`.

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
docker build -t kairo-agents agents
docker build -t kairo-web web
```

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
````

- [ ] **Step 3: Start the stack and smoke-test it**

Make sure Docker is running. If the Google client doesn't exist yet, store placeholder values so the AppHost can start; real sign-in then fails at Google, but everything else can be checked:

```bash
dotnet user-secrets list --project core/src/Kairo.AppHost
```

If `Parameters:google-client-id` isn't listed, set placeholders:

```bash
dotnet user-secrets set "Parameters:google-client-id" "not-configured" --project core/src/Kairo.AppHost
dotnet user-secrets set "Parameters:google-client-secret" "not-configured" --project core/src/Kairo.AppHost
```

Start the AppHost as a background task:

```bash
dotnet run --project core/src/Kairo.AppHost
```

Wait until `curl -sf http://localhost:8000/health` succeeds. The first start can take several minutes. Then check what's reachable through the gateway:

```bash
curl -s http://localhost:8000/health
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/api/me
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:8000/acme/today
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/sign-in
curl -s -o /dev/null -w "%{redirect_url}\n" "http://localhost:8000/api/auth/sign-in?returnUrl=/"
```

Expected:
1. `{"status":"ok"}`, from the web app through the gateway.
2. `401`. The API is up, which means the Migrator finished, and the endpoint needs a session.
3. `307 http://localhost:8000/sign-in?returnUrl=%2Facme%2Ftoday`, from the proxy.
4. `200`, the sign-in page.
5. A URL on `accounts.google.com` that contains `redirect_uri=http%3A%2F%2Flocalhost%3A8000%2Fapi%2Fauth%2Fcallback%2Fgoogle`.

In the Aspire dashboard, `migrator` shows as finished, and `api`, `worker`, `agents`, `web` and `gateway` as healthy or running.

- [ ] **Step 4: Ask your human partner to check real sign-in in a browser**

This needs a person, because Google sign-in can't be automated. It also needs the real OAuth client from "Set up Google sign-in" in the README. Ask them to do the following in Chrome or Edge at `http://localhost:8000`, and to report anything that doesn't happen as described:

1. Sign in with a Google account. They land on `/welcome`.
2. Create a workspace. They land on `/<handle>/today`, with the workspace switcher, the menu, and their name with "Sign out".
3. Open Settings → Team and invite a second Google account as a marketer.
4. In a private window, sign in as the second account. `/welcome` shows the invitation. Accept it.
5. Back in the first window:
   - Give the second account the approver role.
   - In Settings → Approvals, add them as an asset approver and set a budget limit.
   - In Settings → Audit log, see each of those changes listed.
6. Create a second workspace from the switcher's link, and switch between the two.
7. Sign out. Opening `http://localhost:8000/` goes to `/sign-in`.

If the browser console shows Next.js blocking requests from `localhost:8000`, add `allowedDevOrigins: ["localhost"]` to `web/next.config.ts`. If forms fail with "Invalid Server Actions request", add `serverActions: { allowedOrigins: ["localhost:8000"] }` under `experimental` in the same file. Rerun `npm --prefix web run build` after either change.

- [ ] **Step 5: Stop the stack and commit**

Stop the background AppHost task. If containers named `sql-*` or `redis-*` are left running, remove them with `docker rm -f <name>`; the SQL Server data volume can stay.

```bash
git add core/Directory.Packages.props core/src/Kairo.AppHost README.md
git commit -m "Run Kairo behind one local address, with the Migrator and Google sign-in"
```

If `web/next.config.ts` changed in Step 4, add it to the same commit.

### Task 17: Documentation and CI

**Files:**
- Modify: `docs/Kairo-SAD.md`
- Modify: `web/README.md`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: everything above.
- Produces: an SAD that matches the sign-in design, and a CI check that the generated web types match the committed OpenAPI document.

- [ ] **Step 1: Update the SAD**

In `docs/Kairo-SAD.md`, replace four table rows.

In §2, replace the `Web app` row:

```markdown
| Web app | Next.js and React in TypeScript, Tailwind CSS, Identity Platform client SDK, SignalR client | Screens, approvals, the Ask Kairo panel and live updates |
```

with:

```markdown
| Web app | Next.js and React in TypeScript, Tailwind CSS, SignalR client | Screens, approvals, the Ask Kairo panel and live updates |
```

In §2, replace the `Identity and secrets` row:

```markdown
| Identity and secrets | Identity Platform (Google sign-in), Secret Manager, Cloud KMS | User sign-in, API keys and the developer token, and encrypted OAuth tokens |
```

with:

```markdown
| Identity and secrets | Google OAuth 2.0 client (Google sign-in), Secret Manager, Cloud KMS | User sign-in, API keys and the developer token, and encrypted OAuth tokens |
```

In §6, replace the `Access` row:

```markdown
| Access | Brand, User, Membership, ApproverAssignment, Connection | OAuth tokens encrypted with Cloud KMS |
```

with:

```markdown
| Access | Brand, User, Session, Membership, Invitation, ApprovalDuty, BudgetLimit, Connection | Sessions kept server-side; OAuth tokens encrypted with Cloud KMS |
```

In §8, replace the `Sign-in` row:

```markdown
| Sign-in | Identity Platform with Google sign-in. The web app sends the ID token with each request, and the core validates it. |
```

with:

```markdown
| Sign-in | Google sign-in through the core's own OAuth client, using OpenID Connect. The core keeps each session in SQL Server behind an httpOnly cookie, and the web app's server code passes that cookie on when it calls the core. |
```

```bash
grep -c "Identity Platform" docs/Kairo-SAD.md || true
```

Expected: `0`.

- [ ] **Step 2: Update the web app README**

Replace `web/README.md`:

```markdown
# Kairo web app

The Next.js app marketers, approvers and admins use. It talks only to the core API.

Run it on its own with `npm run dev`, with `KAIRO_API_URL` set to the core's address. Or start the whole stack as described in the [root README](../README.md) and open http://localhost:8000.

**Routes and components.**
- Every screen except sign-in and welcome sits under `src/app/[workspace]/`, so the workspace handle is in every URL.
- Components used by one area go next to its routes, in a private `_components/` folder. Shared components are in `src/components/`.

**The core API client** lives in `src/lib/core-api/`.
- `schema.ts` is generated from the core's OpenAPI document by `npm run generate:api`. Never edit it by hand; CI fails if it drifts.
- Server components read through `coreApi()` and `unwrap()`. Together they forward the session cookie, send the user to sign-in when the session has ended, and render "not found" for a 404.
- Changes go through Server Actions. Forms receive their action as a prop, so tests can pass a fake one.

**The proxy.** `src/proxy.ts` sends visitors without a session cookie to `/sign-in`, and remembers the last workspace they opened.

Test with `npm test`, and lint with `npm run lint`.
```

- [ ] **Step 3: Check the generated types in CI**

In `.github/workflows/ci.yml`, in the `web` job, add two steps right after `- run: npm ci`:

```yaml
      - run: npm run generate:api
      - run: git diff --exit-code -- src/lib/core-api/schema.ts
```

The `core` job needs no change. GitHub's `ubuntu-latest` runners have Docker, which `Kairo.IntegrationTests` uses through Testcontainers.

- [ ] **Step 4: Run every check the spec's done criteria name**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
npm --prefix web run generate:api && git diff --exit-code -- web/src/lib/core-api/schema.ts
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
uv run --directory agents ruff check
uv run --directory agents ruff format --check
uv run --directory agents pytest
docker build -f core/src/Kairo.Api/Dockerfile -t kairo-api core
docker build -t kairo-web web
```

Expected: every command succeeds.
- The core build has 0 warnings, and the generated web types are unchanged.
- The tests are: `Kairo.ArchitectureTests`, `Kairo.Api.Tests` and `Kairo.Worker.Tests` passing as before, plus the new schema rule; `Kairo.IntegrationTests` with 106 test cases; and the 46 web tests.
- Both images build, which shows the new projects and dependencies don't break the containers.

Then confirm the browser check from Task 16, Step 4 passed. If the real Google client wasn't ready then, say so in the hand-over, because real sign-in is still unverified.

- [ ] **Step 5: Commit**

```bash
git add docs/Kairo-SAD.md web/README.md .github/workflows/ci.yml
git commit -m "Describe Google sign-in in the SAD and check the generated web types in CI"
```
