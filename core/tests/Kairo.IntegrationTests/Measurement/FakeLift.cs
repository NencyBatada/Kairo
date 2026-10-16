using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.Measurement;

public sealed class FakeLift
{
    public List<JsonElement> Requests { get; } = [];

    public bool Fails { get; set; }

    public string Status { get; set; } = "ok";

    public double Lift { get; set; } = 0.12;

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) => path == "/prove/lift" ? Answer(body) : previous(path, body);
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

        var days = body.GetProperty("testDays").GetInt32();
        var tests = body.GetProperty("regions").EnumerateArray().Where(region => region.GetProperty("test").GetBoolean()).ToList();
        var figures = Status == "ok"
            ? new
            {
                lift = Lift, low = Lift - 0.06, high = Lift + 0.06, actual = 112.0 * days, expected = 100.0 * days, extra = 12.0 * days,
                extraLow = 6.0 * days, extraHigh = 18.0 * days, conclusive = Lift - 0.06 > 0, placebos = 40, spread = 0.03,
            }
            : null;
        return FakeAgents.Json(new
        {
            status = Status,
            reason = Status == "ok" ? null : "few_placebos",
            kpi = body.GetProperty("kpi").GetString(),
            testDays = days,
            outcome = figures,
            revenue = figures,
            regions = tests.Select(region => new { id = region.GetProperty("id").GetString(), status = Status, reason = (string?)null, figures }),
            daily = Enumerable.Range(0, Status == "ok" ? days : 0).Select(_ => new { actual = 112.0, expected = 100.0 }),
            method = "placebo-did/1",
        });
    }
}
