# Kairo Account Connections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a workspace admin connect the brand's Google Ads, YouTube, Merchant Center, GA4 and BigQuery accounts with their own Google consent, keep each refresh token encrypted, and give later modules one place to get a working access token or learn that access is gone.

**Architecture:**
- **Brands module.** The Brands module gains a `Connections` folder:
  - one `brands.Connections` row per brand and product, with its refresh token encrypted under the brand and connection IDs
  - consent through Kairo's existing Google OAuth client, with PKCE, a protected single-use `state` and offline access
  - one small REST client per product, which lists the accounts the person can reach and runs a read-and-write test call
- **Contract for other modules.** `IConnections` in `Kairo.Contracts` gives other modules a fresh access token, or throws when access is gone. Identity calls it to release a departing admin's connections.
- **Encryption.** Cloud KMS encrypts tokens in Google Cloud. ASP.NET Data Protection stands in locally and in tests.

**Tech Stack:**
- **Core:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 on SQL Server 2022.
- **Google APIs:** plain `HttpClient` calls to the Google REST APIs (Google Ads API v25, YouTube Data API v3, Merchant API Accounts v1, Analytics Admin API v1beta, BigQuery API v2, Google OAuth 2.0).
- **KMS:** `Google.Cloud.Kms.V1` 3.27.0.
- **Tests:** xUnit v3, Testcontainers SQL Server, and Pester 5.9.1 for the deployment scripts.

**Spec:** `docs/superpowers/specs/2026-10-13-account-connections-design.md`

## Global Constraints

- **Worktree and branch.**
  - Work only in `C:\Kairo-worktrees\account-connections`, on branch `feature/account-connections`.
  - Run every command in Git Bash from `/c/Kairo-worktrees/account-connections`, unless a step says otherwise.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user (`CLAUDE.md` §1).
  - Stage files by explicit path, never with `git add -A` or `git add .`. Never stage `CLAUDE.md` or `.claude/`.
  - Don't push.
- **No comments in code** (`CLAUDE.md` §3). That means no `//`, `#`, `/* */` or `<!-- -->` comments, no XML doc comments and no JSDoc. Generated EF Core migrations go through `core/scripts/tidy-migrations.sh`.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build must finish with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container.
- **Module rules stay true.**
  - Each module exposes exactly one public type and adds no `ProjectReference`.
  - Tables live only in the module's schema.
  - Every new class in `Kairo.Modules.Brands` is `internal`. Public types go only in `Kairo.Contracts`.
- **Products and slugs.** These slugs are used in routes and API responses:
  - `google-ads`
  - `youtube`
  - `merchant-center`
  - `ga4`
  - `bigquery`

  Anything else returns 404.
- **States** in API responses (camel case): `notConnected`, `choosingAccount`, `connected`, `needsReconnect`.
- **Audit actions:**
  - `connection.connected`
  - `connection.account_chosen`
  - `connection.disconnected`
  - `connection.needs_reconnect`, with the reason `token_rejected`, `token_unreadable` or `admin_left`
- **Callback** path: `/api/connections/callback/google`.
- **Failure codes** on the settings redirect: `cancelled`, `scopes_missing`, `expired`, `failed`.
- **Timings.**
  - Consent `state` lives 10 minutes.
  - A connection left in Choosing account counts as Not connected after 1 hour.
  - Cached access tokens are reused until 1 minute before they expire.
- **Configuration.**
  - Existing: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, `Kairo:PublicOrigin`.
  - New: `Kairo:Kms:KeyName`, which turns on Cloud KMS, and `Kairo:Google:AdsApiVersion`, which defaults to `v25`.
- **Named HTTP client** for every Google call: `kairo-google`. Tests replace its primary handler with `FakeGoogleApis`.
- **Versions.**
  - `Google.Cloud.Kms.V1`: 3.27.0
  - Google Ads REST API: `v25`, the current major version on 13 October 2026
- **Running tests.**
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`
  - Whole core: `dotnet test --solution core/Kairo.slnx`
  - `OpenApiDocumentTests` fails from Task 2 until Task 6 regenerates the document. Until Task 6, run each task's own test classes.

## Review Focus

These are the inputs most likely to hurt a real user. Each one is pinned by a test in the task named in brackets.

1. **Disconnecting one product, reconnecting, or a departing admin, when the same Google account also drives another Kairo connection.**
   - Google's revoke endpoint may drop the person's whole grant to Kairo's OAuth client.
   - Kairo must revoke only when that Google account has no other live connection, so the other products keep working.
   - (Task 4: `Disconnecting_one_product_keeps_another_on_the_same_Google_account`; Task 2: `Reconnecting_revokes_the_old_token_only_when_its_Google_account_is_done_with_Kairo`.)
2. **A consent link finished in someone else's session.** Someone opens a callback URL that another admin started.
   - Nothing may be attached to the workspace.
   - The page must reveal nothing about it.
   - (Task 2: `A_callback_in_someone_elses_session_changes_nothing`.)
3. **A writable Google Ads account rejects the `validateOnly` budget for a field reason**, such as a minimum budget in that currency. It must still count as write access. Only 401 and 403 mean "can't make changes". (Task 3: `A_Google_Ads_validation_error_still_proves_write_access`.)
4. **The database is read or rows are tampered with.**
   - The refresh token must never be stored in plain text.
   - A ciphertext copied to another brand's row must not decrypt there.
   - (Task 2: `The_refresh_token_is_never_stored_in_plain_text`; Task 4: `A_token_copied_to_another_workspace_cannot_be_used`.)
5. **The callback is replayed or arrives late.** For example, the browser goes back to it, or the admin loses the role while at Google.
   - No second connection appears.
   - A demoted admin's consent is dropped.
   - (Task 2: `A_replayed_callback_is_refused` and `Someone_who_stopped_being_an_admin_cannot_finish_connecting`.)

## Where this plan differs from the spec

1. **REST over `HttpClient` instead of the official .NET clients**, for Google Ads, YouTube, Merchant, Analytics Admin and BigQuery.
   - Every call goes through the `kairo-google` named client, so tests fake Google at the HTTP boundary with one handler. The module's types stay internal.
   - It avoids gRPC fakes, and avoids the Google Ads library's developer-token configuration.
   - KMS still uses `Google.Cloud.Kms.V1`, which tests never call.
2. **BigQuery** uses the BigQuery API's own `projects.list`. Its test call is `datasets.list` plus a dry-run query job, not Cloud Resource Manager `projects.search` and `testIamPermissions`.
   - The `bigquery` scope doesn't cover Cloud Resource Manager.
   - A dry run proves `bigquery.jobs.create` directly.
3. **Revocation only when the Google account is done with Kairo.**
   - Each consent also asks for `openid`, and the connection stores the Google account's `sub` as `GoogleSubject`.
   - Disconnecting, reconnecting, missing scopes and a departing admin all revoke the old token only if no other live connection, in any workspace, uses that Google account.
   - Kairo's copy is always erased.
4. **Nonce table.** It is named `brands.ConsentNonces`. Expired nonces are deleted at each callback rather than after a day, because an expired `state` is refused before its nonce matters.
5. **An extra reconnect reason**, `token_unreadable`, for a ciphertext that won't decrypt.
6. **Callbacks that can't be trusted redirect to `/`, not to the workspace's settings page.** That covers a `state` that can't be read, belongs to another user, or comes back after the user stopped being an admin. Redirecting to `/` reveals nothing about the workspace.
7. **Product listing and test-call mapping are tested through the API** with `FakeGoogleApis`, not as unit tests. No module has a unit test project, and the module's internals stay internal.
8. **SAD sections.** The developer token appears in SAD §2, §7 (integrations, which the spec called §5), §8 and §13.
9. **The worker** gets the KMS key name and permission now. It gets the Google OAuth secrets when it first uses a connection.
10. **YouTube's test call** checks that the channel is still among the ones that Google account manages. The upload scope is enforced at the callback.

---

### Task 1: Connection storage

**Files:**
- Create: `core/src/Kairo.Contracts/Connections/IConnections.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Connections/Connection.cs`
- Create: `core/src/Modules/Kairo.Modules.Brands/Connections/ConsentNonce.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContext.cs`
- Create (generated): `core/src/Modules/Kairo.Modules.Brands/Data/Migrations/<timestamp>_Connections.cs` and `<timestamp>_Connections.Designer.cs`
- Modify (generated): `core/src/Modules/Kairo.Modules.Brands/Data/Migrations/BrandsDbContextModelSnapshot.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/ConnectionTableTests.cs`

**Interfaces:**
- Consumes: `ModuleDbContext`, `IBrandOwned`, and the `AddBrandSeparation` and `RemoveBrandSeparation` migration helpers.
- Produces:
  - In `Kairo.Contracts.Connections`:
    - `enum ConnectedProduct { GoogleAds, YouTube, MerchantCenter, Ga4, BigQuery }`
    - `enum ConnectionState { NotConnected, ChoosingAccount, Connected, NeedsReconnect }`
    - `interface IConnections`
    - `record ConnectionStatus`
    - `record ConnectionAccess`
    - `class ConnectionUnavailableException`
  - In `Kairo.Modules.Brands.Connections`:
    - `Connection`, with `Id`, `BrandId`, `Product`, `State`, `ExternalId`, `DisplayName`, `Scopes`, `GoogleSubject`, `RefreshToken` (`byte[]?`), `KeyVersion`, `ConnectedBy`, `ConnectedByName`, `ConnectedAt`, `CheckedAt` and `ReconnectReason`
    - `ConsentNonce`, with `Nonce` and `ExpiresAt`
  - `BrandsDbContext.Connections` and `BrandsDbContext.ConsentNonces`.

- [ ] **Step 1: Write the failing test**

Create `core/tests/Kairo.IntegrationTests/Persistence/ConnectionTableTests.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class ConnectionTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connections_are_covered_by_the_brands_security_policy()
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'brands.Connections')";

        Assert.Equal(3, (int)(await command.ExecuteScalarAsync(Token))!);
    }

    [Fact]
    public async Task A_workspace_has_at_most_one_connection_per_product()
    {
        var brandId = Guid.NewGuid();
        await InsertAsync(brandId);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(brandId));

        Assert.Equal(2601, duplicate.Number);
    }

    private async Task InsertAsync(Guid brandId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            INSERT INTO brands.Connections (Id, BrandId, Product, State, Scopes, GoogleSubject, ConnectedBy, ConnectedByName, ConnectedAt)
            VALUES (NEWID(), @brand, N'GoogleAds', N'ChoosingAccount', N'', N'subject', NEWID(), N'Neha', SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        await command.ExecuteNonQueryAsync(Token);
    }
}
```

- [ ] **Step 2: Run the test to see it fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.ConnectionTableTests"
```

Expected: both tests fail, because there is no `brands.Connections` table.
- The first counts 0 predicates.
- The second throws `SqlException` 208 (invalid object name) rather than 2601.

- [ ] **Step 3: Add the contract**

Create `core/src/Kairo.Contracts/Connections/IConnections.cs`:

```csharp
namespace Kairo.Contracts.Connections;

public enum ConnectedProduct
{
    GoogleAds,
    YouTube,
    MerchantCenter,
    Ga4,
    BigQuery,
}

public enum ConnectionState
{
    NotConnected,
    ChoosingAccount,
    Connected,
    NeedsReconnect,
}

public interface IConnections
{
    Task<ConnectionStatus> GetStatusAsync(ConnectedProduct product, CancellationToken cancellationToken);

    Task<ConnectionAccess> GetAccessAsync(ConnectedProduct product, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record ConnectionStatus(ConnectedProduct Product, ConnectionState State, string? ExternalId, string? DisplayName);

public sealed record ConnectionAccess(string ExternalId, string AccessToken, DateTimeOffset ExpiresAt);

public sealed class ConnectionUnavailableException(ConnectedProduct product, string reason)
    : Exception($"The {product} connection is unavailable: {reason}.")
{
    public ConnectedProduct Product { get; } = product;

    public string Reason { get; } = reason;
}
```

- [ ] **Step 4: Add the entities**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Connection.cs`:

```csharp
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections;

internal sealed class Connection : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public ConnectedProduct Product { get; set; }

    public ConnectionState State { get; set; }

    public string? ExternalId { get; set; }

    public string? DisplayName { get; set; }

    public string Scopes { get; set; } = "";

    public string GoogleSubject { get; set; } = "";

    public byte[]? RefreshToken { get; set; }

    public string? KeyVersion { get; set; }

    public Guid ConnectedBy { get; set; }

    public string ConnectedByName { get; set; } = "";

    public DateTimeOffset ConnectedAt { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public string? ReconnectReason { get; set; }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConsentNonce.cs`:

```csharp
namespace Kairo.Modules.Brands.Connections;

internal sealed class ConsentNonce
{
    public string Nonce { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }
}
```

- [ ] **Step 5: Map the tables**

Replace `core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContext.cs` with:

```csharp
using Kairo.Modules.Brands.Connections;

namespace Kairo.Modules.Brands.Data;

internal sealed class BrandsDbContext(DbContextOptions<BrandsDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "brands";

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<ConsentNonce> ConsentNonces => Set<ConsentNonce>();

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

        modelBuilder.Entity<Connection>(connection =>
        {
            connection.ToTable("Connections");
            connection.HasIndex(c => new { c.BrandId, c.Product }).IsUnique();
            connection.HasIndex(c => c.GoogleSubject);
            connection.Property(c => c.Product).HasConversion<string>().HasMaxLength(20);
            connection.Property(c => c.State).HasConversion<string>().HasMaxLength(20);
            connection.Property(c => c.ExternalId).HasMaxLength(100);
            connection.Property(c => c.DisplayName).HasMaxLength(200);
            connection.Property(c => c.Scopes).HasMaxLength(1000);
            connection.Property(c => c.GoogleSubject).HasMaxLength(255);
            connection.Property(c => c.RefreshToken).HasMaxLength(4096);
            connection.Property(c => c.KeyVersion).HasMaxLength(300);
            connection.Property(c => c.ConnectedByName).HasMaxLength(200);
            connection.Property(c => c.ReconnectReason).HasMaxLength(40);
        });

        modelBuilder.Entity<ConsentNonce>(nonce =>
        {
            nonce.ToTable("ConsentNonces");
            nonce.HasKey(n => n.Nonce);
            nonce.Property(n => n.Nonce).HasMaxLength(32);
            nonce.HasIndex(n => n.ExpiresAt);
        });
    }
}
```

- [ ] **Step 6: Generate and tidy the migration**

```bash
dotnet tool restore
dotnet ef migrations add Connections --project core/src/Modules/Kairo.Modules.Brands --startup-project core/src/Kairo.Migrator --context BrandsDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.Brands/Data/Migrations/*.cs
```

Open the new `core/src/Modules/Kairo.Modules.Brands/Data/Migrations/<timestamp>_Connections.cs`:
- In `Up`, add this as the last line, after the generated `CreateIndex` calls:

  ```csharp
              migrationBuilder.AddBrandSeparation("brands", "Connections");
  ```

- In `Down`, add this as the first line:

  ```csharp
              migrationBuilder.RemoveBrandSeparation("brands", "Connections");
  ```

Then check that `git diff --stat` shows no `// <auto-generated` lines, and that `grep -n "public partial class" core/src/Modules/Kairo.Modules.Brands/Data/Migrations/*.cs` prints nothing.

- [ ] **Step 7: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.ConnectionTableTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.SecurityPolicyCoverageTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: all pass with 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add core/src/Kairo.Contracts/Connections/IConnections.cs \
  core/src/Modules/Kairo.Modules.Brands/Connections/Connection.cs \
  core/src/Modules/Kairo.Modules.Brands/Connections/ConsentNonce.cs \
  core/src/Modules/Kairo.Modules.Brands/Data/BrandsDbContext.cs \
  core/src/Modules/Kairo.Modules.Brands/Data/Migrations \
  core/tests/Kairo.IntegrationTests/Persistence/ConnectionTableTests.cs
git commit -m "Store account connections in the brands schema, one per product"
```

---

### Task 2: Giving consent

**Files:**
- Create in `core/src/Modules/Kairo.Modules.Brands/Connections/`:
  - `Products.cs`
  - `GoogleHttp.cs`
  - `GoogleOAuth.cs`
  - `ConsentStates.cs`
  - `TokenCipher.cs`
  - `ConnectionTokens.cs`
  - `GoogleRevocations.cs`
  - `ConnectionLifecycle.cs`
  - `ConnectionEndpoints.cs`
  - `ConsentEndpoints.cs`
  - `ConnectionSetup.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/BrandsModule.cs`
- Create: `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`
- Create: `core/tests/Kairo.IntegrationTests/Api/Connections.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/ConnectionConsentTests.cs`

**Interfaces:**
- Consumes:
  - Task 1's entities and `BrandsDbContext`
  - `IBrandAccess`, `KairoDatabase`, `CurrentMember` and `IAuditLog`
  - the `MapBrandApi()` and `RequireBrandRole()` route helpers
  - `IDataProtectionProvider`, which the Identity module registers
- Produces:
  - `Products.All`, `Products.FindBySlug(string?)` and `Products.Get(ConnectedProduct)`, returning `ProductInfo(ConnectedProduct Product, string Slug, string Name, string[] Scopes)`
  - `GoogleHttp.ClientName` (`"kairo-google"`), `GoogleHttp.Json`, `GoogleHttp.SnakeCase`, and the `HttpClient.CallAsync(method, url, accessToken, body, ct)` extension
  - `GoogleOAuth`, with:
    - `RedirectUri(HttpRequest)`
    - `AuthorizeUrl(ProductInfo, ConsentState, string protectedState, string redirectUri)`
    - `ExchangeAsync(code, verifier, redirectUri, ct)`, which returns `Grant?`
    - `RefreshAsync(refreshToken, ct)`, which returns `Grant?` and returns null when Google answers `invalid_grant`
    - `RevokeAsync(token, ct)`, which is best effort
  - `Grant(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken, string[] Scopes, string? Subject)`
  - `ConsentState` and `ConsentStates`, with `Create`, `Protect` and `Unprotect`
  - `ITokenCipher`, with `SealAsync(token, context, ct)` returning `SealedToken(byte[] Ciphertext, string KeyVersion)` and `OpenAsync(ciphertext, context, ct)` returning a string. `DataProtectionTokenCipher` implements it.
  - `ConnectionTokens.SealAsync(Connection, string, ct)` and `ConnectionTokens.OpenAsync(Connection, ct)`
  - `GoogleRevocations.RevokeIfUnusedAsync(subject, refreshToken, ct)`
  - `ConnectionLifecycle.StateOf`, `FindAsync` and `Snapshot`
  - `ConnectionView` and `ConnectionView.Of`
  - `ConnectionSetup.AddConnections` and `ConnectionSetup.MapConnections`
  - Endpoints:
    - `GET /api/brands/{brandId}/connections`
    - `GET /api/brands/{brandId}/connections/{product}/connect`
    - `GET /api/connections/callback/google`
  - Test helpers:
    - `FakeGoogleApis`
    - `IssuedCode`
    - `KairoApiFactory.GoogleApis`
    - `Connections.StartAsync`, `CompleteAsync`, `GetAsync` and `StoredTokenAsync`
    - `Callback`

- [ ] **Step 1: Add the fake Google APIs and the test helpers**

Create `core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Api;

public sealed record IssuedCode(string Code, string RefreshToken, string Subject);

public sealed class FakeGoogleApis : HttpMessageHandler
{
    public const string ClientSecret = "kairo-test-secret";

    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private readonly ConcurrentDictionary<string, string> _refreshTokens = new();
    private readonly ConcurrentDictionary<string, bool> _accessTokens = new();

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> Revoked { get; } = new();

    public bool RevocationFails { get; set; }

    public HttpStatusCode AdsWrite { get; set; } = HttpStatusCode.OK;

    public string[] YouTubeChannels { get; set; } = ["UC-acme"];

    public string[] MerchantAccessRights { get; set; } = ["ADMIN"];

    public HttpStatusCode Ga4Read { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BigQueryRead { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BigQueryJobs { get; set; } = HttpStatusCode.OK;

    public int Refreshes => Requests.Count(request => request == "refresh_token");

    public IssuedCode IssueCode(string challenge, string redirectUri, IEnumerable<string> scopes, string subject)
    {
        var issued = new IssuedCode(Guid.NewGuid().ToString("N"), $"refresh-{Guid.NewGuid():N}", subject);
        _codes[issued.Code] = new PendingCode(issued, challenge, redirectUri, string.Join(' ', scopes.Append("openid").Distinct()));
        return issued;
    }

    public void Reject(string refreshToken) => _refreshTokens.TryRemove(refreshToken, out _);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        if (uri.Host == "oauth2.googleapis.com")
        {
            var form = QueryHelpers.ParseQuery("?" + body).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            return uri.AbsolutePath == "/revoke" ? Revoke(form) : Token(form);
        }

        Requests.Enqueue($"{request.Method} {uri.Host}{uri.AbsolutePath}");
        if (request.Headers.Authorization?.Parameter is not { } accessToken || !_accessTokens.ContainsKey(accessToken))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return Product(uri, body);
    }

    private HttpResponseMessage Token(Dictionary<string, string> form)
    {
        if (form.GetValueOrDefault("client_id") != FakeGoogle.ClientId || form.GetValueOrDefault("client_secret") != ClientSecret)
        {
            return Error(HttpStatusCode.Unauthorized, "invalid_client");
        }

        if (form.GetValueOrDefault("grant_type") == "refresh_token")
        {
            Requests.Enqueue("refresh_token");
            return _refreshTokens.ContainsKey(form.GetValueOrDefault("refresh_token") ?? "")
                ? Tokens(null, null, null)
                : Error(HttpStatusCode.BadRequest, "invalid_grant");
        }

        Requests.Enqueue("authorization_code");
        if (!_codes.TryRemove(form.GetValueOrDefault("code") ?? "", out var pending)
            || pending.RedirectUri != form.GetValueOrDefault("redirect_uri")
            || pending.Challenge != Challenge(form.GetValueOrDefault("code_verifier") ?? ""))
        {
            return Error(HttpStatusCode.BadRequest, "invalid_grant");
        }

        _refreshTokens[pending.Issued.RefreshToken] = pending.Issued.Subject;
        return Tokens(pending.Issued.RefreshToken, pending.Scopes, pending.Issued.Subject);
    }

    private HttpResponseMessage Tokens(string? refreshToken, string? scope, string? subject)
    {
        var accessToken = $"access-{Guid.NewGuid():N}";
        _accessTokens[accessToken] = true;
        var response = new Dictionary<string, object> { ["access_token"] = accessToken, ["token_type"] = "Bearer", ["expires_in"] = 3600 };
        if (refreshToken is not null)
        {
            response["refresh_token"] = refreshToken;
        }

        if (scope is not null)
        {
            response["scope"] = scope;
        }

        if (subject is not null)
        {
            response["id_token"] = string.Join('.', Encode(new { alg = "none" }), Encode(new { sub = subject }), "signature");
        }

        return Json(HttpStatusCode.OK, response);
    }

    private HttpResponseMessage Revoke(Dictionary<string, string> form)
    {
        if (RevocationFails)
        {
            return Error(HttpStatusCode.BadRequest, "invalid_token");
        }

        var token = form.GetValueOrDefault("token") ?? "";
        Revoked.Enqueue(token);
        _refreshTokens.TryRemove(token, out _);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private HttpResponseMessage Product(Uri uri, string body)
    {
        var path = uri.AbsolutePath;
        return (uri.Host, path) switch
        {
            ("googleads.googleapis.com", "/v25/customers:listAccessibleCustomers") =>
                Json(HttpStatusCode.OK, new { resourceNames = new[] { "customers/1111111111", "customers/2222222222", "customers/3333333333" } }),
            ("googleads.googleapis.com", _) when path.EndsWith("/googleAds:search", StringComparison.Ordinal) => AdsCustomer(path),
            ("googleads.googleapis.com", _) when path.EndsWith("/campaignBudgets:mutate", StringComparison.Ordinal) =>
                body.Contains("\"validateOnly\":true", StringComparison.Ordinal) ? Json(AdsWrite, new { }) : new HttpResponseMessage(HttpStatusCode.BadRequest),
            ("www.googleapis.com", "/youtube/v3/channels") =>
                Json(HttpStatusCode.OK, new { items = YouTubeChannels.Select(id => new { id, snippet = new { title = $"Channel {id}" } }) }),
            ("merchantapi.googleapis.com", "/accounts/v1/accounts") =>
                Json(HttpStatusCode.OK, new { accounts = new[] { new { accountId = "555", accountName = "Acme Store" } } }),
            ("merchantapi.googleapis.com", "/accounts/v1/accounts/555/users/me") =>
                Json(HttpStatusCode.OK, new { accessRights = MerchantAccessRights }),
            ("analyticsadmin.googleapis.com", "/v1beta/accountSummaries") =>
                Json(HttpStatusCode.OK, new { accountSummaries = new[] { new { displayName = "Acme", propertySummaries = new[] { new { property = "properties/777", displayName = "Acme web" } } } } }),
            ("analyticsadmin.googleapis.com", "/v1beta/properties/777") => Json(Ga4Read, new { name = "properties/777" }),
            ("bigquery.googleapis.com", "/bigquery/v2/projects") =>
                Json(HttpStatusCode.OK, new { projects = new[] { new { id = "acme-data", friendlyName = "Acme data" } } }),
            ("bigquery.googleapis.com", "/bigquery/v2/projects/acme-data/datasets") => Json(BigQueryRead, new { datasets = Array.Empty<object>() }),
            ("bigquery.googleapis.com", "/bigquery/v2/projects/acme-data/jobs") => Json(BigQueryJobs, new { }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage AdsCustomer(string path)
    {
        var id = path.Split('/')[3];
        object? customer = id switch
        {
            "1111111111" => new { id, descriptiveName = "Acme Shoes India", manager = false },
            "2222222222" => new { id, descriptiveName = "Acme Agency", manager = true },
            "3333333333" => new { id, descriptiveName = "Acme Outlet", manager = false },
            _ => null,
        };
        return customer is null
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Json(HttpStatusCode.OK, new { results = new[] { new { customer } } });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };

    private static HttpResponseMessage Error(HttpStatusCode status, string error) => Json(status, new { error });

    private static string Challenge(string verifier) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Encode(object value) => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(value));

    private sealed record PendingCode(IssuedCode Issued, string Challenge, string RedirectUri, string Scopes);
}
```

In `core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs`:
- Add this property below `public FakeGoogle Google { get; } = new();`:

  ```csharp
      public FakeGoogleApis GoogleApis { get; } = new();
  ```

- Inside `ConfigureTestServices`, add this line after `services.PostConfigureAll<OpenIdConnectOptions>(Google.Configure);`:

  ```csharp
              services.AddHttpClient("kairo-google").ConfigurePrimaryHttpMessageHandler(() => GoogleApis);
  ```

- Replace `builder.UseSetting("Authentication:Google:ClientSecret", "kairo-test-secret");` with:

  ```csharp
          builder.UseSetting("Authentication:Google:ClientSecret", FakeGoogleApis.ClientSecret);
  ```

Create `core/tests/Kairo.IntegrationTests/Api/Connections.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Api;

public sealed record Callback(HttpResponseMessage Response, IssuedCode Issued, string Url)
{
    public string Location => Response.Headers.Location!.OriginalString;
}

internal static class Connections
{
    public static readonly Dictionary<string, string> StoredProducts = new()
    {
        ["google-ads"] = "GoogleAds",
        ["youtube"] = "YouTube",
        ["merchant-center"] = "MerchantCenter",
        ["ga4"] = "Ga4",
        ["bigquery"] = "BigQuery",
    };

    public static async Task<Uri> StartAsync(HttpClient admin, Guid brandId, string product)
    {
        var response = await admin.GetAsync($"/api/brands/{brandId}/connections/{product}/connect");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    public static async Task<Callback> CompleteAsync(
        KairoApiFactory api,
        HttpClient browser,
        Uri authorize,
        string? subject = null,
        IEnumerable<string>? scopes = null)
    {
        var query = QueryHelpers.ParseQuery(authorize.Query);
        var issued = api.GoogleApis.IssueCode(
            query["code_challenge"]!,
            query["redirect_uri"]!,
            scopes ?? query["scope"].ToString().Split(' '),
            subject ?? $"google-{Guid.NewGuid():N}");
        var url = $"/api/connections/callback/google?code={issued.Code}&state={Uri.EscapeDataString(query["state"]!)}";
        return new Callback(await browser.GetAsync(url), issued, url);
    }

    public static async Task<JsonElement> GetAsync(HttpClient browser, Guid brandId, string product)
    {
        var list = await browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections");
        return list.EnumerateArray().Single(connection => connection.GetProperty("product").GetString() == product);
    }

    public static async Task<byte[]?> StoredTokenAsync(string connectionString, Guid brandId, string product)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT RefreshToken FROM brands.Connections WHERE BrandId = @brand AND Product = @product;
            """;
        command.Parameters.AddWithValue("@brand", brandId);
        command.Parameters.AddWithValue("@product", StoredProducts[product]);
        return await command.ExecuteScalarAsync() as byte[];
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/ConnectionConsentTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionConsentTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_lists_every_product_as_not_connected()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var list = await admin.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections", Token);

        Assert.Equal(
            ["google-ads", "youtube", "merchant-center", "ga4", "bigquery"],
            list.EnumerateArray().Select(connection => connection.GetProperty("product").GetString()!));
        Assert.All(list.EnumerateArray(), connection => Assert.Equal("notConnected", connection.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Connecting_asks_Google_for_offline_access_to_only_that_products_scopes()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var authorize = await Connections.StartAsync(admin.Browser, brandId, "youtube");

        var query = QueryHelpers.ParseQuery(authorize.Query);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", authorize.GetLeftPart(UriPartial.Path));
        Assert.Equal(FakeGoogle.ClientId, query["client_id"]);
        Assert.Equal($"{KairoApiFactory.PublicOrigin}/api/connections/callback/google", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.upload", query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent select_account", query["prompt"]);
        Assert.Equal("false", query["include_granted_scopes"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var response = await admin.Browser.GetAsync($"/api/brands/{brandId}/connections/tiktok/connect", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Giving_consent_keeps_the_connection_while_the_admin_chooses_an_account()
    {
        var admin = await Workspaces.SignInAsync(_api, "Neha Rao");
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);

        var callback = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        Assert.Equal(HttpStatusCode.Redirect, callback.Response.StatusCode);
        Assert.Equal($"/{handle}/settings/connections?choose=google-ads", callback.Location);
        var connection = await Connections.GetAsync(admin.Browser, brandId, "google-ads");
        Assert.Equal("choosingAccount", connection.GetProperty("state").GetString());
        Assert.Equal(admin.UserId, connection.GetProperty("connectedBy").GetGuid());
        Assert.Equal("Neha Rao", connection.GetProperty("connectedByName").GetString());
        Assert.Contains("connection.connected", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task The_refresh_token_is_never_stored_in_plain_text()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var callback = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        var stored = await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads");
        Assert.NotNull(stored);
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(callback.Issued.RefreshToken)));
    }

    [Fact]
    public async Task Consent_without_every_scope_is_refused_and_given_back()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);

        var callback = await Connections.CompleteAsync(
            _api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "youtube"),
            scopes: ["https://www.googleapis.com/auth/youtube.readonly"]);

        Assert.Equal($"/{handle}/settings/connections?error=scopes_missing", callback.Location);
        Assert.Contains(callback.Issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "youtube")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Cancelling_at_Google_returns_to_the_settings_page()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        var state = QueryHelpers.ParseQuery((await Connections.StartAsync(admin.Browser, brandId, "ga4")).Query)["state"].ToString();

        var response = await admin.Browser.GetAsync($"/api/connections/callback/google?error=access_denied&state={Uri.EscapeDataString(state)}", Token);

        Assert.Equal($"/{handle}/settings/connections?error=cancelled", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_tampered_state_changes_nothing()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var authorize = await Connections.StartAsync(admin.Browser, brandId, "google-ads");
        var query = QueryHelpers.ParseQuery(authorize.Query);
        var state = query["state"].ToString();
        var tampered = state[..^4] + (state.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await admin.Browser.GetAsync($"/api/connections/callback/google?code=x&state={Uri.EscapeDataString(tampered)}", Token);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Consent_that_comes_back_after_ten_minutes_has_expired()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        var authorize = await Connections.StartAsync(admin.Browser, brandId, "google-ads");
        _api.Clock.Advance(TimeSpan.FromMinutes(11));

        var callback = await Connections.CompleteAsync(_api, admin.Browser, authorize);

        Assert.Equal($"/{handle}/settings/connections?error=expired", callback.Location);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_replayed_callback_is_refused()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var handle = Workspaces.NewHandle();
        var brandId = await Workspaces.CreateAsync(admin.Browser, handle);
        var first = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        var replay = await admin.Browser.GetAsync(first.Url, Token);

        Assert.Equal($"/{handle}/settings/connections?error=failed", replay.Headers.Location!.OriginalString);
        Assert.Single(await Workspaces.AuditActionsAsync(admin.Browser, brandId), action => action == "connection.connected");
    }

    [Fact]
    public async Task A_callback_in_someone_elses_session_changes_nothing()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var authorize = await Connections.StartAsync(admin.Browser, brandId, "google-ads");
        var stranger = await Workspaces.SignInAsync(_api, "Stranger");

        var callback = await Connections.CompleteAsync(_api, stranger.Browser, authorize);

        Assert.Equal("/", callback.Location);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
        Assert.DoesNotContain("authorization_code", _api.GoogleApis.Requests);
    }

    [Fact]
    public async Task Someone_who_stopped_being_an_admin_cannot_finish_connecting()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        var authorize = await Connections.StartAsync(other.Browser, brandId, "google-ads");
        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        var callback = await Connections.CompleteAsync(_api, other.Browser, authorize);

        Assert.Equal("/", callback.Location);
        Assert.Equal("notConnected", (await Connections.GetAsync(owner.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_connection_left_unfinished_for_an_hour_shows_as_not_connected()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"));

        _api.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal("notConnected", (await Connections.GetAsync(admin.Browser, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Reconnecting_revokes_the_old_token_only_when_its_Google_account_is_done_with_Kairo()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var first = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"), subject: "account-a");

        var second = await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"), subject: "account-b");
        await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, "google-ads"), subject: "account-b");

        Assert.Contains(first.Issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.DoesNotContain(second.Issued.RefreshToken, _api.GoogleApis.Revoked);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionConsentTests"
```

Expected: every test fails. The connection routes return 404, so `StartAsync` sees 404 instead of 302, and the list test can't read the response.

- [ ] **Step 4: Add the products and the Google plumbing**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Products.cs`:

```csharp
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections;

internal sealed record ProductInfo(ConnectedProduct Product, string Slug, string Name, string[] Scopes);

internal static class Products
{
    public static readonly ProductInfo[] All =
    [
        new(ConnectedProduct.GoogleAds, "google-ads", "Google Ads", ["https://www.googleapis.com/auth/adwords"]),
        new(ConnectedProduct.YouTube, "youtube", "YouTube",
            ["https://www.googleapis.com/auth/youtube.readonly", "https://www.googleapis.com/auth/youtube.upload"]),
        new(ConnectedProduct.MerchantCenter, "merchant-center", "Merchant Center", ["https://www.googleapis.com/auth/content"]),
        new(ConnectedProduct.Ga4, "ga4", "Google Analytics", ["https://www.googleapis.com/auth/analytics.readonly"]),
        new(ConnectedProduct.BigQuery, "bigquery", "BigQuery", ["https://www.googleapis.com/auth/bigquery"]),
    ];

    public static ProductInfo? FindBySlug(string? slug) => All.FirstOrDefault(product => product.Slug == slug);

    public static ProductInfo Get(ConnectedProduct product) => All.Single(info => info.Product == product);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/GoogleHttp.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.Modules.Brands.Connections;

internal static class GoogleHttp
{
    public const string ClientName = "kairo-google";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly JsonSerializerOptions SnakeCase = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static async Task<HttpResponseMessage> CallAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return await client.SendAsync(request, cancellationToken);
    }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConsentStates.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.Modules.Brands.Connections;

internal sealed record ConsentState(
    Guid BrandId,
    ConnectedProduct Product,
    Guid UserId,
    string Verifier,
    string Nonce,
    DateTimeOffset ExpiresAt)
{
    public string Challenge() => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));
}

internal sealed class ConsentStates(IDataProtectionProvider dataProtection, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Kairo.Connections.Consent");

    public ConsentState Create(Guid brandId, ConnectedProduct product, Guid userId) =>
        new(brandId, product, userId, RandomText(32), RandomText(16), time.GetUtcNow() + Lifetime);

    public string Protect(ConsentState state) => _protector.Protect(JsonSerializer.Serialize(state));

    public ConsentState? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConsentState>(_protector.Unprotect(value));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static string RandomText(int bytes) => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/GoogleOAuth.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Brands.Connections;

internal sealed record Grant(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken, string[] Scopes, string? Subject);

internal sealed class GoogleOAuth(IHttpClientFactory http, IConfiguration configuration, TimeProvider time, ILogger<GoogleOAuth> logger)
{
    public const string CallbackPath = "/api/connections/callback/google";

    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private string ClientId => configuration["Authentication:Google:ClientId"]
        ?? throw new InvalidOperationException("Authentication:Google:ClientId is not set.");

    private string ClientSecret => configuration["Authentication:Google:ClientSecret"]
        ?? throw new InvalidOperationException("Authentication:Google:ClientSecret is not set.");

    public string RedirectUri(HttpRequest request)
    {
        var origin = (configuration["Kairo:PublicOrigin"] ?? "").TrimEnd('/');
        return (origin.Length > 0 ? origin : $"{request.Scheme}://{request.Host}") + CallbackPath;
    }

    public string AuthorizeUrl(ProductInfo product, ConsentState state, string protectedState, string redirectUri) =>
        QueryHelpers.AddQueryString(AuthorizeEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', product.Scopes.Prepend("openid")),
            ["access_type"] = "offline",
            ["prompt"] = "consent select_account",
            ["include_granted_scopes"] = "false",
            ["code_challenge"] = state.Challenge(),
            ["code_challenge_method"] = "S256",
            ["state"] = protectedState,
        });

    public async Task<Grant?> ExchangeAsync(string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google refused an authorization code with status {Status}.", (int)response.StatusCode);
            return null;
        }

        return await ReadGrantAsync(response, cancellationToken);
    }

    public async Task<Grant?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
        }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest
            && (await response.Content.ReadFromJsonAsync<TokenError>(GoogleHttp.SnakeCase, cancellationToken))?.Error == "invalid_grant")
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadGrantAsync(response, cancellationToken);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await PostAsync(RevokeEndpoint, new Dictionary<string, string> { ["token"] = token }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google refused to revoke a token with status {Status}.", (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not revoke a Google token.");
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        return await http.CreateClient(GoogleHttp.ClientName).PostAsync(url, content, cancellationToken);
    }

    private async Task<Grant> ReadGrantAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(GoogleHttp.SnakeCase, cancellationToken)
            ?? throw new InvalidOperationException("Google returned an empty token response.");
        return new Grant(
            token.AccessToken,
            time.GetUtcNow().AddSeconds(token.ExpiresIn),
            token.RefreshToken,
            (token.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            SubjectOf(token.IdToken));
    }

    private static string? SubjectOf(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
        return payload.RootElement.TryGetProperty("sub", out var subject) ? subject.GetString() : null;
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresIn, string? RefreshToken, string? Scope, string? IdToken);

    private sealed record TokenError(string? Error);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/TokenCipher.cs`:

```csharp
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Kairo.Modules.Brands.Connections;

internal sealed record SealedToken(byte[] Ciphertext, string KeyVersion);

internal interface ITokenCipher
{
    Task<SealedToken> SealAsync(string token, string context, CancellationToken cancellationToken);

    Task<string> OpenAsync(byte[] ciphertext, string context, CancellationToken cancellationToken);
}

internal sealed class DataProtectionTokenCipher(IDataProtectionProvider dataProtection) : ITokenCipher
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Kairo.Connections.Tokens");

    public Task<SealedToken> SealAsync(string token, string context, CancellationToken cancellationToken) =>
        Task.FromResult(new SealedToken(_protector.CreateProtector(context).Protect(Encoding.UTF8.GetBytes(token)), "data-protection"));

    public Task<string> OpenAsync(byte[] ciphertext, string context, CancellationToken cancellationToken) =>
        Task.FromResult(Encoding.UTF8.GetString(_protector.CreateProtector(context).Unprotect(ciphertext)));
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs`. Task 3 replaces this file with a larger version.

```csharp
using System.Security.Cryptography;

namespace Kairo.Modules.Brands.Connections;

internal sealed class ConnectionTokens(ITokenCipher cipher)
{
    public async Task SealAsync(Connection connection, string refreshToken, CancellationToken cancellationToken)
    {
        var sealedToken = await cipher.SealAsync(refreshToken, Context(connection), cancellationToken);
        connection.RefreshToken = sealedToken.Ciphertext;
        connection.KeyVersion = sealedToken.KeyVersion;
    }

    public async Task<string?> OpenAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            return null;
        }

        try
        {
            return await cipher.OpenAsync(connection.RefreshToken, Context(connection), cancellationToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string Context(Connection connection) => $"{connection.BrandId}:{connection.Id}";
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/GoogleRevocations.cs`:

```csharp
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal sealed class GoogleRevocations(BrandsDbContext db, KairoDatabase database, GoogleOAuth oauth)
{
    public async Task RevokeIfUnusedAsync(string subject, string refreshToken, CancellationToken cancellationToken)
    {
        bool inUse;
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            inUse = await db.Connections.AnyAsync(
                connection => connection.GoogleSubject == subject && connection.RefreshToken != null,
                cancellationToken);
        }

        if (!inUse)
        {
            await oauth.RevokeAsync(refreshToken, cancellationToken);
        }
    }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionLifecycle.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionLifecycle
{
    public static readonly TimeSpan ChoosingTimeout = TimeSpan.FromHours(1);

    public static ConnectionState StateOf(Connection? connection, DateTimeOffset now) => connection switch
    {
        null => ConnectionState.NotConnected,
        { State: ConnectionState.ChoosingAccount } choosing when choosing.ConnectedAt + ChoosingTimeout < now => ConnectionState.NotConnected,
        { } existing => existing.State,
    };

    public static Task<Connection?> FindAsync(BrandsDbContext db, ConnectedProduct product, CancellationToken cancellationToken) =>
        db.Connections.SingleOrDefaultAsync(connection => connection.Product == product, cancellationToken);

    public static object Snapshot(Connection connection) => new
    {
        Product = Products.Get(connection.Product).Slug,
        State = connection.State.ToString(),
        connection.ExternalId,
        connection.DisplayName,
        connection.ReconnectReason,
    };
}
```

- [ ] **Step 5: Add the endpoints**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionEndpoints.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionEndpoints
{
    public static async Task<Ok<ConnectionView[]>> ListAsync(BrandsDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var rows = await db.Connections.ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        return TypedResults.Ok(Products.All
            .Select(product => ConnectionView.Of(product, rows.SingleOrDefault(row => row.Product == product.Product), now))
            .ToArray());
    }
}

internal sealed record ConnectionView(
    string Product,
    string Name,
    ConnectionState State,
    string? ExternalId,
    string? DisplayName,
    Guid? ConnectedBy,
    string? ConnectedByName,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? CheckedAt,
    string? ReconnectReason)
{
    public static ConnectionView Of(ProductInfo product, Connection? connection, DateTimeOffset now)
    {
        var state = ConnectionLifecycle.StateOf(connection, now);
        return connection is null || state == ConnectionState.NotConnected
            ? new(product.Slug, product.Name, ConnectionState.NotConnected, null, null, null, null, null, null, null)
            : new(product.Slug, product.Name, state, connection.ExternalId, connection.DisplayName, connection.ConnectedBy,
                connection.ConnectedByName, connection.ConnectedAt, connection.CheckedAt, connection.ReconnectReason);
    }
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConsentEndpoints.cs`:

```csharp
using System.Security.Claims;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;
using Microsoft.Data.SqlClient;

namespace Kairo.Modules.Brands.Connections;

internal static class ConsentEndpoints
{
    public static Results<RedirectHttpResult, NotFound> Connect(
        string product,
        CurrentMember member,
        ConsentStates states,
        GoogleOAuth oauth,
        HttpRequest request)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        var consent = states.Create(member.BrandId, info.Product, member.UserId);
        return TypedResults.Redirect(oauth.AuthorizeUrl(info, consent, states.Protect(consent), oauth.RedirectUri(request)));
    }

    public static async Task<RedirectHttpResult> CallbackAsync(
        string? code,
        string? state,
        string? error,
        ClaimsPrincipal principal,
        ConsentStates states,
        GoogleOAuth oauth,
        IBrandAccess access,
        KairoDatabase database,
        BrandsDbContext db,
        ConnectionTokens tokens,
        GoogleRevocations revocations,
        IAuditLog audit,
        TimeProvider time,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (principal.FindUserId() is not { } userId)
        {
            return TypedResults.Redirect("/sign-in");
        }

        if (states.Unprotect(state) is not { } consent
            || consent.UserId != userId
            || await access.FindRolesAsync(consent.BrandId, userId, cancellationToken) is not { } roles
            || !roles.HasFlag(BrandRoles.Admin))
        {
            return TypedResults.Redirect("/");
        }

        await database.UseBrandAsync(consent.BrandId, cancellationToken);
        var handle = await db.Brands.Select(brand => brand.Handle).SingleAsync(cancellationToken);
        var settings = $"/{handle}/settings/connections";
        var now = time.GetUtcNow();
        if (consent.ExpiresAt <= now)
        {
            return Failed(settings, "expired");
        }

        if (!await UseNonceAsync(db, consent, now, cancellationToken))
        {
            return Failed(settings, "failed");
        }

        if (error is not null)
        {
            return Failed(settings, error == "access_denied" ? "cancelled" : "failed");
        }

        var product = Products.Get(consent.Product);
        var grant = string.IsNullOrEmpty(code)
            ? null
            : await oauth.ExchangeAsync(code, consent.Verifier, oauth.RedirectUri(request), cancellationToken);
        if (grant is not { RefreshToken: { } refreshToken, Subject: { } subject })
        {
            return Failed(settings, "failed");
        }

        if (product.Scopes.Except(grant.Scopes).Any())
        {
            await revocations.RevokeIfUnusedAsync(subject, refreshToken, cancellationToken);
            return Failed(settings, "scopes_missing");
        }

        (string Subject, string Token)? replaced = null;
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var connection = await ConnectionLifecycle.FindAsync(db, product.Product, cancellationToken);
            var before = connection is null ? null : ConnectionLifecycle.Snapshot(connection);
            if (connection is not null && await tokens.OpenAsync(connection, cancellationToken) is { } oldToken)
            {
                replaced = (connection.GoogleSubject, oldToken);
            }

            if (connection is null)
            {
                connection = new Connection { BrandId = consent.BrandId, Product = product.Product };
                db.Connections.Add(connection);
            }

            connection.State = ConnectionState.ChoosingAccount;
            connection.ExternalId = null;
            connection.DisplayName = null;
            connection.Scopes = string.Join(' ', product.Scopes);
            connection.GoogleSubject = subject;
            connection.ConnectedBy = userId;
            connection.ConnectedByName = principal.FindFirstValue(KairoClaims.Name) ?? "";
            connection.ConnectedAt = now;
            connection.CheckedAt = null;
            connection.ReconnectReason = null;
            await tokens.SealAsync(connection, refreshToken, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(
                new AuditRecord("connection.connected", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
                cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }

        if (replaced is { } previous)
        {
            await revocations.RevokeIfUnusedAsync(previous.Subject, previous.Token, cancellationToken);
        }

        return TypedResults.Redirect($"{settings}?choose={product.Slug}");
    }

    private static async Task<bool> UseNonceAsync(BrandsDbContext db, ConsentState consent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.ConsentNonces.Where(nonce => nonce.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        var used = new ConsentNonce { Nonce = consent.Nonce, ExpiresAt = consent.ExpiresAt };
        db.ConsentNonces.Add(used);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(used).State = EntityState.Detached;
            return false;
        }
    }

    private static RedirectHttpResult Failed(string settings, string code) => TypedResults.Redirect($"{settings}?error={code}");
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`:

```csharp
namespace Kairo.Modules.Brands.Connections;

internal static class ConnectionSetup
{
    public static void AddConnections(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(GoogleHttp.ClientName);
        services.AddSingleton<ConsentStates>();
        services.AddSingleton<GoogleOAuth>();
        services.AddSingleton<ITokenCipher, DataProtectionTokenCipher>();
        services.AddScoped<ConnectionTokens>();
        services.AddScoped<GoogleRevocations>();
    }

    public static void MapConnections(this IEndpointRouteBuilder api)
    {
        api.MapGet("/connections/callback/google", ConsentEndpoints.CallbackAsync);

        var connections = api.MapBrandApi().MapGroup("/connections");
        connections.MapGet("", ConnectionEndpoints.ListAsync);

        var product = connections.MapGroup("/{product}").RequireBrandRole(BrandRoles.Admin);
        product.MapGet("/connect", ConsentEndpoints.Connect);
    }
}
```

Modify `core/src/Modules/Kairo.Modules.Brands/BrandsModule.cs`:
- Add `using Kairo.Modules.Brands.Connections;` to the usings.
- In `AddServices`, add `services.AddConnections(configuration);` after `services.AddScoped<IBrandDirectory, BrandDirectory>();`.
- In `MapApi`, add `api.MapConnections();` after `api.MapWorkspaces();`.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet build core/Kairo.slnx
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionConsentTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.SignInTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: all pass, and the build has 0 warnings. `SignInTests` shows that the shared client secret setting still works.

- [ ] **Step 7: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Brands/Connections \
  core/src/Modules/Kairo.Modules.Brands/BrandsModule.cs \
  core/tests/Kairo.IntegrationTests/Api/FakeGoogleApis.cs \
  core/tests/Kairo.IntegrationTests/Api/Connections.cs \
  core/tests/Kairo.IntegrationTests/Api/KairoApiFactory.cs \
  core/tests/Kairo.IntegrationTests/Api/ConnectionConsentTests.cs
git commit -m "Let an admin give Google consent for a product, keeping the refresh token encrypted"
```

---

### Task 3: Choosing an account

**Files:**
- Create in `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/`:
  - `ProductAccounts.cs`
  - `GoogleAdsAccounts.cs`
  - `YouTubeAccounts.cs`
  - `MerchantCenterAccounts.cs`
  - `Ga4Accounts.cs`
  - `BigQueryAccounts.cs`
- Create in `core/src/Modules/Kairo.Modules.Brands/Connections/`:
  - `AccessTokenCache.cs`
  - `AccountEndpoints.cs`
- Replace: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/Connections.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/ConnectionAccountTests.cs`

**Interfaces:**
- Consumes: from Task 2, `GoogleHttp`, `GoogleOAuth.RefreshAsync`, `ConnectionLifecycle`, `ConnectionView`, `Products` and `FakeGoogleApis`.
- Produces:
  - `ProductAccount(string Id, string Name)`
  - `IProductAccounts`, with `Product`, `ListAsync(accessToken, ct)` and `TestAsync(accessToken, externalId, ct)`. `TestAsync` returns null when the test call passes, or a plain-language problem.
  - `ProductCatalog.For(ConnectedProduct)`
  - `AccessToken(string Value, DateTimeOffset ExpiresAt)`, and `AccessTokenCache` with `Find`, `Store` and `Forget`
  - `ConnectionTokens.GetAsync(Connection, bool fresh, ct)`, which returns `AccessToken?`, and `ConnectionTokens.LoseAsync(Connection, string reason, ct)`
  - `ReconnectReasons`: `TokenRejected`, `TokenUnreadable` and `AdminLeft`
  - `AccountEndpoints.ConnectFirst`, `AccessLost` and `GoogleDidNotAnswer`
  - Endpoints:
    - `GET /api/brands/{brandId}/connections/{product}/accounts`
    - `POST /api/brands/{brandId}/connections/{product}/account`
  - Test helpers: `Connections.DefaultAccount(product)` and `Connections.ConnectAsync(api, admin, brandId, product, subject?)`, which returns `IssuedCode`

- [ ] **Step 1: Add the connect helper**

Append these members inside the `Connections` class in `core/tests/Kairo.IntegrationTests/Api/Connections.cs`:

```csharp
    public static string DefaultAccount(string product) => product switch
    {
        "google-ads" => "1111111111",
        "youtube" => "UC-acme",
        "merchant-center" => "555",
        "ga4" => "777",
        "bigquery" => "acme-data",
        _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
    };

    public static async Task<IssuedCode> ConnectAsync(KairoApiFactory api, HttpClient admin, Guid brandId, string product, string? subject = null)
    {
        var callback = await CompleteAsync(api, admin, await StartAsync(admin, brandId, product), subject);
        Assert.Contains($"choose={product}", callback.Location);
        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/{product}/account", new { externalId = DefaultAccount(product) });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        return callback.Issued;
    }
```

- [ ] **Step 2: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/ConnectionAccountTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionAccountTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> EveryProduct => new()
    {
        { "google-ads", "1111111111", "Acme Shoes India" },
        { "youtube", "UC-acme", "Channel UC-acme" },
        { "merchant-center", "555", "Acme Store" },
        { "ga4", "777", "Acme web (Acme)" },
        { "bigquery", "acme-data", "Acme data" },
    };

    [Theory]
    [MemberData(nameof(EveryProduct))]
    public async Task An_admin_connects_each_product_by_choosing_an_account(string product, string accountId, string accountName)
    {
        var (admin, brandId) = await ConsentedAsync(product);

        var accounts = await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/{product}/accounts", Token);
        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/{product}/account", new { externalId = accountId }, Token);

        Assert.Contains(accounts.EnumerateArray(), account =>
            account.GetProperty("id").GetString() == accountId && account.GetProperty("name").GetString() == accountName);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var connection = await Connections.GetAsync(admin, brandId, product);
        Assert.Equal("connected", connection.GetProperty("state").GetString());
        Assert.Equal(accountId, connection.GetProperty("externalId").GetString());
        Assert.Equal(accountName, connection.GetProperty("displayName").GetString());
        Assert.Contains("connection.account_chosen", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task Google_Ads_lists_only_accounts_that_can_run_campaigns()
    {
        var (admin, brandId) = await ConsentedAsync("google-ads");

        var accounts = await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/google-ads/accounts", Token);

        Assert.Equal(["1111111111", "3333333333"], accounts.EnumerateArray().Select(account => account.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task An_account_the_Google_login_cannot_reach_is_refused()
    {
        var (admin, brandId) = await ConsentedAsync("google-ads");

        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = "2222222222" }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, chosen.StatusCode);
        Assert.Equal("choosingAccount", (await Connections.GetAsync(admin, brandId, "google-ads")).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("google-ads", "can't make changes to it")]
    [InlineData("merchant-center", "can't make changes to it")]
    [InlineData("ga4", "can't read that Google Analytics property")]
    [InlineData("bigquery-read", "can't read datasets")]
    [InlineData("bigquery-jobs", "can't run queries")]
    public async Task A_failing_test_call_keeps_the_connection_unfinished_and_says_why(string failure, string reason)
    {
        var product = failure.StartsWith("bigquery", StringComparison.Ordinal) ? "bigquery" : failure;
        switch (failure)
        {
            case "google-ads":
                _api.GoogleApis.AdsWrite = HttpStatusCode.Forbidden;
                break;
            case "merchant-center":
                _api.GoogleApis.MerchantAccessRights = ["READ_ONLY"];
                break;
            case "ga4":
                _api.GoogleApis.Ga4Read = HttpStatusCode.Forbidden;
                break;
            case "bigquery-read":
                _api.GoogleApis.BigQueryRead = HttpStatusCode.Forbidden;
                break;
            case "bigquery-jobs":
                _api.GoogleApis.BigQueryJobs = HttpStatusCode.Forbidden;
                break;
        }

        var (admin, brandId) = await ConsentedAsync(product);

        var chosen = await admin.PostAsJsonAsync(
            $"/api/brands/{brandId}/connections/{product}/account", new { externalId = Connections.DefaultAccount(product) }, Token);

        Assert.Equal(HttpStatusCode.Conflict, chosen.StatusCode);
        Assert.Contains(reason, (await chosen.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString());
        Assert.Equal("choosingAccount", (await Connections.GetAsync(admin, brandId, product)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_Google_Ads_validation_error_still_proves_write_access()
    {
        _api.GoogleApis.AdsWrite = HttpStatusCode.BadRequest;
        var (admin, brandId) = await ConsentedAsync("google-ads");

        var chosen = await admin.PostAsJsonAsync($"/api/brands/{brandId}/connections/google-ads/account", new { externalId = "1111111111" }, Token);

        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
    }

    [Fact]
    public async Task Listing_accounts_again_reuses_the_access_token()
    {
        var (admin, brandId) = await ConsentedAsync("youtube");

        await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/youtube/accounts", Token);
        await admin.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/connections/youtube/accounts", Token);

        Assert.Equal(1, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_product_must_be_connected_before_choosing()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);

        var accounts = await admin.Browser.GetAsync($"/api/brands/{brandId}/connections/google-ads/accounts", Token);

        Assert.Equal(HttpStatusCode.Conflict, accounts.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(HttpClient Admin, Guid BrandId)> ConsentedAsync(string product)
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        await Connections.CompleteAsync(_api, admin.Browser, await Connections.StartAsync(admin.Browser, brandId, product));
        return (admin.Browser, brandId);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionAccountTests"
```

Expected: every test fails, because the accounts and account routes return 404.

- [ ] **Step 4: Add the access-token cache and token handling**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/AccessTokenCache.cs`:

```csharp
using System.Collections.Concurrent;

namespace Kairo.Modules.Brands.Connections;

internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

internal sealed class AccessTokenCache(TimeProvider time)
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, CachedToken> _tokens = new();

    public AccessToken? Find(Connection connection) =>
        _tokens.TryGetValue(connection.Id, out var cached)
        && cached.ConnectedAt == connection.ConnectedAt
        && cached.Token.ExpiresAt - Margin > time.GetUtcNow()
            ? cached.Token
            : null;

    public void Store(Connection connection, AccessToken token) => _tokens[connection.Id] = new CachedToken(connection.ConnectedAt, token);

    public void Forget(Guid connectionId) => _tokens.TryRemove(connectionId, out _);

    private sealed record CachedToken(DateTimeOffset ConnectedAt, AccessToken Token);
}
```

Replace `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionTokens.cs` with:

```csharp
using System.Security.Cryptography;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal static class ReconnectReasons
{
    public const string TokenRejected = "token_rejected";
    public const string TokenUnreadable = "token_unreadable";
    public const string AdminLeft = "admin_left";
}

internal sealed class ConnectionTokens(
    BrandsDbContext db,
    ITokenCipher cipher,
    GoogleOAuth oauth,
    AccessTokenCache cache,
    IAuditLog audit)
{
    public async Task SealAsync(Connection connection, string refreshToken, CancellationToken cancellationToken)
    {
        var sealedToken = await cipher.SealAsync(refreshToken, Context(connection), cancellationToken);
        connection.RefreshToken = sealedToken.Ciphertext;
        connection.KeyVersion = sealedToken.KeyVersion;
    }

    public async Task<string?> OpenAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (connection.RefreshToken is null)
        {
            return null;
        }

        try
        {
            return await cipher.OpenAsync(connection.RefreshToken, Context(connection), cancellationToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task<AccessToken?> GetAsync(Connection connection, bool fresh, CancellationToken cancellationToken)
    {
        if (!fresh && cache.Find(connection) is { } cached)
        {
            return cached;
        }

        if (await OpenAsync(connection, cancellationToken) is not { } refreshToken)
        {
            await LoseAsync(connection, ReconnectReasons.TokenUnreadable, cancellationToken);
            return null;
        }

        if (await oauth.RefreshAsync(refreshToken, cancellationToken) is not { } grant)
        {
            await LoseAsync(connection, ReconnectReasons.TokenRejected, cancellationToken);
            return null;
        }

        var token = new AccessToken(grant.AccessToken, grant.ExpiresAt);
        cache.Store(connection, token);
        return token;
    }

    public async Task LoseAsync(Connection connection, string reason, CancellationToken cancellationToken)
    {
        var before = ConnectionLifecycle.Snapshot(connection);
        connection.State = ConnectionState.NeedsReconnect;
        connection.RefreshToken = null;
        connection.KeyVersion = null;
        connection.ReconnectReason = reason;
        cache.Forget(connection.Id);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("connection.needs_reconnect", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
            cancellationToken);
    }

    private static string Context(Connection connection) => $"{connection.BrandId}:{connection.Id}";
}
```

- [ ] **Step 5: Add the product clients**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/ProductAccounts.cs`:

```csharp
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed record ProductAccount(string Id, string Name);

internal interface IProductAccounts
{
    ConnectedProduct Product { get; }

    Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken);

    Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken);
}

internal sealed class ProductCatalog(IEnumerable<IProductAccounts> all)
{
    public IProductAccounts For(ConnectedProduct product) => all.Single(accounts => accounts.Product == product);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/GoogleAdsAccounts.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class GoogleAdsAccounts(IHttpClientFactory http, IConfiguration configuration) : IProductAccounts
{
    public const string CannotOpen = "This Google account can't open that Google Ads account.";
    public const string CannotChange =
        "This Google account can see the Google Ads account but can't make changes to it. Ask for Standard or Admin access in Google Ads.";

    public ConnectedProduct Product => ConnectedProduct.GoogleAds;

    private string BaseUrl => $"https://googleads.googleapis.com/{configuration["Kairo:Google:AdsApiVersion"] ?? "v25"}";

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        using var listed = await client.CallAsync(HttpMethod.Get, $"{BaseUrl}/customers:listAccessibleCustomers", accessToken, null, cancellationToken);
        listed.EnsureSuccessStatusCode();
        var names = (await listed.Content.ReadFromJsonAsync<AccessibleCustomers>(GoogleHttp.Json, cancellationToken))?.ResourceNames ?? [];

        var accounts = new List<ProductAccount>();
        foreach (var name in names.Take(50))
        {
            var id = name["customers/".Length..];
            if (await DescribeAsync(client, accessToken, id, cancellationToken) is { Manager: false } customer)
            {
                accounts.Add(new ProductAccount(id, customer.DescriptiveName ?? id));
            }
        }

        return accounts;
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        if (await DescribeAsync(client, accessToken, externalId, cancellationToken) is null)
        {
            return CannotOpen;
        }

        using var write = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/customers/{Uri.EscapeDataString(externalId)}/campaignBudgets:mutate",
            accessToken,
            new
            {
                operations = new[]
                {
                    new { create = new { name = $"Kairo access check {Guid.NewGuid():N}", amountMicros = "1000000", deliveryMethod = "STANDARD", explicitlyShared = false } },
                },
                validateOnly = true,
            },
            cancellationToken);
        return write.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? CannotChange : null;
    }

    private async Task<CustomerRow?> DescribeAsync(HttpClient client, string accessToken, string id, CancellationToken cancellationToken)
    {
        using var response = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/customers/{Uri.EscapeDataString(id)}/googleAds:search",
            accessToken,
            new { query = "SELECT customer.id, customer.descriptive_name, customer.manager FROM customer LIMIT 1" },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var page = await response.Content.ReadFromJsonAsync<SearchPage>(GoogleHttp.Json, cancellationToken);
        return page?.Results?.FirstOrDefault()?.Customer;
    }

    private sealed record AccessibleCustomers(string[]? ResourceNames);

    private sealed record SearchPage(SearchRow[]? Results);

    private sealed record SearchRow(CustomerRow? Customer);

    private sealed record CustomerRow(string? Id, string? DescriptiveName, bool Manager);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/YouTubeAccounts.cs`:

```csharp
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class YouTubeAccounts(IHttpClientFactory http) : IProductAccounts
{
    public const string NotManaged = "This Google account no longer manages that YouTube channel.";

    public ConnectedProduct Product => ConnectedProduct.YouTube;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true&maxResults=50",
            accessToken,
            null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ChannelPage>(GoogleHttp.Json, cancellationToken);
        return (page?.Items ?? []).Select(channel => new ProductAccount(channel.Id, channel.Snippet?.Title ?? channel.Id)).ToList();
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken) =>
        (await ListAsync(accessToken, cancellationToken)).Any(channel => channel.Id == externalId) ? null : NotManaged;

    private sealed record ChannelPage(Channel[]? Items);

    private sealed record Channel(string Id, ChannelSnippet? Snippet);

    private sealed record ChannelSnippet(string? Title);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/MerchantCenterAccounts.cs`:

```csharp
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class MerchantCenterAccounts(IHttpClientFactory http) : IProductAccounts
{
    public const string CannotOpen = "This Google account can't open that Merchant Center account.";
    public const string CannotChange =
        "This Google account can see the Merchant Center account but can't make changes to it. Ask for Standard or Admin access in Merchant Center.";

    private const string BaseUrl = "https://merchantapi.googleapis.com/accounts/v1/accounts";

    public ConnectedProduct Product => ConnectedProduct.MerchantCenter;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}?pageSize=250", accessToken, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<AccountPage>(GoogleHttp.Json, cancellationToken);
        return (page?.Accounts ?? [])
            .Where(account => account.AccountId is not null)
            .Select(account => new ProductAccount(account.AccountId!, account.AccountName ?? account.AccountId!))
            .ToList();
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}/{Uri.EscapeDataString(externalId)}/users/me", accessToken, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return CannotOpen;
        }

        var user = await response.Content.ReadFromJsonAsync<MerchantUser>(GoogleHttp.Json, cancellationToken);
        return (user?.AccessRights ?? []).Any(right => right is "STANDARD" or "ADMIN") ? null : CannotChange;
    }

    private sealed record AccountPage(MerchantAccount[]? Accounts);

    private sealed record MerchantAccount(string? AccountId, string? AccountName);

    private sealed record MerchantUser(string[]? AccessRights);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/Ga4Accounts.cs`:

```csharp
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class Ga4Accounts(IHttpClientFactory http) : IProductAccounts
{
    public const string CannotRead = "This Google account can't read that Google Analytics property.";

    private const string BaseUrl = "https://analyticsadmin.googleapis.com/v1beta";

    public ConnectedProduct Product => ConnectedProduct.Ga4;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}/accountSummaries?pageSize=200", accessToken, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<SummaryPage>(GoogleHttp.Json, cancellationToken);
        return (page?.AccountSummaries ?? [])
            .SelectMany(account => (account.PropertySummaries ?? []).Select(property => new ProductAccount(
                property.Property["properties/".Length..],
                $"{property.DisplayName} ({account.DisplayName})")))
            .ToList();
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}/properties/{Uri.EscapeDataString(externalId)}", accessToken, null, cancellationToken);
        return response.IsSuccessStatusCode ? null : CannotRead;
    }

    private sealed record SummaryPage(AccountSummary[]? AccountSummaries);

    private sealed record AccountSummary(string? DisplayName, PropertySummary[]? PropertySummaries);

    private sealed record PropertySummary(string Property, string? DisplayName);
}
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/Accounts/BigQueryAccounts.cs`:

```csharp
using System.Net.Http.Json;
using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections.Accounts;

internal sealed class BigQueryAccounts(IHttpClientFactory http) : IProductAccounts
{
    public const string CannotRead =
        "This Google account can't read datasets in that BigQuery project. Ask for the BigQuery Data Viewer role.";
    public const string CannotQuery =
        "This Google account can't run queries in that BigQuery project. Ask for the BigQuery Job User role.";

    private const string BaseUrl = "https://bigquery.googleapis.com/bigquery/v2/projects";

    public ConnectedProduct Product => ConnectedProduct.BigQuery;

    public async Task<IReadOnlyList<ProductAccount>> ListAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await http.CreateClient(GoogleHttp.ClientName).CallAsync(
            HttpMethod.Get, $"{BaseUrl}?maxResults=1000", accessToken, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<ProjectPage>(GoogleHttp.Json, cancellationToken);
        return (page?.Projects ?? []).Select(project => new ProductAccount(project.Id, project.FriendlyName ?? project.Id)).ToList();
    }

    public async Task<string?> TestAsync(string accessToken, string externalId, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(GoogleHttp.ClientName);
        var project = Uri.EscapeDataString(externalId);
        using var datasets = await client.CallAsync(HttpMethod.Get, $"{BaseUrl}/{project}/datasets?maxResults=1", accessToken, null, cancellationToken);
        if (!datasets.IsSuccessStatusCode)
        {
            return CannotRead;
        }

        using var job = await client.CallAsync(
            HttpMethod.Post,
            $"{BaseUrl}/{project}/jobs",
            accessToken,
            new { configuration = new { dryRun = true, query = new { query = "SELECT 1", useLegacySql = false } } },
            cancellationToken);
        return job.IsSuccessStatusCode ? null : CannotQuery;
    }

    private sealed record ProjectPage(Project[]? Projects);

    private sealed record Project(string Id, string? FriendlyName);
}
```

- [ ] **Step 6: Add the account endpoints**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/AccountEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Connections.Accounts;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal static class AccountEndpoints
{
    public const string ConnectFirst = "Connect this product first.";
    public const string AccessLost = "Google no longer accepts Kairo's access to this account. Connect it again.";
    public const string GoogleDidNotAnswer = "Google didn't answer. Try again in a minute.";

    public static async Task<Results<Ok<ProductAccount[]>, NotFound, ProblemHttpResult>> ListAsync(
        string product,
        BrandsDbContext db,
        ConnectionTokens tokens,
        ProductCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        if (await UsableAsync(db, info, time, cancellationToken) is not { } connection)
        {
            return Conflict(ConnectFirst);
        }

        if (await tokens.GetAsync(connection, fresh: false, cancellationToken) is not { } token)
        {
            return Conflict(AccessLost);
        }

        try
        {
            return TypedResults.Ok((await catalog.For(info.Product).ListAsync(token.Value, cancellationToken)).ToArray());
        }
        catch (HttpRequestException)
        {
            return Conflict(GoogleDidNotAnswer);
        }
    }

    public static async Task<Results<Ok<ConnectionView>, NotFound, ValidationProblem, ProblemHttpResult>> ChooseAsync(
        string product,
        ChooseAccountRequest request,
        BrandsDbContext db,
        ConnectionTokens tokens,
        ProductCatalog catalog,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (Products.FindBySlug(product) is not { } info)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ExternalId))
        {
            return Invalid("Choose an account.");
        }

        if (await UsableAsync(db, info, time, cancellationToken) is not { } connection)
        {
            return Conflict(ConnectFirst);
        }

        if (await tokens.GetAsync(connection, fresh: false, cancellationToken) is not { } token)
        {
            return Conflict(AccessLost);
        }

        var accounts = catalog.For(info.Product);
        ProductAccount? account;
        string? problem;
        try
        {
            account = (await accounts.ListAsync(token.Value, cancellationToken)).FirstOrDefault(candidate => candidate.Id == request.ExternalId);
            if (account is null)
            {
                return Invalid("Choose one of the accounts this Google account can reach.");
            }

            problem = await accounts.TestAsync(token.Value, account.Id, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Conflict(GoogleDidNotAnswer);
        }

        if (problem is not null)
        {
            return Conflict(problem);
        }

        var now = time.GetUtcNow();
        var before = ConnectionLifecycle.Snapshot(connection);
        connection.ExternalId = account.Id;
        connection.DisplayName = account.Name;
        connection.State = ConnectionState.Connected;
        connection.CheckedAt = now;
        connection.ReconnectReason = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("connection.account_chosen", "connection", connection.Id.ToString(), before, ConnectionLifecycle.Snapshot(connection)),
            cancellationToken);
        return TypedResults.Ok(ConnectionView.Of(info, connection, now));
    }

    private static async Task<Connection?> UsableAsync(BrandsDbContext db, ProductInfo info, TimeProvider time, CancellationToken cancellationToken)
    {
        var connection = await ConnectionLifecycle.FindAsync(db, info.Product, cancellationToken);
        return ConnectionLifecycle.StateOf(connection, time.GetUtcNow()) is ConnectionState.ChoosingAccount or ConnectionState.Connected
            ? connection
            : null;
    }

    private static ProblemHttpResult Conflict(string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static ValidationProblem Invalid(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["externalId"] = [message] });
}

internal sealed record ChooseAccountRequest(string? ExternalId);
```

In `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`:
- Add `using Kairo.Modules.Brands.Connections.Accounts;` at the top.
- In `AddConnections`, add these lines after `services.AddSingleton<ITokenCipher, DataProtectionTokenCipher>();`:

  ```csharp
          services.AddSingleton<AccessTokenCache>();
          services.AddSingleton<IProductAccounts, GoogleAdsAccounts>();
          services.AddSingleton<IProductAccounts, YouTubeAccounts>();
          services.AddSingleton<IProductAccounts, MerchantCenterAccounts>();
          services.AddSingleton<IProductAccounts, Ga4Accounts>();
          services.AddSingleton<IProductAccounts, BigQueryAccounts>();
          services.AddSingleton<ProductCatalog>();
  ```

- In `MapConnections`, add these lines after `product.MapGet("/connect", ConsentEndpoints.Connect);`:

  ```csharp
          product.MapGet("/accounts", AccountEndpoints.ListAsync);
          product.MapPost("/account", AccountEndpoints.ChooseAsync);
  ```

- [ ] **Step 7: Run the tests to see them pass**

```bash
dotnet build core/Kairo.slnx
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionAccountTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionConsentTests"
```

Expected: all pass, and the build has 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Brands/Connections \
  core/tests/Kairo.IntegrationTests/Api/Connections.cs \
  core/tests/Kairo.IntegrationTests/Api/ConnectionAccountTests.cs
git commit -m "List the accounts a Google login can reach, and confirm read and write access before connecting one"
```

---

### Task 4: Checking, disconnecting and losing access

**Files:**
- Modify: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`
- Modify: `core/tests/Kairo.IntegrationTests/Api/Connections.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/ConnectionLifecycleTests.cs`

**Interfaces:**
- Consumes:
  - from Task 3, `ConnectionTokens.GetAsync` and `LoseAsync`, `ProductCatalog`, `AccessTokenCache.Forget`, and the `AccountEndpoints` messages
  - from Task 2, `GoogleRevocations`
- Produces:
  - `CheckResult(ConnectionView Connection, string? Problem)`
  - Endpoints:
    - `POST /api/brands/{brandId}/connections/{product}/check`
    - `DELETE /api/brands/{brandId}/connections/{product}`
  - Test helper: `Connections.CopyStoredTokenAsync(connectionString, fromBrand, toBrand, product)`

- [ ] **Step 1: Add the copy helper**

Append this method inside the `Connections` class in `core/tests/Kairo.IntegrationTests/Api/Connections.cs`:

```csharp
    public static async Task CopyStoredTokenAsync(string connectionString, Guid fromBrand, Guid toBrand, string product)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            UPDATE brands.Connections
            SET RefreshToken = (SELECT RefreshToken FROM brands.Connections WHERE BrandId = @from AND Product = @product)
            WHERE BrandId = @to AND Product = @product;
            """;
        command.Parameters.AddWithValue("@from", fromBrand);
        command.Parameters.AddWithValue("@to", toBrand);
        command.Parameters.AddWithValue("@product", StoredProducts[product]);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
```

- [ ] **Step 2: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/ConnectionLifecycleTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionLifecycleTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checking_a_healthy_connection_refreshes_access_and_records_the_check()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        var refreshes = _api.GoogleApis.Refreshes;
        _api.Clock.Advance(TimeSpan.FromMinutes(5));

        var check = await CheckAsync(admin, brandId, "google-ads");

        Assert.Equal(JsonValueKind.Null, check.GetProperty("problem").ValueKind);
        Assert.Equal("connected", check.GetProperty("connection").GetProperty("state").GetString());
        Assert.Equal(_api.Clock.GetUtcNow(), check.GetProperty("connection").GetProperty("checkedAt").GetDateTimeOffset());
        Assert.Equal(refreshes + 1, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_revoked_Google_grant_moves_the_connection_to_needs_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);

        var check = await CheckAsync(admin, brandId, "google-ads");

        var connection = check.GetProperty("connection");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal("token_rejected", connection.GetProperty("reconnectReason").GetString());
        Assert.Contains("Connect it again", check.GetProperty("problem").GetString());
        Assert.Null(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads"));
        Assert.Contains("connection.needs_reconnect", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task A_failing_check_reports_the_problem_without_disconnecting()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "youtube");
        _api.GoogleApis.YouTubeChannels = [];

        var check = await CheckAsync(admin, brandId, "youtube");

        Assert.Contains("no longer manages", check.GetProperty("problem").GetString());
        Assert.Equal("connected", check.GetProperty("connection").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Disconnecting_erases_the_token_and_revokes_it_at_Google()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "merchant-center");

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/merchant-center", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "merchant-center")).GetProperty("state").GetString());
        Assert.Contains(issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Contains("connection.disconnected", await Workspaces.AuditActionsAsync(admin, brandId));
    }

    [Fact]
    public async Task Disconnecting_still_works_when_Google_will_not_revoke()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "ga4");
        _api.GoogleApis.RevocationFails = true;

        var response = await admin.DeleteAsync($"/api/brands/{brandId}/connections/ga4", Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("notConnected", (await Connections.GetAsync(admin, brandId, "ga4")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Disconnecting_one_product_keeps_another_on_the_same_Google_account()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var ads = await Connections.ConnectAsync(_api, admin, brandId, "google-ads", subject: "shared-account");
        var youtube = await Connections.ConnectAsync(_api, admin, brandId, "youtube", subject: "shared-account");

        await admin.DeleteAsync($"/api/brands/{brandId}/connections/youtube", Token);

        Assert.DoesNotContain(youtube.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal(JsonValueKind.Null, (await CheckAsync(admin, brandId, "google-ads")).GetProperty("problem").ValueKind);

        await admin.DeleteAsync($"/api/brands/{brandId}/connections/google-ads", Token);

        Assert.Contains(ads.RefreshToken, _api.GoogleApis.Revoked);
    }

    [Fact]
    public async Task A_token_copied_to_another_workspace_cannot_be_used()
    {
        var (admin, first) = await WorkspaceAsync();
        var second = await Workspaces.CreateAsync(admin);
        await Connections.ConnectAsync(_api, admin, first, "google-ads");
        await Connections.ConnectAsync(_api, admin, second, "google-ads");

        await Connections.CopyStoredTokenAsync(sqlServer.KairoConnectionString, first, second, "google-ads");
        var check = await CheckAsync(admin, second, "google-ads");

        Assert.Equal("needsReconnect", check.GetProperty("connection").GetProperty("state").GetString());
        Assert.Equal("token_unreadable", check.GetProperty("connection").GetProperty("reconnectReason").GetString());
    }

    [Fact]
    public async Task Checking_needs_a_finished_connection()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.PostAsync($"/api/brands/{brandId}/connections/google-ads/check", null, Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(HttpClient Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin.Browser, await Workspaces.CreateAsync(admin.Browser));
    }

    private static async Task<JsonElement> CheckAsync(HttpClient admin, Guid brandId, string product)
    {
        var response = await admin.PostAsync($"/api/brands/{brandId}/connections/{product}/check", null, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionLifecycleTests"
```

Expected: the tests fail, because the check and delete routes return 404 (or 405 for DELETE on a matched path).

- [ ] **Step 4: Add check and disconnect**

In `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionEndpoints.cs`:
- Add these usings at the top:

  ```csharp
  using Kairo.Contracts.Audit;
  using Kairo.Modules.Brands.Connections.Accounts;
  ```

- Add these methods to the `ConnectionEndpoints` class, after `ListAsync`:

  ```csharp
      public static async Task<Results<Ok<CheckResult>, NotFound, ProblemHttpResult>> CheckAsync(
          string product,
          BrandsDbContext db,
          ConnectionTokens tokens,
          ProductCatalog catalog,
          TimeProvider time,
          CancellationToken cancellationToken)
      {
          if (Products.FindBySlug(product) is not { } info)
          {
              return TypedResults.NotFound();
          }

          var now = time.GetUtcNow();
          var connection = await ConnectionLifecycle.FindAsync(db, info.Product, cancellationToken);
          if (connection is null || ConnectionLifecycle.StateOf(connection, now) is not (ConnectionState.Connected or ConnectionState.NeedsReconnect))
          {
              return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: AccountEndpoints.ConnectFirst);
          }

          if (connection.State == ConnectionState.NeedsReconnect
              || await tokens.GetAsync(connection, fresh: true, cancellationToken) is not { } token)
          {
              return TypedResults.Ok(new CheckResult(ConnectionView.Of(info, connection, now), AccountEndpoints.AccessLost));
          }

          string? problem;
          try
          {
              problem = await catalog.For(info.Product).TestAsync(token.Value, connection.ExternalId!, cancellationToken);
          }
          catch (HttpRequestException)
          {
              problem = AccountEndpoints.GoogleDidNotAnswer;
          }

          connection.CheckedAt = now;
          await db.SaveChangesAsync(cancellationToken);
          return TypedResults.Ok(new CheckResult(ConnectionView.Of(info, connection, now), problem));
      }

      public static async Task<Results<NoContent, NotFound>> DisconnectAsync(
          string product,
          BrandsDbContext db,
          ConnectionTokens tokens,
          AccessTokenCache cache,
          GoogleRevocations revocations,
          IAuditLog audit,
          CancellationToken cancellationToken)
      {
          if (Products.FindBySlug(product) is not { } info)
          {
              return TypedResults.NotFound();
          }

          if (await ConnectionLifecycle.FindAsync(db, info.Product, cancellationToken) is not { } connection)
          {
              return TypedResults.NoContent();
          }

          var refreshToken = await tokens.OpenAsync(connection, cancellationToken);
          var before = ConnectionLifecycle.Snapshot(connection);
          db.Connections.Remove(connection);
          cache.Forget(connection.Id);
          await db.SaveChangesAsync(cancellationToken);
          await audit.RecordAsync(new AuditRecord("connection.disconnected", "connection", connection.Id.ToString(), before), cancellationToken);
          if (refreshToken is not null)
          {
              await revocations.RevokeIfUnusedAsync(connection.GoogleSubject, refreshToken, cancellationToken);
          }

          return TypedResults.NoContent();
      }
  ```

- Add this record at the end of the file:

  ```csharp
  internal sealed record CheckResult(ConnectionView Connection, string? Problem);
  ```

In `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`, add these lines in `MapConnections` after `product.MapPost("/account", AccountEndpoints.ChooseAsync);`:

```csharp
        product.MapPost("/check", ConnectionEndpoints.CheckAsync);
        product.MapDelete("", ConnectionEndpoints.DisconnectAsync);
```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet build core/Kairo.slnx
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionLifecycleTests"
```

Expected: all pass, and the build has 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionEndpoints.cs \
  core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs \
  core/tests/Kairo.IntegrationTests/Api/Connections.cs \
  core/tests/Kairo.IntegrationTests/Api/ConnectionLifecycleTests.cs
git commit -m "Check and disconnect connections, and ask for a reconnect when Google stops accepting a token"
```

---

### Task 5: Access for other modules and departing admins

**Files:**
- Create: `core/src/Modules/Kairo.Modules.Brands/Connections/BrandConnections.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`
- Modify: `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/ConnectionAccessTests.cs`
- Test: `core/tests/Kairo.IntegrationTests/Api/ConnectionReleaseTests.cs`

**Interfaces:**
- Consumes:
  - Task 1's `IConnections` contract
  - from Tasks 2 and 3, `ConnectionTokens`, `GoogleRevocations` and `ReconnectReasons`
- Produces:
  - `BrandConnections : IConnections`, registered scoped. It works on the current brand context.
  - Identity calls `IConnections.ReleaseAsync(userId)` when a member is removed, leaves, or loses the admin role.

- [ ] **Step 1: Write the failing tests**

Create `core/tests/Kairo.IntegrationTests/Api/ConnectionAccessTests.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionAccessTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Other_modules_get_the_account_and_a_working_access_token()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await Connections.ConnectAsync(_api, admin, brandId, "google-ads");

        var first = await InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token));
        var refreshes = _api.GoogleApis.Refreshes;
        var second = await InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token));

        Assert.Equal("1111111111", first.ExternalId);
        Assert.StartsWith("access-", first.AccessToken);
        Assert.Equal(first.AccessToken, second.AccessToken);
        Assert.Equal(refreshes, _api.GoogleApis.Refreshes);
    }

    [Fact]
    public async Task A_product_that_is_not_connected_is_unavailable()
    {
        var (_, brandId) = await WorkspaceAsync();

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.BigQuery, Token)));

        Assert.Equal("not_connected", unavailable.Reason);
    }

    [Fact]
    public async Task A_rejected_token_makes_the_connection_unavailable_and_asks_for_a_reconnect()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var issued = await Connections.ConnectAsync(_api, admin, brandId, "google-ads");
        _api.GoogleApis.Reject(issued.RefreshToken);
        _api.Clock.Advance(TimeSpan.FromHours(2));

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            InBrandAsync(brandId, connections => connections.GetAccessAsync(ConnectedProduct.GoogleAds, Token)));
        var status = await InBrandAsync(brandId, connections => connections.GetStatusAsync(ConnectedProduct.GoogleAds, Token));

        Assert.Equal("token_rejected", unavailable.Reason);
        Assert.Equal(ConnectionState.NeedsReconnect, status.State);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(HttpClient Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin.Browser, await Workspaces.CreateAsync(admin.Browser));
    }

    private async Task<T> InBrandAsync<T>(Guid brandId, Func<IConnections, Task<T>> action)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await action(scope.ServiceProvider.GetRequiredService<IConnections>());
    }
}
```

Create `core/tests/Kairo.IntegrationTests/Api/ConnectionReleaseTests.cs`:

```csharp
using System.Net.Http.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class ConnectionReleaseTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Removing_an_admin_releases_the_connections_they_made()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        await AssertReleasedAsync(owner.Browser, brandId, issued);
    }

    [Fact]
    public async Task An_admin_who_leaves_releases_the_connections_they_made()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await other.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        await AssertReleasedAsync(owner.Browser, brandId, issued);
    }

    [Fact]
    public async Task Losing_the_admin_role_releases_the_connections_they_made()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await owner.Browser.PutAsJsonAsync($"/api/brands/{brandId}/members/{other.UserId}/roles", new { roles = new[] { "marketer" } }, Token))
            .EnsureSuccessStatusCode();

        await AssertReleasedAsync(owner.Browser, brandId, issued);
    }

    [Fact]
    public async Task Another_admins_connections_are_untouched()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        await Connections.ConnectAsync(_api, owner.Browser, brandId, "youtube");
        await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.Equal("connected", (await Connections.GetAsync(owner.Browser, brandId, "youtube")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_departing_admins_Google_account_stays_granted_while_another_workspace_uses_it()
    {
        var (owner, other, brandId) = await TwoAdminsAsync();
        var elsewhere = await Workspaces.CreateAsync(other.Browser);
        await Connections.ConnectAsync(_api, other.Browser, elsewhere, "youtube", subject: "agency-account");
        var issued = await Connections.ConnectAsync(_api, other.Browser, brandId, "google-ads", subject: "agency-account");

        (await owner.Browser.DeleteAsync($"/api/brands/{brandId}/members/{other.UserId}", Token)).EnsureSuccessStatusCode();

        Assert.DoesNotContain(issued.RefreshToken, _api.GoogleApis.Revoked);
        Assert.Equal("connected", (await Connections.GetAsync(other.Browser, elsewhere, "youtube")).GetProperty("state").GetString());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Owner, Member Other, Guid BrandId)> TwoAdminsAsync()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var other = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "admin");
        return (owner, other, brandId);
    }

    private async Task AssertReleasedAsync(HttpClient owner, Guid brandId, IssuedCode issued)
    {
        var connection = await Connections.GetAsync(owner, brandId, "google-ads");
        Assert.Equal("needsReconnect", connection.GetProperty("state").GetString());
        Assert.Equal("admin_left", connection.GetProperty("reconnectReason").GetString());
        Assert.Null(await Connections.StoredTokenAsync(sqlServer.KairoConnectionString, brandId, "google-ads"));
        Assert.Contains(issued.RefreshToken, _api.GoogleApis.Revoked);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionAccessTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionReleaseTests"
```

Expected:
- `ConnectionAccessTests` fails with "No service for type 'Kairo.Contracts.Connections.IConnections'".
- The release tests fail, because the connections stay `connected`. `Another_admins_connections_are_untouched` passes already.

- [ ] **Step 3: Implement `IConnections`**

Create `core/src/Modules/Kairo.Modules.Brands/Connections/BrandConnections.cs`:

```csharp
using Kairo.Contracts.Connections;
using Kairo.Modules.Brands.Data;

namespace Kairo.Modules.Brands.Connections;

internal sealed class BrandConnections(
    BrandsDbContext db,
    ConnectionTokens tokens,
    GoogleRevocations revocations,
    TimeProvider time) : IConnections
{
    public async Task<ConnectionStatus> GetStatusAsync(ConnectedProduct product, CancellationToken cancellationToken)
    {
        var connection = await ConnectionLifecycle.FindAsync(db, product, cancellationToken);
        var state = ConnectionLifecycle.StateOf(connection, time.GetUtcNow());
        return state == ConnectionState.NotConnected
            ? new ConnectionStatus(product, state, null, null)
            : new ConnectionStatus(product, state, connection!.ExternalId, connection.DisplayName);
    }

    public async Task<ConnectionAccess> GetAccessAsync(ConnectedProduct product, CancellationToken cancellationToken)
    {
        var connection = await ConnectionLifecycle.FindAsync(db, product, cancellationToken);
        if (connection is not { State: ConnectionState.Connected, ExternalId: { } externalId })
        {
            throw new ConnectionUnavailableException(
                product,
                connection?.State == ConnectionState.NeedsReconnect ? connection.ReconnectReason ?? "needs_reconnect" : "not_connected");
        }

        var token = await tokens.GetAsync(connection, fresh: false, cancellationToken)
            ?? throw new ConnectionUnavailableException(product, connection.ReconnectReason ?? ReconnectReasons.TokenRejected);
        return new ConnectionAccess(externalId, token.Value, token.ExpiresAt);
    }

    public async Task ReleaseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var theirs = await db.Connections
            .Where(connection => connection.ConnectedBy == userId && connection.RefreshToken != null)
            .ToListAsync(cancellationToken);

        var revoke = new List<(string Subject, string Token)>();
        foreach (var connection in theirs)
        {
            if (await tokens.OpenAsync(connection, cancellationToken) is { } refreshToken)
            {
                revoke.Add((connection.GoogleSubject, refreshToken));
            }

            await tokens.LoseAsync(connection, ReconnectReasons.AdminLeft, cancellationToken);
        }

        foreach (var (subject, refreshToken) in revoke)
        {
            await revocations.RevokeIfUnusedAsync(subject, refreshToken, cancellationToken);
        }
    }
}
```

In `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`:
- Add `using Kairo.Contracts.Connections;` at the top.
- In `AddConnections`, add this line after `services.AddScoped<GoogleRevocations>();`:

  ```csharp
          services.AddScoped<IConnections, BrandConnections>();
  ```

- [ ] **Step 4: Release connections in Identity**

In `core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs`:
- Add `using Kairo.Contracts.Connections;` below `using Kairo.Contracts.Audit;`.
- In `ChangeRolesAsync`, add the parameter `IConnections connections,` after `IAuditLog audit,`. Then insert this after `await db.SaveChangesAsync(cancellationToken);`:

  ```csharp
          if (before.HasFlag(BrandRoles.Admin) && !after.HasFlag(BrandRoles.Admin))
          {
              await connections.ReleaseAsync(userId, cancellationToken);
          }
  ```

- In `RemoveAsync`, add the parameter `IConnections connections,` after `IAuditLog audit,`. Then insert this after `await db.SaveChangesAsync(cancellationToken);`:

  ```csharp
          await connections.ReleaseAsync(userId, cancellationToken);
  ```

- [ ] **Step 5: Run the tests to see them pass**

```bash
dotnet build core/Kairo.slnx
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionAccessTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.ConnectionReleaseTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.TeamTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: all pass, and the build has 0 warnings. `TeamTests` shows that member management still behaves as before.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.Brands/Connections/BrandConnections.cs \
  core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs \
  core/src/Modules/Kairo.Modules.Identity/Members/MemberEndpoints.cs \
  core/tests/Kairo.IntegrationTests/Api/ConnectionAccessTests.cs \
  core/tests/Kairo.IntegrationTests/Api/ConnectionReleaseTests.cs
git commit -m "Give other modules connection access, and release an admin's connections when they leave or lose the role"
```

---

### Task 6: Permission matrix and API contract

**Files:**
- Modify: `core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs`
- Modify (regenerated): `core/src/Kairo.Api/openapi.json`
- Modify (regenerated): `web/src/lib/core-api/schema.ts`

**Interfaces:**
- Consumes: every endpoint from Tasks 2 to 4.
- Produces: the committed OpenAPI document and the web app's generated types for the connection endpoints. Nency's screen builds on these.

- [ ] **Step 1: Add the endpoints to the permission matrix**

In `core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs`, add these rows to the `endpoints` array after the `audit-log` row:

```csharp
            (HttpMethod.Get, $"{brand}/connections", null, false),
            (HttpMethod.Get, $"{brand}/connections/google-ads/connect", null, true),
            (HttpMethod.Get, $"{brand}/connections/google-ads/accounts", null, true),
            (HttpMethod.Post, $"{brand}/connections/google-ads/account", new { externalId = "1" }, true),
            (HttpMethod.Post, $"{brand}/connections/google-ads/check", null, true),
            (HttpMethod.Delete, $"{brand}/connections/google-ads", null, true),
```

- [ ] **Step 2: Run the permission test**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.PermissionTests"
```

Expected: it passes. The group-level admin requirement already gives marketers and approvers 403. If a row reports a mismatch, check that the `/{product}` group in `MapConnections` carries `.RequireBrandRole(BrandRoles.Admin)`.

- [ ] **Step 3: Regenerate the OpenAPI document and the web types**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
```

Expected:
- The second `dotnet test` passes.
- `git diff --stat` shows only `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts` changed.
- The diff adds `/api/brands/{brandId}/connections`, its `{product}` routes and `/api/connections/callback/google`.
- The web lint, tests and build pass.

- [ ] **Step 4: Run the whole core suite**

```bash
dotnet test --solution core/Kairo.slnx
```

Expected: every test passes, with 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add core/tests/Kairo.IntegrationTests/Api/PermissionTests.cs core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts
git commit -m "Add the connection endpoints to the permission matrix and the API contract"
```

---

### Task 7: Cloud KMS and deployment

**Files:**
- Modify: `core/Directory.Packages.props`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Kairo.Modules.Brands.csproj`
- Create: `core/src/Modules/Kairo.Modules.Brands/Connections/KmsTokenCipher.cs`
- Modify: `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`
- Test: `core/tests/Kairo.Api.Tests/KmsSetupTests.cs`
- Modify: `deploy/Kairo.Deploy/Common.ps1`, `deploy/Kairo.Deploy/Setup.ps1`, `deploy/Kairo.Deploy/Release.ps1`
- Test: `deploy/tests/Common.Tests.ps1`, `deploy/tests/Setup.Tests.ps1`, `deploy/tests/Release.Tests.ps1`

**Interfaces:**
- Consumes: Task 2's `ITokenCipher` and `SealedToken`, and the deploy module's `Invoke-Gcloud`, `Test-Gcloud`, `Add-KairoBinding` and `Get-KairoServiceAccount`.
- Produces:
  - `KmsTokenCipher`, used when `Kairo:Kms:KeyName` is set
  - `Get-KairoTokenKeyName -Environment`
  - `Initialize-KairoKeys -Environment`, called by `Initialize-KairoProject`
  - `Kairo__Kms__KeyName` on the API and the worker

- [ ] **Step 1: Write the failing core test**

Create `core/tests/Kairo.Api.Tests/KmsSetupTests.cs`:

```csharp
using Kairo.Contracts.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.Api.Tests;

public sealed class KmsSetupTests
{
    private const string KeyName = "projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens";

    [Fact]
    public async Task A_configured_key_makes_connections_use_Cloud_KMS_without_calling_it_at_startup()
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(builder => builder.UseSetting("Kairo:Kms:KeyName", KeyName));
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConnections>());
        Assert.Equal("KmsTokenCipher", Cipher(scope.ServiceProvider).GetType().Name);
    }

    [Fact]
    public async Task Without_a_key_connections_use_Data_Protection()
    {
        await using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal("DataProtectionTokenCipher", Cipher(scope.ServiceProvider).GetType().Name);
    }

    private static object Cipher(IServiceProvider services) =>
        services.GetRequiredService(Type.GetType("Kairo.Modules.Brands.Connections.ITokenCipher, Kairo.Modules.Brands", throwOnError: true)!);
}
```

- [ ] **Step 2: Run it to see it fail**

```bash
dotnet test --project core/tests/Kairo.Api.Tests --filter-class "Kairo.Api.Tests.KmsSetupTests"
```

Expected: the first test fails, because the cipher is `DataProtectionTokenCipher`. The second passes.

- [ ] **Step 3: Add the KMS cipher**

In `core/Directory.Packages.props`, add a new item group before the `Tests` group:

```xml
  <ItemGroup Label="Google">
    <PackageVersion Include="Google.Cloud.Kms.V1" Version="3.27.0" />
  </ItemGroup>
```

Replace `core/src/Modules/Kairo.Modules.Brands/Kairo.Modules.Brands.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Google.Cloud.Kms.V1" />
  </ItemGroup>

</Project>
```

Create `core/src/Modules/Kairo.Modules.Brands/Connections/KmsTokenCipher.cs`:

```csharp
using System.Security.Cryptography;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Grpc.Core;

namespace Kairo.Modules.Brands.Connections;

internal sealed class KmsTokenCipher(string keyName) : ITokenCipher
{
    private readonly Lazy<Task<KeyManagementServiceClient>> _client = new(() => KeyManagementServiceClient.CreateAsync());

    public async Task<SealedToken> SealAsync(string token, string context, CancellationToken cancellationToken)
    {
        var client = await _client.Value;
        var response = await client.EncryptAsync(
            new EncryptRequest
            {
                Name = keyName,
                Plaintext = ByteString.CopyFromUtf8(token),
                AdditionalAuthenticatedData = ByteString.CopyFromUtf8(context),
            },
            cancellationToken);
        return new SealedToken(response.Ciphertext.ToByteArray(), response.Name);
    }

    public async Task<string> OpenAsync(byte[] ciphertext, string context, CancellationToken cancellationToken)
    {
        var client = await _client.Value;
        try
        {
            var response = await client.DecryptAsync(
                new DecryptRequest
                {
                    Name = keyName,
                    Ciphertext = ByteString.CopyFrom(ciphertext),
                    AdditionalAuthenticatedData = ByteString.CopyFromUtf8(context),
                },
                cancellationToken);
            return response.Plaintext.ToStringUtf8();
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument)
        {
            throw new CryptographicException("The token could not be decrypted.", exception);
        }
    }
}
```

In `core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs`, replace `services.AddSingleton<ITokenCipher, DataProtectionTokenCipher>();` with:

```csharp
        var keyName = configuration["Kairo:Kms:KeyName"];
        if (string.IsNullOrEmpty(keyName))
        {
            services.AddSingleton<ITokenCipher, DataProtectionTokenCipher>();
        }
        else
        {
            services.AddSingleton<ITokenCipher>(new KmsTokenCipher(keyName));
        }
```

- [ ] **Step 4: Run the core tests**

```bash
dotnet build core/Kairo.slnx
dotnet test --project core/tests/Kairo.Api.Tests
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: all pass, and the build has 0 warnings. If NuGet reports a downgrade or conflict warning (NU1605 or NU1608), raise the conflicting `PackageVersion` in `core/Directory.Packages.props` to the version NuGet names. Don't suppress the warning.

- [ ] **Step 5: Write the failing deployment tests**

In `deploy/tests/Common.Tests.ps1`, add this test inside `Describe 'names'`, after `It 'derives a service account'`:

```powershell
    It 'derives the key that encrypts connection tokens' {
        Get-KairoTokenKeyName -Environment $environment |
            Should -Be 'projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens'
    }
```

In `deploy/tests/Setup.Tests.ps1`:
- Replace the body of `It 'enables the APIs Kairo needs'` with:

  ```powershell
            $calls | Should -Contain ('services enable run.googleapis.com sqladmin.googleapis.com compute.googleapis.com ' +
                'servicenetworking.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com ' +
                'secretmanager.googleapis.com iam.googleapis.com storage.googleapis.com cloudkms.googleapis.com ' +
                'googleads.googleapis.com youtube.googleapis.com merchantapi.googleapis.com analyticsadmin.googleapis.com ' +
                'bigquery.googleapis.com --project kairo-test')
  ```

- Add these tests inside `Context 'in an empty project'`, after `It 'lets Cloud Build read the source, push images and write logs'`:

  ```powershell
          It 'creates a key for connection tokens that rotates every 90 days' {
              $calls | Should -Contain 'kms keyrings create kairo --location asia-south1 --project kairo-test'
              $key = $calls | Where-Object { $_ -like 'kms keys create connection-tokens *' }
              $key | Should -BeLike '*--keyring kairo*'
              $key | Should -BeLike '*--purpose encryption*'
              $key | Should -BeLike '*--rotation-period 90d*'
              $key | Should -Match '--next-rotation-time \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z'
              $key | Should -BeLike '*--location asia-south1 --project kairo-test'
          }

          It 'lets only the API and the worker use the token key' {
              foreach ($name in 'kairo-api', 'kairo-worker') {
                  $calls | Should -Contain "kms keys add-iam-policy-binding connection-tokens --keyring kairo --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/cloudkms.cryptoKeyEncrypterDecrypter --location asia-south1 --project kairo-test"
              }
              ($calls | Where-Object { $_ -like 'kms keys add-iam-policy-binding*kairo-web@*' }) | Should -BeNullOrEmpty
          }
  ```

In `deploy/tests/Release.Tests.ps1`, add this test inside `Describe 'Invoke-KairoDeploy'`, after `It 'deploys the worker privately with the database'`:

```powershell
    It 'tells the API and the worker which key encrypts connection tokens' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        $key = 'Kairo__Kms__KeyName=projects/kairo-test/locations/asia-south1/keyRings/kairo/cryptoKeys/connection-tokens'
        ($calls | Where-Object { $_ -like 'run deploy kairo-api *' }) |
            Should -BeLike "*--set-env-vars=Kairo__PublicOrigin=https://kairo-web-123.asia-south1.run.app,$key*"
        ($calls | Where-Object { $_ -like 'run deploy kairo-worker *' }) | Should -BeLike "*--set-env-vars=$key*"
    }
```

- [ ] **Step 6: Run them to see them fail**

```bash
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
```

Expected: the four new or changed tests fail. `Get-KairoTokenKeyName` isn't recognised, and no `kms` calls or `Kairo__Kms__KeyName` setting exist yet.

- [ ] **Step 7: Add the key to the deploy scripts**

In `deploy/Kairo.Deploy/Common.ps1`:
- Add these lines after `$script:SqlSecret = 'kairo-sql-connection'`:

  ```powershell
  $script:KeyRing = 'kairo'
  $script:TokenKey = 'connection-tokens'
  ```

- Add this function after `Get-KairoServiceAccount`:

  ```powershell
  function Get-KairoTokenKeyName {
      param([Parameter(Mandatory)][hashtable]$Environment)

      "projects/$($Environment.Project)/locations/$($Environment.Region)/keyRings/$script:KeyRing/cryptoKeys/$script:TokenKey"
  }
  ```

In `deploy/Kairo.Deploy/Setup.ps1`:
- Replace the `$script:Apis` array with:

  ```powershell
  $script:Apis = @(
      'run.googleapis.com', 'sqladmin.googleapis.com', 'compute.googleapis.com', 'servicenetworking.googleapis.com',
      'artifactregistry.googleapis.com', 'cloudbuild.googleapis.com', 'secretmanager.googleapis.com',
      'iam.googleapis.com', 'storage.googleapis.com', 'cloudkms.googleapis.com',
      'googleads.googleapis.com', 'youtube.googleapis.com', 'merchantapi.googleapis.com', 'analyticsadmin.googleapis.com',
      'bigquery.googleapis.com'
  )
  ```

- In `Initialize-KairoProject`, add `Initialize-KairoKeys -Environment $Environment` after `Initialize-KairoServiceAccounts -Environment $Environment`.
- Add this function after `Initialize-KairoServiceAccounts`:

  ```powershell
  function Initialize-KairoKeys {
      param([Parameter(Mandatory)][hashtable]$Environment)

      $scope = @('--location', $Environment.Region, '--project', $Environment.Project)
      if (-not (Test-Gcloud -ArgumentList (@('kms', 'keyrings', 'describe', $script:KeyRing) + $scope))) {
          Invoke-Gcloud -ArgumentList (@('kms', 'keyrings', 'create', $script:KeyRing) + $scope)
      }

      if (-not (Test-Gcloud -ArgumentList (@('kms', 'keys', 'describe', $script:TokenKey, '--keyring', $script:KeyRing) + $scope))) {
          $nextRotation = [DateTime]::UtcNow.AddDays(90).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
          Invoke-Gcloud -ArgumentList (@(
                  'kms', 'keys', 'create', $script:TokenKey,
                  '--keyring', $script:KeyRing,
                  '--purpose', 'encryption',
                  '--rotation-period', '90d',
                  '--next-rotation-time', $nextRotation
              ) + $scope)
      }

      foreach ($name in 'kairo-api', 'kairo-worker') {
          Add-KairoBinding -ArgumentList (@(
                  'kms', 'keys', 'add-iam-policy-binding', $script:TokenKey,
                  '--keyring', $script:KeyRing,
                  '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $name)",
                  '--role', 'roles/cloudkms.cryptoKeyEncrypterDecrypter'
              ) + $scope)
      }
  }
  ```

In `deploy/Kairo.Deploy/Release.ps1`, in `Invoke-KairoDeploy`:
- Add `$keyName = Get-KairoTokenKeyName -Environment $Environment` after the line `$sqlSecret = "ConnectionStrings__kairo=$($script:SqlSecret):latest"`.
- In the `kairo-api` settings, replace `"--set-env-vars=Kairo__PublicOrigin=$($urls['kairo-web'])",` with:

  ```powershell
              "--set-env-vars=Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName",
  ```

- Replace the `kairo-worker` settings `-Settings (@('--no-allow-unauthenticated') + $script:Vpc + @("--set-secrets=$sqlSecret"))` with:

  ```powershell
          -Settings (@('--no-allow-unauthenticated') + $script:Vpc + @("--set-env-vars=Kairo__Kms__KeyName=$keyName", "--set-secrets=$sqlSecret"))
  ```

- After `Write-Host "Google sign-in redirect URI: $($urls['kairo-web'])/api/auth/callback/google"`, add:

  ```powershell
      Write-Host "Google account connections redirect URI: $($urls['kairo-web'])/api/connections/callback/google"
  ```

- [ ] **Step 8: Run the deployment tests to see them pass**

```bash
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
```

Expected: every test passes. `in a project that is already set up` still makes no `create` calls, because each `kms` create is guarded by a `describe`.

- [ ] **Step 9: Commit**

```bash
git add core/Directory.Packages.props \
  core/src/Modules/Kairo.Modules.Brands/Kairo.Modules.Brands.csproj \
  core/src/Modules/Kairo.Modules.Brands/Connections/KmsTokenCipher.cs \
  core/src/Modules/Kairo.Modules.Brands/Connections/ConnectionSetup.cs \
  core/tests/Kairo.Api.Tests/KmsSetupTests.cs \
  deploy/Kairo.Deploy/Common.ps1 deploy/Kairo.Deploy/Setup.ps1 deploy/Kairo.Deploy/Release.ps1 \
  deploy/tests/Common.Tests.ps1 deploy/tests/Setup.Tests.ps1 deploy/tests/Release.Tests.ps1
git commit -m "Encrypt connection tokens with Cloud KMS when deployed, with a rotating key only the API and worker can use"
```

---

### Task 8: Documentation

**Files:**
- Modify: `docs/Kairo-SAD.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the behaviour built in Tasks 1 to 7.
- Produces: SAD and README text that matches the code.

- [ ] **Step 1: Correct the SAD**

Make these replacements in `docs/Kairo-SAD.md`. Each "before" is an exact existing line.

1. **§2.** Replace:

   ```markdown
   | Identity and secrets | Google OAuth 2.0 client (Google sign-in), Secret Manager, Cloud KMS | User sign-in, API keys and the developer token, and encrypted OAuth tokens |
   ```

   with:

   ```markdown
   | Identity and secrets | Google OAuth 2.0 client (Google sign-in and account connections), Secret Manager, Cloud KMS | User sign-in, each brand's consent to its Google accounts, API keys, and encrypted OAuth tokens |
   ```

2. **§7.** Replace the `Google Ads API` row's "How Kairo connects" cell:

   ```markdown
   | Google Ads API | Keyword metrics, performance and recommendations; creating campaigns, pausing and resuming ads, budgets and bid targets | Google Ads .NET client library in the core, with Kairo's developer token and each brand's OAuth consent | Keyword planning needs Basic access or above. Basic allows 15,000 operations a day; Standard has no daily cap. |
   ```

   with:

   ```markdown
   | Google Ads API | Keyword metrics, performance and recommendations; creating campaigns, pausing and resuming ads, budgets and bid targets | The Google Ads REST API from the core, with each brand's OAuth consent through Kairo's OAuth client. Since 9 September 2026 access levels belong to the Google Cloud project behind the OAuth client, and no developer token is sent. | Keyword planning needs Basic access or above, which the `kairo2` project has. Basic allows 15,000 operations a day; Standard has no daily cap. |
   ```

3. **§7.** Replace:

   ```markdown
   | Merchant API | Checking the Merchant Center link and product status, and updating titles if chosen | Google API .NET client in the core | Shopping needs the Merchant Center account linked to Google Ads |
   ```

   with:

   ```markdown
   | Merchant API | Checking the Merchant Center link and product status, and updating titles if chosen | The Merchant API v1 REST endpoints from the core, with the brand's consent. The Content API for Shopping it replaces was sunset on 18 August 2026. | Shopping needs the Merchant Center account linked to Google Ads |
   ```

4. **§8.** Replace:

   ```markdown
   | Secrets | The developer token, OAuth client secret and API keys sit in Secret Manager. Brand OAuth refresh tokens are encrypted with Cloud KMS keys. |
   ```

   with:

   ```markdown
   | Secrets | The OAuth client secret and API keys sit in Secret Manager. Each brand connection's refresh token is encrypted with the Cloud KMS key `connection-tokens`, which rotates every 90 days, with the brand and connection IDs as additional authenticated data. Only the API and worker service accounts can use the key. |
   ```

5. **§13.** Replace:

   ```markdown
   The biggest technical risk is Google Ads API access: keyword planning needs Basic access, which takes brand verification and a review.
   ```

   with:

   ```markdown
   The biggest technical risk is Google's review of Kairo's OAuth app: connecting brand accounts asks for sensitive scopes, which need OAuth verification before more than 100 users can consent.
   ```

6. **§13.** Replace:

   ```markdown
   | Google Ads developer token access | Explorer access blocks keyword planning; Basic access needs brand verification and a review | Apply for Basic access early and for Standard before scaling; build on test accounts meanwhile |
   ```

   with:

   ```markdown
   | OAuth verification for sensitive scopes | Until `kairo2` is verified, consent shows an "unverified app" warning and is limited to 100 users | Enough for the pilot; submit verification with a privacy policy, homepage, demo video and scope justifications before scaling. Google Ads Basic access is already granted to the project; apply for Standard before scaling. |
   ```

7. **§13.** Replace:

   ```markdown
   - [ ] Who owns the Google Ads manager account and developer token, and who completes brand verification?
   ```

   with:

   ```markdown
   - [ ] Who completes OAuth verification for `kairo2`'s sensitive scopes?
   ```

8. **§6.** In the `Access` row, replace `OAuth tokens encrypted with Cloud KMS` with `Connection rows live in the brands schema; OAuth refresh tokens encrypted with Cloud KMS`.

Then run `grep -n -i "developer token" docs/Kairo-SAD.md`. It must print only the §7 Google Ads row, which says no developer token is sent.

- [ ] **Step 2: Add the README section**

In `README.md`, insert this section after the `## Set up Google sign-in` section, before `## Run everything locally`:

````markdown
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
````

- [ ] **Step 3: Commit**

```bash
git add docs/Kairo-SAD.md README.md
git commit -m "Document account connections and drop the retired Google Ads developer token from the SAD"
```

---

### Task 9: Final verification

**Files:** none changed, unless a check fails.

- [ ] **Step 1: Run every automated check**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
uv run --directory agents ruff check
uv run --directory agents ruff format --check
uv run --directory agents pytest
npm --prefix web run lint
npm --prefix web test
npm --prefix web run build
npm --prefix web run generate:api && git diff --exit-code web/src/lib/core-api/schema.ts
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
git status --short
```

Expected: every command succeeds, the build has 0 warnings, `git diff --exit-code` exits 0, and `git status --short` is empty.

- [ ] **Step 2: Scan for comments and attribution**

```bash
git diff origin/develop --name-only --diff-filter=AM | grep -E '\.(cs|ts|tsx)$' | xargs -r grep -n -E '^\s*(//|/\*)' || true
git diff origin/develop --name-only --diff-filter=AM | grep -E '\.ps1$' | xargs -r grep -n -E '^\s*#' || true
git log origin/develop..HEAD --format=%B | grep -i -E 'co-authored|claude|generated with' || true
```

Expected: all three print nothing. The C# pattern leaves out `#nullable disable`, which EF Core migrations keep.

- [ ] **Step 3: Hand the manual checks to the user**

These need real Google accounts and the Cloud Console changes from the README section. List them for the user to run with `dotnet run --project core/src/Kairo.AppHost` at `http://localhost:8000`:

1. As a workspace admin, open `GET /api/brands/{id}/connections/google-ads/connect` in the browser. Consent with the demo Google Ads test account, choose it from `/accounts`, and see `connected` in `GET /api/brands/{id}/connections`.
2. Connect a YouTube channel the same way, with the same Google account.
3. Disconnect YouTube, then `POST …/connections/google-ads/check`. Google Ads must still be connected, with no problem. This confirms how Google's revocation behaves with two connections on one Google account.
4. Remove Kairo's access at `myaccount.google.com/permissions`, then `POST …/connections/google-ads/check`. The connection must move to `needsReconnect` with `token_rejected`.
5. Disconnect Google Ads.

---

## Self-review notes

- **Spec coverage:**
  - Products table: Tasks 2 and 3.
  - States: Tasks 2 to 5.
  - Connecting steps 1 to 4: Tasks 2 and 3.
  - Disconnecting and losing access: Tasks 4 and 5.
  - Contract: Tasks 1 and 5.
  - Data: Task 1.
  - Encryption: Tasks 2 and 7.
  - API: Tasks 2 to 4 and 6.
  - Audit: Tasks 2 to 4.
  - Code layout: every task.
  - Google Cloud setup: Tasks 7 and 8.
  - Testing: every task.
  - Done criteria: Task 9.
- **Not built, as the spec's out-of-scope list says:**
  - the web screen
  - daily token checks
  - email alerts
  - data transfers
  - catalogue import
  - the `connection.changed` live event, which is wired by whichever of this branch and `feature/live-updates` merges second
- **A rejected token found during a request that then fails with 409** (choosing an account) rolls back with the request. The next use finds the rejection again. `check` returns 200, so its Needs reconnect change always commits.
