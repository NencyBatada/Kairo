using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record CopyPart(string Language, IReadOnlyList<string> CampaignTypes, IReadOnlyList<string> Kinds);

internal sealed record LanguageCopy(string Language, IReadOnlyList<string> Kinds, List<GeneratedAsset> Assets);

internal sealed record CopyDraft(int BriefVersion, IReadOnlyList<string> Languages, string? FeedLanguage, List<LanguageCopy> Parts)
{
    public int Count => Parts.Sum(part => part.Assets.Count);
}

internal sealed class CopyWork(
    CampaignRoomsDbContext db,
    CopyDesk desk,
    CopyAgent agent,
    AssetStore store,
    IAuditLog audit) : IStageWork
{
    public const int MaxLockedTexts = 60;
    public const int MaxLockedAssets = 100;

    public static readonly string[] SharedKinds = [AssetKinds.KeywordSet, AssetKinds.SearchAd, AssetKinds.DemandGenText];

    public string Kind => StageKinds.CreateCopy;

    public string Agent => StageAgents.Create;

    public string Stage => RoomStates.Creating;

    public string? NextKind(StageRun run, StageOutcome outcome) => StageKinds.CreateImages;

    public static IReadOnlyList<CopyPart> Parts(IReadOnlyList<string> languages, IReadOnlyList<string> campaignTypes, string? feedLanguage)
    {
        var parts = new List<CopyPart>();
        var shared = campaignTypes.Where(type => type != BriefChoices.Shopping).ToList();
        foreach (var language in languages)
        {
            var feed = language == feedLanguage;
            List<string> types = feed ? [.. campaignTypes] : shared;
            if (types.Count > 0)
            {
                parts.Add(new CopyPart(language, types, feed ? [.. SharedKinds, AssetKinds.ShoppingTitle] : SharedKinds));
            }
        }

        if (feedLanguage is not null && !languages.Contains(feedLanguage))
        {
            parts.Add(new CopyPart(feedLanguage, [BriefChoices.Shopping], [AssetKinds.ShoppingTitle]));
        }

        return parts;
    }

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(room, cancellationToken) is not { } context)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to write ads from.");
        }

        if (context.CampaignTypes.Count == 0)
        {
            throw new StageFailure(StageFailure.NoProducts, "None of the brief's products are in stock, so Kairo can't write shopping titles.");
        }

        var feedLanguage = context.CampaignTypes.Contains(BriefChoices.Shopping) ? context.FeedLanguage() : null;
        var parts = Parts(context.Languages, context.CampaignTypes, feedLanguage);
        var locked = await db.Assets.AsNoTracking()
            .Where(asset => asset.RoomId == room.Id && asset.Locked && asset.Status == AssetStatuses.Active && AssetKinds.Text.Contains(asset.Kind))
            .OrderBy(asset => asset.Key)
            .ToListAsync(cancellationToken);
        var lockedTexts = new Dictionary<string, List<LockedCopyData>>();
        foreach (var asset in locked)
        {
            var list = lockedTexts.TryGetValue(asset.Language, out var found) ? found : lockedTexts[asset.Language] = [];
            if (list.Count < MaxLockedAssets)
            {
                var version = await store.CurrentAsync(asset, cancellationToken);
                list.Add(new LockedCopyData(asset.Kind, asset.AdGroup is { } group ? CopyDesk.Cut(group, 100) : null, Texts(version?.Read())));
            }
        }

        desk.Remember(context);
        var answers = await Task.WhenAll(parts.Select(part => agent.WriteAsync(
            new CopyRequest(
                context.Brand,
                part.Language,
                part.CampaignTypes,
                context.BriefData(),
                context.ProductData(),
                context.Keywords,
                lockedTexts.GetValueOrDefault(part.Language) ?? [],
                context.BrandKit,
                context.MemoryData()),
            cancellationToken)));
        var written = new List<LanguageCopy>();
        foreach (var (part, answer) in parts.Zip(answers))
        {
            var assets = Assets(answer.Assets, context, part.Language);
            if (assets.Count == 0)
            {
                throw new AgentCallException(AgentCallException.BadAnswer, $"The Create agent's {CreativeLanguages.Name(part.Language)} copy broke the Google Ads limits.");
            }

            written.Add(new LanguageCopy(part.Language, part.Kinds, assets));
        }

        var draft = new CopyDraft(context.Brief.Version, context.Languages, feedLanguage, written);
        var names = string.Join(", ", written.Select(part => CreativeLanguages.Name(part.Language)).Distinct());
        return new StageOutcome(
            draft,
            answers.Select(answer => answer.Model).FirstOrDefault(model => model is not null),
            answers.Select(answer => answer.PromptVersion).FirstOrDefault(version => version is not null),
            MediaDesk.Sum([.. answers.Select(answer => answer.Usage)]),
            $"Wrote {draft.Count} ad text assets in {names}");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var draft = (CopyDraft)outcome.Output;
        var (created, updated, removed, kept) = (0, 0, 0, 0);
        foreach (var part in draft.Parts)
        {
            var saved = await store.SaveGeneratedAsync(room, draft.BriefVersion, part.Language, part.Kinds, part.Assets, run, outcome, cancellationToken);
            (created, updated, removed, kept) = (created + saved.Created, updated + saved.Updated, removed + saved.Removed, kept + saved.KeptLocked);
        }

        removed += await store.RetireAsync(room, SharedKinds, draft.Languages, cancellationToken);
        removed += await store.RetireAsync(room, [AssetKinds.ShoppingTitle], draft.FeedLanguage is { } feed ? [feed] : [], cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "asset.generated",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                null,
                new { runId = run.Id, run.Kind, languages = draft.Parts.Select(part => part.Language).ToArray(), Created = created, Updated = updated, Removed = removed, KeptLocked = kept },
                AuditActor.Agent(StageProgress.ActorName(run.Agent), run.Agent)),
            cancellationToken);
    }

    public static List<GeneratedAsset> Assets(CopyAssets? answer, CopyContext context, string language)
    {
        var assets = new List<GeneratedAsset>();
        if (answer is null)
        {
            return assets;
        }

        var urls = context.BriefData().LandingPages
            .Concat(context.Products.Select(entry => entry.Product.Link).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        var fallbackUrl = context.BriefData().LandingPages.FirstOrDefault() ?? context.Products.Select(entry => entry.Product.Link).OfType<string>().FirstOrDefault();
        if (context.CampaignTypes.Contains(BriefChoices.Search))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in (answer.AdGroups ?? []).OfType<CopyAdGroup>())
            {
                var name = CopyDesk.Cut(AdLimits.Tidy(group.Name), 100);
                if (name.Length == 0 || !names.Add(name))
                {
                    continue;
                }

                var products = ProductIds(group.ProductIds, context);
                var keywords = new AssetContent
                {
                    Keywords = [.. (group.Keywords ?? []).OfType<CopyKeyword>().Select(Keyword).Where(keyword => keyword.Text.Length > 0)],
                };
                Add(assets, new GeneratedAsset(KeyFor(AssetKinds.KeywordSet, language, name), AssetKinds.KeywordSet, BriefChoices.Search, name, products, keywords));
                var path1 = Optional(group.Path1);
                var ad = new AssetContent
                {
                    Headlines = Lines(group.Headlines),
                    Descriptions = Lines(group.Descriptions),
                    Path1 = path1,
                    Path2 = path1 is null ? null : Optional(group.Path2),
                    FinalUrl = group.FinalUrl is { } url && urls.Contains(url) ? url : products.Select(UrlOf(context)).OfType<string>().FirstOrDefault() ?? fallbackUrl,
                };
                Add(assets, new GeneratedAsset(KeyFor(AssetKinds.SearchAd, language, name), AssetKinds.SearchAd, BriefChoices.Search, name, products, ad));
            }
        }

        if (context.CampaignTypes.Contains(BriefChoices.DemandGen) && answer.DemandGen is { } demandGen)
        {
            var content = new AssetContent
            {
                Headlines = Lines(demandGen.Headlines),
                LongHeadlines = Lines(demandGen.LongHeadlines),
                Descriptions = Lines(demandGen.Descriptions),
                BusinessName = Optional(demandGen.BusinessName),
                CallToAction = Optional(demandGen.CallToAction),
                FinalUrl = demandGen.FinalUrl is { } url && urls.Contains(url) ? url : fallbackUrl,
            };
            Add(assets, new GeneratedAsset(KeyFor(AssetKinds.DemandGenText, language, BriefChoices.DemandGen), AssetKinds.DemandGenText, BriefChoices.DemandGen, null, [.. context.Products.Select(entry => entry.Product.Id.ToString())], content));
        }

        if (context.CampaignTypes.Contains(BriefChoices.Shopping))
        {
            var seen = new HashSet<Guid>();
            foreach (var title in (answer.ShoppingTitles ?? []).OfType<CopyTitle>())
            {
                if (context.ProductOf(title.ProductId?.Trim()) is not { } product || !seen.Add(product.Id))
                {
                    continue;
                }

                var content = new AssetContent
                {
                    Title = Optional(title.Title),
                    CurrentTitle = product.Title,
                    OfferId = product.OfferId,
                    FinalUrl = product.Link,
                };
                Add(assets, new GeneratedAsset(KeyFor(AssetKinds.ShoppingTitle, language, product.Id.ToString()), AssetKinds.ShoppingTitle, BriefChoices.Shopping, null, [product.Id.ToString()], content));
            }
        }

        return assets;
    }

    public static string KeyFor(string kind, string language, string discriminator) =>
        CopyDesk.Cut($"{kind}:{language}:{discriminator.ToLowerInvariant()}", AssetLimits.Key);

    public static List<string> Texts(AssetContent? content)
    {
        if (content is null)
        {
            return [];
        }

        return
        [
            .. (content.Keywords ?? []).Select(keyword => keyword.Text)
                .Concat(content.Headlines ?? [])
                .Concat(content.LongHeadlines ?? [])
                .Concat(content.Descriptions ?? [])
                .Concat(content.Title is { } title ? [title] : [])
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => CopyDesk.Cut(text, 200))
                .Take(MaxLockedTexts),
        ];
    }

    private static void Add(List<GeneratedAsset> assets, GeneratedAsset asset)
    {
        if (AdLimits.Check(asset.Kind, asset.Content).Count == 0)
        {
            assets.Add(asset);
        }
    }

    private static Func<string, string?> UrlOf(CopyContext context) =>
        id => context.Products.FirstOrDefault(entry => entry.Product.Id.ToString() == id)?.Product.Link;

    private static List<string> ProductIds(IReadOnlyList<string?>? keys, CopyContext context) =>
    [
        .. (keys ?? []).OfType<string>()
            .Select(key => context.ProductOf(key.Trim())?.Id.ToString())
            .OfType<string>()
            .Distinct(),
    ];

    private static AssetKeyword Keyword(CopyKeyword keyword)
    {
        var match = keyword.Match?.Trim().ToLowerInvariant();
        return new AssetKeyword
        {
            Text = AdLimits.Tidy(keyword.Text).ToLowerInvariant(),
            Match = match is not null && AdLimits.MatchTypes.Contains(match) ? match : AdLimits.Phrase,
        };
    }

    private static List<string> Lines(IReadOnlyList<string?>? texts) =>
        [.. (texts ?? []).Select(AdLimits.Tidy).Where(text => text.Length > 0)];

    private static string? Optional(string? text) => AdLimits.Tidy(text) is { Length: > 0 } value ? value : null;
}
