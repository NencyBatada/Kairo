using System.Text.Json;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record CopyProductData(
    string Id,
    string Title,
    string? Description,
    string? Brand,
    string? Category,
    decimal? Price,
    decimal? SalePrice,
    string? Currency,
    string? Link,
    string? Gtin);

internal sealed record CopyBriefData(
    string Title,
    string Objective,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> KeyMessages,
    string? Offer,
    IReadOnlyList<string> LandingPages);

internal sealed record LockedCopyData(string Kind, string? AdGroup, IReadOnlyList<string> Texts);

internal sealed record CopyRequest(
    string Brand,
    string Language,
    IReadOnlyList<string> CampaignTypes,
    CopyBriefData Brief,
    IReadOnlyList<CopyProductData> Products,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<LockedCopyData> Locked,
    JsonElement? BrandKit);

internal sealed record CopyKeyword(string? Text, string? Match);

internal sealed record CopyAdGroup(
    string? Name,
    IReadOnlyList<string?>? ProductIds,
    IReadOnlyList<CopyKeyword?>? Keywords,
    IReadOnlyList<string?>? Headlines,
    IReadOnlyList<string?>? Descriptions,
    string? Path1,
    string? Path2,
    string? FinalUrl);

internal sealed record CopyDemandGen(
    IReadOnlyList<string?>? Headlines,
    IReadOnlyList<string?>? LongHeadlines,
    IReadOnlyList<string?>? Descriptions,
    string? BusinessName,
    string? CallToAction,
    string? FinalUrl);

internal sealed record CopyTitle(string? ProductId, string? Title);

internal sealed record CopyAssets(IReadOnlyList<CopyAdGroup?>? AdGroups, CopyDemandGen? DemandGen, IReadOnlyList<CopyTitle?>? ShoppingTitles);

internal sealed record CopyAnswer(CopyAssets? Assets, string? Model, string? PromptVersion, AgentUsage? Usage = null);

internal sealed record AssetTextData(
    string? AdGroup,
    IReadOnlyList<string> ProductIds,
    IReadOnlyList<CopyKeyword> Keywords,
    IReadOnlyList<string> Headlines,
    IReadOnlyList<string> LongHeadlines,
    IReadOnlyList<string> Descriptions,
    string? Path1,
    string? Path2,
    string? BusinessName,
    string? CallToAction,
    string? Title,
    string? CurrentTitle);

internal sealed record CopyInstructionRequest(
    string Brand,
    string Language,
    string Instruction,
    string Kind,
    AssetTextData Current,
    CopyBriefData Brief,
    IReadOnlyList<CopyProductData> Products,
    JsonElement? BrandKit);

internal sealed record RevisedText(
    IReadOnlyList<CopyKeyword?>? Keywords,
    IReadOnlyList<string?>? Headlines,
    IReadOnlyList<string?>? LongHeadlines,
    IReadOnlyList<string?>? Descriptions,
    string? Path1,
    string? Path2,
    string? BusinessName,
    string? CallToAction,
    string? Title);

internal sealed record CopyInstructionAnswer(RevisedText? Asset, string? Summary, string? Model, string? PromptVersion, AgentUsage? Usage = null);

internal sealed class CopyAgent(AgentClient agents, ProvenanceTrail trail)
{
    public const string CopyRoute = "create/copy";
    public const string InstructionRoute = "create/copy-instruction";

    public static readonly TimeSpan CopyTimeout = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan InstructionTimeout = TimeSpan.FromSeconds(60);

    public Task<CopyAnswer> WriteAsync(CopyRequest request, CancellationToken cancellationToken)
    {
        trail.Called(CopyRoute, request, request.Language);
        return agents.PostAsync<CopyRequest, CopyAnswer>(CopyRoute, request, CopyTimeout, cancellationToken);
    }

    public Task<CopyInstructionAnswer> ReviseAsync(CopyInstructionRequest request, CancellationToken cancellationToken)
    {
        trail.Called(InstructionRoute, request, request.Language);
        return agents.PostAsync<CopyInstructionRequest, CopyInstructionAnswer>(InstructionRoute, request, InstructionTimeout, cancellationToken);
    }
}
