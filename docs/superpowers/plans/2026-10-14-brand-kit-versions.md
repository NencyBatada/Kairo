# Kairo Brand Rules and Versions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admins keep the Brand Kit (voice, colours, logo, key messages, product categories, rules and guideline files) as versions: every edit goes into one draft, an admin approves it, any older version can be restored into the draft, and other modules only ever read the approved version.

**Architecture:**
- **File store.** `Kairo.SharedKernel` gains `IFileStore`, with a Cloud Storage implementation over REST for deployed environments and a disk implementation when no project is configured.
- **Brand Kit module.** The empty `Kairo.Modules.BrandKit` stub gains the SQL schema `brand_kit` with two tables, `KitVersions` (content as JSON, one draft and one approved per brand) and `KitFiles`. It exposes the draft, approve, history, restore and file endpoints under `/api/brands/{brandId}/brand-kit`.
- **Contract.** `IBrandKit.GetApprovedAsync` in `Kairo.Contracts` gives other modules the approved version only.
- **Concurrency.** Every write takes a SQL application lock on the brand inside the request transaction, and every draft change carries the `revision` it was based on, so concurrent edits fail with `409` instead of overwriting each other.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 on SQL Server 2022, `Google.Apis.Auth` 1.73.0 for Application Default Credentials, Cloud Storage JSON API v1 over `HttpClient`, xUnit v3 with Testcontainers, Pester 5.9.1.

**Spec:** `docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md` (sections **Shared building blocks → File store** and **Brand Kit**). Drafting and the catalogue are separate plans.

## Global Constraints

- **Worktree and branch.**
  - Work only in `D:\VSTS\Kairo-worktrees\brand-kit-versions`, on branch `feature/brand-kit-versions`.
  - Run every command in Git Bash from `/d/VSTS/Kairo-worktrees/brand-kit-versions`, unless a step says otherwise.
- **Commits.**
  - Never add `Co-Authored-By` or any other AI attribution. The author is the configured git user (`AGENTS.md` §1).
  - Stage files by explicit path, never with `git add -A` or `git add .`. Never stage `CLAUDE.md` or `.claude/`.
  - Don't push, and never touch `docs/task.md` on this branch.
- **No comments in code** (`AGENTS.md` §3): no `//`, `#`, `/* */` or `<!-- -->` comments and no XML doc comments. Generated EF Core migrations go through `core/scripts/tidy-migrations.sh`.
- **Warnings are errors** in the core (`core/Directory.Build.props`). Every build ends with 0 warnings.
- **Docker must be running** for `dotnet test`, because `Kairo.IntegrationTests` starts SQL Server in a container.
- **Module rules stay true.**
  - `Kairo.Modules.BrandKit` exposes exactly one public type, `BrandKitModule`, and adds no `ProjectReference`. Everything else in it is `internal`.
  - Its tables live only in the `brand_kit` schema (the architecture test derives it from the module name).
  - The only contract change is `Kairo.Contracts/BrandKit/IBrandKit.cs`.
- **Names, exactly.**
  - Version statuses, as stored: `Draft`, `Approved`, `Superseded`, `Discarded`; in JSON: `draft`, `approved`, `superseded`, `discarded`.
  - Origins: `Manual`, `Drafted`, `Restored` (JSON `manual`, `drafted`, `restored`).
  - File kinds: `Guideline`, `Logo` (JSON `guideline`, `logo`).
  - Colour roles: `primary`, `secondary`, `accent`, `background`, `text`. Competitor policies: `never`, `neutral`. Source kinds: `page`, `ad`.
  - Audit actions: `brand_kit.draft_saved`, `brand_kit.draft_discarded`, `brand_kit.approved`, `brand_kit.restored`, `brand_kit.file_uploaded`, `brand_kit.file_deleted`. Audit target types: `brand_kit_version`, `brand_kit_file`.
  - Live events: `brand_kit.changed` with `{ number, status }`, and `brand_kit.files_changed` with `{ fileId }`, both to everyone.
  - Lock resource: `brand-kit:{brandId:N}`.
  - Bucket: logical `brand-files`, real `{project}-brand-files`. Keys: `{brandId:N}/guidelines/{id:N}` and `{brandId:N}/logos/{id:N}`.
  - HTTP client name: `kairo-files`.
- **Limits.** Voice summary 500 characters; at most 10 traits, 20 dos, 20 don'ts and 10 examples of 200 characters each; 12 colours (name 50 characters); 20 key messages of 200; 50 product categories of 100; 200 banned words of 50; 20 disclaimers (text 500, when 200); 20 tone rules of 200; 50 competitors (name 100); 20 guideline files; 10 sources per item; web addresses at most 2,000 characters. Uploads at most 20 MB.
- **Messages, exactly.**
  - `Someone else changed the draft. Reload it and try again.` (409)
  - `There's no draft.` (404)
  - `Only an older, superseded version can be restored.` (400)
  - `Use at most {n} characters.`, `Add at most {n}.`, `Use a colour code like #E4572E.`, `Use a full web address starting with https://.`, `Use the Google Ads ad ID, in digits.`
  - `Upload the logo to the Brand Kit first.`, `Upload this guideline file to the Brand Kit first.`
  - `Choose a file to upload.`, `The file is larger than 20 MB.`, `Upload a PDF, PNG, JPEG, WebP, SVG or Word (.docx) file.`, `A logo must be a PNG, JPEG, WebP or SVG image.`, `Use guideline or logo.`
- **Configuration.** `Kairo:Files:Project` (empty means the disk store) and `Kairo:Files:Root` (default `Path.Combine(Path.GetTempPath(), "kairo-files")`).
- **Running tests.**
  - One class: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "<namespace>.<Class>"`
  - Architecture: `dotnet test --project core/tests/Kairo.ArchitectureTests`
  - Whole core: `dotnet test --solution core/Kairo.slnx`
  - Deployment scripts: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
  - A task that adds API endpoints (Tasks 4, 5 and 6) regenerates the contract and stages `core/src/Kairo.Api/openapi.json` and `web/src/lib/core-api/schema.ts`. The first time in this worktree, run `npm --prefix web ci`.
    ```bash
    KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
    npm --prefix web run generate:api
    ```

## Review Focus

These are the inputs most likely to hurt a real admin. Each is pinned by a test in the task named in brackets.

1. **Two admins press Save at the same moment** on the same revision. Exactly one wins and the other gets `409`, never a silent overwrite and never a `500` from the unique index. (Task 4: `Two_admins_saving_the_same_revision_at_once_leave_one_winner`.)
2. **A request with `null` instead of a list** (`"traits": null`, `"colours": null`), as a form library often sends. It is saved as empty lists, not a `500`. (Task 3: `Null_lists_and_items_are_treated_as_empty`; Task 4: `Null_lists_are_saved_as_empty`.)
3. **A file that lies about what it is**: an HTML page renamed `brand-guide.pdf`, or an SVG logo carrying a script. The HTML is refused by its bytes; the SVG is stored but only ever served as an attachment with `nosniff` and a sandbox policy. (Task 6: `A_file_whose_bytes_are_not_an_allowed_type_is_refused`, `A_download_is_an_attachment_that_the_browser_will_not_run`.)
4. **Deleting a guideline file the approved kit still uses.** The approved version keeps it, it still downloads, and `IBrandKit` still lists it, while the draft drops it. (Task 6: `Deleting_a_file_removes_it_from_the_draft_but_not_from_the_approved_version`; Task 7: `A_deleted_file_still_reaches_agents_through_the_approved_version`.)
5. **Approving a second time.** The old approved version must become superseded before the draft is approved, or the one-approved index fails. (Task 4: `A_second_approval_supersedes_the_first`; Task 5: `History_lists_versions_newest_first_without_discarded_drafts`.)

## Where this plan differs from the spec

1. **Schema `brand_kit`, not `brandkit`.** `ModuleRulesTests.Module_tables_live_in_the_module_schema` derives the schema from the module name. Task 8 corrects the spec.
2. **Live event names use underscores**: `brand_kit.changed`, because the live outbox only accepts `^[a-z_]+(\.[a-z_]+)+$`. File uploads and deletes publish `brand_kit.files_changed`, since they don't belong to one version.
3. **Downloads stream through the API** as an attachment, instead of a `302` to a signed URL. Files are at most 20 MB, the web app already proxies `/api`, and this needs no IAM `signBlob` grant and no development-only download route. `IFileStore` has only `PutAsync` and `OpenAsync`; nothing deletes stored objects yet.
4. **Discarded drafts are kept** with status `Discarded` and hidden from history, so their numbers are never reused.
5. **A draft may refer to a deleted file**, as long as the file belongs to the brand and is the right kind. Restoring an older version therefore keeps its files. Deleting a file removes it from the current draft only.
6. **Only the `brand-files` bucket** is created now. `catalogue-imports` comes with the catalogue plan.

## File structure

**SharedKernel** (`core/src/Kairo.SharedKernel/`)
- `Kairo.SharedKernel.csproj`: `Google.Apis.Auth`, `Microsoft.Extensions.Http.Resilience`.
- `KairoCoreServices.cs`: calls `AddKairoFiles`.
- `Files/IFileStore.cs`: the interface, `FileBuckets`, and the two exceptions.
- `Files/FileKeys.cs`: key building and checking.
- `Files/FileStoreOptions.cs`: project, root and bucket names.
- `Files/DiskFileStore.cs`, `Files/GoogleCloudFileStore.cs`, `Files/StorageAccessToken.cs`, `Files/FileStoreSetup.cs`.

**Contracts**
- `core/src/Kairo.Contracts/BrandKit/IBrandKit.cs`.

**Brand Kit module** (`core/src/Modules/Kairo.Modules.BrandKit/`)
- `Kairo.Modules.BrandKit.csproj`, `BrandKitModule.cs`.
- `Data/BrandKitDbContext.cs`, `Data/BrandKitDbContextFactory.cs`, `Data/Migrations/*`.
- `Kit/KitVersion.cs`: entity and enums.
- `Kit/KitContent.cs`: the content records.
- `Kit/KitJson.cs`: storage JSON and enum names.
- `Kit/KitContentRules.cs`: cleaning and checking content.
- `Kit/KitViews.cs`: request and response records.
- `Kit/KitVersions.cs`: version reads and writes, the lock, file reference checks.
- `Kit/KitEndpoints.cs`: draft, approve, history and restore endpoints.
- `Kit/BrandKitLiveEvents.cs`, `Kit/KitSetup.cs`, `Kit/BrandKitReader.cs`.
- `Files/KitFile.cs`, `Files/KitFileView.cs`, `Files/KitFileTypes.cs`, `Files/KitFileEndpoints.cs`.

**Tests** (`core/tests/Kairo.IntegrationTests/`)
- `Files/FileStoreTests.cs`, `Files/FakeCloudStorage.cs`.
- `Persistence/BrandKitTableTests.cs`.
- `BrandKit/BrandKits.cs`, `BrandKit/KitContentRulesTests.cs`, `BrandKit/BrandKitDraftTests.cs`, `BrandKit/BrandKitHistoryTests.cs`, `BrandKit/BrandKitFileTests.cs`, `BrandKit/BrandKitContractTests.cs`.

**Deployment and docs**
- `deploy/Kairo.Deploy/Setup.ps1`, `deploy/Kairo.Deploy/Release.ps1`, `deploy/tests/Setup.Tests.ps1`, `deploy/tests/Release.Tests.ps1`.
- `docs/Kairo-SAD.md`, `docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md`.

---

### Task 1: File store

**Files:**
- Modify: `core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj`
- Modify: `core/src/Kairo.SharedKernel/KairoCoreServices.cs`
- Create: `core/src/Kairo.SharedKernel/Files/IFileStore.cs`
- Create: `core/src/Kairo.SharedKernel/Files/FileKeys.cs`
- Create: `core/src/Kairo.SharedKernel/Files/FileStoreOptions.cs`
- Create: `core/src/Kairo.SharedKernel/Files/DiskFileStore.cs`
- Create: `core/src/Kairo.SharedKernel/Files/StorageAccessToken.cs`
- Create: `core/src/Kairo.SharedKernel/Files/GoogleCloudFileStore.cs`
- Create: `core/src/Kairo.SharedKernel/Files/FileStoreSetup.cs`
- Test: `core/tests/Kairo.IntegrationTests/Files/FakeCloudStorage.cs`
- Test: `core/tests/Kairo.IntegrationTests/Files/FileStoreTests.cs`

**Interfaces:**
- Produces: `IFileStore.PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken)`, `IFileStore.OpenAsync(string bucket, string key, CancellationToken) -> Task<Stream>`, `FileBuckets.BrandFiles = "brand-files"`, `FileKeys.For(Guid brandId, string area, Guid id) -> string`, `StoredFileMissingException`, `FileStoreException`, `IStorageAccessToken.GetAsync(CancellationToken) -> Task<string>`, `FileStoreSetup.AddKairoFiles(IServiceCollection)`, `FileStoreSetup.HttpClientName = "kairo-files"`.

- [ ] **Step 1: Write the fake Cloud Storage handler**

`core/tests/Kairo.IntegrationTests/Files/FakeCloudStorage.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Kairo.SharedKernel.Files;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Files;

internal sealed class FakeStorageToken : IStorageAccessToken
{
    public const string Value = "storage-token";

    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Value);
}

internal sealed class FakeCloudStorage : HttpMessageHandler
{
    public Dictionary<string, (byte[] Content, string ContentType)> Objects { get; } = [];

    public List<string> Calls { get; } = [];

    public HttpStatusCode? FailWith { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var raw = request.RequestUri!.OriginalString;
        Calls.Add($"{request.Method} {raw} {request.Headers.Authorization}");
        if (FailWith is { } status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent("{\"error\":{\"message\":\"unavailable\"}}") };
        }

        var path = request.RequestUri.AbsolutePath.Split('/');
        if (request.Method == HttpMethod.Post && path is ["", "upload", "storage", "v1", "b", var bucket, "o"])
        {
            var name = QueryHelpers.ParseQuery(request.RequestUri.Query)["name"].ToString();
            Objects[$"{bucket}/{name}"] = (await request.Content!.ReadAsByteArrayAsync(cancellationToken), request.Content.Headers.ContentType!.MediaType!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }

        if (request.Method == HttpMethod.Get && path is ["", "storage", "v1", "b", var readBucket, "o", ..])
        {
            var start = raw.IndexOf("/o/", StringComparison.Ordinal) + 3;
            var end = raw.IndexOf('?', start);
            var key = Uri.UnescapeDataString(raw[start..(end < 0 ? raw.Length : end)]);
            if (!Objects.TryGetValue($"{readBucket}/{key}", out var stored))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }

            var content = new ByteArrayContent(stored.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(stored.ContentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"Unexpected call {request.Method} {raw}") };
    }
}
```

- [ ] **Step 2: Write the failing tests**

`core/tests/Kairo.IntegrationTests/Files/FileStoreTests.cs`:

```csharp
using System.Net;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Files;

public sealed class FileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kairo-files-{Guid.NewGuid():N}");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_project_files_are_kept_on_disk_under_the_root()
    {
        var store = DiskStore();
        var key = FileKeys.For(Guid.NewGuid(), "guidelines", Guid.NewGuid());

        await store.PutAsync(FileBuckets.BrandFiles, key, [1, 2, 3], "application/pdf", Token);

        Assert.True(File.Exists(Path.Combine([_root, FileBuckets.BrandFiles, .. key.Split('/')])));
        await using var stream = await store.OpenAsync(FileBuckets.BrandFiles, key, Token);
        Assert.Equal(new byte[] { 1, 2, 3 }, await ReadAllAsync(stream));
    }

    [Fact]
    public async Task A_missing_file_on_disk_is_reported()
    {
        var store = DiskStore();

        await Assert.ThrowsAsync<StoredFileMissingException>(() =>
            store.OpenAsync(FileBuckets.BrandFiles, FileKeys.For(Guid.NewGuid(), "guidelines", Guid.NewGuid()), Token));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../../b")]
    [InlineData("Upper/Case")]
    [InlineData("")]
    [InlineData("a//b")]
    public async Task Keys_that_could_leave_the_bucket_are_refused(string key)
    {
        var store = DiskStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(FileBuckets.BrandFiles, key, [1], "application/pdf", Token));
    }

    [Fact]
    public void Keys_are_the_brand_the_area_and_the_id()
    {
        var brandId = Guid.NewGuid();
        var id = Guid.NewGuid();

        Assert.Equal($"{brandId:N}/logos/{id:N}", FileKeys.For(brandId, "logos", id));
    }

    [Fact]
    public async Task With_a_project_files_go_to_the_projects_bucket_with_the_service_token()
    {
        var storage = new FakeCloudStorage();
        var store = CloudStore(storage);
        var key = FileKeys.For(Guid.NewGuid(), "guidelines", Guid.NewGuid());

        await store.PutAsync(FileBuckets.BrandFiles, key, [4, 5], "application/pdf", Token);

        var stored = storage.Objects[$"kairo-test-brand-files/{key}"];
        Assert.Equal(new byte[] { 4, 5 }, stored.Content);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(
            $"POST https://storage.googleapis.com/upload/storage/v1/b/kairo-test-brand-files/o?uploadType=media&name={Uri.EscapeDataString(key)} Bearer {FakeStorageToken.Value}",
            storage.Calls[0]);
        await using var stream = await store.OpenAsync(FileBuckets.BrandFiles, key, Token);
        Assert.Equal(new byte[] { 4, 5 }, await ReadAllAsync(stream));
        Assert.StartsWith(
            $"GET https://storage.googleapis.com/storage/v1/b/kairo-test-brand-files/o/{Uri.EscapeDataString(key)}?alt=media Bearer",
            storage.Calls[1]);
    }

    [Fact]
    public async Task A_missing_cloud_object_is_reported()
    {
        var store = CloudStore(new FakeCloudStorage());

        await Assert.ThrowsAsync<StoredFileMissingException>(() =>
            store.OpenAsync(FileBuckets.BrandFiles, FileKeys.For(Guid.NewGuid(), "guidelines", Guid.NewGuid()), Token));
    }

    [Fact]
    public async Task A_cloud_storage_error_names_its_status()
    {
        var store = CloudStore(new FakeCloudStorage { FailWith = HttpStatusCode.ServiceUnavailable });

        var error = await Assert.ThrowsAsync<FileStoreException>(() =>
            store.PutAsync(FileBuckets.BrandFiles, FileKeys.For(Guid.NewGuid(), "guidelines", Guid.NewGuid()), [1], "application/pdf", Token));

        Assert.Contains("503", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private IFileStore DiskStore() => Build(new() { ["Kairo:Files:Root"] = _root }, _ => { });

    private static IFileStore CloudStore(FakeCloudStorage storage) => Build(
        new() { ["Kairo:Files:Project"] = "kairo-test" },
        services =>
        {
            services.AddSingleton<IStorageAccessToken>(new FakeStorageToken());
            services.AddHttpClient(FileStoreSetup.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => storage);
        });

    private static IFileStore Build(Dictionary<string, string?> settings, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddKairoFiles();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IFileStore>();
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, Token);
        return buffer.ToArray();
    }
}
```

`Build` calls `AddKairoFiles` before `configure`, so the fake token and handler registered in `CloudStore` are the last registrations and win.

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Files.FileStoreTests"`
Expected: build FAILS with `The type or namespace name 'Files' does not exist in the namespace 'Kairo.SharedKernel'`.

- [ ] **Step 4: Add the packages to SharedKernel**

Replace `core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <NoWarn>$(NoWarn);EXTEXP0001</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Google.Apis.Auth" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Write the interface, keys and options**

`core/src/Kairo.SharedKernel/Files/IFileStore.cs`:

```csharp
namespace Kairo.SharedKernel.Files;

public interface IFileStore
{
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken);
}

public static class FileBuckets
{
    public const string BrandFiles = "brand-files";
}

public sealed class StoredFileMissingException(string bucket, string key)
    : Exception($"{bucket}/{key} isn't in the file store.");

public sealed class FileStoreException(string message) : Exception(message);
```

`core/src/Kairo.SharedKernel/Files/FileKeys.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Kairo.SharedKernel.Files;

public static partial class FileKeys
{
    public static string For(Guid brandId, string area, Guid id) => $"{brandId:N}/{area}/{id:N}";

    internal static void EnsureValid(string bucket, string key)
    {
        if (!Bucket().IsMatch(bucket))
        {
            throw new ArgumentException($"'{bucket}' isn't a valid bucket name.", nameof(bucket));
        }

        if (!Key().IsMatch(key))
        {
            throw new ArgumentException($"'{key}' isn't a valid file key.", nameof(key));
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex Bucket();

    [GeneratedRegex("^[a-z0-9-]{1,64}(/[a-z0-9-]{1,64}){0,7}$")]
    private static partial Regex Key();
}
```

`core/src/Kairo.SharedKernel/Files/FileStoreOptions.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace Kairo.SharedKernel.Files;

public sealed class FileStoreOptions(IConfiguration configuration)
{
    public string? Project => configuration["Kairo:Files:Project"] is { Length: > 0 } project ? project : null;

    public string Root => configuration["Kairo:Files:Root"] is { Length: > 0 } root
        ? root
        : Path.Combine(Path.GetTempPath(), "kairo-files");

    public string BucketName(string bucket) => $"{Project}-{bucket}";
}
```

- [ ] **Step 6: Write the two stores, the token and the setup**

`core/src/Kairo.SharedKernel/Files/DiskFileStore.cs`:

```csharp
namespace Kairo.SharedKernel.Files;

internal sealed class DiskFileStore(FileStoreOptions options) : IFileStore
{
    public async Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathOf(bucket, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    public Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        var path = PathOf(bucket, key);
        if (!File.Exists(path))
        {
            throw new StoredFileMissingException(bucket, key);
        }

        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    private string PathOf(string bucket, string key)
    {
        FileKeys.EnsureValid(bucket, key);
        return Path.Combine([options.Root, bucket, .. key.Split('/')]);
    }
}
```

`core/src/Kairo.SharedKernel/Files/StorageAccessToken.cs`:

```csharp
using Google.Apis.Auth.OAuth2;

namespace Kairo.SharedKernel.Files;

public interface IStorageAccessToken
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationDefaultStorageToken : IStorageAccessToken
{
    public const string Scope = "https://www.googleapis.com/auth/devstorage.read_write";

    private GoogleCredential? _credential;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)).CreateScoped(Scope);
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}
```

`core/src/Kairo.SharedKernel/Files/GoogleCloudFileStore.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;

namespace Kairo.SharedKernel.Files;

internal sealed class GoogleCloudFileStore(IHttpClientFactory http, IStorageAccessToken token, FileStoreOptions options) : IFileStore
{
    private const string Api = "https://storage.googleapis.com";

    public async Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        FileKeys.EnsureValid(bucket, key);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Api}/upload/storage/v1/b/{options.BucketName(bucket)}/o?uploadType=media&name={Uri.EscapeDataString(key)}")
        {
            Content = new ByteArrayContent(content),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureSuccessAsync(response, bucket, key, cancellationToken);
    }

    public async Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        FileKeys.EnsureValid(bucket, key);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Api}/storage/v1/b/{options.BucketName(bucket)}/o/{Uri.EscapeDataString(key)}?alt=media");
        var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new StoredFileMissingException(bucket, key);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                await EnsureSuccessAsync(response, bucket, key, cancellationToken);
            }
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token.GetAsync(cancellationToken));
        return await http.CreateClient(FileStoreSetup.HttpClientName).SendAsync(request, completion, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string bucket, string key, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new FileStoreException(
            $"Cloud Storage answered {(int)response.StatusCode} for {bucket}/{key}: {(body.Length > 300 ? body[..300] : body)}");
    }
}
```

`core/src/Kairo.SharedKernel/Files/FileStoreSetup.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kairo.SharedKernel.Files;

public static class FileStoreSetup
{
    public const string HttpClientName = "kairo-files";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static IServiceCollection AddKairoFiles(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout).RemoveAllResilienceHandlers();
        services.TryAddSingleton<FileStoreOptions>();
        services.TryAddSingleton<IStorageAccessToken, ApplicationDefaultStorageToken>();
        services.TryAddSingleton<IFileStore>(provider =>
        {
            var options = provider.GetRequiredService<FileStoreOptions>();
            return options.Project is null
                ? new DiskFileStore(options)
                : ActivatorUtilities.CreateInstance<GoogleCloudFileStore>(provider);
        });
        return services;
    }
}
```

In `core/src/Kairo.SharedKernel/KairoCoreServices.cs`, add `using Kairo.SharedKernel.Files;` and call `services.AddKairoFiles();` just before `return services;`.

- [ ] **Step 7: Run the tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Files.FileStoreTests"`
Expected: PASS, 11 tests.

Run: `dotnet test --project core/tests/Kairo.ArchitectureTests`
Expected: PASS (`SharedKernel_references_no_other_Kairo_assembly` still holds).

- [ ] **Step 8: Commit**

```bash
git add core/src/Kairo.SharedKernel/Kairo.SharedKernel.csproj core/src/Kairo.SharedKernel/KairoCoreServices.cs core/src/Kairo.SharedKernel/Files core/tests/Kairo.IntegrationTests/Files
git commit -m "Add a file store backed by Cloud Storage, or the disk in development"
```

---

### Task 2: Brand Kit storage

**Files:**
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kairo.Modules.BrandKit.csproj`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/BrandKitModule.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersion.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Files/KitFile.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Data/BrandKitDbContext.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Data/BrandKitDbContextFactory.cs`
- Create (generated): `core/src/Modules/Kairo.Modules.BrandKit/Data/Migrations/<timestamp>_Versions.cs`, `<timestamp>_Versions.Designer.cs`, `BrandKitDbContextModelSnapshot.cs`
- Test: `core/tests/Kairo.IntegrationTests/Persistence/BrandKitTableTests.cs`

**Interfaces:**
- Produces: `BrandKitDbContext` with `DbSet<KitVersion> Versions` and `DbSet<KitFile> Files`, `BrandKitDbContext.SchemaName = "brand_kit"`; `KitVersion` (`Id`, `BrandId`, `Number`, `Status`, `Content`, `Revision`, `Origin`, `RestoredFrom`, `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`, `ApprovedBy`, `ApprovedAt`); `enum VersionStatus { Draft, Approved, Superseded, Discarded }`; `enum VersionOrigin { Manual, Drafted, Restored }`; `KitFile` (`Id`, `BrandId`, `Kind`, `FileName`, `ContentType`, `Size`, `StorageKey`, `UploadedBy`, `UploadedAt`, `DeletedAt`); `enum KitFileKind { Guideline, Logo }`.

- [ ] **Step 1: Write the failing table tests**

`core/tests/Kairo.IntegrationTests/Persistence/BrandKitTableTests.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Kairo.IntegrationTests.Persistence;

public sealed class BrandKitTableTests(SqlServer sqlServer)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("KitVersions")]
    [InlineData("KitFiles")]
    public async Task The_table_is_covered_by_the_brand_kit_security_policy(string table)
    {
        var predicates = await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id = OBJECT_ID(N'brand_kit.{table}')",
            Guid.Empty);

        Assert.Equal(3, predicates);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Approved")]
    public async Task A_workspace_has_at_most_one_version_in_this_status(string status)
    {
        var brandId = Guid.NewGuid();
        await InsertVersionAsync(brandId, 1, status);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertVersionAsync(brandId, 2, status));

        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task Superseded_and_discarded_versions_can_pile_up()
    {
        var brandId = Guid.NewGuid();
        await InsertVersionAsync(brandId, 1, "Superseded");
        await InsertVersionAsync(brandId, 2, "Superseded");
        await InsertVersionAsync(brandId, 3, "Discarded");
        await InsertVersionAsync(brandId, 4, "Discarded");

        var count = await ScalarAsync(
            """
            EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
            SELECT COUNT(*) FROM brand_kit.KitVersions WHERE BrandId = @brand;
            """,
            brandId);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Version_numbers_are_unique_in_a_workspace()
    {
        var brandId = Guid.NewGuid();
        await InsertVersionAsync(brandId, 1, "Superseded");

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => InsertVersionAsync(brandId, 1, "Discarded"));

        Assert.Equal(2601, duplicate.Number);
    }

    private Task InsertVersionAsync(Guid brandId, int number, string status) => ScalarAsync(
        $$"""
        EXEC sys.sp_set_session_context @key = N'kairo.all_brands', @value = 1;
        INSERT INTO brand_kit.KitVersions (Id, BrandId, Number, Status, Content, Revision, Origin, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
        VALUES (NEWID(), @brand, {{number}}, N'{{status}}', N'{}', 1, N'Manual', NEWID(), SYSDATETIMEOFFSET(), NEWID(), SYSDATETIMEOFFSET());
        SELECT 0;
        """,
        brandId);

    private async Task<int> ScalarAsync(string sql, Guid brandId)
    {
        await using var connection = new SqlConnection(sqlServer.KairoConnectionString);
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@brand", brandId);
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.BrandKitTableTests"`
Expected: FAIL with `Invalid object name 'brand_kit.KitVersions'` and policy counts of 0.

- [ ] **Step 3: Write the entities**

Replace `core/src/Modules/Kairo.Modules.BrandKit/Kairo.Modules.BrandKit.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="Kairo.IntegrationTests" />
  </ItemGroup>

</Project>
```

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersion.cs`:

```csharp
namespace Kairo.Modules.BrandKit.Kit;

internal enum VersionStatus
{
    Draft,
    Approved,
    Superseded,
    Discarded,
}

internal enum VersionOrigin
{
    Manual,
    Drafted,
    Restored,
}

internal sealed class KitVersion : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public int Number { get; set; }

    public VersionStatus Status { get; set; }

    public string Content { get; set; } = "{}";

    public int Revision { get; set; }

    public VersionOrigin Origin { get; set; }

    public int? RestoredFrom { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
}
```

`core/src/Modules/Kairo.Modules.BrandKit/Files/KitFile.cs`:

```csharp
namespace Kairo.Modules.BrandKit.Files;

internal enum KitFileKind
{
    Guideline,
    Logo,
}

internal sealed class KitFile : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public KitFileKind Kind { get; set; }

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long Size { get; set; }

    public string StorageKey { get; set; } = "";

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
```

- [ ] **Step 4: Write the context, its factory, and register it**

`core/src/Modules/Kairo.Modules.BrandKit/Data/BrandKitDbContext.cs`:

```csharp
using Kairo.Modules.BrandKit.Files;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Data;

internal sealed class BrandKitDbContext(DbContextOptions<BrandKitDbContext> options, KairoDatabase database)
    : ModuleDbContext(options, database)
{
    public const string SchemaName = "brand_kit";

    public DbSet<KitVersion> Versions => Set<KitVersion>();

    public DbSet<KitFile> Files => Set<KitFile>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KitVersion>(version =>
        {
            version.ToTable("KitVersions");
            version.HasIndex(v => new { v.BrandId, v.Number }).IsUnique();
            version.HasIndex(v => v.BrandId, "IX_KitVersions_OneDraft").IsUnique().HasFilter("[Status] = N'Draft'");
            version.HasIndex(v => v.BrandId, "IX_KitVersions_OneApproved").IsUnique().HasFilter("[Status] = N'Approved'");
            version.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
            version.Property(v => v.Origin).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<KitFile>(file =>
        {
            file.ToTable("KitFiles");
            file.HasIndex(f => new { f.BrandId, f.UploadedAt });
            file.Property(f => f.Kind).HasConversion<string>().HasMaxLength(20);
            file.Property(f => f.FileName).HasMaxLength(200);
            file.Property(f => f.ContentType).HasMaxLength(100);
            file.Property(f => f.StorageKey).HasMaxLength(200);
        });
    }
}
```

`core/src/Modules/Kairo.Modules.BrandKit/Data/BrandKitDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace Kairo.Modules.BrandKit.Data;

internal sealed class BrandKitDbContextFactory : IDesignTimeDbContextFactory<BrandKitDbContext>
{
    public BrandKitDbContext CreateDbContext(string[] args) =>
        KairoDbContextOptions.CreateForDesignTime<BrandKitDbContext>(
            BrandKitDbContext.SchemaName,
            (options, database) => new BrandKitDbContext(options, database));
}
```

Replace `core/src/Modules/Kairo.Modules.BrandKit/BrandKitModule.cs` with:

```csharp
using Kairo.Modules.BrandKit.Data;

namespace Kairo.Modules.BrandKit;

public sealed class BrandKitModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BrandKitDbContext>((provider, options) =>
            options.UseKairoDatabase(provider, BrandKitDbContext.SchemaName));
    }

    public void MapApi(IEndpointRouteBuilder api)
    {
    }

    public void MapJobs(IEndpointRouteBuilder jobs)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BrandKitDbContext>().Database.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 5: Generate and tidy the migration**

```bash
dotnet tool restore
dotnet ef migrations add Versions --project core/src/Modules/Kairo.Modules.BrandKit --startup-project core/src/Kairo.Migrator --context BrandKitDbContext --output-dir Data/Migrations
bash core/scripts/tidy-migrations.sh core/src/Modules/Kairo.Modules.BrandKit/Data/Migrations/*.cs
```

In `<timestamp>_Versions.cs`:
- Add these as the last lines of `Up`, after the generated `CreateIndex` calls:

  ```csharp
              migrationBuilder.CreateBrandAccessPredicate("brand_kit");
              migrationBuilder.AddBrandSeparation("brand_kit", "KitVersions");
              migrationBuilder.AddBrandSeparation("brand_kit", "KitFiles");
  ```

- Add this as the first line of `Down`:

  ```csharp
              migrationBuilder.DropBrandSeparation("brand_kit");
  ```

- Check that the generated `CreateIndex` for `IX_KitVersions_OneDraft` has `filter: "[Status] = N'Draft'"` and `unique: true`, and the same for `IX_KitVersions_OneApproved`.

Then check that `grep -rn "auto-generated\|public partial class" core/src/Modules/Kairo.Modules.BrandKit/Data/Migrations` prints nothing.

- [ ] **Step 6: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.BrandKitTableTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Persistence.SecurityPolicyCoverageTests"
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.MigratorTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add core/src/Modules/Kairo.Modules.BrandKit core/tests/Kairo.IntegrationTests/Persistence/BrandKitTableTests.cs
git commit -m "Store Brand Kit versions and files in the brand_kit schema"
```

---

### Task 3: Brand Kit content and its rules

**Files:**
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitContent.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitJson.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitContentRules.cs`
- Test: `core/tests/Kairo.IntegrationTests/BrandKit/KitContentRulesTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: records `KitContent`, `KitVoice`, `KitColour`, `KitLogo`, `KitMessage`, `KitCategory`, `KitRules`, `KitDisclaimer`, `KitCompetitor`, `KitSource(SourceKind Kind, string Ref)`; enums `ColourRole`, `CompetitorPolicy`, `SourceKind`; `KitContent.FileIds() -> List<Guid>` (logo first), `KitContent.WithoutFile(Guid) -> KitContent`; `KitJson.Write(KitContent) -> string`, `KitJson.Read(string) -> KitContent`, `KitJson.Element(string) -> JsonElement`, `KitJson.Name<T>(T) -> string`; `KitContentRules.Check(KitContent?) -> KitCheck` with `KitCheck.Content`, `KitCheck.Errors` (`Dictionary<string, string[]>`) and `KitCheck.IsValid`.

- [ ] **Step 1: Write the failing tests**

`core/tests/Kairo.IntegrationTests/BrandKit/KitContentRulesTests.cs`:

```csharp
using System.Text.Json;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class KitContentRulesTests
{
    [Fact]
    public void An_empty_kit_is_valid()
    {
        var check = KitContentRules.Check(null);

        Assert.True(check.IsValid);
        Assert.Equal("", check.Content.Voice.Summary);
        Assert.Empty(check.Content.Colours);
        Assert.Null(check.Content.Logo);
        Assert.Empty(check.Content.Rules.BannedWords);
    }

    [Fact]
    public void Text_is_trimmed_and_blank_entries_are_dropped()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Voice = new KitVoice { Summary = "  Warm.  ", Traits = [" bold ", "", "   "] },
            KeyMessages = [new KitMessage { Text = "  Free returns  " }, new KitMessage { Text = " " }],
            Rules = new KitRules { BannedWords = ["cheap", " "], Competitors = [new KitCompetitor { Name = " " }] },
        });

        Assert.True(check.IsValid);
        Assert.Equal("Warm.", check.Content.Voice.Summary);
        Assert.Equal(new[] { "bold" }, check.Content.Voice.Traits);
        Assert.Equal("Free returns", Assert.Single(check.Content.KeyMessages).Text);
        Assert.Equal(new[] { "cheap" }, check.Content.Rules.BannedWords);
        Assert.Empty(check.Content.Rules.Competitors);
    }

    [Fact]
    public void Null_lists_and_items_are_treated_as_empty()
    {
        var content = JsonSerializer.Deserialize<KitContent>(
            """{ "voice": { "summary": null, "traits": null }, "colours": null, "keyMessages": [null], "rules": null, "guidelineFileIds": null }""",
            KitJson.Options)!;

        var check = KitContentRules.Check(content);

        Assert.True(check.IsValid);
        Assert.Equal("", check.Content.Voice.Summary);
        Assert.Empty(check.Content.Voice.Traits);
        Assert.Empty(check.Content.Colours);
        Assert.Empty(check.Content.KeyMessages);
        Assert.Empty(check.Content.Rules.Tone);
        Assert.Empty(check.Content.GuidelineFileIds);
    }

    [Fact]
    public void Text_over_its_limit_is_reported_at_its_place()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Voice = new KitVoice { Summary = new string('a', 501), Dos = ["ok", new string('b', 201)] },
        });

        Assert.Equal(new[] { "Use at most 500 characters." }, check.Errors["content.voice.summary"]);
        Assert.Equal(new[] { "Use at most 200 characters." }, check.Errors["content.voice.dos[1]"]);
    }

    [Fact]
    public void Too_many_entries_are_reported_on_the_list()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Voice = new KitVoice { Traits = Enumerable.Range(1, 11).Select(i => $"trait {i}").ToList() },
        });

        Assert.Equal(new[] { "Add at most 10." }, check.Errors["content.voice.traits"]);
    }

    [Fact]
    public void Colours_are_upper_cased_and_duplicates_dropped()
    {
        var check = KitContentRules.Check(new KitContent
        {
            Colours =
            [
                new KitColour { Hex = "#e4572e", Name = "Ember", Role = ColourRole.Primary },
                new KitColour { Hex = "#E4572E", Name = "Ember again", Role = ColourRole.Accent },
                new KitColour { Hex = "", Name = "" },
            ],
        });

        Assert.True(check.IsValid);
        var colour = Assert.Single(check.Content.Colours);
        Assert.Equal(("#E4572E", "Ember", ColourRole.Primary), (colour.Hex, colour.Name, colour.Role));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("E4572E")]
    public void A_colour_that_is_not_a_hex_code_is_reported(string hex)
    {
        var check = KitContentRules.Check(new KitContent
        {
            Colours = [new KitColour { Hex = "#000000", Name = "Ink" }, new KitColour { Hex = hex, Name = "Bad" }],
        });

        Assert.Equal(new[] { "Use a colour code like #E4572E." }, check.Errors["content.colours[1].hex"]);
    }

    [Fact]
    public void A_logo_with_neither_a_file_nor_an_address_is_dropped()
    {
        var check = KitContentRules.Check(new KitContent { Logo = new KitLogo { SourceUrl = "  " } });

        Assert.True(check.IsValid);
        Assert.Null(check.Content.Logo);
    }

    [Fact]
    public void Sources_must_be_web_addresses_or_ad_ids()
    {
        var check = KitContentRules.Check(new KitContent
        {
            KeyMessages =
            [
                new KitMessage
                {
                    Text = "Free returns",
                    Sources = [new KitSource(SourceKind.Page, "https://acme.example/returns"), new KitSource(SourceKind.Page, "javascript:alert(1)"), new KitSource(SourceKind.Ad, "12x")],
                },
            ],
            Logo = new KitLogo { SourceUrl = "ftp://acme.example/logo.png" },
        });

        Assert.Equal(new[] { "Use a full web address starting with https://." }, check.Errors["content.keyMessages[0].sources[1].ref"]);
        Assert.Equal(new[] { "Use the Google Ads ad ID, in digits." }, check.Errors["content.keyMessages[0].sources[2].ref"]);
        Assert.Equal(new[] { "Use a full web address starting with https://." }, check.Errors["content.logo.sourceUrl"]);
    }

    [Fact]
    public void Guideline_files_are_listed_once()
    {
        var file = Guid.NewGuid();

        var check = KitContentRules.Check(new KitContent { GuidelineFileIds = [file, file, Guid.Empty] });

        Assert.Equal(new[] { file }, check.Content.GuidelineFileIds);
    }

    [Fact]
    public void File_ids_put_the_logo_first_and_removing_a_file_clears_it_everywhere()
    {
        var (logo, guide) = (Guid.NewGuid(), Guid.NewGuid());
        var content = new KitContent
        {
            Logo = new KitLogo { FileId = logo, SourceUrl = "https://acme.example/logo.svg" },
            GuidelineFileIds = [guide],
        };

        Assert.Equal(new[] { logo, guide }, content.FileIds());
        var withoutLogo = content.WithoutFile(logo);
        Assert.Null(withoutLogo.Logo!.FileId);
        Assert.Equal("https://acme.example/logo.svg", withoutLogo.Logo.SourceUrl);
        Assert.Empty(content.WithoutFile(guide).GuidelineFileIds);
        Assert.Null(new KitContent { Logo = new KitLogo { FileId = logo } }.WithoutFile(logo).Logo);
    }

    [Fact]
    public void Stored_json_uses_camel_case_names_and_enum_values()
    {
        var json = KitJson.Write(new KitContent
        {
            Colours = [new KitColour { Hex = "#E4572E", Name = "Ember", Role = ColourRole.Background }],
            Rules = new KitRules { Competitors = [new KitCompetitor { Name = "Rival", Policy = CompetitorPolicy.Neutral }] },
        });

        var element = KitJson.Element(json);
        Assert.Equal("background", element.GetProperty("colours")[0].GetProperty("role").GetString());
        Assert.Equal("neutral", element.GetProperty("rules").GetProperty("competitors")[0].GetProperty("policy").GetString());
        Assert.Equal(ColourRole.Background, KitJson.Read(json).Colours[0].Role);
        Assert.Equal("superseded", KitJson.Name(VersionStatus.Superseded));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.KitContentRulesTests"`
Expected: build FAILS with `The type or namespace name 'KitContent' could not be found`.

- [ ] **Step 3: Write the content records**

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitContent.cs`:

```csharp
namespace Kairo.Modules.BrandKit.Kit;

internal enum ColourRole
{
    Primary,
    Secondary,
    Accent,
    Background,
    Text,
}

internal enum CompetitorPolicy
{
    Never,
    Neutral,
}

internal enum SourceKind
{
    Page,
    Ad,
}

internal sealed record KitSource(SourceKind Kind, string Ref);

internal sealed record KitVoice
{
    public string Summary { get; init; } = "";

    public IReadOnlyList<string> Traits { get; init; } = [];

    public IReadOnlyList<string> Dos { get; init; } = [];

    public IReadOnlyList<string> Donts { get; init; } = [];

    public IReadOnlyList<string> Examples { get; init; } = [];

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitColour
{
    public string Hex { get; init; } = "";

    public string Name { get; init; } = "";

    public ColourRole Role { get; init; }

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitLogo
{
    public Guid? FileId { get; init; }

    public string? SourceUrl { get; init; }
}

internal sealed record KitMessage
{
    public string Text { get; init; } = "";

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitCategory
{
    public string Name { get; init; } = "";

    public IReadOnlyList<KitSource> Sources { get; init; } = [];
}

internal sealed record KitDisclaimer
{
    public string Text { get; init; } = "";

    public string When { get; init; } = "";
}

internal sealed record KitCompetitor
{
    public string Name { get; init; } = "";

    public CompetitorPolicy Policy { get; init; }
}

internal sealed record KitRules
{
    public IReadOnlyList<string> BannedWords { get; init; } = [];

    public IReadOnlyList<KitDisclaimer> RequiredDisclaimers { get; init; } = [];

    public IReadOnlyList<string> Tone { get; init; } = [];

    public IReadOnlyList<KitCompetitor> Competitors { get; init; } = [];
}

internal sealed record KitContent
{
    public KitVoice Voice { get; init; } = new();

    public IReadOnlyList<KitColour> Colours { get; init; } = [];

    public KitLogo? Logo { get; init; }

    public IReadOnlyList<KitMessage> KeyMessages { get; init; } = [];

    public IReadOnlyList<KitCategory> ProductCategories { get; init; } = [];

    public KitRules Rules { get; init; } = new();

    public IReadOnlyList<Guid> GuidelineFileIds { get; init; } = [];

    public List<Guid> FileIds()
    {
        var ids = new List<Guid>();
        if (Logo?.FileId is { } logo)
        {
            ids.Add(logo);
        }

        foreach (var id in GuidelineFileIds)
        {
            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public KitContent WithoutFile(Guid fileId) => this with
    {
        Logo = Logo is { FileId: var id } logo && id == fileId
            ? logo.SourceUrl is null ? null : logo with { FileId = null }
            : Logo,
        GuidelineFileIds = GuidelineFileIds.Where(id => id != fileId).ToList(),
    };
}
```

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kairo.Modules.BrandKit.Kit;

internal static class KitJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(KitContent content) => JsonSerializer.Serialize(content, Options);

    public static KitContent Read(string json) => JsonSerializer.Deserialize<KitContent>(json, Options) ?? new KitContent();

    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string Name<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
```

- [ ] **Step 4: Write the rules**

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitContentRules.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed record KitCheck(KitContent Content, Dictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal static partial class KitContentRules
{
    public const int SummaryLength = 500;
    public const int ShortText = 200;
    public const int Traits = 10;
    public const int VoiceLines = 20;
    public const int Examples = 10;
    public const int Colours = 12;
    public const int ColourNameLength = 50;
    public const int KeyMessages = 20;
    public const int Categories = 50;
    public const int NameLength = 100;
    public const int BannedWords = 200;
    public const int BannedWordLength = 50;
    public const int Disclaimers = 20;
    public const int DisclaimerLength = 500;
    public const int ToneRules = 20;
    public const int Competitors = 50;
    public const int GuidelineFiles = 20;
    public const int SourcesPerItem = 10;
    public const int UrlLength = 2000;

    public const string BadColour = "Use a colour code like #E4572E.";
    public const string NotWebAddress = "Use a full web address starting with https://.";
    public const string NotAdId = "Use the Google Ads ad ID, in digits.";

    public static KitCheck Check(KitContent? input)
    {
        var errors = new Dictionary<string, string[]>();
        var content = input ?? new KitContent();
        var voice = content.Voice ?? new KitVoice();
        var rules = content.Rules ?? new KitRules();

        var normalized = new KitContent
        {
            Voice = new KitVoice
            {
                Summary = Text(voice.Summary, "content.voice.summary", SummaryLength, errors),
                Traits = Texts(voice.Traits, "content.voice.traits", Traits, ShortText, errors),
                Dos = Texts(voice.Dos, "content.voice.dos", VoiceLines, ShortText, errors),
                Donts = Texts(voice.Donts, "content.voice.donts", VoiceLines, ShortText, errors),
                Examples = Texts(voice.Examples, "content.voice.examples", Examples, ShortText, errors),
                Sources = Sources(voice.Sources, "content.voice.sources", errors),
            },
            Colours = Items(content.Colours, "content.colours", Colours, errors, (colour, path) =>
            {
                var hex = (colour.Hex ?? "").Trim();
                var name = Text(colour.Name, $"{path}.name", ColourNameLength, errors);
                if (hex.Length == 0 && name.Length == 0)
                {
                    return null;
                }

                if (!Hex().IsMatch(hex))
                {
                    errors[$"{path}.hex"] = [BadColour];
                }

                return colour with { Hex = hex.ToUpperInvariant(), Name = name, Sources = Sources(colour.Sources, $"{path}.sources", errors) };
            }).DistinctBy(colour => colour.Hex).ToList(),
            Logo = LogoOf(content.Logo, errors),
            KeyMessages = Items(content.KeyMessages, "content.keyMessages", KeyMessages, errors, (message, path) =>
            {
                var text = Text(message.Text, $"{path}.text", ShortText, errors);
                return text.Length == 0 ? null : message with { Text = text, Sources = Sources(message.Sources, $"{path}.sources", errors) };
            }),
            ProductCategories = Items(content.ProductCategories, "content.productCategories", Categories, errors, (category, path) =>
            {
                var name = Text(category.Name, $"{path}.name", NameLength, errors);
                return name.Length == 0 ? null : category with { Name = name, Sources = Sources(category.Sources, $"{path}.sources", errors) };
            }),
            Rules = new KitRules
            {
                BannedWords = Texts(rules.BannedWords, "content.rules.bannedWords", BannedWords, BannedWordLength, errors),
                RequiredDisclaimers = Items(rules.RequiredDisclaimers, "content.rules.requiredDisclaimers", Disclaimers, errors, (disclaimer, path) =>
                {
                    var text = Text(disclaimer.Text, $"{path}.text", DisclaimerLength, errors);
                    var when = Text(disclaimer.When, $"{path}.when", ShortText, errors);
                    return text.Length == 0 ? null : new KitDisclaimer { Text = text, When = when };
                }),
                Tone = Texts(rules.Tone, "content.rules.tone", ToneRules, ShortText, errors),
                Competitors = Items(rules.Competitors, "content.rules.competitors", Competitors, errors, (competitor, path) =>
                {
                    var name = Text(competitor.Name, $"{path}.name", NameLength, errors);
                    return name.Length == 0 ? null : competitor with { Name = name };
                }),
            },
            GuidelineFileIds = GuidelineFileIdsOf(content.GuidelineFileIds, errors),
        };

        return new KitCheck(normalized, errors);
    }

    public static bool IsWebAddress(string value) =>
        value.Length <= UrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string Text(string? value, string path, int max, Dictionary<string, string[]> errors)
    {
        var text = (value ?? "").Trim();
        if (text.Length > max)
        {
            errors[path] = [$"Use at most {max} characters."];
        }

        return text;
    }

    private static List<string> Texts(IReadOnlyList<string?>? values, string path, int max, int length, Dictionary<string, string[]> errors)
    {
        var kept = new List<string>();
        var list = values ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var text = Text(list[i], $"{path}[{i}]", length, errors);
            if (text.Length > 0)
            {
                kept.Add(text);
            }
        }

        if (kept.Count > max)
        {
            errors[path] = [$"Add at most {max}."];
        }

        return kept;
    }

    private static List<T> Items<T>(
        IReadOnlyList<T?>? values,
        string path,
        int max,
        Dictionary<string, string[]> errors,
        Func<T, string, T?> check)
        where T : class
    {
        var kept = new List<T>();
        var list = values ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is { } item && check(item, $"{path}[{i}]") is { } normalized)
            {
                kept.Add(normalized);
            }
        }

        if (kept.Count > max)
        {
            errors[path] = [$"Add at most {max}."];
        }

        return kept;
    }

    private static List<KitSource> Sources(IReadOnlyList<KitSource?>? values, string path, Dictionary<string, string[]> errors)
    {
        var kept = new List<KitSource>();
        var list = values ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not { } source)
            {
                continue;
            }

            var reference = (source.Ref ?? "").Trim();
            if (source.Kind == SourceKind.Page && !IsWebAddress(reference))
            {
                errors[$"{path}[{i}].ref"] = [NotWebAddress];
            }
            else if (source.Kind == SourceKind.Ad && !AdId().IsMatch(reference))
            {
                errors[$"{path}[{i}].ref"] = [NotAdId];
            }
            else
            {
                kept.Add(source with { Ref = reference });
            }
        }

        if (kept.Count > SourcesPerItem)
        {
            errors[path] = [$"Add at most {SourcesPerItem}."];
        }

        return kept;
    }

    private static KitLogo? LogoOf(KitLogo? logo, Dictionary<string, string[]> errors)
    {
        if (logo is null)
        {
            return null;
        }

        var url = (logo.SourceUrl ?? "").Trim();
        if (logo.FileId is null && url.Length == 0)
        {
            return null;
        }

        if (url.Length > 0 && !IsWebAddress(url))
        {
            errors["content.logo.sourceUrl"] = [NotWebAddress];
        }

        return new KitLogo { FileId = logo.FileId, SourceUrl = url.Length == 0 ? null : url };
    }

    private static List<Guid> GuidelineFileIdsOf(IReadOnlyList<Guid>? values, Dictionary<string, string[]> errors)
    {
        var ids = (values ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > GuidelineFiles)
        {
            errors["content.guidelineFileIds"] = [$"Add at most {GuidelineFiles}."];
        }

        return ids;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();

    [GeneratedRegex("^[0-9]{1,20}$")]
    private static partial Regex AdId();
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.KitContentRulesTests"`
Expected: PASS, 14 tests.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.BrandKit/Kit/KitContent.cs core/src/Modules/Kairo.Modules.BrandKit/Kit/KitJson.cs core/src/Modules/Kairo.Modules.BrandKit/Kit/KitContentRules.cs core/tests/Kairo.IntegrationTests/BrandKit/KitContentRulesTests.cs
git commit -m "Describe Brand Kit content and the rules that clean and check it"
```

---

### Task 4: Draft, approve and discard

**Files:**
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Files/KitFileView.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitViews.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersions.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/BrandKitLiveEvents.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitEndpoints.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/BrandKitModule.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/BrandKit/BrandKits.cs`
- Test: `core/tests/Kairo.IntegrationTests/BrandKit/BrandKitDraftTests.cs`

**Interfaces:**
- Consumes: Task 2's entities and context, Task 3's content, `KitJson` and `KitContentRules`.
- Produces: `KitVersions` (scoped) with `DraftAsync`, `ApprovedAsync`, `FindAsync(int number)`, `LockAsync`, `CreateDraftAsync(KitContent, VersionOrigin, int? restoredFrom)`, `UpdateDraftAsync(KitVersion, KitContent)`, `RestoreIntoAsync(KitVersion, KitContent, int from)`, `ApproveAsync(KitVersion)`, `DiscardAsync(KitVersion)`, `ViewAsync(KitVersion)`, static `AuditOf(KitVersion)`, `AuditTarget`, `Number(KitVersion)`; records `KitVersionView`, `BrandKitView`, `SaveDraftRequest`, `RevisionRequest`, `KitVersionSummary`, `KitFileView`; `BrandKitLiveEvents.Changed(KitVersion)`, `BrandKitLiveEvents.FilesChanged(Guid)`; `KitSetup.AddBrandKit()` and `KitSetup.MapBrandKit()`; later tasks add their routes and registrations to these two methods.

- [ ] **Step 1: Write the test helpers**

`core/tests/Kairo.IntegrationTests/BrandKit/BrandKits.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.BrandKit;

internal static class BrandKits
{
    public static readonly byte[] Pdf = "%PDF-1.7\n%Acme brand guidelines\n"u8.ToArray();

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Kit(Guid brandId) => $"/api/brands/{brandId}/brand-kit";

    public static string Draft(Guid brandId) => $"{Kit(brandId)}/draft";

    public static object Content(string summary = "Friendly and practical.") => new
    {
        voice = new { summary, traits = new[] { "warm" } },
        colours = new[] { new { hex = "#e4572e", name = "Ember", role = "primary" } },
        rules = new { bannedWords = new[] { "cheap" } },
    };

    public static Task<HttpResponseMessage> PutDraftAsync(HttpClient browser, Guid brandId, object content, int revision) =>
        browser.PutAsJsonAsync(Draft(brandId), new { content, revision }, Token);

    public static async Task<JsonElement> SaveAsync(HttpClient admin, Guid brandId, object content, int revision)
    {
        var response = await PutDraftAsync(admin, brandId, content, revision);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    public static async Task<JsonElement> ApproveAsync(HttpClient admin, Guid brandId, int revision)
    {
        var response = await admin.PostAsJsonAsync($"{Draft(brandId)}/approve", new { revision }, Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    public static async Task<JsonElement> ApproveNewAsync(HttpClient admin, Guid brandId, object content)
    {
        var draft = await SaveAsync(admin, brandId, content, await DraftRevisionAsync(admin, brandId));
        return await ApproveAsync(admin, brandId, draft.GetProperty("revision").GetInt32());
    }

    public static async Task<JsonElement> GetAsync(HttpClient browser, Guid brandId) =>
        await browser.GetFromJsonAsync<JsonElement>(Kit(brandId), Token);

    public static async Task<int> DraftRevisionAsync(HttpClient browser, Guid brandId)
    {
        var draft = (await GetAsync(browser, brandId)).GetProperty("draft");
        return draft.ValueKind == JsonValueKind.Null ? 0 : draft.GetProperty("revision").GetInt32();
    }

    public static string Summary(JsonElement version) =>
        version.GetProperty("content").GetProperty("voice").GetProperty("summary").GetString()!;

    public static MultipartFormDataContent File(byte[] bytes, string fileName, string? kind = null)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (kind is not null)
        {
            form.Add(new StringContent(kind), "kind");
        }

        return form;
    }

    public static async Task<Guid> UploadAsync(HttpClient admin, Guid brandId, byte[] bytes, string fileName, string? kind = null)
    {
        var response = await admin.PostAsync($"{Kit(brandId)}/files", File(bytes, fileName, kind), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
    }

    public static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString()!;

    public static async Task<string[]> ErrorsAsync(HttpResponseMessage response, string field)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()!).ToArray();
    }
}
```

- [ ] **Step 2: Write the failing endpoint tests**

`core/tests/Kairo.IntegrationTests/BrandKit/BrandKitDraftTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitDraftTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string StaleDraft = "Someone else changed the draft. Reload it and try again.";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_has_no_brand_kit()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var kit = await BrandKits.GetAsync(admin.Browser, brandId);

        Assert.Equal(JsonValueKind.Null, kit.GetProperty("approved").ValueKind);
        Assert.Equal(JsonValueKind.Null, kit.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task Saving_creates_the_first_draft_with_cleaned_content()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var draft = await BrandKits.SaveAsync(
            admin.Browser,
            brandId,
            new
            {
                voice = new { summary = "  Warm.  ", traits = new[] { " bold ", "", "  " } },
                colours = new[] { new { hex = "#e4572e", name = "Ember", role = "primary" } },
            },
            0);

        Assert.Equal(1, draft.GetProperty("number").GetInt32());
        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal("manual", draft.GetProperty("origin").GetString());
        Assert.Equal(1, draft.GetProperty("revision").GetInt32());
        Assert.Equal(admin.UserId, draft.GetProperty("createdBy").GetGuid());
        Assert.Equal("Warm.", BrandKits.Summary(draft));
        var content = draft.GetProperty("content");
        Assert.Equal(new[] { "bold" }, content.GetProperty("voice").GetProperty("traits").EnumerateArray().Select(trait => trait.GetString()).ToArray());
        Assert.Equal("#E4572E", content.GetProperty("colours")[0].GetProperty("hex").GetString());
        Assert.Equal("primary", content.GetProperty("colours")[0].GetProperty("role").GetString());
        Assert.Equal(1, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Null_lists_are_saved_as_empty()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var draft = await BrandKits.SaveAsync(
            admin.Browser,
            brandId,
            new { voice = new { summary = "Warm.", traits = (string[]?)null }, colours = (object[]?)null, rules = (object?)null },
            0);

        Assert.Empty(draft.GetProperty("content").GetProperty("voice").GetProperty("traits").EnumerateArray());
        Assert.Empty(draft.GetProperty("content").GetProperty("colours").EnumerateArray());
    }

    [Fact]
    public async Task Each_save_raises_the_revision_and_a_stale_one_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);
        var second = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 1);

        var stale = await BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content("third"), 1);

        Assert.Equal(2, second.GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(StaleDraft, await BrandKits.ProblemTitleAsync(stale));
        Assert.Equal("second", BrandKits.Summary((await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft")));
    }

    [Fact]
    public async Task A_first_draft_must_start_from_revision_0()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content(), 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Two_admins_saving_the_same_revision_at_once_leave_one_winner()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var other = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "admin");
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);

        var responses = await Task.WhenAll(
            BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content("from one"), 1),
            BrandKits.PutDraftAsync(other.Browser, brandId, BrandKits.Content("from two"), 1));

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(2, await BrandKits.DraftRevisionAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Invalid_content_lists_its_problems_by_field_and_saves_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await BrandKits.PutDraftAsync(
            admin.Browser,
            brandId,
            new { colours = new[] { new { hex = "red", name = "Red", role = "primary" } } },
            0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { "Use a colour code like #E4572E." }, await BrandKits.ErrorsAsync(response, "content.colours[0].hex"));
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task Approving_makes_the_draft_the_approved_version()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);

        var approved = await BrandKits.ApproveAsync(admin.Browser, brandId, 1);

        Assert.Equal("approved", approved.GetProperty("status").GetString());
        Assert.Equal(admin.UserId, approved.GetProperty("approvedBy").GetGuid());
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Equal(1, kit.GetProperty("approved").GetProperty("number").GetInt32());
        Assert.Equal(JsonValueKind.Null, kit.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task A_second_approval_supersedes_the_first()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));

        var draft = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);
        var kitWithDraft = await BrandKits.GetAsync(admin.Browser, brandId);
        await BrandKits.ApproveAsync(admin.Browser, brandId, draft.GetProperty("revision").GetInt32());

        Assert.Equal(2, draft.GetProperty("number").GetInt32());
        Assert.Equal("first", BrandKits.Summary(kitWithDraft.GetProperty("approved")));
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Equal(2, kit.GetProperty("approved").GetProperty("number").GetInt32());
        Assert.Equal("second", BrandKits.Summary(kit.GetProperty("approved")));
    }

    [Fact]
    public async Task Approving_needs_a_draft_at_the_current_revision()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var noDraft = await admin.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 0 }, Token);
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        var stale = await admin.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 9 }, Token);

        Assert.Equal(HttpStatusCode.NotFound, noDraft.StatusCode);
        Assert.Equal("There's no draft.", await BrandKits.ProblemTitleAsync(noDraft));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("approved").ValueKind);
    }

    [Fact]
    public async Task Discarding_removes_the_draft_and_its_number_is_not_reused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        var discarded = await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token);
        var again = await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token);
        var next = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(2, next.GetProperty("number").GetInt32());
    }

    [Theory]
    [InlineData("marketer")]
    [InlineData("approver")]
    public async Task Only_admins_can_change_the_brand_kit(string role)
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var save = await BrandKits.PutDraftAsync(member.Browser, brandId, BrandKits.Content(), 1);
        var approve = await member.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 1 }, Token);
        var discard = await member.Browser.DeleteAsync(BrandKits.Draft(brandId), Token);
        var read = await member.Browser.GetAsync(BrandKits.Kit(brandId), Token);

        Assert.Equal(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK },
            new[] { save.StatusCode, approve.StatusCode, discard.StatusCode, read.StatusCode });
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_brand_kit()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(BrandKits.Kit(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Changes_are_audited_and_published_live()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);
        (await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token)).EnsureSuccessStatusCode();

        var actions = await Workspaces.AuditActionsAsync(admin.Browser, brandId);
        var events = (await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId))
            .Where(row => row.Type == "brand_kit.changed")
            .Select(row => JsonDocument.Parse(row.Payload).RootElement.GetProperty("status").GetString())
            .ToArray();

        Assert.Equal(2, actions.Count(action => action == "brand_kit.draft_saved"));
        Assert.Contains("brand_kit.approved", actions);
        Assert.Contains("brand_kit.draft_discarded", actions);
        Assert.Equal(new[] { "draft", "approved", "draft", "discarded" }, events);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitDraftTests"`
Expected: FAIL. Requests to `/api/brands/{id}/brand-kit` return `404`.

- [ ] **Step 4: Write the views**

`core/src/Modules/Kairo.Modules.BrandKit/Files/KitFileView.cs`:

```csharp
namespace Kairo.Modules.BrandKit.Files;

internal sealed record KitFileView(
    Guid Id,
    KitFileKind Kind,
    string FileName,
    string ContentType,
    long Size,
    bool Deleted,
    Guid UploadedBy,
    DateTimeOffset UploadedAt)
{
    public static KitFileView Of(KitFile file) => new(
        file.Id, file.Kind, file.FileName, file.ContentType, file.Size, file.DeletedAt is not null, file.UploadedBy, file.UploadedAt);
}
```

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitViews.cs`:

```csharp
using Kairo.Modules.BrandKit.Files;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed record SaveDraftRequest(KitContent? Content, int Revision);

internal sealed record RevisionRequest(int Revision);

internal sealed record KitVersionView(
    int Number,
    VersionStatus Status,
    VersionOrigin Origin,
    int? RestoredFrom,
    int Revision,
    KitContent Content,
    KitFileView[] Files,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt)
{
    public static KitVersionView Of(KitVersion version, KitContent content, KitFileView[] files) => new(
        version.Number,
        version.Status,
        version.Origin,
        version.RestoredFrom,
        version.Revision,
        content,
        files,
        version.CreatedBy,
        version.CreatedAt,
        version.UpdatedBy,
        version.UpdatedAt,
        version.ApprovedBy,
        version.ApprovedAt);
}

internal sealed record BrandKitView(KitVersionView? Approved, KitVersionView? Draft);

internal sealed record KitVersionSummary(
    int Number,
    VersionStatus Status,
    VersionOrigin Origin,
    int? RestoredFrom,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt)
{
    public static KitVersionSummary Of(KitVersion version) => new(
        version.Number,
        version.Status,
        version.Origin,
        version.RestoredFrom,
        version.CreatedBy,
        version.CreatedAt,
        version.UpdatedBy,
        version.UpdatedAt,
        version.ApprovedBy,
        version.ApprovedAt);
}
```

- [ ] **Step 5: Write the version service and live events**

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersions.cs`:

```csharp
using System.Globalization;
using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Files;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed class KitVersions(BrandKitDbContext db, KairoDatabase database, CurrentMember member, TimeProvider time)
{
    public const string AuditTarget = "brand_kit_version";
    public const string StaleDraft = "Someone else changed the draft. Reload it and try again.";
    public const string NoDraft = "There's no draft.";

    public Task<KitVersion?> DraftAsync(CancellationToken cancellationToken) =>
        db.Versions.SingleOrDefaultAsync(version => version.Status == VersionStatus.Draft, cancellationToken);

    public Task<KitVersion?> ApprovedAsync(CancellationToken cancellationToken) =>
        db.Versions.SingleOrDefaultAsync(version => version.Status == VersionStatus.Approved, cancellationToken);

    public Task<KitVersion?> FindAsync(int number, CancellationToken cancellationToken) =>
        db.Versions.SingleOrDefaultAsync(
            version => version.Number == number && version.Status != VersionStatus.Discarded,
            cancellationToken);

    public Task LockAsync(CancellationToken cancellationToken) =>
        database.LockAsync($"brand-kit:{member.BrandId:N}", cancellationToken);

    public async Task<KitVersion> CreateDraftAsync(KitContent content, VersionOrigin origin, int? restoredFrom, CancellationToken cancellationToken)
    {
        var number = (await db.Versions.MaxAsync(version => (int?)version.Number, cancellationToken) ?? 0) + 1;
        var now = time.GetUtcNow();
        var draft = new KitVersion
        {
            Id = Guid.NewGuid(),
            Number = number,
            Status = VersionStatus.Draft,
            Content = KitJson.Write(content),
            Revision = 1,
            Origin = origin,
            RestoredFrom = restoredFrom,
            CreatedBy = member.UserId,
            CreatedAt = now,
            UpdatedBy = member.UserId,
            UpdatedAt = now,
        };
        db.Versions.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return draft;
    }

    public async Task UpdateDraftAsync(KitVersion draft, KitContent content, CancellationToken cancellationToken)
    {
        draft.Content = KitJson.Write(content);
        draft.Revision++;
        draft.UpdatedBy = member.UserId;
        draft.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RestoreIntoAsync(KitVersion draft, KitContent content, int from, CancellationToken cancellationToken)
    {
        draft.Origin = VersionOrigin.Restored;
        draft.RestoredFrom = from;
        return UpdateDraftAsync(draft, content, cancellationToken);
    }

    public async Task ApproveAsync(KitVersion draft, CancellationToken cancellationToken)
    {
        if (await ApprovedAsync(cancellationToken) is { } current)
        {
            current.Status = VersionStatus.Superseded;
            await db.SaveChangesAsync(cancellationToken);
        }

        draft.Status = VersionStatus.Approved;
        draft.ApprovedBy = member.UserId;
        draft.ApprovedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscardAsync(KitVersion draft, CancellationToken cancellationToken)
    {
        draft.Status = VersionStatus.Discarded;
        draft.UpdatedBy = member.UserId;
        draft.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<KitVersionView> ViewAsync(KitVersion version, CancellationToken cancellationToken)
    {
        var content = KitJson.Read(version.Content);
        var ids = content.FileIds();
        var files = await db.Files.Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);
        return KitVersionView.Of(
            version,
            content,
            files.OrderBy(file => ids.IndexOf(file.Id)).Select(KitFileView.Of).ToArray());
    }

    public static string Number(KitVersion version) => version.Number.ToString(CultureInfo.InvariantCulture);

    public static object AuditOf(KitVersion version) => new
    {
        number = version.Number,
        status = KitJson.Name(version.Status),
        revision = version.Revision,
        content = KitJson.Element(version.Content),
    };
}
```

`core/src/Modules/Kairo.Modules.BrandKit/Kit/BrandKitLiveEvents.cs`:

```csharp
using Kairo.Contracts.Live;

namespace Kairo.Modules.BrandKit.Kit;

internal static class BrandKitLiveEvents
{
    public static LiveEvent Changed(KitVersion version) =>
        new("brand_kit.changed", new { number = version.Number, status = KitJson.Name(version.Status) }, LiveAudience.Everyone);

    public static LiveEvent FilesChanged(Guid fileId) =>
        new("brand_kit.files_changed", new { fileId }, LiveAudience.Everyone);
}
```

- [ ] **Step 6: Write the endpoints and map them**

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;

namespace Kairo.Modules.BrandKit.Kit;

internal static class KitEndpoints
{
    public static async Task<Ok<BrandKitView>> GetAsync(KitVersions versions, CancellationToken cancellationToken)
    {
        var approved = await versions.ApprovedAsync(cancellationToken);
        var draft = await versions.DraftAsync(cancellationToken);
        return TypedResults.Ok(new BrandKitView(
            approved is null ? null : await versions.ViewAsync(approved, cancellationToken),
            draft is null ? null : await versions.ViewAsync(draft, cancellationToken)));
    }

    public static async Task<Results<Ok<KitVersionView>, ValidationProblem, ProblemHttpResult>> SaveDraftAsync(
        SaveDraftRequest request,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        var check = KitContentRules.Check(request.Content);
        if (!check.IsValid)
        {
            return TypedResults.ValidationProblem(check.Errors);
        }

        await versions.LockAsync(cancellationToken);
        var draft = await versions.DraftAsync(cancellationToken);
        if ((draft?.Revision ?? 0) != request.Revision)
        {
            return Stale();
        }

        var before = draft is null ? null : KitVersions.AuditOf(draft);
        if (draft is null)
        {
            draft = await versions.CreateDraftAsync(check.Content, VersionOrigin.Manual, null, cancellationToken);
        }
        else
        {
            await versions.UpdateDraftAsync(draft, check.Content, cancellationToken);
        }

        await audit.RecordAsync(
            new AuditRecord("brand_kit.draft_saved", KitVersions.AuditTarget, KitVersions.Number(draft), before, KitVersions.AuditOf(draft)),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.Ok(await versions.ViewAsync(draft, cancellationToken));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> DiscardDraftAsync(
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await versions.DraftAsync(cancellationToken) is not { } draft)
        {
            return NoDraft();
        }

        var before = KitVersions.AuditOf(draft);
        await versions.DiscardAsync(draft, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand_kit.draft_discarded", KitVersions.AuditTarget, KitVersions.Number(draft), before),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<KitVersionView>, ProblemHttpResult>> ApproveAsync(
        RevisionRequest request,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await versions.DraftAsync(cancellationToken) is not { } draft)
        {
            return NoDraft();
        }

        if (draft.Revision != request.Revision)
        {
            return Stale();
        }

        await versions.ApproveAsync(draft, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("brand_kit.approved", KitVersions.AuditTarget, KitVersions.Number(draft), After: KitVersions.AuditOf(draft)),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.Ok(await versions.ViewAsync(draft, cancellationToken));
    }

    public static ProblemHttpResult Stale() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: KitVersions.StaleDraft);

    public static ProblemHttpResult NoDraft() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: KitVersions.NoDraft);
}
```

`core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`:

```csharp
namespace Kairo.Modules.BrandKit.Kit;

internal static class KitSetup
{
    public static void AddBrandKit(this IServiceCollection services)
    {
        services.AddScoped<KitVersions>();
    }

    public static void MapBrandKit(this IEndpointRouteBuilder api)
    {
        var kit = api.MapBrandApi().MapGroup("/brand-kit");
        kit.MapGet("", KitEndpoints.GetAsync);

        var draft = kit.MapGroup("/draft").RequireBrandRole(BrandRoles.Admin);
        draft.MapPut("", KitEndpoints.SaveDraftAsync);
        draft.MapDelete("", KitEndpoints.DiscardDraftAsync);
        draft.MapPost("/approve", KitEndpoints.ApproveAsync);
    }
}
```

In `core/src/Modules/Kairo.Modules.BrandKit/BrandKitModule.cs`, add `using Kairo.Modules.BrandKit.Kit;`, call `services.AddBrandKit();` after `AddDbContext`, and make `MapApi` call `api.MapBrandKit();`.

- [ ] **Step 7: Run the tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitDraftTests"`
Expected: PASS, 15 tests.

- [ ] **Step 8: Regenerate the contract and run the whole core**

```bash
npm --prefix web ci
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --solution core/Kairo.slnx
```

Expected: the whole suite PASSES. `git diff --stat core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts` shows the new `/brand-kit` paths.

- [ ] **Step 9: Commit**

```bash
git add core/src/Modules/Kairo.Modules.BrandKit core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts core/tests/Kairo.IntegrationTests/BrandKit/BrandKits.cs core/tests/Kairo.IntegrationTests/BrandKit/BrandKitDraftTests.cs
git commit -m "Let admins save, approve and discard the Brand Kit draft"
```

---

### Task 5: History and restore

**Files:**
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersions.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/BrandKit/BrandKitHistoryTests.cs`

**Interfaces:**
- Consumes: Task 4's `KitVersions`, `KitEndpoints.Stale()`, `BrandKitLiveEvents.Changed`, `KitVersionSummary`, `RevisionRequest`.
- Produces: `GET /versions`, `GET /versions/{number}`, `POST /versions/{number}/restore`; `KitVersions.NotRestorable` message.

- [ ] **Step 1: Write the failing tests**

`core/tests/Kairo.IntegrationTests/BrandKit/BrandKitHistoryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitHistoryTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_lists_versions_newest_first_without_discarded_drafts()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("thrown away"), 0);
        (await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token)).EnsureSuccessStatusCode();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("fourth"), 0);

        var history = await admin.Browser.GetFromJsonAsync<JsonElement>($"{BrandKits.Kit(brandId)}/versions", Token);

        Assert.Equal(
            new[] { (4, "draft"), (2, "approved"), (1, "superseded") },
            history.EnumerateArray().Select(version => (version.GetProperty("number").GetInt32(), version.GetProperty("status").GetString()!)).ToArray());
        Assert.All(history.EnumerateArray(), version => Assert.False(version.TryGetProperty("content", out _)));
        Assert.Equal(admin.UserId, history[1].GetProperty("approvedBy").GetGuid());
    }

    [Fact]
    public async Task A_version_can_be_read_with_its_content()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));

        var first = await admin.Browser.GetFromJsonAsync<JsonElement>($"{BrandKits.Kit(brandId)}/versions/1", Token);

        Assert.Equal("superseded", first.GetProperty("status").GetString());
        Assert.Equal("first", BrandKits.Summary(first));
    }

    [Fact]
    public async Task An_unknown_or_discarded_version_is_not_found()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        (await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token)).EnsureSuccessStatusCode();

        var discarded = await admin.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions/1", Token);
        var unknown = await admin.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions/99", Token);
        var restore = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);

        Assert.Equal(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound },
            new[] { discarded.StatusCode, unknown.StatusCode, restore.StatusCode });
    }

    [Fact]
    public async Task Restoring_copies_an_old_version_into_a_new_draft()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));

        var response = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(3, draft.GetProperty("number").GetInt32());
        Assert.Equal("restored", draft.GetProperty("origin").GetString());
        Assert.Equal(1, draft.GetProperty("restoredFrom").GetInt32());
        Assert.Equal("first", BrandKits.Summary(draft));
        Assert.Equal("second", BrandKits.Summary((await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("approved")));
    }

    [Fact]
    public async Task Restoring_replaces_the_current_draft_only_at_its_revision()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("third"), 0);

        var stale = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);
        var restored = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 1 }, Token);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var draft = await restored.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal((3, 2, "first"), (draft.GetProperty("number").GetInt32(), draft.GetProperty("revision").GetInt32(), BrandKits.Summary(draft)));
    }

    [Fact]
    public async Task Only_a_superseded_version_can_be_restored()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);

        var approved = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 1 }, Token);
        var draft = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/2/restore", new { revision = 1 }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, approved.StatusCode);
        Assert.Equal("Only an older, superseded version can be restored.", await BrandKits.ProblemTitleAsync(approved));
        Assert.Equal(HttpStatusCode.BadRequest, draft.StatusCode);
    }

    [Fact]
    public async Task Marketers_can_read_the_history_but_not_restore()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");

        var history = await marketer.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions", Token);
        var version = await marketer.Browser.GetAsync($"{BrandKits.Kit(brandId)}/versions/1", Token);
        var restore = await marketer.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden },
            new[] { history.StatusCode, version.StatusCode, restore.StatusCode });
    }

    [Fact]
    public async Task Restoring_is_audited()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("second"));

        (await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token)).EnsureSuccessStatusCode();

        var entry = await Workspaces.LatestAuditEntryAsync(admin.Browser, brandId);
        Assert.Equal("brand_kit.restored", entry.GetProperty("action").GetString());
        Assert.Equal("3", entry.GetProperty("targetId").GetString());
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitHistoryTests"`
Expected: FAIL. `/versions` routes return `404` (the restore test's `404` passes by accident).

- [ ] **Step 3: Add the history and restore endpoints**

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersions.cs`, add next to the other constants:

```csharp
    public const string NotRestorable = "Only an older, superseded version can be restored.";
```

and add this method after `FindAsync`:

```csharp
    public Task<List<KitVersion>> HistoryAsync(CancellationToken cancellationToken) =>
        db.Versions
            .Where(version => version.Status != VersionStatus.Discarded)
            .OrderByDescending(version => version.Number)
            .ToListAsync(cancellationToken);
```

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitEndpoints.cs`, add these methods before `Stale()`:

```csharp
    public static async Task<Ok<KitVersionSummary[]>> HistoryAsync(KitVersions versions, CancellationToken cancellationToken) =>
        TypedResults.Ok((await versions.HistoryAsync(cancellationToken)).Select(KitVersionSummary.Of).ToArray());

    public static async Task<Results<Ok<KitVersionView>, NotFound>> GetVersionAsync(
        int number,
        KitVersions versions,
        CancellationToken cancellationToken)
    {
        if (await versions.FindAsync(number, cancellationToken) is not { } version)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await versions.ViewAsync(version, cancellationToken));
    }

    public static async Task<Results<Ok<KitVersionView>, NotFound, ProblemHttpResult>> RestoreAsync(
        int number,
        RevisionRequest request,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await versions.FindAsync(number, cancellationToken) is not { } source)
        {
            return TypedResults.NotFound();
        }

        if (source.Status != VersionStatus.Superseded)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: KitVersions.NotRestorable);
        }

        var draft = await versions.DraftAsync(cancellationToken);
        if ((draft?.Revision ?? 0) != request.Revision)
        {
            return Stale();
        }

        var before = draft is null ? null : KitVersions.AuditOf(draft);
        var content = KitJson.Read(source.Content);
        if (draft is null)
        {
            draft = await versions.CreateDraftAsync(content, VersionOrigin.Restored, number, cancellationToken);
        }
        else
        {
            await versions.RestoreIntoAsync(draft, content, number, cancellationToken);
        }

        await audit.RecordAsync(
            new AuditRecord("brand_kit.restored", KitVersions.AuditTarget, KitVersions.Number(draft), before, KitVersions.AuditOf(draft)),
            cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
        return TypedResults.Ok(await versions.ViewAsync(draft, cancellationToken));
    }
```

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`, add to the end of `MapBrandKit`:

```csharp
        var history = kit.MapGroup("/versions");
        history.MapGet("", KitEndpoints.HistoryAsync);
        history.MapGet("/{number:int}", KitEndpoints.GetVersionAsync);
        history.MapPost("/{number:int}/restore", KitEndpoints.RestoreAsync).RequireBrandRole(BrandRoles.Admin);
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitHistoryTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Regenerate the contract and run the whole core**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --solution core/Kairo.slnx
```

Expected: the whole suite PASSES.

- [ ] **Step 6: Commit**

```bash
git add core/src/Modules/Kairo.Modules.BrandKit/Kit core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts core/tests/Kairo.IntegrationTests/BrandKit/BrandKitHistoryTests.cs
git commit -m "Show Brand Kit history and restore an older version into the draft"
```

---

### Task 6: Guideline and logo files

**Files:**
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Files/KitFileTypes.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Files/KitFileEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersions.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitEndpoints.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`
- Modify (generated): `core/src/Kairo.Api/openapi.json`, `web/src/lib/core-api/schema.ts`
- Test: `core/tests/Kairo.IntegrationTests/BrandKit/BrandKitFileTests.cs`

**Interfaces:**
- Consumes: Task 1's `IFileStore`, `FileBuckets.BrandFiles`, `FileKeys.For`; Task 4's `KitVersions`, `KitFileView`, `BrandKitLiveEvents`; Task 3's `KitContent.FileIds()` and `WithoutFile`.
- Produces: `POST/GET /files`, `GET /files/{fileId}/download`, `DELETE /files/{fileId}`; `KitFileTypes.MaxBytes`, `KitFileTypes.Detect(byte[], string)`; `KitVersions.FileProblemsAsync(KitContent)`.

- [ ] **Step 1: Write the failing tests**

`core/tests/Kairo.IntegrationTests/BrandKit/BrandKitFileTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.BrandKit.Files;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitFileTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_uploaded_guideline_is_listed_and_downloads_as_it_was_sent()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Pdf, "brand-guide.pdf"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal("guideline", file.GetProperty("kind").GetString());
        Assert.Equal("brand-guide.pdf", file.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", file.GetProperty("contentType").GetString());
        Assert.Equal(BrandKits.Pdf.Length, file.GetProperty("size").GetInt64());
        Assert.False(file.GetProperty("deleted").GetBoolean());
        var id = file.GetProperty("id").GetGuid();
        var listed = await admin.Browser.GetFromJsonAsync<JsonElement>(Files(brandId), Token);
        Assert.Equal(id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        var download = await admin.Browser.GetAsync($"{Files(brandId)}/{id}/download", Token);
        Assert.Equal(BrandKits.Pdf, await download.Content.ReadAsByteArrayAsync(Token));
        Assert.Contains("brand_kit.file_uploaded", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task A_download_is_an_attachment_that_the_browser_will_not_run()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();
        var id = await BrandKits.UploadAsync(admin.Browser, brandId, svg, "logo.svg", "logo");

        var download = await admin.Browser.GetAsync($"{Files(brandId)}/{id}/download", Token);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/svg+xml", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("logo.svg", download.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("sandbox", Assert.Single(download.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("brand-guide.pdf")]
    [InlineData("notes.txt")]
    public async Task A_file_whose_bytes_are_not_an_allowed_type_is_refused(string fileName)
    {
        var (admin, brandId) = await WorkspaceAsync();
        var html = Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>");

        var response = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(html, fileName), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { "Upload a PDF, PNG, JPEG, WebP, SVG or Word (.docx) file." }, await BrandKits.ErrorsAsync(response, "file"));
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Files(brandId), Token)).EnumerateArray());
    }

    [Fact]
    public async Task A_logo_must_be_an_image()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var pdf = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Pdf, "logo.pdf", "logo"), Token);
        var png = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Png, "logo.png", "logo"), Token);
        var unknown = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Png, "logo.png", "banner"), Token);

        Assert.Equal(new[] { "A logo must be a PNG, JPEG, WebP or SVG image." }, await BrandKits.ErrorsAsync(pdf, "file"));
        Assert.Equal(HttpStatusCode.Created, png.StatusCode);
        Assert.Equal("logo", (await png.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("kind").GetString());
        Assert.Equal(new[] { "Use guideline or logo." }, await BrandKits.ErrorsAsync(unknown, "kind"));
    }

    [Fact]
    public async Task A_file_over_20_MB_or_no_file_at_all_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var huge = new byte[KitFileTypes.MaxBytes + 1];
        BrandKits.Pdf.CopyTo(huge, 0);

        var tooLarge = await admin.Browser.PostAsync(Files(brandId), BrandKits.File(huge, "huge.pdf"), Token);
        var missing = await admin.Browser.PostAsync(Files(brandId), new MultipartFormDataContent { { new StringContent("x"), "notes" } }, Token);

        Assert.Equal(new[] { "The file is larger than 20 MB." }, await BrandKits.ErrorsAsync(tooLarge, "file"));
        Assert.Equal(new[] { "Choose a file to upload." }, await BrandKits.ErrorsAsync(missing, "file"));
    }

    [Fact]
    public async Task A_draft_can_only_refer_to_this_brands_files_of_the_right_kind()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var logo = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Png, "logo.png", "logo");

        var wrongLogo = await BrandKits.PutDraftAsync(admin.Browser, brandId, new { logo = new { fileId = guide } }, 0);
        var unknown = await BrandKits.PutDraftAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { Guid.NewGuid() } }, 0);
        var saved = await BrandKits.SaveAsync(admin.Browser, brandId, new { logo = new { fileId = logo }, guidelineFileIds = new[] { guide } }, 0);

        Assert.Equal(new[] { "Upload the logo to the Brand Kit first." }, await BrandKits.ErrorsAsync(wrongLogo, "content.logo.fileId"));
        Assert.Equal(new[] { "Upload this guideline file to the Brand Kit first." }, await BrandKits.ErrorsAsync(unknown, "content.guidelineFileIds[0]"));
        Assert.Equal(
            new[] { (logo, "logo"), (guide, "guideline") },
            saved.GetProperty("files").EnumerateArray().Select(file => (file.GetProperty("id").GetGuid(), file.GetProperty("kind").GetString()!)).ToArray());
    }

    [Fact]
    public async Task Deleting_a_file_removes_it_from_the_draft_but_not_from_the_approved_version()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } });
        await BrandKits.SaveAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } }, 0);

        var deleted = await admin.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token);
        var again = await admin.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty((await admin.Browser.GetFromJsonAsync<JsonElement>(Files(brandId), Token)).EnumerateArray());
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Empty(kit.GetProperty("draft").GetProperty("content").GetProperty("guidelineFileIds").EnumerateArray());
        Assert.Equal(2, kit.GetProperty("draft").GetProperty("revision").GetInt32());
        var approvedFile = Assert.Single(kit.GetProperty("approved").GetProperty("files").EnumerateArray());
        Assert.True(approvedFile.GetProperty("deleted").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.Browser.GetAsync($"{Files(brandId)}/{guide}/download", Token)).StatusCode);
        Assert.Contains("brand_kit.file_deleted", await Workspaces.AuditActionsAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Restoring_a_version_keeps_its_deleted_files()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } });
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content());
        (await admin.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token)).EnsureSuccessStatusCode();

        var restored = await admin.Browser.PostAsJsonAsync($"{BrandKits.Kit(brandId)}/versions/1/restore", new { revision = 0 }, Token);
        var draft = await restored.Content.ReadFromJsonAsync<JsonElement>(Token);
        var approved = await BrandKits.ApproveAsync(admin.Browser, brandId, draft.GetProperty("revision").GetInt32());

        Assert.Equal(guide, Assert.Single(approved.GetProperty("files").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("marketer")]
    [InlineData("approver")]
    public async Task Only_admins_can_upload_or_delete_but_members_can_read(string role)
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var upload = await member.Browser.PostAsync(Files(brandId), BrandKits.File(BrandKits.Pdf, "guide.pdf"), Token);
        var delete = await member.Browser.DeleteAsync($"{Files(brandId)}/{guide}", Token);
        var list = await member.Browser.GetAsync(Files(brandId), Token);
        var download = await member.Browser.GetAsync($"{Files(brandId)}/{guide}/download", Token);

        Assert.Equal(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.OK },
            new[] { upload.StatusCode, delete.StatusCode, list.StatusCode, download.StatusCode });
    }

    [Fact]
    public async Task Another_workspace_cannot_download_the_files()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var (outsider, outsiderBrandId) = await WorkspaceAsync();

        var direct = await outsider.Browser.GetAsync($"{Files(brandId)}/{guide}/download", Token);
        var throughOwnWorkspace = await outsider.Browser.GetAsync($"{Files(outsiderBrandId)}/{guide}/download", Token);

        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, throughOwnWorkspace.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Files(Guid brandId) => $"{BrandKits.Kit(brandId)}/files";

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitFileTests"`
Expected: build FAILS with `The type or namespace name 'KitFileTypes' could not be found`.

- [ ] **Step 3: Write the file type checks**

`core/src/Modules/Kairo.Modules.BrandKit/Files/KitFileTypes.cs`:

```csharp
using System.Text;

namespace Kairo.Modules.BrandKit.Files;

internal static class KitFileTypes
{
    public const long MaxBytes = 20 * 1024 * 1024;
    public const string Pdf = "application/pdf";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static readonly byte[] PdfStart = "%PDF-"u8.ToArray();
    private static readonly byte[] PngStart = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegStart = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] ZipStart = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();

    public static KitFileKind? ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        null or "" or "guideline" => KitFileKind.Guideline,
        "logo" => KitFileKind.Logo,
        _ => null,
    };

    public static string? Detect(byte[] content, string? fileName)
    {
        var head = content.AsSpan();
        if (head.StartsWith(PdfStart))
        {
            return Pdf;
        }

        if (head.StartsWith(PngStart))
        {
            return "image/png";
        }

        if (head.StartsWith(JpegStart))
        {
            return "image/jpeg";
        }

        if (head.Length >= 12 && head[..4].SequenceEqual(Riff) && head[8..12].SequenceEqual(Webp))
        {
            return "image/webp";
        }

        if (head.StartsWith(ZipStart) && (fileName ?? "").EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return Docx;
        }

        return LooksLikeSvg(head) ? "image/svg+xml" : null;
    }

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.Ordinal);

    public static string CleanName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (name.Length == 0)
        {
            return "file";
        }

        return name.Length > 200 ? name[..200] : name;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> head)
    {
        var text = Encoding.UTF8.GetString(head[..Math.Min(head.Length, 1024)]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var opens = text.StartsWith("<?xml", StringComparison.Ordinal)
            || text.StartsWith("<svg", StringComparison.Ordinal)
            || text.StartsWith("<!--", StringComparison.Ordinal);
        return opens && text.Contains("<svg", StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Write the file endpoints**

`core/src/Modules/Kairo.Modules.BrandKit/Files/KitFileEndpoints.cs`:

```csharp
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.BrandKit.Data;
using Kairo.Modules.BrandKit.Kit;
using Kairo.SharedKernel.Files;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Kairo.Modules.BrandKit.Files;

internal sealed record UploadSizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;

internal static class KitFileEndpoints
{
    public const long MaxRequestBytes = KitFileTypes.MaxBytes + 64 * 1024;
    public const string AuditTarget = "brand_kit_file";
    public const string ChooseFile = "Choose a file to upload.";
    public const string TooLarge = "The file is larger than 20 MB.";
    public const string WrongType = "Upload a PDF, PNG, JPEG, WebP, SVG or Word (.docx) file.";
    public const string NotAnImage = "A logo must be a PNG, JPEG, WebP or SVG image.";
    public const string BadKind = "Use guideline or logo.";

    public static async Task<Results<Created<KitFileView>, ValidationProblem>> UploadAsync(
        IFormFile? file,
        [FromForm] string? kind,
        BrandKitDbContext db,
        IFileStore store,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (KitFileTypes.ParseKind(kind) is not { } fileKind)
        {
            return Problem("kind", BadKind);
        }

        if (file is null)
        {
            return Problem("file", ChooseFile);
        }

        if (file.Length > KitFileTypes.MaxBytes)
        {
            return Problem("file", TooLarge);
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (KitFileTypes.Detect(bytes, file.FileName) is not { } contentType)
        {
            return Problem("file", WrongType);
        }

        if (fileKind == KitFileKind.Logo && !KitFileTypes.IsImage(contentType))
        {
            return Problem("file", NotAnImage);
        }

        var id = Guid.NewGuid();
        var row = new KitFile
        {
            Id = id,
            Kind = fileKind,
            FileName = KitFileTypes.CleanName(file.FileName),
            ContentType = contentType,
            Size = bytes.LongLength,
            StorageKey = FileKeys.For(member.BrandId, fileKind == KitFileKind.Logo ? "logos" : "guidelines", id),
            UploadedBy = member.UserId,
            UploadedAt = time.GetUtcNow(),
        };
        await store.PutAsync(FileBuckets.BrandFiles, row.StorageKey, bytes, contentType, cancellationToken);
        db.Files.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var view = KitFileView.Of(row);
        await audit.RecordAsync(new AuditRecord("brand_kit.file_uploaded", AuditTarget, id.ToString(), After: view), cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.FilesChanged(id), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/brand-kit/files/{id}", view);
    }

    public static async Task<Ok<KitFileView[]>> ListAsync(BrandKitDbContext db, CancellationToken cancellationToken)
    {
        var files = await db.Files
            .Where(file => file.DeletedAt == null)
            .OrderByDescending(file => file.UploadedAt)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(files.Select(KitFileView.Of).ToArray());
    }

    public static async Task<Results<FileStreamHttpResult, NotFound>> DownloadAsync(
        Guid fileId,
        BrandKitDbContext db,
        IFileStore store,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (await db.Files.AsNoTracking().SingleOrDefaultAsync(file => file.Id == fileId, cancellationToken) is not { } file)
        {
            return TypedResults.NotFound();
        }

        var stream = await store.OpenAsync(FileBuckets.BrandFiles, file.StorageKey, cancellationToken);
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        return TypedResults.Stream(stream, file.ContentType, file.FileName);
    }

    public static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid fileId,
        BrandKitDbContext db,
        KitVersions versions,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await versions.LockAsync(cancellationToken);
        if (await db.Files.SingleOrDefaultAsync(file => file.Id == fileId && file.DeletedAt == null, cancellationToken) is not { } file)
        {
            return TypedResults.NotFound();
        }

        var before = KitFileView.Of(file);
        file.DeletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        if (await versions.DraftAsync(cancellationToken) is { } draft)
        {
            var content = KitJson.Read(draft.Content);
            if (content.FileIds().Contains(fileId))
            {
                await versions.UpdateDraftAsync(draft, content.WithoutFile(fileId), cancellationToken);
                await live.PublishAsync(BrandKitLiveEvents.Changed(draft), cancellationToken);
            }
        }

        await audit.RecordAsync(new AuditRecord("brand_kit.file_deleted", AuditTarget, fileId.ToString(), before), cancellationToken);
        await live.PublishAsync(BrandKitLiveEvents.FilesChanged(fileId), cancellationToken);
        return TypedResults.NoContent();
    }

    private static ValidationProblem Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
```

- [ ] **Step 5: Check file references when a draft is saved**

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitVersions.cs`, add next to the other constants:

```csharp
    public const string NotALogo = "Upload the logo to the Brand Kit first.";
    public const string NotAGuideline = "Upload this guideline file to the Brand Kit first.";
```

and add this method after `ViewAsync`:

```csharp
    public async Task<Dictionary<string, string[]>> FileProblemsAsync(KitContent content, CancellationToken cancellationToken)
    {
        var ids = content.FileIds();
        var files = await db.Files.Where(file => ids.Contains(file.Id)).ToDictionaryAsync(file => file.Id, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        if (content.Logo?.FileId is { } logo && (!files.TryGetValue(logo, out var logoFile) || logoFile.Kind != KitFileKind.Logo))
        {
            errors["content.logo.fileId"] = [NotALogo];
        }

        for (var i = 0; i < content.GuidelineFileIds.Count; i++)
        {
            if (!files.TryGetValue(content.GuidelineFileIds[i], out var guide) || guide.Kind != KitFileKind.Guideline)
            {
                errors[$"content.guidelineFileIds[{i}]"] = [NotAGuideline];
            }
        }

        return errors;
    }
```

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitEndpoints.cs`, in `SaveDraftAsync`, insert right after the `if (!check.IsValid) { … }` block:

```csharp
        var fileProblems = await versions.FileProblemsAsync(check.Content, cancellationToken);
        if (fileProblems.Count > 0)
        {
            return TypedResults.ValidationProblem(fileProblems);
        }
```

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`, add `using Kairo.Modules.BrandKit.Files;` and add to the end of `MapBrandKit`:

```csharp
        var files = kit.MapGroup("/files");
        files.MapPost("", KitFileEndpoints.UploadAsync)
            .RequireBrandRole(BrandRoles.Admin)
            .WithMetadata(new UploadSizeLimit(KitFileEndpoints.MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: KitFileEndpoints.MaxRequestBytes)
            .DisableAntiforgery();
        files.MapGet("", KitFileEndpoints.ListAsync);
        files.MapGet("/{fileId:guid}/download", KitFileEndpoints.DownloadAsync);
        files.MapDelete("/{fileId:guid}", KitFileEndpoints.DeleteAsync).RequireBrandRole(BrandRoles.Admin);
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitFileTests"`
Expected: PASS, 12 tests.

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitDraftTests"`
Expected: still PASS.

- [ ] **Step 7: Regenerate the contract and run the whole core**

```bash
KAIRO_UPDATE_OPENAPI=1 dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.Api.OpenApiDocumentTests"
npm --prefix web run generate:api
dotnet test --solution core/Kairo.slnx
```

Expected: the whole suite PASSES.

- [ ] **Step 8: Commit**

```bash
git add core/src/Modules/Kairo.Modules.BrandKit core/src/Kairo.Api/openapi.json web/src/lib/core-api/schema.ts core/tests/Kairo.IntegrationTests/BrandKit/BrandKitFileTests.cs
git commit -m "Upload, list, download and delete Brand Kit guideline and logo files"
```

---

### Task 7: The approved kit for other modules

**Files:**
- Create: `core/src/Kairo.Contracts/BrandKit/IBrandKit.cs`
- Create: `core/src/Modules/Kairo.Modules.BrandKit/Kit/BrandKitReader.cs`
- Modify: `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`
- Test: `core/tests/Kairo.IntegrationTests/BrandKit/BrandKitContractTests.cs`

**Interfaces:**
- Consumes: Task 2's context, Task 3's `KitJson` and `FileIds()`.
- Produces: `IBrandKit.GetApprovedAsync(CancellationToken) -> Task<ApprovedBrandKit?>`, `ApprovedBrandKit(int Version, JsonElement Content, IReadOnlyList<BrandKitFile> Files)`, `BrandKitFile(Guid Id, string Kind, string FileName, string ContentType)`.

- [ ] **Step 1: Write the failing tests**

`core/tests/Kairo.IntegrationTests/BrandKit/BrandKitContractTests.cs`:

```csharp
using Kairo.Contracts.BrandKit;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitContractTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task There_is_no_approved_kit_until_one_is_approved()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var before = await ApprovedAsync(brandId);
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        var withDraftOnly = await ApprovedAsync(brandId);

        Assert.Null(before);
        Assert.Null(withDraftOnly);
    }

    [Fact]
    public async Task Agents_get_the_approved_version_and_never_the_draft()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("approved words"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("draft words"), 0);

        var kit = await ApprovedAsync(brandId);

        Assert.NotNull(kit);
        Assert.Equal(1, kit.Version);
        Assert.Equal("approved words", kit.Content.GetProperty("voice").GetProperty("summary").GetString());
        Assert.Equal("primary", kit.Content.GetProperty("colours")[0].GetProperty("role").GetString());
        Assert.Equal("cheap", kit.Content.GetProperty("rules").GetProperty("bannedWords")[0].GetString());
    }

    [Fact]
    public async Task The_kits_files_come_with_it_logo_first()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        var logo = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Png, "logo.png", "logo");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { logo = new { fileId = logo }, guidelineFileIds = new[] { guide } });

        var kit = await ApprovedAsync(brandId);

        Assert.Equal(
            new[] { new BrandKitFile(logo, "logo", "logo.png", "image/png"), new BrandKitFile(guide, "guideline", "guide.pdf", "application/pdf") },
            kit!.Files);
    }

    [Fact]
    public async Task A_deleted_file_still_reaches_agents_through_the_approved_version()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var guide = await BrandKits.UploadAsync(admin.Browser, brandId, BrandKits.Pdf, "guide.pdf");
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, new { guidelineFileIds = new[] { guide } });

        (await admin.Browser.DeleteAsync($"{BrandKits.Kit(brandId)}/files/{guide}", Token)).EnsureSuccessStatusCode();

        Assert.Equal(guide, Assert.Single((await ApprovedAsync(brandId))!.Files).Id);
    }

    [Fact]
    public async Task Each_workspace_gets_only_its_own_kit()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content());
        var (_, otherBrandId) = await WorkspaceAsync();

        Assert.Null(await ApprovedAsync(otherBrandId));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<ApprovedBrandKit?> ApprovedAsync(Guid brandId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IBrandKit>().GetApprovedAsync(Token);
    }

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitContractTests"`
Expected: build FAILS with `The type or namespace name 'BrandKit' does not exist in the namespace 'Kairo.Contracts'`.

- [ ] **Step 3: Write the contract and the reader**

`core/src/Kairo.Contracts/BrandKit/IBrandKit.cs`:

```csharp
using System.Text.Json;

namespace Kairo.Contracts.BrandKit;

public interface IBrandKit
{
    Task<ApprovedBrandKit?> GetApprovedAsync(CancellationToken cancellationToken);
}

public sealed record ApprovedBrandKit(int Version, JsonElement Content, IReadOnlyList<BrandKitFile> Files);

public sealed record BrandKitFile(Guid Id, string Kind, string FileName, string ContentType);
```

`core/src/Modules/Kairo.Modules.BrandKit/Kit/BrandKitReader.cs`:

```csharp
using Kairo.Contracts.BrandKit;
using Kairo.Modules.BrandKit.Data;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed class BrandKitReader(BrandKitDbContext db) : IBrandKit
{
    public async Task<ApprovedBrandKit?> GetApprovedAsync(CancellationToken cancellationToken)
    {
        var approved = await db.Versions
            .AsNoTracking()
            .SingleOrDefaultAsync(version => version.Status == VersionStatus.Approved, cancellationToken);
        if (approved is null)
        {
            return null;
        }

        var ids = KitJson.Read(approved.Content).FileIds();
        var files = await db.Files.AsNoTracking().Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);
        return new ApprovedBrandKit(
            approved.Number,
            KitJson.Element(approved.Content),
            files
                .OrderBy(file => ids.IndexOf(file.Id))
                .Select(file => new BrandKitFile(file.Id, KitJson.Name(file.Kind), file.FileName, file.ContentType))
                .ToList());
    }
}
```

In `core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs`, add `using Kairo.Contracts.BrandKit;` and register the reader in `AddBrandKit`:

```csharp
        services.AddScoped<IBrandKit, BrandKitReader>();
```

- [ ] **Step 4: Run the tests to see them pass**

```bash
dotnet test --project core/tests/Kairo.IntegrationTests --filter-class "Kairo.IntegrationTests.BrandKit.BrandKitContractTests"
dotnet test --project core/tests/Kairo.ArchitectureTests
```

Expected: PASS, 5 tests, and the architecture tests still PASS (`Contracts_references_only_SharedKernel`).

- [ ] **Step 5: Commit**

```bash
git add core/src/Kairo.Contracts/BrandKit/IBrandKit.cs core/src/Modules/Kairo.Modules.BrandKit/Kit/BrandKitReader.cs core/src/Modules/Kairo.Modules.BrandKit/Kit/KitSetup.cs core/tests/Kairo.IntegrationTests/BrandKit/BrandKitContractTests.cs
git commit -m "Give other modules the approved Brand Kit through IBrandKit"
```

---

### Task 8: Deployment, docs and the final check

**Files:**
- Modify: `deploy/Kairo.Deploy/Setup.ps1`
- Modify: `deploy/Kairo.Deploy/Release.ps1`
- Modify: `deploy/tests/Setup.Tests.ps1`
- Modify: `deploy/tests/Release.Tests.ps1`
- Modify: `docs/Kairo-SAD.md`
- Modify: `docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md`

**Interfaces:**
- Consumes: Task 1's `Kairo:Files:Project` setting.
- Produces: the `{project}-brand-files` bucket with object access for `kairo-api` and `kairo-worker`, and `Kairo__Files__Project` on both services.

- [ ] **Step 1: Write the failing deployment tests**

In `deploy/tests/Setup.Tests.ps1`, inside `Context 'in an empty project'`, add after `It 'lets the API and the worker write brand datasets and run BigQuery jobs'`:

```powershell
        It 'creates a private bucket for brand files that only the API and the worker can use' {
            $calls | Should -Contain 'storage buckets create gs://kairo-test-brand-files --location asia-south1 --uniform-bucket-level-access --public-access-prevention --project kairo-test'
            foreach ($name in 'kairo-api', 'kairo-worker') {
                $calls | Should -Contain "storage buckets add-iam-policy-binding gs://kairo-test-brand-files --member serviceAccount:$name@kairo-test.iam.gserviceaccount.com --role roles/storage.objectAdmin --project kairo-test"
            }
            ($calls | Where-Object { $_ -like 'storage buckets add-iam-policy-binding gs://kairo-test-brand-files *kairo-web@*' }) | Should -BeNullOrEmpty
        }
```

In `deploy/tests/Release.Tests.ps1`, add after `It 'tells the API and the worker where brand datasets live'`:

```powershell
    It 'tells the API and the worker which project holds the file buckets' {
        Invoke-KairoDeploy -Environment $environment -Root $root | Out-Null

        foreach ($service in 'kairo-api', 'kairo-worker') {
            ($calls | Where-Object { $_ -like "run deploy $service *" }) | Should -BeLike '*Kairo__Files__Project=kairo-test*'
        }
    }
```

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: the two new tests FAIL; every other test PASSES.

- [ ] **Step 2: Create the bucket in setup**

In `deploy/Kairo.Deploy/Setup.ps1`:
- Add under the `$script:ServiceAccounts` line:

  ```powershell
  $script:FileBuckets = @('brand-files')
  ```

- In `Initialize-KairoProject`, add `Initialize-KairoBuckets -Environment $Environment` on the line after `Initialize-KairoServiceAccounts -Environment $Environment`.
- Add this function after `Initialize-KairoServiceAccounts`:

  ```powershell
  function Initialize-KairoBuckets {
      param([Parameter(Mandatory)][hashtable]$Environment)

      $scope = @('--project', $Environment.Project)
      foreach ($name in $script:FileBuckets) {
          $bucket = "gs://$($Environment.Project)-$name"
          if (-not (Test-Gcloud -ArgumentList (@('storage', 'buckets', 'describe', $bucket) + $scope))) {
              Invoke-Gcloud -ArgumentList (@('storage', 'buckets', 'create', $bucket, '--location', $Environment.Region, '--uniform-bucket-level-access', '--public-access-prevention') + $scope)
          }

          foreach ($service in 'kairo-api', 'kairo-worker') {
              Add-KairoBinding -ArgumentList (@('storage', 'buckets', 'add-iam-policy-binding', $bucket, '--member', "serviceAccount:$(Get-KairoServiceAccount -Environment $Environment -Name $service)", '--role', 'roles/storage.objectAdmin') + $scope)
          }
      }
  }
  ```

- [ ] **Step 3: Pass the project to the services**

In `deploy/Kairo.Deploy/Release.ps1`:
- Add under the `$analytics = …` line:

  ```powershell
      $files = "Kairo__Files__Project=$($Environment.Project)"
  ```

- Change `$apiSettings = "Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName,$analytics"` to:

  ```powershell
      $apiSettings = "Kairo__PublicOrigin=$($urls['kairo-web']),Kairo__Kms__KeyName=$keyName,$analytics,$files"
  ```

- In the `kairo-worker` deploy, change `"--set-env-vars=Kairo__Kms__KeyName=$keyName,$analytics",` to:

  ```powershell
              "--set-env-vars=Kairo__Kms__KeyName=$keyName,$analytics,$files",
  ```

Run: `powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"`
Expected: every test PASSES. The `'in a project that is already set up'` context still finds no `create` calls.

- [ ] **Step 4: Update the SAD**

In `docs/Kairo-SAD.md` §6:
- Replace the table row that starts `| Brand | BrandKitVersion, BrandRule, BrandFile, BestAd |` with:

  ```markdown
  | Brand Kit | KitVersion, KitFile | Kit content, rules included, stored as JSON in each version; at most one draft and one approved version; guideline and logo files in Cloud Storage |
  ```

- Replace the first sentence of the Cloud Storage paragraph, `Separate buckets hold brand files, catalogue imports, generated media and reports.`, with:

  ```markdown
  Separate buckets hold brand files (`{project}-brand-files`), catalogue imports, generated media and reports. Brand files are private and reach the browser only through the core, as attachments.
  ```

- [ ] **Step 5: Bring the spec in line with what was built**

```bash
sed -i \
  -e 's/SQL schema `brandkit`/SQL schema `brand_kit`/' \
  -e 's/| `brandkit` |/| `brand_kit` |/g' \
  -e 's/`brandkit\.DraftRuns`/`brand_kit.DraftRuns`/' \
  -e 's/`brand-kit\.changed`/`brand_kit.changed`/g' \
  -e 's/`brand-kit\.drafting`/`brand_kit.drafting`/g' \
  docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md
grep -n "brandkit\|brand-kit\.\(changed\|drafting\)" docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md
```

Expected: the `grep` prints nothing.

Then make these edits in the same file:
- In **File store**, replace the code block and the two bullets **Cloud Storage** and **Development and tests** with:

  ````markdown
  ```csharp
  public interface IFileStore
  {
      Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken);

      Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken);
  }
  ```
  ````

  ```markdown
  - **Cloud Storage.** JSON API over REST (`https://storage.googleapis.com/storage/v1` and `/upload/storage/v1`), with the service account's Application Default Credentials scoped to `devstorage.read_write`. Files reach the browser only through the core's download endpoints, as attachments, so no signed URLs are needed.
  - **Development and tests.** When `Kairo:Files:Project` isn't set, a disk store writes under `Kairo:Files:Root` (default: the `kairo-files` folder in the system temp folder).
  ```

- In **Buckets**, replace `creates both with uniform bucket-level access,` with `creates both with uniform bucket-level access and public access blocked,`.
- In the Brand Kit **API** table, replace the `GET /files/{fileId}/download` row's description `` `302` to a download link valid for 5 minutes `` with `The file itself, as an attachment, with `X-Content-Type-Options: nosniff` and `Content-Security-Policy: sandbox``.
- In **Tables**, under the Brand Kit table, replace `A discarded draft's number is not reused.` with `A discarded draft is kept with status `Discarded`, hidden from history, so its number is not reused.`
- In Brand Kit **Side effects**, replace the **Live events** bullet with:

  ```markdown
  - **Live events.** `brand_kit.changed` with `{ number, status }` to everyone in the workspace, after every change to a version, and `brand_kit.files_changed` with `{ fileId }` after a file is uploaded or deleted.
  ```

- [ ] **Step 6: Run everything**

```bash
dotnet build core/Kairo.slnx
dotnet test --solution core/Kairo.slnx
powershell -NoProfile -Command "Import-Module Pester -RequiredVersion 5.9.1; Invoke-Pester -Path deploy/tests"
npm --prefix web run lint
npm --prefix web test
grep -rn "//\|/\*" core/src/Modules/Kairo.Modules.BrandKit core/src/Kairo.SharedKernel/Files core/src/Kairo.Contracts/BrandKit --include=*.cs | grep -v "https\?://"
```

Expected: the build has 0 warnings; every suite PASSES; the last `grep` prints nothing.

- [ ] **Step 7: Commit**

```bash
git add deploy/Kairo.Deploy/Setup.ps1 deploy/Kairo.Deploy/Release.ps1 deploy/tests/Setup.Tests.ps1 deploy/tests/Release.Tests.ps1 docs/Kairo-SAD.md docs/superpowers/specs/2026-10-14-brand-kit-catalogue-design.md
git commit -m "Create the brand files bucket and record the Brand Kit storage in the SAD"
```

---

## After the last task

1. Ask the user to merge `feature/brand-kit-versions` into `develop` (`AGENTS.md` §7). Push the branch only when they ask for a pull request.
2. Once the merge is confirmed, on `develop` in `D:\VSTS\Kairo`: mark **Brand rules and versions** `[x]` in `docs/task.md` and push that status-only commit. Then remove the worktree and delete the local branch.
3. Tell Nency the Brand Kit API is in `develop`: `openapi.json` and `web/src/lib/core-api/schema.ts` carry the contract for **Brand Kit screens**.
