using System.Text.Json;
using Kairo.SharedKernel.Agents;
using Kairo.Contracts.Ask;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record AssetPreviewPayload(int Version, AssetEditRequest? Edit, AssetRegeneration? Generated, ProvenanceState? Provenance, string? Instruction);

internal sealed class AssetAskResource(CampaignRoomsDbContext db, AssetStore store, AssetEditor editor, RoomWorkflow workflow,
    IEnumerable<IAssetRegenerator> regenerators, ProvenanceTrail trail, AgentUsageContext usage) : IAskResource
{
    public string Kind => "asset";
    public BrandRoles Editors => BrandRoles.Marketer;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var asset = await AssetAsync(target, ct);
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.CampaignId, ct);
        if (asset is null || room is null || await store.CurrentAsync(asset, ct) is not { } current) return null;
        var data = AskJson.Element(new { asset.Id, asset.Kind, asset.Language, asset.Locked, version = asset.CurrentVersion, content = Content(current.Read()), room.State, room.RunStatus });
        var siblings = await db.Assets.AsNoTracking().Where(row => row.RoomId == room.Id && row.Language == asset.Language && row.Status == AssetStatuses.Active)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.CurrentVersion, row.Locked, row.UpdatedAt }).ToListAsync(ct);
        return new($"{asset.Kind} ({asset.Language})", $"campaigns/{room.Id}/assets", data, AskJson.Revision(new { room.Version, asset.CurrentVersion, asset.Locked, siblings }));
    }
    public async Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct)
    {
        var asset = await AssetAsync(target, ct) ?? throw new AskResourceException("This asset is unavailable.", 404);
        var room = await db.Rooms.AsNoTracking().SingleAsync(row => row.Id == asset.RoomId, ct);
        using var usageScope = usage.Enter(new AgentUsageScope(room.BrandId, room.Id, usage.Current?.RunId));
        if (AssetEditor.EditBlocker(room) is { } blocker) throw new AskResourceException(blocker);
        if (asset.Locked) throw new AskResourceException("This asset is locked.");
        var current = await store.CurrentAsync(asset, ct) ?? throw new AskResourceException("This asset has no current version.");
        var before = Content(current.Read(), AssetViews.View(asset, current, room.BrandId).MediaUrl);
        if (changes.TryGetProperty("instruction", out var instructionElement))
        {
            AskJson.Only(changes, "instruction");
            var instruction = instructionElement.GetString()?.Trim() ?? "";
            if (instruction.Length is < AssetLimits.MinInstruction or > AssetLimits.Instruction) throw new AskResourceException("Supply a valid regeneration instruction.", 400);
            var regenerator = regenerators.FirstOrDefault(value => value.Kinds.Contains(asset.Kind)) ?? throw new AskResourceException("This asset cannot be regenerated.");
            trail.Take();
            var generated = await regenerator.RegenerateAsync(new AssetRegenerationContext(room, asset, current, current.Read(), instruction), ct);
            if (generated.Companions is { Count: > 0 } candidates)
            {
                var keys = candidates.Select(value => value.Key).ToList();
                var locked = await db.Assets.AsNoTracking().Where(row => row.RoomId == room.Id && row.Status == AssetStatuses.Active && row.Locked && keys.Contains(row.Key))
                    .Select(row => row.Key).ToListAsync(ct);
                generated = generated with { Companions = candidates.Where(value => !locked.Contains(value.Key)).ToArray() };
            }
            var provenance = trail.Take();
            var files = new List<AskPreviewFile>();
            if (generated.Content.Media is { } media) files.Add(new("candidate", media.FileKey, media.ContentType));
            foreach (var companion in generated.Companions ?? [])
                if (companion.Content.Media is { } file) files.Add(new($"companion-{files.Count}", file.FileKey, file.ContentType));
            var after = System.Text.Json.Nodes.JsonNode.Parse(Content(generated.Content).GetRawText())!.AsObject();
            after["companions"] = JsonSerializer.SerializeToNode((generated.Companions ?? []).Select(value => new
            {
                value.Kind, value.Language, value.DurationSeconds, content = Content(value.Content),
            }), AskJson.Options);
            return new(before, AskJson.Element(after), AskJson.Element(new AssetPreviewPayload(asset.CurrentVersion, null, generated, provenance, instruction)),
                ["Confirmation replaces this draft asset and its listed companion media. The result needs the normal asset checks and approval."], [.. files]);
        }
        AskJson.Only(changes, "keywords", "headlines", "longHeadlines", "descriptions", "path1", "path2", "businessName", "callToAction", "title");
        var edit = AskJson.Read<AssetEditRequest>(changes) with { Version = asset.CurrentVersion };
        var next = AssetEditor.PreviewEdit(asset, current.Read(), edit);
        return new(before, Content(next), AskJson.Element(new AssetPreviewPayload(asset.CurrentVersion, edit, null, null, null)), ["The edited asset needs the normal asset checks and approval."]);
    }
    public async Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct)
    {
        await workflow.LockedAsync(target.CampaignId!.Value, ct);
        AskJson.Unchanged(revision, await ReadAsync(target, ct));
        var preview = AskJson.Read<AssetPreviewPayload>(payload);
        if (preview.Edit is { } edit)
        {
            var result = await editor.EditAsync(target.CampaignId.Value, target.Id!.Value, edit, ct);
            if (result.Error != AssetError.None) throw new AskResourceException(result.Message ?? JsonSerializer.Serialize(result.Errors));
        }
        else if (preview.Generated is not null) await editor.ApplyPreviewAsync(target.CampaignId.Value, target.Id!.Value, preview, ct);
        else throw new AskResourceException("This preview contains no asset change.");
    }
    private Task<Asset?> AssetAsync(AskTarget target, CancellationToken ct) => db.Assets.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id && row.RoomId == target.CampaignId && row.Status == AssetStatuses.Active && !row.Hidden, ct);
    private static JsonElement Content(AssetContent content, string? mediaUrl = null)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(content.Write())!.AsObject();
        node.Remove("media");
        if (mediaUrl is not null) node["mediaUrl"] = mediaUrl;
        return AskJson.Element(node);
    }
}
