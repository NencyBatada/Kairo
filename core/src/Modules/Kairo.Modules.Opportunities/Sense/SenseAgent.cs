using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Opportunities.Sense;

internal sealed record SenseMarket(string Country, string Language);

internal sealed record SenseKeyword(string Text, long LastMonthSearches, double Growth);

internal sealed record SenseCategory(string Name, int InStock, IReadOnlyList<string> SampleTitles);

internal sealed record SenseDismissal(string Topic, string Reason);

internal sealed record MatchRequest(
    string Brand,
    SenseMarket Market,
    IReadOnlyList<SenseKeyword> Keywords,
    IReadOnlyList<SenseCategory> Categories,
    IReadOnlyList<SenseDismissal> Dismissed,
    IReadOnlyList<string> Watched);

internal sealed record MatchedTopic(string? Name, IReadOnlyList<string?>? Keywords, IReadOnlyList<string?>? Categories, double? Fit, string? FitReason);

internal sealed record MatchAnswer(IReadOnlyList<MatchedTopic?>? Topics, string? Model, string? PromptVersion);

internal sealed record ExplainTopic(string Id, string Name, IReadOnlyList<string> Keywords, double Growth, double? Yoy);

internal sealed record ExplainRequest(string Brand, SenseMarket Market, IReadOnlyList<ExplainTopic> Topics);

internal sealed record ExplainedSource(string? Title, string? Url);

internal sealed record ExplainedReason(string? Text, IReadOnlyList<ExplainedSource?>? Sources);

internal sealed record ExplainedTopic(string? Id, IReadOnlyList<ExplainedReason?>? Reasons);

internal sealed record ExplainAnswer(IReadOnlyList<ExplainedTopic?>? Topics, string? Model, string? PromptVersion);

internal sealed class SenseAgent(AgentClient agents)
{
    public const string MatchRoute = "sense/match";
    public const string ExplainRoute = "sense/explain";

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan ExplainTimeout = TimeSpan.FromSeconds(180);

    public Task<MatchAnswer> MatchAsync(MatchRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<MatchRequest, MatchAnswer>(MatchRoute, request, MatchTimeout, cancellationToken);

    public Task<ExplainAnswer> ExplainAsync(ExplainRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<ExplainRequest, ExplainAnswer>(ExplainRoute, request, ExplainTimeout, cancellationToken);
}
