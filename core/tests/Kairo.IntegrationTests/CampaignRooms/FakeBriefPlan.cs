using System.Net;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class FakeBriefPlan
{
    public bool Fails { get; set; }

    public List<JsonElement> Requests { get; } = [];

    public List<JsonElement> Instructions { get; } = [];

    public Action? WhileDrafting { get; set; }

    public Action? WhileInstructing { get; set; }

    public Func<JsonElement, object>? Answer { get; set; }

    public Func<JsonElement, object>? Changes { get; set; }

    public static object Reasons => new
    {
        objective = "Searches grew last month.",
        kpi = "Orders measure sales.",
        campaignTypes = "People search for these products.",
        budget = "Searches and bids point to this budget.",
        dates = "Two weeks while demand is high.",
        regions = "These regions search most.",
        audiences = "Commuters search most.",
        keyMessages = "Staying dry matters in the rain.",
        products = "Best sellers for rain.",
        languages = "English reaches most people.",
    };

    public static object Draft(JsonElement request) => new
    {
        title = "Monsoon runners",
        objective = "Sell waterproof shoes while monsoon searches peak.",
        kpi = "orders",
        campaignTypes = new[] { "search", "shopping" },
        budget = request.GetProperty("budget").GetProperty("suggested").GetDecimal(),
        startDate = request.GetProperty("dates").GetProperty("suggestedStart").GetString(),
        endDate = request.GetProperty("dates").GetProperty("suggestedEnd").GetString(),
        regions = request.GetProperty("opportunity").GetProperty("regions").EnumerateArray().Select(region => region.GetProperty("name").GetString()).ToArray(),
        audiences = new[] { "Commuters caught in the rain" },
        keyMessages = new[] { "Stay dry on the way to work.", "Dries in minutes." },
        productIds = new[] { "p1", "p9" },
        languages = new[] { "en" },
        reasons = Reasons,
    };

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) =>
        {
            if (path is not ("/plan/brief" or "/plan/brief-instruction"))
            {
                return previous(path, body);
            }

            var drafting = path == "/plan/brief";
            lock (Requests)
            {
                (drafting ? Requests : Instructions).Add(body);
                if (Fails)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                }
            }

            if (!drafting)
            {
                WhileInstructing?.Invoke();
                return FakeAgents.Json(new
                {
                    changes = Changes?.Invoke(body) ?? new { reasons = new { }, summary = "Nothing to change." },
                    model = "gemini-flash-test",
                    promptVersion = "plan-brief-instruction/1",
                    usage = new { inputTokens = 300, outputTokens = 80 },
                });
            }

            WhileDrafting?.Invoke();
            return FakeAgents.Json(new
            {
                brief = Answer?.Invoke(body) ?? Draft(body),
                model = "gemini-test",
                promptVersion = "plan-brief/1",
                usage = new { inputTokens = 812, outputTokens = 240 },
            });
        };
    }
}
