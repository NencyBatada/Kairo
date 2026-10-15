using System.Text.Json;

namespace Kairo.SharedKernel.Agents;

public sealed record BriefProduct(string Id, string Title, decimal? Price, string? Currency);

public sealed record BriefTrigger(string Source, string Summary, IReadOnlyList<string> Regions, string Until);

public sealed record TriggerBriefRequest(
    string Brand,
    string Market,
    string Category,
    BriefTrigger Trigger,
    IReadOnlyList<BriefProduct> Products,
    JsonElement? BrandKit);

public sealed record DraftedBrief(
    string? Title,
    string? Goal,
    string? Audience,
    string? KeyMessage,
    string? Offer,
    IReadOnlyList<string?>? Regions,
    IReadOnlyList<string?>? ProductIds,
    IReadOnlyList<string?>? Channels,
    string? Timing);

public sealed record AgentUsage(int? InputTokens, int? OutputTokens);

public sealed record TriggerBriefAnswer(DraftedBrief? Brief, string? Model, string? PromptVersion, AgentUsage? Usage = null);

public sealed record CleanBrief(
    string Title,
    string Goal,
    string Audience,
    string KeyMessage,
    string? Offer,
    IReadOnlyList<string> Regions,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<string> Channels,
    string Timing);

public sealed class PlanAgent(AgentClient agents)
{
    public const string BriefRoute = "plan/trigger-brief";
    public const int MaxBriefProducts = 12;

    public static readonly TimeSpan BriefTimeout = TimeSpan.FromSeconds(120);

    public Task<TriggerBriefAnswer> DraftAsync(TriggerBriefRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<TriggerBriefRequest, TriggerBriefAnswer>(BriefRoute, request, BriefTimeout, cancellationToken);

    public static string ProductKey(int index) => $"p{index + 1}";

    public static CleanBrief? Clean(TriggerBriefAnswer answer, IReadOnlyList<(string Key, Guid Id)> products, IReadOnlyList<string> regions)
    {
        if (answer.Brief is not { } drafted
            || Text(drafted.Title, 120) is not { } title
            || Text(drafted.Goal, 500) is not { } goal
            || Text(drafted.Audience, 500) is not { } audience
            || Text(drafted.KeyMessage, 500) is not { } keyMessage
            || Text(drafted.Timing, 300) is not { } timing)
        {
            return null;
        }

        var chosenRegions = (drafted.Regions ?? []).OfType<string>()
            .Select(region => regions.FirstOrDefault(known => string.Equals(known, region.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct()
            .ToList();
        var chosenProducts = (drafted.ProductIds ?? []).OfType<string>()
            .Select(key => products.FirstOrDefault(product => product.Key == key.Trim()))
            .Where(product => product.Key is not null)
            .Select(product => product.Id)
            .Distinct()
            .ToList();
        var channels = (drafted.Channels ?? []).OfType<string>()
            .Select(channel => channel.Trim())
            .Where(channel => channel.Length is > 0 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        return new CleanBrief(
            title,
            goal,
            audience,
            keyMessage,
            Text(drafted.Offer, 300),
            chosenRegions.Count > 0 ? chosenRegions : [.. regions],
            chosenProducts.Count > 0 ? chosenProducts : products.Take(3).Select(product => product.Id).ToList(),
            channels,
            timing);
    }

    private static string? Text(string? value, int length)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > length ? trimmed[..length] : trimmed;
    }
}
