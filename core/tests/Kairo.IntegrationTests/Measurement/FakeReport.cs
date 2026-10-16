using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.Measurement;

public sealed class FakeReport
{
    public List<JsonElement> Requests { get; } = [];

    public bool Fails { get; set; }

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) => path == "/prove/report" ? Answer(body) : previous(path, body);
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

        var inconclusive = body.GetProperty("outcome").GetString() == "inconclusive";
        return FakeAgents.Json(new
        {
            summary = $"Outcome {body.GetProperty("outcome").GetString()} for {body.GetProperty("title").GetString()}.",
            nextStepReasoning = $"Next step {body.GetProperty("nextStep").GetString()}.",
            inconclusiveReason = inconclusive ? "The range was too wide." : null,
            nextTestAdvice = inconclusive ? "Run a longer test." : null,
            model = "gemini-pro-test",
            promptVersion = "prove-report/1",
        });
    }
}
