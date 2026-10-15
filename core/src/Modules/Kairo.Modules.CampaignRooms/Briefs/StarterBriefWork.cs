using System.Globalization;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Catalogue;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed class StarterBriefWork(
    CampaignRoomsDbContext db,
    PlanAgent plan,
    ICatalogue catalogue,
    IBrandKit brandKit,
    IBrandDirectory brands,
    TimeProvider time) : IStageWork
{
    public const string Market = "India";
    public const string RadarSource = "radar";
    public const int MaxCategoryLength = 750;
    public const int MaxSummaryLength = 500;

    public static readonly TimeSpan RadarWindow = TimeSpan.FromDays(28);

    public string Kind => StageKinds.StarterBrief;

    public string Agent => StageAgents.Plan;

    public string Stage => RoomStates.BriefDraft;

    public string? FollowUp => StageKinds.ProofPlan;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        var evidence = RoomEvidence.Read(room.Evidence);
        if (FromTrigger(evidence.Brief) is { } drafted)
        {
            return new StageOutcome(
                new StarterBrief(drafted, BriefOrigins.Trigger),
                evidence.Brief!.Model,
                evidence.Brief.PromptVersion,
                null,
                "Used the brief drafted when the trigger fired");
        }

        var ids = (evidence.Products ?? []).Select(product => product.Id).Where(id => id != Guid.Empty).Distinct().ToList();
        var products = (await catalogue.GetInStockAsync(ids, cancellationToken)).Take(PlanAgent.MaxBriefProducts).ToList();
        if (products.Count == 0)
        {
            throw new StageFailure(StageFailure.NoProducts, "None of this campaign's products are in stock, so Kairo can't draft a brief.");
        }

        var keyed = products.Select((product, index) => (Key: PlanAgent.ProductKey(index), Product: product)).ToList();
        var regions = evidence.RegionNames() is { Count: > 0 } named ? named : [Market];
        var brand = (await brands.GetAsync([room.BrandId], cancellationToken)).Single();
        var kit = await brandKit.GetApprovedAsync(cancellationToken);
        var answer = await plan.DraftAsync(
            new TriggerBriefRequest(
                brand.Name,
                Market,
                Category(evidence, room),
                new BriefTrigger(
                    evidence.Trigger?.Source ?? RadarSource,
                    Summary(evidence, room),
                    regions,
                    Until(evidence, brand)),
                keyed.Select(entry => new BriefProduct(entry.Key, entry.Product.Title, entry.Product.SalePrice ?? entry.Product.Price, entry.Product.Currency)).ToList(),
                kit?.Content),
            cancellationToken);
        if (PlanAgent.Clean(answer, keyed.Select(entry => (entry.Key, entry.Product.Id)).ToList(), regions) is not { } clean)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Plan agent's brief is missing required fields.");
        }

        return new StageOutcome(
            new StarterBrief(Content(clean), BriefOrigins.Starter),
            answer.Model,
            answer.PromptVersion,
            answer.Usage,
            "Drafted a first brief");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var starter = (StarterBrief)outcome.Output;
        var latest = await db.Briefs.Where(brief => brief.RoomId == room.Id).MaxAsync(brief => (int?)brief.Version, cancellationToken) ?? 0;
        db.Briefs.Add(new Brief
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            Version = latest + 1,
            Content = starter.Content,
            Origin = starter.Origin,
            StageRunId = run.Id,
            Model = outcome.Model,
            PromptVersion = outcome.PromptVersion,
            CreatedAt = time.GetUtcNow(),
        });
    }

    public static BriefContent? FromTrigger(EvidenceBrief? brief)
    {
        if (brief is null
            || string.IsNullOrWhiteSpace(brief.Title)
            || string.IsNullOrWhiteSpace(brief.Goal)
            || string.IsNullOrWhiteSpace(brief.Audience)
            || string.IsNullOrWhiteSpace(brief.KeyMessage)
            || string.IsNullOrWhiteSpace(brief.Timing))
        {
            return null;
        }

        return new BriefContent
        {
            Title = brief.Title.Trim(),
            Goal = brief.Goal.Trim(),
            Audience = brief.Audience.Trim(),
            KeyMessage = brief.KeyMessage.Trim(),
            Offer = string.IsNullOrWhiteSpace(brief.Offer) ? null : brief.Offer.Trim(),
            Regions = [.. brief.Regions ?? []],
            ProductIds = [.. (brief.ProductIds ?? []).Select(id => id.ToString())],
            Channels = [.. brief.Channels ?? []],
            Timing = brief.Timing.Trim(),
        };
    }

    public static BriefContent Content(CleanBrief clean) => new()
    {
        Title = clean.Title,
        Goal = clean.Goal,
        Audience = clean.Audience,
        KeyMessage = clean.KeyMessage,
        Offer = clean.Offer,
        Regions = [.. clean.Regions],
        ProductIds = [.. clean.ProductIds.Select(id => id.ToString())],
        Channels = [.. clean.Channels],
        Timing = clean.Timing,
    };

    private static string Category(RoomEvidence evidence, Room room)
    {
        var text = evidence.Categories is { Length: > 0 } categories ? string.Join(", ", categories) : evidence.Topic ?? room.Title;
        return Cut(text, MaxCategoryLength);
    }

    private static string Summary(RoomEvidence evidence, Room room)
    {
        if (evidence.Trigger?.Summary is { Length: > 0 } triggered)
        {
            return Cut(triggered, MaxSummaryLength);
        }

        var topic = evidence.Topic ?? room.Title;
        var text = string.IsNullOrWhiteSpace(evidence.Reason)
            ? $"Searches for {topic} are rising."
            : $"Searches for {topic} are rising. {evidence.Reason.Trim()}";
        return Cut(text, MaxSummaryLength);
    }

    private string Until(RoomEvidence evidence, BrandSummary brand)
    {
        var end = evidence.Trigger?.ConditionEndsAt?.AddTicks(-1) ?? time.GetUtcNow() + RadarWindow;
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(brand.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(end, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Cut(string text, int length)
    {
        var trimmed = text.Trim();
        return trimmed.Length > length ? trimmed[..length] : trimmed;
    }
}

internal sealed record StarterBrief(BriefContent Content, string Origin);
