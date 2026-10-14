using System.Net;
using Kairo.IntegrationTests.Web;
using Kairo.Modules.BrandKit.Drafting;
using Kairo.Modules.BrandKit.Files;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class LogoSaverTests : IDisposable
{
    private const string LogoUrl = "https://acme.example/img/Acme%20Logo.png";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kairo-logos-{Guid.NewGuid():N}");
    private readonly FakeWebsites _sites = new();
    private readonly IFileStore _store;
    private readonly LogoSaver _saver;

    public LogoSaverTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kairo:Files:Root"] = _root })
            .Build());
        services.AddKairoFiles();
        _store = services.BuildServiceProvider().GetRequiredService<IFileStore>();
        _saver = new LogoSaver(SafeWeb.Client(_sites), _store, TimeProvider.System, NullLogger<LogoSaver>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stores_the_logo_as_a_logo_file_of_the_brand()
    {
        var brandId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _sites.File(LogoUrl, "image/png", BrandKits.Png);

        var file = await _saver.DownloadAsync(Candidate(LogoUrl), brandId, userId, Token);

        Assert.NotNull(file);
        Assert.Equal(
            (brandId, KitFileKind.Logo, "Acme Logo.png", "image/png", (long)BrandKits.Png.Length, userId),
            (file.BrandId, file.Kind, file.FileName, file.ContentType, file.Size, file.UploadedBy));
        Assert.Equal(FileKeys.For(brandId, "logos", file.Id), file.StorageKey);
        await using var stored = await _store.OpenAsync(FileBuckets.BrandFiles, file.StorageKey, Token);
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy, Token);
        Assert.Equal(BrandKits.Png, copy.ToArray());
    }

    [Fact]
    public async Task Keeps_icons()
    {
        _sites.File("https://acme.example/favicon.ico", "image/x-icon", KitFileTypesTests.Ico);

        var file = await _saver.DownloadAsync(Candidate("https://acme.example/favicon.ico"), Guid.NewGuid(), Guid.NewGuid(), Token);

        Assert.Equal("image/x-icon", file!.ContentType);
    }

    [Fact]
    public async Task A_logo_that_cannot_be_downloaded_or_is_not_an_image_is_skipped()
    {
        _sites.Status(LogoUrl, HttpStatusCode.NotFound);
        _sites.Html("https://acme.example/logo", "<html><body>Logo</body></html>");

        Assert.Null(await _saver.DownloadAsync(Candidate(LogoUrl), Guid.NewGuid(), Guid.NewGuid(), Token));
        Assert.Null(await _saver.DownloadAsync(Candidate("https://acme.example/logo"), Guid.NewGuid(), Guid.NewGuid(), Token));
    }

    [Fact]
    public async Task A_logo_over_2_MB_is_skipped()
    {
        var large = new byte[(2 * 1024 * 1024) + 1];
        BrandKits.Png.CopyTo(large, 0);
        _sites.File(LogoUrl, "image/png", large);

        Assert.Null(await _saver.DownloadAsync(Candidate(LogoUrl), Guid.NewGuid(), Guid.NewGuid(), Token));
    }

    [Fact]
    public void A_logo_without_a_file_name_is_called_logo()
    {
        Assert.Equal("logo", LogoSaver.FileNameOf(new Uri("https://acme.example/")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static LogoCandidate Candidate(string url) => new("l1", new Uri(url), "header-img", "Acme");
}
