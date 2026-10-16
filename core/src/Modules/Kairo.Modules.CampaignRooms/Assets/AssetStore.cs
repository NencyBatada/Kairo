using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;

namespace Kairo.Modules.CampaignRooms.Assets;

internal sealed record GeneratedAsset(
    string Key,
    string Kind,
    string CampaignType,
    string? AdGroup,
    List<string> ProductIds,
    AssetContent Content,
    string? Format = null,
    int? Width = null,
    int? Height = null,
    decimal? DurationSeconds = null,
    bool Hidden = false,
    string? Language = null);

internal sealed record SavedAssets(int Created, int Updated, int Removed, int KeptLocked);

internal sealed class AssetStore(CampaignRoomsDbContext db, ProvenanceTrail trail, TimeProvider time)
{
    public Task<AssetVersion?> CurrentAsync(Asset asset, CancellationToken cancellationToken) =>
        db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(version => version.AssetId == asset.Id && version.Version == asset.CurrentVersion, cancellationToken);

    public async Task<SavedAssets> SaveGeneratedAsync(
        Room room,
        int briefVersion,
        string language,
        IReadOnlyCollection<string> kinds,
        IReadOnlyList<GeneratedAsset> generated,
        StageRun run,
        StageOutcome outcome,
        CancellationToken cancellationToken)
    {
        var scope = kinds.ToList();
        var existing = await db.Assets
            .Where(asset => asset.RoomId == room.Id && asset.Language == language && scope.Contains(asset.Kind))
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(asset => asset.Key, StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var (created, updated, kept) = (0, 0, 0);
        foreach (var item in FootageFirst(generated))
        {
            if (!keys.Add(item.Key))
            {
                continue;
            }

            if (byKey.TryGetValue(item.Key, out var asset))
            {
                if (asset.Locked && asset.Status == AssetStatuses.Active)
                {
                    kept++;
                    continue;
                }

                Describe(asset, item, briefVersion);
                asset.Status = AssetStatuses.Active;
                AddVersion(asset, item.Content, AssetOrigins.Generated, run.Id, outcome.Model, outcome.PromptVersion, null, null);
                updated++;
                continue;
            }

            var now = time.GetUtcNow();
            asset = new Asset
            {
                Id = Guid.NewGuid(),
                BrandId = room.BrandId,
                RoomId = room.Id,
                Key = item.Key,
                Language = language,
                CreatedAt = now,
            };
            Describe(asset, item, briefVersion);
            db.Assets.Add(asset);
            AddVersion(asset, item.Content, AssetOrigins.Generated, run.Id, outcome.Model, outcome.PromptVersion, null, null);
            created++;
        }

        var removed = 0;
        foreach (var asset in existing.Where(asset => asset.Status == AssetStatuses.Active && !asset.Locked && !keys.Contains(asset.Key)))
        {
            asset.Status = AssetStatuses.Removed;
            asset.UpdatedAt = time.GetUtcNow();
            removed++;
        }

        return new SavedAssets(created, updated, removed, kept);
    }

    public async Task SaveCompanionsAsync(
        Room room,
        Asset main,
        IReadOnlyList<GeneratedAsset> companions,
        StageRun run,
        StageOutcome outcome,
        string instruction,
        string origin,
        Guid? createdBy,
        CancellationToken cancellationToken)
    {
        var keys = companions.Select(item => item.Key).ToList();
        var existing = await db.Assets
            .Where(asset => asset.RoomId == room.Id && keys.Contains(asset.Key))
            .ToDictionaryAsync(asset => asset.Key, StringComparer.Ordinal, cancellationToken);
        foreach (var item in FootageFirst(companions))
        {
            if (existing.TryGetValue(item.Key, out var asset))
            {
                if (asset.Locked && asset.Status == AssetStatuses.Active)
                {
                    continue;
                }

                Describe(asset, item, main.BriefVersion);
                asset.Status = AssetStatuses.Active;
            }
            else
            {
                asset = new Asset
                {
                    Id = Guid.NewGuid(),
                    BrandId = room.BrandId,
                    RoomId = room.Id,
                    Key = item.Key,
                    Language = item.Language ?? main.Language,
                    CreatedAt = time.GetUtcNow(),
                };
                Describe(asset, item, main.BriefVersion);
                db.Assets.Add(asset);
            }

            AddVersion(asset, item.Content, origin, run.Id, outcome.Model, outcome.PromptVersion, createdBy, instruction);
        }
    }

    public async Task<int> RetireAsync(Room room, IReadOnlyCollection<string> kinds, IReadOnlyCollection<string> languages, CancellationToken cancellationToken)
    {
        var scope = kinds.ToList();
        var kept = languages.ToList();
        var stale = await db.Assets
            .Where(asset => asset.RoomId == room.Id && scope.Contains(asset.Kind) && !kept.Contains(asset.Language) && asset.Status == AssetStatuses.Active && !asset.Locked)
            .ToListAsync(cancellationToken);
        foreach (var asset in stale)
        {
            asset.Status = AssetStatuses.Removed;
            asset.UpdatedAt = time.GetUtcNow();
        }

        return stale.Count;
    }

    public AssetVersion AddVersion(
        Asset asset,
        AssetContent content,
        string origin,
        Guid? stageRunId,
        string? model,
        string? promptVersion,
        Guid? createdBy,
        string? instruction,
        IReadOnlyList<string>? changedFields = null)
    {
        var now = time.GetUtcNow();
        var previous = asset.CurrentVersion;
        asset.CurrentVersion++;
        asset.CheckState = AssetCheckStates.Unchecked;
        asset.UpdatedAt = now;
        var version = new AssetVersion
        {
            Id = Guid.NewGuid(),
            BrandId = asset.BrandId,
            RoomId = asset.RoomId,
            AssetId = asset.Id,
            Version = asset.CurrentVersion,
            Content = content.Write(),
            Origin = origin,
            Instruction = instruction,
            StageRunId = stageRunId,
            Model = model,
            PromptVersion = promptVersion,
            CreatedBy = createdBy,
            CreatedAt = now,
            Provenance = trail.For(asset, previous, changedFields).Write(),
        };
        db.AssetVersions.Add(version);
        if (asset.Kind == AssetKinds.Footage)
        {
            trail.Used(asset, version.Version, content.Media?.FileKey);
        }

        return version;
    }

    private static IEnumerable<GeneratedAsset> FootageFirst(IEnumerable<GeneratedAsset> items) =>
        items.OrderBy(item => item.Kind == AssetKinds.Footage ? 0 : 1);

    private static void Describe(Asset asset, GeneratedAsset item, int briefVersion)
    {
        asset.BriefVersion = briefVersion;
        asset.Kind = item.Kind;
        asset.CampaignType = item.CampaignType;
        asset.AdGroup = item.AdGroup;
        asset.ProductIds = [.. item.ProductIds];
        asset.Format = item.Format;
        asset.Width = item.Width;
        asset.Height = item.Height;
        asset.DurationSeconds = item.DurationSeconds;
        asset.Hidden = item.Hidden;
    }
}
