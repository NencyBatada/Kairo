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
