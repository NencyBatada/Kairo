using System.Text.Json;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Triggers.Checks;

internal sealed record BriefProduct(string Id, string Title, decimal? Price, string? Currency);

internal sealed record BriefTrigger(string Source, string Summary, IReadOnlyList<string> Regions, string Until);

internal sealed record TriggerBriefRequest(
    string Brand,
    string Market,
    string Category,
    BriefTrigger Trigger,
    IReadOnlyList<BriefProduct> Products,
    JsonElement? BrandKit);

internal sealed record DraftedBrief(
    string? Title,
    string? Goal,
    string? Audience,
    string? KeyMessage,
    string? Offer,
    IReadOnlyList<string?>? Regions,
    IReadOnlyList<string?>? ProductIds,
    IReadOnlyList<string?>? Channels,
    string? Timing);

internal sealed record TriggerBriefAnswer(DraftedBrief? Brief, string? Model, string? PromptVersion);

internal sealed class PlanAgent(AgentClient agents)
{
    public const string BriefRoute = "plan/trigger-brief";

    public static readonly TimeSpan BriefTimeout = TimeSpan.FromSeconds(120);

    public Task<TriggerBriefAnswer> DraftAsync(TriggerBriefRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<TriggerBriefRequest, TriggerBriefAnswer>(BriefRoute, request, BriefTimeout, cancellationToken);
}
