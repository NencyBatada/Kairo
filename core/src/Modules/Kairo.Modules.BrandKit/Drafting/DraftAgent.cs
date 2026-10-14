using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed record AgentBrand(string Name, string Currency);

internal sealed record AgentPage(string Id, string Url, string Title, string Description, IReadOnlyList<string> Headings, string Text);

internal sealed record AgentAd(string Id, IReadOnlyList<string> Headlines, IReadOnlyList<string> Descriptions);

internal sealed record AgentColourCandidate(string Hex, int Count, IReadOnlyList<string> From);

internal sealed record AgentLogoCandidate(string Id, string Url, string From, string Alt);

internal sealed record DraftAgentRequest(
    AgentBrand Brand,
    IReadOnlyList<AgentPage> Pages,
    IReadOnlyList<AgentAd> Ads,
    IReadOnlyList<AgentColourCandidate> ColourCandidates,
    IReadOnlyList<AgentLogoCandidate> LogoCandidates);

internal sealed record AnswerVoice(
    string? Summary,
    IReadOnlyList<string?>? Traits,
    IReadOnlyList<string?>? Dos,
    IReadOnlyList<string?>? Donts,
    IReadOnlyList<string?>? Examples,
    IReadOnlyList<string?>? Sources);

internal sealed record AnswerColour(string? Hex, string? Name, string? Role, IReadOnlyList<string?>? Sources);

internal sealed record AnswerLogo(string? CandidateId);

internal sealed record AnswerMessage(string? Text, IReadOnlyList<string?>? Sources);

internal sealed record AnswerCategory(string? Name, IReadOnlyList<string?>? Sources);

internal sealed record DraftAnswer(
    AnswerVoice? Voice,
    IReadOnlyList<AnswerColour?>? Colours,
    AnswerLogo? Logo,
    IReadOnlyList<AnswerMessage?>? KeyMessages,
    IReadOnlyList<AnswerCategory?>? ProductCategories,
    string? Model,
    string? PromptVersion);

internal sealed class DraftAgent(AgentClient agents)
{
    public const string Route = "brand-kit/draft";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public Task<DraftAnswer> DraftAsync(DraftAgentRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<DraftAgentRequest, DraftAnswer>(Route, request, Timeout, cancellationToken);
}
