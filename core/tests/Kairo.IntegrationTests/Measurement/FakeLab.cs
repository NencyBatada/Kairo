using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.Measurement;

public sealed class FakeLab
{
    public List<JsonElement> Checks { get; } = [];

    public List<JsonElement> Runs { get; } = [];

    public double Lift { get; set; } = 0.18;

    public bool Conclusive { get; set; } = true;

    public int RunFailures { get; set; }

    public object[] Warnings { get; set; } = [];

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) => path switch
        {
            "/prove/lab/check" => Check(body),
            "/prove/lab/run" => Run(body),
            _ => previous(path, body),
        };
    }

    private HttpResponseMessage Check(JsonElement body)
    {
        lock (Checks)
        {
            Checks.Add(body);
        }

        var affected = Keys(body, "affected");
        var candidates = body.GetProperty("series").EnumerateArray()
            .Select(series => series.GetProperty("key").GetString()!)
            .Where(key => !affected.Contains(key))
            .Select((key, index) => new { key, fit = index < 2 ? 0.9 - (index * 0.1) : 0.2, suggested = index < 2 })
            .ToList();
        var comparison = body.GetProperty("comparison").ValueKind == JsonValueKind.Array
            ? Keys(body, "comparison")
            : candidates.Where(candidate => candidate.suggested).Select(candidate => candidate.key).ToList();
        return FakeAgents.Json(new
        {
            candidates,
            comparison,
            fit = comparison.Count > 0 ? 0.85 : (double?)null,
            detectable = 0.12,
            placebos = 40,
            warnings = comparison.Count > 0 ? Warnings : [new { code = "no_comparison", value = (double?)null }],
        });
    }

    private HttpResponseMessage Run(JsonElement body)
    {
        lock (Runs)
        {
            Runs.Add(body);
        }

        if (RunFailures > 0)
        {
            RunFailures--;
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        var outcome = !Conclusive ? "inconclusive" : Lift > 0 ? "rose" : "fell";
        return FakeAgents.Json(new
        {
            status = "ok",
            reason = (string?)null,
            outcome,
            figures = new
            {
                lift = Lift,
                low = Conclusive ? Lift - 0.06 : -0.05,
                high = Lift + 0.06,
                actual = 1180.0,
                expected = 1000.0,
                extra = 180.0,
                extraLow = 120.0,
                extraHigh = 240.0,
                conclusive = Conclusive,
                placebos = 40,
                spread = 0.03,
            },
            fit = 0.85,
            detectable = 0.12,
            placebos = 40,
            warnings = Warnings,
            daily = new[] { new { offset = -1, actual = 40.0, expected = 41.0 }, new { offset = 0, actual = 50.0, expected = 42.0 } },
            summary = $"{body.GetProperty("name").GetString()} changed the outcome.",
            explanation = "The comparison tracked it closely.",
            inconclusiveReason = Conclusive ? null : "The range is too wide.",
            nextAdvice = Conclusive ? null : "Measure a longer change.",
            method = "placebo-did/1",
            model = "gemini-pro-test",
            promptVersion = "prove-lab/1",
        });
    }

    private static List<string> Keys(JsonElement body, string name) =>
        body.GetProperty(name).EnumerateArray().Select(key => key.GetString()!).ToList();
}
