using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed class TextRegenerator(CopyDesk desk, CopyAgent agent) : IAssetRegenerator
{
    public IReadOnlyCollection<string> Kinds => AssetKinds.Text;

    public async Task<AssetRegeneration> RegenerateAsync(AssetRegenerationContext context, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(context.Room, cancellationToken) is not { } copy)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to write ads from.");
        }

        var content = context.Content;
        var answer = await agent.ReviseAsync(
            new CopyInstructionRequest(
                copy.Brand,
                context.Asset.Language,
                context.Instruction,
                context.Asset.Kind,
                Data(context.Asset, content, copy),
                copy.BriefData(),
                copy.ProductData(),
                copy.BrandKit),
            cancellationToken);
        if (answer.Asset is not { } revised)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Create agent didn't return the asset.");
        }

        var next = Apply(context.Asset.Kind, content, revised);
        if (AdLimits.Check(context.Asset.Kind, next).Count > 0)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Create agent's rewrite broke the Google Ads limits.");
        }

        var summary = AdLimits.Tidy(answer.Summary) is { Length: > 0 } said ? CopyDesk.Cut(said, 300) : "Rewrote the asset.";
        return new AssetRegeneration(next, answer.Model, answer.PromptVersion, answer.Usage, summary);
    }

    public static AssetTextData Data(Asset asset, AssetContent content, CopyContext copy) => new(
        asset.AdGroup is { } group ? CopyDesk.Cut(group, 100) : null,
        [.. asset.ProductIds.Select(copy.KeyOf).OfType<string>()],
        [.. (content.Keywords ?? []).Take(50).Select(keyword => new CopyKeyword(CopyDesk.Cut(keyword.Text, 200), keyword.Match))],
        Lines(content.Headlines, 15),
        Lines(content.LongHeadlines, 5),
        Lines(content.Descriptions, 5),
        content.Path1,
        content.Path2,
        content.BusinessName,
        content.CallToAction,
        content.Title is { } title ? CopyDesk.Cut(title, 300) : null,
        content.CurrentTitle is { } current ? CopyDesk.Cut(current, 300) : null);

    public static AssetContent Apply(string kind, AssetContent current, RevisedText revised)
    {
        var next = current.Copy();
        switch (kind)
        {
            case AssetKinds.KeywordSet:
                next.Keywords =
                [
                    .. (revised.Keywords ?? []).OfType<CopyKeyword>()
                        .Select(keyword => new AssetKeyword
                        {
                            Text = AdLimits.Tidy(keyword.Text).ToLowerInvariant(),
                            Match = keyword.Match?.Trim().ToLowerInvariant() is { } match && AdLimits.MatchTypes.Contains(match) ? match : AdLimits.Phrase,
                        })
                        .Where(keyword => keyword.Text.Length > 0),
                ];
                break;
            case AssetKinds.SearchAd:
                next.Headlines = Tidied(revised.Headlines);
                next.Descriptions = Tidied(revised.Descriptions);
                next.Path1 = Optional(revised.Path1);
                next.Path2 = next.Path1 is null ? null : Optional(revised.Path2);
                break;
            case AssetKinds.DemandGenText:
                next.Headlines = Tidied(revised.Headlines);
                next.LongHeadlines = Tidied(revised.LongHeadlines);
                next.Descriptions = Tidied(revised.Descriptions);
                next.BusinessName = Optional(revised.BusinessName) ?? current.BusinessName;
                next.CallToAction = Optional(revised.CallToAction) ?? current.CallToAction;
                break;
            default:
                next.Title = Optional(revised.Title);
                break;
        }

        return next;
    }

    private static List<string> Lines(List<string>? texts, int most) =>
        [.. (texts ?? []).Take(most).Select(text => CopyDesk.Cut(text, 200))];

    private static List<string> Tidied(IReadOnlyList<string?>? texts) =>
        [.. (texts ?? []).Select(AdLimits.Tidy).Where(text => text.Length > 0)];

    private static string? Optional(string? text) => AdLimits.Tidy(text) is { Length: > 0 } value ? value : null;
}
