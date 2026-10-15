using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class FakeProof
{
    public List<JsonElement> Requests { get; } = [];

    public bool Fails { get; set; }

    public object[] Warnings { get; set; } = [];

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) => path == "/plan/proof-plan" ? Answer(body) : previous(path, body);
    }

    private HttpResponseMessage Answer(JsonElement body)
    {
        lock (Requests)
        {
            Requests.Add(body);
        }

        if (Fails)
        {
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        var regions = body.GetProperty("regions").EnumerateArray().ToList();
        var area = regions.Where(region => region.GetProperty("inArea").GetBoolean()).Select(region => region.GetProperty("id").GetString()!).ToList();
        var outside = regions.Where(region => !region.GetProperty("inArea").GetBoolean()).Select(region => region.GetProperty("id").GetString()!).ToList();
        var inside = area.Count >= 3;
        var pool = inside ? area : outside;
        var holdouts = body.GetProperty("holdoutIds").ValueKind == JsonValueKind.Array
            ? body.GetProperty("holdoutIds").EnumerateArray().Select(id => id.GetString()!).ToList()
            : pool.Skip(inside ? 1 : 0).Take(2).ToList();
        var maxDays = body.GetProperty("maxDays").ValueKind == JsonValueKind.Number ? body.GetProperty("maxDays").GetInt32() : 28;
        var window = new[] { 14, 21, 28 }.Where(days => days <= maxDays).DefaultIfEmpty(maxDays).First();
        return FakeAgents.Json(new
        {
            kpi = "orders",
            testIds = area.Where(id => !holdouts.Contains(id)).ToArray(),
            holdoutIds = holdouts,
            candidates = pool.Select((id, index) => new { id, score = 90.0 - index, share = 0.1, inArea = inside }).ToArray(),
            insideArea = inside,
            windowDays = area.Count == 0 ? (int?)null : window,
            lifts = new[] { new { days = window, lift = (double?)0.08, placebos = 30 } },
            minDetectableLift = area.Count == 0 ? (double?)null : 0.08,
            placeboWindows = 30,
            holdoutShare = inside ? 0.2 : (double?)null,
            holdoutDailyRevenue = 1000.0,
            holdoutCost = inside ? 1120.5 : 0.0,
            dataDays = body.GetProperty("days").GetInt32(),
            warnings = Warnings,
            method = "placebo-geo/1",
        });
    }
}
