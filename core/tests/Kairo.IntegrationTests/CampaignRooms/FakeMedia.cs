using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Web;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class FakeMedia
{
    public static readonly byte[] Photo = [0xFF, 0xD8, 0xFF, 0xE0, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private static readonly Dictionary<string, (int Width, int Height)> Sizes = new()
    {
        ["landscape"] = (1200, 628),
        ["square"] = (1200, 1200),
        ["portrait"] = (960, 1200),
        ["vertical"] = (1080, 1920),
    };

    public bool ImagesFail { get; set; }

    public bool FootageFails { get; set; }

    public int Generation { get; set; } = 1;

    public List<JsonElement> ImageRequests { get; } = [];

    public List<JsonElement> FootageRequests { get; } = [];

    public List<JsonElement> VideoRequests { get; } = [];

    public static byte[] ImageBytes(string format, int generation) => [0xFF, 0xD8, 0xFF, (byte)format.Length, (byte)generation];

    public static byte[] ClipBytes(int index, int generation) => [0x00, 0x00, 0x00, 0x18, (byte)index, (byte)generation];

    public static byte[] VideoBytes(int generation) => [0x00, 0x00, 0x00, 0x20, 0x66, (byte)generation];

    public static byte[] VoiceBytes(int generation) => [0x49, 0x44, 0x33, (byte)generation];

    public static double VideoSeconds(int generation) => 15.0 - ((generation - 1) * 0.5);

    public static void ServePhotos(FakeWebsites websites)
    {
        foreach (var id in new[] { "RAIN-1", "RAIN-2", "SCHOOL-1" })
        {
            websites.File($"https://acme.example/i/{id}.jpg", "image/jpeg", Photo);
        }
    }

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) => path switch
        {
            "/create/images" => Images(body),
            "/create/footage" => Footage(body),
            "/create/video" => Video(body),
            _ => previous(path, body),
        };
    }

    private HttpResponseMessage Images(JsonElement body)
    {
        lock (ImageRequests)
        {
            ImageRequests.Add(body);
        }

        if (ImagesFail)
        {
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        var images = body.GetProperty("shots").EnumerateArray()
            .SelectMany(shot => shot.GetProperty("formats").EnumerateArray().Select(format => new
            {
                shotId = shot.GetProperty("id").GetString(),
                productId = shot.GetProperty("productId").GetString(),
                style = shot.GetProperty("style").GetString(),
                format = format.GetString(),
                width = Sizes[format.GetString()!].Width,
                height = Sizes[format.GetString()!].Height,
                mimeType = "image/jpeg",
                data = Convert.ToBase64String(ImageBytes(format.GetString()!, Generation)),
                prompt = "data",
            }))
            .ToArray();
        return FakeAgents.Json(new
        {
            images,
            failures = Array.Empty<object>(),
            model = "gemini-image-test",
            promptVersion = "create-image/1",
            usage = new { inputTokens = 300, outputTokens = 1290 },
        });
    }

    private HttpResponseMessage Footage(JsonElement body)
    {
        lock (FootageRequests)
        {
            FootageRequests.Add(body);
        }

        if (FootageFails)
        {
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        var count = body.GetProperty("clips").GetInt32();
        return FakeAgents.Json(new
        {
            clips = Enumerable.Range(0, count).Select(index => new
            {
                index,
                prompt = $"Scene {index}",
                mimeType = "video/mp4",
                data = Convert.ToBase64String(ClipBytes(index, Generation)),
                seconds = 8.0,
            }),
            model = "veo-test",
            plannerModel = "gemini-flash-test",
            promptVersion = "create-footage/1",
            usage = new { inputTokens = 500, outputTokens = 80 },
        });
    }

    private HttpResponseMessage Video(JsonElement body)
    {
        lock (VideoRequests)
        {
            VideoRequests.Add(body);
        }

        return FakeAgents.Json(new
        {
            video = new { mimeType = "video/mp4", data = Convert.ToBase64String(VideoBytes(Generation)), seconds = VideoSeconds(Generation) },
            voiceover = new { mimeType = "audio/mpeg", data = Convert.ToBase64String(VoiceBytes(Generation)), seconds = 15.0 },
            width = 1080,
            height = 1920,
            language = body.GetProperty("language").GetString(),
            voice = "en-IN-Chirp3-HD-Achird",
            script = new[]
            {
                new { voiceover = "Rain again?", caption = "Rain again?", start = 0.3, end = 1.4 },
                new { voiceover = "Shop Acme today.", caption = "Shop Acme today", start = 1.65, end = 3.1 },
            },
            model = "gemini-flash-test",
            promptVersion = "create-video-script/1",
            usage = new { inputTokens = 200, outputTokens = 60 },
        });
    }
}
