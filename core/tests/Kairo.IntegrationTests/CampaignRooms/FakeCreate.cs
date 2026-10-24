using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.CampaignRooms;

public sealed class FakeCreate
{
    public bool Fails { get; set; }
    public string? ErrorCode { get; set; }

    public List<JsonElement> Requests { get; } = [];

    public List<JsonElement> Instructions { get; } = [];

    public Func<JsonElement, object>? Answer { get; set; }

    public Func<JsonElement, object>? Revision { get; set; }

    public static object Copy(JsonElement request)
    {
        var types = request.GetProperty("campaignTypes").EnumerateArray().Select(type => type.GetString()).ToList();
        var products = request.GetProperty("products").EnumerateArray().ToList();
        return new
        {
            adGroups = types.Contains("search")
                ? new[]
                {
                    new
                    {
                        name = "Rain runners",
                        productIds = new[] { "p1", "p9" },
                        keywords = new[] { new { text = "waterproof shoes", match = "phrase" }, new { text = "rain shoes", match = "exact" } },
                        headlines = new[] { "Waterproof Running Shoes", "Stay Dry On Your Commute", "Dries In Minutes" },
                        descriptions = new[] { "Mesh runners that keep your feet dry.", "Dries in minutes after the rain." },
                        path1 = "rain-shoes",
                        path2 = "runners",
                        finalUrl = "https://elsewhere.example/landing",
                    },
                }
                : [],
            demandGen = types.Contains("demand_gen")
                ? new
                {
                    headlines = new[] { "Dry feet all monsoon", "Monsoon-ready shoes that dry fast" },
                    longHeadlines = new[] { "Waterproof runners for every rainy commute" },
                    descriptions = new[] { "Mesh that keeps water out and dries fast." },
                    businessName = "Acme",
                    callToAction = "shop_now",
                    finalUrl = (string?)null,
                }
                : null,
            shoppingTitles = types.Contains("shopping")
                ? products.Select(product => new
                {
                    productId = product.GetProperty("id").GetString(),
                    title = $"Acme {product.GetProperty("title").GetString()} - Waterproof",
                }).ToArray()
                : [],
        };
    }

    public static object Revised(JsonElement request) => new
    {
        keywords = new[] { new { text = "quick dry shoes", match = "phrase" } },
        headlines = new[] { "Quick-Dry Running Shoes", "Dries In Minutes", "Stay Dry Today" },
        longHeadlines = Array.Empty<string>(),
        descriptions = new[] { "Quick-dry mesh keeps feet dry.", "Dries in minutes after rain." },
        path1 = "quick-dry",
        path2 = (string?)null,
        businessName = (string?)null,
        callToAction = (string?)null,
        title = "Acme Runner - Quick-Dry Mesh",
    };

    public void Attach(FakeAgents agents)
    {
        var previous = agents.Respond;
        agents.Respond = (path, body) =>
        {
            if (path is not ("/create/copy" or "/create/copy-instruction"))
            {
                return previous(path, body);
            }

            var writing = path == "/create/copy";
            lock (Requests)
            {
                (writing ? Requests : Instructions).Add(body);
                if (ErrorCode is { } code)
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = JsonContent.Create(new { error = code }) };
                if (Fails)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                }
            }

            if (!writing)
            {
                return FakeAgents.Json(new
                {
                    asset = Revision?.Invoke(body) ?? Revised(body),
                    summary = "Led with quick-dry.",
                    model = "gemini-flash-test",
                    promptVersion = "create-copy-instruction/1",
                    usage = new { inputTokens = 400, outputTokens = 90 },
                });
            }

            return FakeAgents.Json(new
            {
                assets = Answer?.Invoke(body) ?? Copy(body),
                model = "gemini-flash-test",
                promptVersion = "create-copy/1",
                usage = new { inputTokens = 1200, outputTokens = 400 },
            });
        };
    }
}
