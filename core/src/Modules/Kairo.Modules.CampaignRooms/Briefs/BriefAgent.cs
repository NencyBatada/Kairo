using System.Text.Json;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed record SmartRegion(string Name, long? LastMonthSearches, double? Growth);

internal sealed record SmartOpportunity(
    string Topic,
    string Category,
    string Source,
    string Summary,
    IReadOnlyList<string> Keywords,
    long? LastMonthSearches,
    double? Growth,
    IReadOnlyList<SmartRegion> Regions);

internal sealed record SmartProduct(string Id, string Title, decimal? Price, string? Currency);

internal sealed record SmartBudgetGuide(decimal Low, decimal High, decimal Suggested, string Currency, int Days, string Basis);

internal sealed record SmartDateGuide(string Today, string SuggestedStart, string SuggestedEnd, string? LatestEnd);

internal sealed record SmartSeed(string? Title, string? Goal, string? Audience, string? KeyMessage, string? Offer);

internal sealed record SmartMemory(string Id, string Text);

internal sealed record SmartBriefRequest(
    string Brand,
    string Market,
    SmartOpportunity Opportunity,
    IReadOnlyList<SmartProduct> Products,
    SmartBudgetGuide Budget,
    SmartDateGuide Dates,
    IReadOnlyList<string> Languages,
    SmartSeed? Seed,
    IReadOnlyList<SmartMemory> Memory,
    JsonElement? BrandKit);

internal sealed record SmartReasons(
    string? Objective,
    string? Kpi,
    string? CampaignTypes,
    string? Budget,
    string? Dates,
    string? Regions,
    string? Audiences,
    string? KeyMessages,
    string? Products,
    string? Languages)
{
    public string? For(string field) => field switch
    {
        BriefFields.Objective => Objective,
        BriefFields.Kpi => Kpi,
        BriefFields.CampaignTypes => CampaignTypes,
        BriefFields.Budget => Budget,
        BriefFields.Dates => Dates,
        BriefFields.Regions => Regions,
        BriefFields.Audiences => Audiences,
        BriefFields.KeyMessages => KeyMessages,
        BriefFields.Products => Products,
        BriefFields.Languages => Languages,
        _ => null,
    };
}

internal sealed record SmartDraft(
    string? Title,
    string? Objective,
    string? Kpi,
    IReadOnlyList<string?>? CampaignTypes,
    decimal? Budget,
    string? StartDate,
    string? EndDate,
    IReadOnlyList<string?>? Regions,
    IReadOnlyList<string?>? Audiences,
    IReadOnlyList<string?>? KeyMessages,
    IReadOnlyList<string?>? ProductIds,
    IReadOnlyList<string?>? Languages,
    SmartReasons? Reasons,
    IReadOnlyList<string?>? MemoryUsed = null);

internal sealed record SmartBriefAnswer(SmartDraft? Brief, string? Model, string? PromptVersion, AgentUsage? Usage = null);

internal sealed record CurrentBriefData(
    string Title,
    string Objective,
    string Kpi,
    IReadOnlyList<string> CampaignTypes,
    decimal? Budget,
    string Currency,
    string? StartDate,
    string? EndDate,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> KeyMessages,
    IReadOnlyList<string> ProductIds,
    IReadOnlyList<string> Languages);

internal sealed record InstructionOptions(IReadOnlyList<string> Regions, IReadOnlyList<SmartProduct> Products, IReadOnlyList<string> Languages);

internal sealed record BriefInstructionRequest(
    string Brand,
    string Today,
    string Instruction,
    CurrentBriefData Brief,
    InstructionOptions Options,
    JsonElement? BrandKit);

internal sealed record ProposedChanges(
    string? Title,
    string? Objective,
    string? Kpi,
    IReadOnlyList<string?>? CampaignTypes,
    decimal? Budget,
    string? StartDate,
    string? EndDate,
    IReadOnlyList<string?>? Regions,
    IReadOnlyList<string?>? Audiences,
    IReadOnlyList<string?>? KeyMessages,
    IReadOnlyList<string?>? ProductIds,
    IReadOnlyList<string?>? Languages,
    SmartReasons? Reasons,
    string? Summary);

internal sealed record BriefInstructionAnswer(ProposedChanges? Changes, string? Model, string? PromptVersion, AgentUsage? Usage = null);

internal sealed class BriefAgent(AgentClient agents)
{
    public const string DraftRoute = "plan/brief";
    public const string InstructionRoute = "plan/brief-instruction";

    public static readonly TimeSpan DraftTimeout = TimeSpan.FromSeconds(55);
    public static readonly TimeSpan InstructionTimeout = TimeSpan.FromSeconds(45);

    public Task<SmartBriefAnswer> DraftAsync(SmartBriefRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<SmartBriefRequest, SmartBriefAnswer>(DraftRoute, request, DraftTimeout, cancellationToken);

    public Task<BriefInstructionAnswer> InstructAsync(BriefInstructionRequest request, CancellationToken cancellationToken) =>
        agents.PostAsync<BriefInstructionRequest, BriefInstructionAnswer>(InstructionRoute, request, InstructionTimeout, cancellationToken);
}
