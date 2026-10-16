using System.Text.Json;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Web;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class MediaAgentTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 7];

    private static readonly MediaBrief Brief = new("Monsoon runners", "Sell waterproof shoes", ["Dries in an hour"], ["Commuters"]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Images_go_to_the_agent_with_the_photos_as_base64_and_come_back_as_bytes()
    {
        var agents = new FakeAgents
        {
            Respond = (_, _) => FakeAgents.Json(new
            {
                images = new[]
                {
                    new { shotId = "s1", productId = "p1", style = "product", format = "square", width = 1200, height = 1200, mimeType = "image/jpeg", data = Convert.ToBase64String(Jpeg), prompt = "data" },
                },
                failures = new[] { new { shotId = "s1", format = "landscape", reason = "no_image" } },
                model = "gemini-2.5-flash-image",
                promptVersion = "create-image/1",
                usage = new { inputTokens = 10, outputTokens = 1290 },
            }),
        };

        var answer = await Agent(agents).ImagesAsync(
            new ImagesRequest(
                Brief,
                null,
                [new MediaReferenceProduct("p1", "Runner", "Waterproof", [new MediaPhoto("image/png", Png)])],
                [new ImageShot("s1", "p1", "product", ["square", "landscape"])]),
            Token);

        var request = Assert.Single(agents.Requests);
        Assert.Equal("http://agents.test/create/images", request.Url);
        var photo = request.Body.GetProperty("products")[0].GetProperty("photos")[0];
        Assert.Equal("image/png", photo.GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String(Png), photo.GetProperty("data").GetString());
        Assert.Equal("Dries in an hour", request.Body.GetProperty("brief").GetProperty("keyMessages")[0].GetString());
        var image = Assert.Single(answer.Images!);
        Assert.Equal(Jpeg, image.Data);
        Assert.Equal(("square", 1200, 1200), (image.Format, image.Width, image.Height));
        Assert.Equal("no_image", Assert.Single(answer.Failures!).Reason);
        Assert.Equal(1290, answer.Usage!.OutputTokens);
    }

    [Fact]
    public async Task Footage_and_video_use_their_own_routes()
    {
        var agents = new FakeAgents
        {
            Respond = (path, _) => path switch
            {
                "/create/footage" => FakeAgents.Json(new
                {
                    clips = new[] { new { index = 0, prompt = "Push in", mimeType = "video/mp4", data = Convert.ToBase64String([1, 2]), seconds = 8.0 } },
                    model = "veo-3.1-generate-001",
                    plannerModel = "gemini-3.5-flash",
                    promptVersion = "create-footage/1",
                }),
                _ => FakeAgents.Json(new
                {
                    video = new { mimeType = "video/mp4", data = Convert.ToBase64String([3]), seconds = 15.0 },
                    voiceover = new { mimeType = "audio/mpeg", data = Convert.ToBase64String([4]), seconds = 15.0 },
                    width = 1080,
                    height = 1920,
                    language = "en",
                    voice = "en-IN-Chirp3-HD-Achird",
                    script = new[] { new { voiceover = "Stay dry.", caption = "Stay dry", start = 0.3, end = 1.4 } },
                    model = "gemini-3.5-flash",
                    promptVersion = "create-video-script/1",
                }),
            },
        };
        var agent = Agent(agents);

        var footage = await agent.FootageAsync(
            new FootageRequest(Brief, null, [new FootageProduct("p1", "Runner")], [new FootageFrame("p1", "image/jpeg", Jpeg)], 2, 8, null),
            Token);
        var video = await agent.VideoAsync(
            new VideoRequest(Brief, null, "Acme", "en", [new VideoClip("video/mp4", [1, 2], 8)], null, "#003366", 15, null, null),
            Token);

        Assert.Equal(["http://agents.test/create/footage", "http://agents.test/create/video"], agents.Requests.Select(request => request.Url));
        Assert.Equal(new byte[] { 1, 2 }, Assert.Single(footage.Clips!).Data);
        Assert.Equal(new byte[] { 3 }, video.Video!.Data);
        Assert.Equal("audio/mpeg", video.Voiceover!.MimeType);
        Assert.Equal(1.4, Assert.Single(video.Script!).End);
        var sent = agents.Requests.Last().Body;
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("logo").ValueKind);
        Assert.Equal(15, sent.GetProperty("seconds").GetInt32());
    }

    [Fact]
    public void Every_media_call_ends_inside_the_agent_timeout_and_the_stage_lease()
    {
        Assert.All(
            new[] { MediaAgent.ImagesTimeout, MediaAgent.FootageTimeout, MediaAgent.VideoTimeout },
            timeout => Assert.True(timeout <= AgentSetup.MaxTimeout));
        Assert.True(AgentSetup.MaxTimeout < StageDispatcher.Lease);
        Assert.True(MediaAgent.FootageTimeout + MediaAgent.VideoTimeout <= StageDispatcher.Lease - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Stage_work_retries_three_times_unless_it_says_otherwise()
    {
        IStageWork work = new PlainWork();

        Assert.Equal(StageRunner.MaxAttempts, work.MaxAttempts);
        Assert.Equal(3, work.MaxAttempts);
    }

    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData(null, new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C })]
    [InlineData(null, new byte[] { })]
    public void Photo_types_come_from_the_bytes(string? type, byte[] body) =>
        Assert.Equal(type, CataloguePhotos.TypeOf(body));

    [Fact]
    public async Task Catalogue_photos_are_read_when_they_are_real_images()
    {
        var websites = new FakeWebsites();
        websites.File("https://shop.test/a.jpg", "image/jpeg", Jpeg);
        websites.File("https://shop.test/b.webp", "application/octet-stream", Webp);
        websites.File("https://shop.test/fake.jpg", "image/jpeg", "<html>not a photo</html>"u8.ToArray());

        var photos = Photos(websites);

        var jpeg = await photos.FetchAsync("https://shop.test/a.jpg", Token);
        Assert.Equal("image/jpeg", jpeg!.MimeType);
        Assert.Equal(Jpeg, jpeg.Data);
        Assert.Equal("image/webp", (await photos.FetchAsync("https://shop.test/b.webp", Token))!.MimeType);
        Assert.Null(await photos.FetchAsync("https://shop.test/fake.jpg", Token));
        Assert.Null(await photos.FetchAsync("https://shop.test/missing.jpg", Token));
        Assert.Null(await photos.FetchAsync("ftp://shop.test/a.jpg", Token));
        Assert.Null(await photos.FetchAsync("not a link", Token));
        Assert.Null(await photos.FetchAsync(null, Token));
    }

    [Fact]
    public async Task Catalogue_photos_larger_than_10_MB_are_skipped()
    {
        var websites = new FakeWebsites();
        var large = new byte[CataloguePhotos.MaxBytes + 1];
        Jpeg.CopyTo(large, 0);
        websites.Unsized("https://shop.test/large.jpg", "image/jpeg", large);

        Assert.Null(await Photos(websites).FetchAsync("https://shop.test/large.jpg", Token));
    }

    [Fact]
    public async Task Catalogue_photos_on_private_addresses_are_never_read()
    {
        var photos = new CataloguePhotos(SafeWeb.Client(), NullLogger<CataloguePhotos>.Instance);

        Assert.Null(await photos.FetchAsync("http://127.0.0.1/photo.jpg", Token));
        Assert.Null(await photos.FetchAsync("http://169.254.169.254/photo.jpg", Token));
    }

    [Fact]
    public async Task The_first_photos_that_work_are_kept_in_order_without_repeats()
    {
        var websites = new FakeWebsites();
        websites.File("https://shop.test/1.jpg", "image/jpeg", Jpeg);
        websites.File("https://shop.test/3.png", "image/png", Png);
        websites.File("https://shop.test/4.jpg", "image/jpeg", Jpeg);

        var photos = await Photos(websites).FetchFirstAsync(
            ["https://shop.test/1.jpg", null, "https://shop.test/1.jpg", "https://shop.test/2.jpg", "https://shop.test/3.png", "https://shop.test/4.jpg"],
            2,
            Token);

        Assert.Equal(["image/jpeg", "image/png"], photos.Select(photo => photo.Photo.MimeType));
        Assert.Equal(["https://shop.test/1.jpg", "https://shop.test/3.png"], photos.Select(photo => photo.Url));
        Assert.DoesNotContain("https://shop.test/4.jpg", websites.Requests);
        Assert.Single(websites.Requests, url => url == "https://shop.test/1.jpg");
    }

    private static CataloguePhotos Photos(FakeWebsites websites) =>
        new(SafeWeb.Client(websites), NullLogger<CataloguePhotos>.Instance);

    private static MediaAgent Agent(FakeAgents agents)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AgentClient.UrlSetting] = "http://agents.test" })
            .Build());
        services.AddKairoAgents();
        services.AddHttpClient(AgentClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => agents);
        return new MediaAgent(services.BuildServiceProvider().GetRequiredService<AgentClient>(), new ProvenanceTrail());
    }

    private sealed class PlainWork : IStageWork
    {
        public string Kind => "plain";

        public string Agent => StageAgents.Create;

        public string Stage => "creating";

        public Task<StageOutcome> RunAsync(StageRun run, Kairo.Modules.CampaignRooms.Rooms.Room room, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(StageRun run, Kairo.Modules.CampaignRooms.Rooms.Room room, StageOutcome outcome, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
