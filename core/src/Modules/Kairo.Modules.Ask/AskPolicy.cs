using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Contracts.Ask;

namespace Kairo.Modules.Ask;

internal sealed record Evidence(string Id, AskTarget Target, string Title, string Path, JsonElement Data, string[]? Sources = null);
internal sealed record AnswerSegment(string Text, string[] EvidenceIds);
internal sealed record ToolRequest(string Operation, AskTarget? Target = null, JsonElement? Changes = null, string? Text = null, string[]? EvidenceIds = null);

internal static partial class AskPolicy
{
    public const int MaxTools = 24;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan InvocationLifetime = TimeSpan.FromMinutes(5);
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, AskJson.Options)!;
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, AskJson.Options);
    public static Guid? Campaign(AskTarget target) => target.Kind switch
    {
        "campaign" or "brief" or "report" => target.Id,
        "asset" => target.CampaignId,
        _ => null,
    };

    public static string[] Sources(JsonElement data)
    {
        var links = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonElement value)
        {
            if (links.Count >= 20) return;
            if (value.ValueKind == JsonValueKind.String && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var url)
                && url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo)) links.Add(url.AbsoluteUri);
            else if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject()) Visit(property.Value);
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Visit(item);
        }
        Visit(data);
        return [.. links];
    }

    public static void Target(AskTarget target)
    {
        if (!new[] { "workspace", "campaign", "opportunity", "brief", "asset", "report", "impact_lab", "brand_kit", "catalogue", "trigger_rule", "brand_memory" }.Contains(target.Kind)
            || (target.Kind is not ("workspace" or "brand_kit") && target.Id is null)
            || (target.Kind == "asset" && target.CampaignId is null))
            throw new AskResourceException("Choose a supported item in this workspace.", 400);
    }

    public static AnswerSegment Segment(string? text, string[]? ids, IReadOnlyList<Evidence> evidence)
    {
        var value = text?.Trim() ?? "";
        if (value.Length is < 1 or > 4000 || UnsafeLink().IsMatch(value))
            throw new AskResourceException("Answer text must use evidence links supplied by core.", 400);
        var citations = ids?.Distinct().ToArray() ?? [];
        if (citations.Length > 20 || citations.Any(id => !evidence.Any(item => item.Id == id)))
            throw new AskResourceException("The answer cites evidence that wasn't retrieved.", 400);
        if (citations.Length == 0 && !Uncertain().IsMatch(value))
            throw new AskResourceException("Cite retrieved evidence, or explain that the evidence is insufficient.", 400);
        return new AnswerSegment(value, citations);
    }

    [GeneratedRegex(@"https?://|javascript:|data:|\]\s*\(|<\s*/?\s*[a-z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeLink();

    [GeneratedRegex(@"don't know|do not know|not enough|insufficient|can't|cannot|approval|approve|launch|spend|preview|confirm|couldn't|could not|unavailable", RegexOptions.IgnoreCase)]
    private static partial Regex Uncertain();
}
