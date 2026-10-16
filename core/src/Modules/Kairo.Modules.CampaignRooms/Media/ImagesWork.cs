using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Briefs;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed class ImagesWork(
    CampaignRoomsDbContext db,
    MediaDesk desk,
    MediaAgent agent,
    AssetStore store,
    IAuditLog audit) : IStageWork
{
    public const int MaxProducts = 4;
    public const int LifestyleProducts = 2;
    public const string ProductStyle = "product";
    public const string LifestyleStyle = "lifestyle";

    public static readonly string[] Kinds = [AssetKinds.Image];

    public string Kind => StageKinds.CreateImages;

    public string Agent => StageAgents.Create;

    public string Stage => RoomStates.Creating;

    public string? NextKind(StageRun run, StageOutcome outcome) =>
        outcome.Output is MediaDraft { Video: true } ? StageKinds.CreateVideo : StageKinds.Check;

    public static IReadOnlyList<string> Formats(IReadOnlyCollection<string> campaignTypes)
    {
        var formats = new List<string>();
        if (campaignTypes.Contains(BriefChoices.Search) || campaignTypes.Contains(BriefChoices.DemandGen))
        {
            formats.AddRange([AssetFormats.Landscape, AssetFormats.Square]);
        }

        if (campaignTypes.Contains(BriefChoices.DemandGen))
        {
            formats.Add(AssetFormats.Portrait);
        }

        return formats;
    }

    public static string KeyFor(string language, Guid productId, string style, string format) =>
        MediaDesk.KeyFor(AssetKinds.Image, language, $"{productId}:{style}:{format}");

    public static string StyleOf(Asset asset) =>
        asset.Key.Split(':') is { Length: 5 } parts && parts[3] is ProductStyle or LifestyleStyle ? parts[3] : ProductStyle;

    public static string CampaignTypeFor(IReadOnlyCollection<string> campaignTypes) =>
        campaignTypes.Contains(BriefChoices.DemandGen) ? BriefChoices.DemandGen : BriefChoices.Search;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        if (await desk.ReadAsync(room, cancellationToken) is not { } context)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to make images from.");
        }

        var video = await desk.VideoWantedAsync(context, cancellationToken);
        var formats = Formats(context.CampaignTypes);
        if (formats.Count == 0)
        {
            return new StageOutcome(new MediaDraft(context.Brief.Version, CreativeLanguages.None, [], video, 0), null, null, null, "No image ads are needed for this campaign.");
        }

        var locked = (await db.Assets.AsNoTracking()
            .Where(asset => asset.RoomId == room.Id && asset.Language == CreativeLanguages.None && asset.Kind == AssetKinds.Image && asset.Locked && asset.Status == AssetStatuses.Active)
            .Select(asset => asset.Key)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var products = await desk.PhotosAsync(context.Products, MaxProducts, cancellationToken);
        if (products.Count == 0)
        {
            throw MediaDesk.NoPhotos();
        }

        var shots = Shots(products, formats, video, CreativeLanguages.None, locked);
        if (shots.Count == 0)
        {
            return new StageOutcome(new MediaDraft(context.Brief.Version, CreativeLanguages.None, null, video, 0), null, null, null, "Every image is locked, so Kairo kept them.");
        }

        var answer = await agent.ImagesAsync(
            new ImagesRequest(
                MediaDesk.Brief(context),
                context.BrandKit,
                [.. products.Where(entry => shots.Any(shot => shot.ProductId == entry.Product.Key)).Select(Reference)],
                shots,
                null),
            cancellationToken);
        var assets = await SaveFilesAsync(room, context, products, shots, answer.Images ?? [], cancellationToken);
        if (assets.Count == 0)
        {
            throw new AgentCallException(AgentCallException.BadAnswer, "The Create agent didn't return any images.");
        }

        var failed = answer.Failures?.Count ?? 0;
        return new StageOutcome(
            new MediaDraft(context.Brief.Version, CreativeLanguages.None, assets, video, failed),
            answer.Model,
            answer.PromptVersion,
            answer.Usage,
            failed == 0 ? $"Made {assets.Count} images" : $"Made {assets.Count} images; {failed} sizes didn't work");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var draft = (MediaDraft)outcome.Output;
        if (draft.Assets is null)
        {
            return;
        }

        var saved = await store.SaveGeneratedAsync(room, draft.BriefVersion, draft.Language, Kinds, draft.Assets, run, outcome, cancellationToken);
        var retired = await store.RetireAsync(room, Kinds, [draft.Language], cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "asset.generated",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                null,
                new { runId = run.Id, run.Kind, language = draft.Language, saved.Created, saved.Updated, Removed = saved.Removed + retired, saved.KeptLocked, failed = draft.Failed },
                AuditActor.Agent(StageProgress.ActorName(run.Agent), run.Agent)),
            cancellationToken);
    }

    public static List<ImageShot> Shots(IReadOnlyList<ProductPhotos> products, IReadOnlyList<string> formats, bool video, string language, IReadOnlySet<string> locked)
    {
        var shots = new List<ImageShot>();
        foreach (var (entry, index) in products.Select((entry, index) => (entry, index)))
        {
            Add(entry, ProductStyle, formats);
            if (index < LifestyleProducts)
            {
                Add(entry, LifestyleStyle, video ? [.. formats, AssetFormats.Vertical] : formats);
            }
        }

        return shots;

        void Add(ProductPhotos entry, string style, IReadOnlyList<string> wanted)
        {
            var open = wanted.Where(format => !locked.Contains(KeyFor(language, entry.Product.Product.Id, style, format))).ToList();
            if (open.Count > 0)
            {
                shots.Add(new ImageShot($"s{shots.Count + 1}", entry.Product.Key, style, open));
            }
        }
    }

    private static MediaReferenceProduct Reference(ProductPhotos entry) => new(
        entry.Product.Key,
        CopyDesk.Cut(entry.Product.Product.Title, 300),
        CopyDesk.Cut(entry.Product.Product.Description, 2000),
        entry.Photos);

    private async Task<List<GeneratedAsset>> SaveFilesAsync(
        Room room,
        CopyContext context,
        IReadOnlyList<ProductPhotos> products,
        IReadOnlyList<ImageShot> shots,
        IReadOnlyList<GeneratedImage> images,
        CancellationToken cancellationToken)
    {
        var assets = new List<GeneratedAsset>();
        var campaignType = CampaignTypeFor(context.CampaignTypes);
        foreach (var image in images)
        {
            var shot = shots.FirstOrDefault(shot => shot.Id == image.ShotId);
            var product = products.FirstOrDefault(entry => entry.Product.Key == shot?.ProductId)?.Product.Product;
            if (shot is null || product is null || !shot.Formats.Contains(image.Format) || image.Data.Length == 0)
            {
                continue;
            }

            var media = await desk.SaveAsync(room.BrandId, image.Data, "image/jpeg", cancellationToken);
            assets.Add(new GeneratedAsset(
                KeyFor(CreativeLanguages.None, product.Id, shot.Style, image.Format),
                AssetKinds.Image,
                campaignType,
                null,
                [product.Id.ToString()],
                new AssetContent { Media = media },
                image.Format,
                image.Width,
                image.Height));
        }

        return assets;
    }
}
