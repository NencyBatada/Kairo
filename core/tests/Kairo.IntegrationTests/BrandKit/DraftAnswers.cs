using System.Text.Json;
using Kairo.IntegrationTests.Agents;

namespace Kairo.IntegrationTests.BrandKit;

internal static class DraftAnswers
{
    public const string Summary = "Friendly and practical, like a running coach.";

    public static HttpResponseMessage From(JsonElement request)
    {
        var colours = request.GetProperty("colourCandidates");
        var logos = request.GetProperty("logoCandidates");
        var pages = request.GetProperty("pages").GetArrayLength();
        var ads = request.GetProperty("ads").GetArrayLength();
        string[] first = pages > 0 ? ["p1"] : ["a1"];
        return FakeAgents.Json(new
        {
            voice = new { summary = Summary, traits = new[] { "warm" }, dos = new[] { "Talk about comfort" }, donts = Array.Empty<string>(), examples = Array.Empty<string>(), sources = first },
            colours = colours.GetArrayLength() == 0
                ? Array.Empty<object>()
                : new object[]
                {
                    new { hex = colours[0].GetProperty("hex").GetString(), name = "Ember", role = "primary", sources = first },
                    new { hex = "#123456", name = "Invented", role = "accent", sources = first },
                },
            logo = logos.GetArrayLength() == 0 ? null : new { candidateId = logos[0].GetProperty("id").GetString() },
            keyMessages = new object[]
            {
                new { text = "Free returns for 30 days", sources = ads > 0 ? new[] { "a1" } : first },
                new { text = "Invented claim", sources = new[] { "p99" } },
            },
            productCategories = pages >= 3 ? new object[] { new { name = "Running shoes", sources = new[] { "p3" } } } : Array.Empty<object>(),
            model = "gemini-test",
            promptVersion = "brand-kit-draft/1",
        });
    }
}
