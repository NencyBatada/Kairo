using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed record CheckCall(string Path, JsonElement Body);

public sealed class FakeCheck
{
    public static readonly string[] Paths = ["/check/copy", "/check/image", "/check/video"];

    public List<CheckCall> Calls { get; } = [];

    public bool Fails { get; set; }

    public bool BadVideo { get; set; }

    public Func<string, string, JsonElement, IEnumerable<object>>? Findings { get; set; }

    public static object Finding(string rule, string message, string? field = null, int? index = null, string? evidence = null) =>
        new { rule, field, index, at = (double?)null, message, evidence };

    public IReadOnlyList<CheckCall> CallsTo(string path)
    {
        lock (Calls)
        {
            return [.. Calls.Where(call => call.Path == path)];
        }
    }

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) => Paths.Contains(path) ? Answer(path, body) : previous(path, body);
    }

    private HttpResponseMessage Answer(string path, JsonElement body)
    {
        lock (Calls)
        {
            Calls.Add(new CheckCall(path, body));
        }

        if (Fails)
        {
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        if (BadVideo && path == "/check/video")
        {
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"error\":\"bad_video\"}") };
        }

        var ids = path == "/check/copy"
            ? body.GetProperty("assets").EnumerateArray().Select(asset => asset.GetProperty("id").GetString()!).ToList()
            : [body.GetProperty("assetId").GetString()!];
        return FakeAgents.Json(new
        {
            results = ids.Select(id => new { assetId = id, findings = Findings?.Invoke(path, id, body).ToArray() ?? [] }).ToArray(),
            model = "gemini-pro-test",
            promptVersion = path[1..].Replace('/', '-') + "/1",
            usage = new { inputTokens = 300, outputTokens = 60 },
        });
    }
}
