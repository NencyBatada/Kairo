using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Assets;

internal enum AssetError
{
    None,
    NotFound,
    Stale,
    NotAllowed,
    Invalid,
}

internal sealed record AssetResult<T>(T? Value, AssetError Error, string? Message = null, Dictionary<string, string[]>? Errors = null)
    where T : class
{
    public static AssetResult<T> Done(T value) => new(value, AssetError.None);

    public static AssetResult<T> Failed(AssetError error, string? message = null) => new(null, error, message);

    public static AssetResult<T> Invalid(Dictionary<string, string[]> errors) => new(null, AssetError.Invalid, null, errors);
}

internal sealed class AssetEditor(
    CampaignRoomsDbContext db,
    RoomWorkflow workflow,
    StageQueue queue,
    AssetStore store,
    IEnumerable<IAssetRegenerator> regenerators,
    IAuditLog audit,
    TimeProvider time,
    CurrentMember member)
{
    public const string NotCreating = "Assets can be changed only while Kairo is creating them.";
    public const string AgentBusy = "Wait for the Create agent to finish.";
    public const string StaleAsset = "This asset changed since you opened it. Refresh and try again.";

    private static readonly Dictionary<string, string[]> EditableFields = new()
    {
        [AssetKinds.KeywordSet] = ["keywords"],
        [AssetKinds.SearchAd] = ["headlines", "descriptions", "path1", "path2"],
        [AssetKinds.DemandGenText] = ["headlines", "longHeadlines", "descriptions", "businessName", "callToAction"],
        [AssetKinds.ShoppingTitle] = ["title"],
    };

    public static string? EditBlocker(Room room) =>
        room.State != RoomStates.Creating ? NotCreating
        : room.RunStatus is { } status && StageRunStatuses.IsActive(status) ? AgentBusy
        : null;

    public async Task<AssetSetView?> ListAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == roomId, cancellationToken);
        if (room is null)
        {
            return null;
        }

        var current = await (
            from asset in db.Assets.AsNoTracking()
            where asset.RoomId == roomId && asset.Status == AssetStatuses.Active && !asset.Hidden
            join version in db.AssetVersions.AsNoTracking()
                on new { AssetId = asset.Id, Version = asset.CurrentVersion } equals new { version.AssetId, version.Version }
            select new { asset, version }).ToListAsync(cancellationToken);
        var views = current.Select(row => AssetViews.View(row.asset, row.version, room.BrandId)).ToList();
        var blocker = EditBlocker(room);
        return new AssetSetView(
            room.Id,
            room.State,
            member.Has(BrandRoles.Marketer) && blocker is null && views.Count > 0,
            blocker,
            [.. views.Select(view => view.Language).Distinct().Order(StringComparer.Ordinal)],
            AssetViews.Groups(views));
    }

    public async Task<AssetResult<AssetView>> EditAsync(Guid roomId, Guid assetId, AssetEditRequest request, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, assetId, request.Version!.Value, true, cancellationToken) is not { } opened)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotFound);
        }

        if (opened.Error is { } failure)
        {
            return AssetResult<AssetView>.Failed(failure.Error, failure.Message);
        }

        var (room, asset) = (opened.Room, opened.Asset);
        if (asset.Locked)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, "Unlock this asset before editing it.");
        }

        if (!EditableFields.TryGetValue(asset.Kind, out var allowed))
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, "Only text assets can be edited here.");
        }

        var current = await store.CurrentAsync(asset, cancellationToken);
        var content = current!.Read();
        var errors = new Dictionary<string, string[]>();
        var fields = Provided(request);
        foreach (var field in fields.Where(field => !allowed.Contains(field)))
        {
            errors[field] = ["This kind of asset doesn't have this field."];
        }

        if (fields.Count == 0)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, "Nothing in the asset changed.");
        }

        if (errors.Count > 0)
        {
            return AssetResult<AssetView>.Invalid(errors);
        }

        var next = Apply(content, request);
        var problems = AdLimits.Check(asset.Kind, next);
        if (problems.Count > 0)
        {
            return AssetResult<AssetView>.Invalid(problems);
        }

        if (next.SameAs(content))
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, "Nothing in the asset changed.");
        }

        var version = store.AddVersion(asset, next, AssetOrigins.Edited, null, null, null, member.UserId, null, fields);
        await FinishAsync(room, new AuditRecord("asset.edited", RoomWorkflow.AuditTarget, room.Id.ToString(), new { assetId = asset.Id, version = current.Version }, new { assetId = asset.Id, asset.Kind, version = version.Version, fields }), cancellationToken);
        return AssetResult<AssetView>.Done(AssetViews.View(asset, version, room.BrandId));
    }

    public async Task<AssetResult<AssetView>> LockAsync(Guid roomId, Guid assetId, int version, bool locked, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, assetId, version, false, cancellationToken) is not { } opened)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotFound);
        }

        if (opened.Error is { } failure)
        {
            return AssetResult<AssetView>.Failed(failure.Error, failure.Message);
        }

        var (room, asset) = (opened.Room, opened.Asset);
        if (asset.Locked == locked)
        {
            return AssetResult<AssetView>.Failed(AssetError.NotAllowed, locked ? "This asset is already locked." : "This asset isn't locked.");
        }

        asset.Locked = locked;
        asset.LockedBy = locked ? member.UserId : null;
        asset.LockedAt = locked ? time.GetUtcNow() : null;
        var current = await store.CurrentAsync(asset, cancellationToken);
        await FinishAsync(room, new AuditRecord(locked ? "asset.locked" : "asset.unlocked", RoomWorkflow.AuditTarget, room.Id.ToString(), null, new { assetId = asset.Id, asset.Kind, version = asset.CurrentVersion }), cancellationToken);
        return AssetResult<AssetView>.Done(AssetViews.View(asset, current!, room.BrandId));
    }

    public async Task<AssetResult<AssetRegenerationView>> RegenerateAsync(Guid roomId, Guid assetId, int version, string instruction, CancellationToken cancellationToken)
    {
        if (await OpenAsync(roomId, assetId, version, true, cancellationToken) is not { } opened)
        {
            return AssetResult<AssetRegenerationView>.Failed(AssetError.NotFound);
        }

        if (opened.Error is { } failure)
        {
            return AssetResult<AssetRegenerationView>.Failed(failure.Error, failure.Message);
        }

        var (room, asset) = (opened.Room, opened.Asset);
        if (asset.Locked)
        {
            return AssetResult<AssetRegenerationView>.Failed(AssetError.NotAllowed, "Unlock this asset before regenerating it.");
        }

        if (!regenerators.Any(regenerator => regenerator.Kinds.Contains(asset.Kind)))
        {
            return AssetResult<AssetRegenerationView>.Failed(AssetError.NotAllowed, "Kairo can't regenerate this kind of asset yet.");
        }

        var run = await queue.EnqueueAsync(room, StageKinds.AssetRegenerate, member.UserId, cancellationToken, AssetRegenerateWork.Input(asset.Id, asset.CurrentVersion, instruction));
        var current = await store.CurrentAsync(asset, cancellationToken);
        await FinishAsync(room, new AuditRecord("asset.regenerated", RoomWorkflow.AuditTarget, room.Id.ToString(), null, new { assetId = asset.Id, asset.Kind, version = asset.CurrentVersion, instruction, runId = run.Id }), cancellationToken);
        return AssetResult<AssetRegenerationView>.Done(new AssetRegenerationView(run.Id, AssetViews.View(asset, current!, room.BrandId)));
    }

    public static AssetContent Apply(AssetContent content, AssetEditRequest request)
    {
        var next = content.Copy();
        if (request.Keywords is { } keywords)
        {
            next.Keywords =
            [
                .. keywords.Select(keyword => new AssetKeyword
                {
                    Text = AdLimits.Tidy(keyword.Text).ToLowerInvariant(),
                    Match = keyword.Match?.Trim().ToLowerInvariant() ?? AdLimits.Phrase,
                }),
            ];
        }

        next.Headlines = request.Headlines is { } headlines ? Lines(headlines) : next.Headlines;
        next.LongHeadlines = request.LongHeadlines is { } longHeadlines ? Lines(longHeadlines) : next.LongHeadlines;
        next.Descriptions = request.Descriptions is { } descriptions ? Lines(descriptions) : next.Descriptions;
        next.Path1 = request.Path1 is { } path1 ? Optional(path1) : next.Path1;
        next.Path2 = request.Path2 is { } path2 ? Optional(path2) : next.Path2;
        next.BusinessName = request.BusinessName is { } name ? Optional(name) : next.BusinessName;
        next.CallToAction = request.CallToAction is { } action ? Optional(action)?.ToLowerInvariant() : next.CallToAction;
        next.Title = request.Title is { } title ? Optional(title) : next.Title;
        return next;
    }

    private static List<string> Provided(AssetEditRequest request)
    {
        var fields = new List<string>();
        if (request.Keywords is not null)
        {
            fields.Add("keywords");
        }

        if (request.Headlines is not null)
        {
            fields.Add("headlines");
        }

        if (request.LongHeadlines is not null)
        {
            fields.Add("longHeadlines");
        }

        if (request.Descriptions is not null)
        {
            fields.Add("descriptions");
        }

        if (request.Path1 is not null)
        {
            fields.Add("path1");
        }

        if (request.Path2 is not null)
        {
            fields.Add("path2");
        }

        if (request.BusinessName is not null)
        {
            fields.Add("businessName");
        }

        if (request.CallToAction is not null)
        {
            fields.Add("callToAction");
        }

        if (request.Title is not null)
        {
            fields.Add("title");
        }

        return fields;
    }

    private static List<string> Lines(List<string> texts) => [.. texts.Select(AdLimits.Tidy)];

    private static string? Optional(string text) => AdLimits.Tidy(text) is { Length: > 0 } value ? value : null;

    private async Task<Opened?> OpenAsync(Guid roomId, Guid assetId, int version, bool needsIdle, CancellationToken cancellationToken)
    {
        if (await workflow.LockedAsync(roomId, cancellationToken) is not { } room)
        {
            return null;
        }

        var asset = await db.Assets.SingleOrDefaultAsync(
            row => row.Id == assetId && row.RoomId == roomId && row.Status == AssetStatuses.Active && !row.Hidden,
            cancellationToken);
        if (asset is null)
        {
            return null;
        }

        if (room.State != RoomStates.Creating)
        {
            return new Opened(room, asset, (AssetError.NotAllowed, NotCreating));
        }

        if (needsIdle && EditBlocker(room) is { } blocker)
        {
            return new Opened(room, asset, (AssetError.NotAllowed, blocker));
        }

        return asset.CurrentVersion != version
            ? new Opened(room, asset, (AssetError.Stale, StaleAsset))
            : new Opened(room, asset, null);
    }

    private async Task FinishAsync(Room room, AuditRecord record, CancellationToken cancellationToken)
    {
        workflow.Touch(room);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(record, cancellationToken);
        await workflow.PublishAsync(room, cancellationToken);
    }

    private sealed record Opened(Room Room, Asset Asset, (AssetError Error, string Message)? Error);
}
