using System.Text.Json;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Assets;

internal interface IAssetRegenerator
{
    IReadOnlyCollection<string> Kinds { get; }

    Task<AssetRegeneration> RegenerateAsync(AssetRegenerationContext context, CancellationToken cancellationToken);
}

internal sealed record AssetRegenerationContext(Room Room, Asset Asset, AssetVersion Current, AssetContent Content, string Instruction, bool? NewVisuals = null);

internal sealed record AssetRegeneration(
    AssetContent Content,
    string? Model,
    string? PromptVersion,
    AgentUsage? Usage,
    string Summary,
    IReadOnlyList<GeneratedAsset>? Companions = null,
    decimal? DurationSeconds = null);

internal sealed record RegenerationInput(Guid AssetId, int Version, string Instruction, string? Kind = null);

internal sealed record RegenerationBatch(List<RegenerationInput> Assets);

internal sealed record RegeneratedAsset(
    Guid AssetId,
    int FromVersion,
    string Instruction,
    AssetContent Content,
    string Summary,
    IReadOnlyList<GeneratedAsset>? Companions = null,
    decimal? DurationSeconds = null,
    string? Model = null,
    string? PromptVersion = null,
    AgentUsage? Usage = null);

internal sealed record RegeneratedBatch(List<RegeneratedAsset> Assets, List<RegenerationInput> Remaining);

internal sealed class AssetReworker(
    CampaignRoomsDbContext db,
    AssetStore store,
    ProvenanceTrail trail,
    IEnumerable<IAssetRegenerator> regenerators)
{
    private readonly Dictionary<Guid, ProvenanceState> _trails = [];

    public const int TextPerRun = 5;
    public const int ImagesPerRun = 2;
    public const int VideosPerRun = 1;

    public bool CanRegenerate(string kind) => regenerators.Any(regenerator => regenerator.Kinds.Contains(kind));

    public static int Order(string kind) => kind switch
    {
        _ when AssetKinds.IsText(kind) => 0,
        AssetKinds.Image => 1,
        _ => 2,
    };

    public static int PerRun(string kind) => Order(kind) switch
    {
        0 => TextPerRun,
        1 => ImagesPerRun,
        _ => VideosPerRun,
    };

    public static (List<T> Now, List<T> Later) Budget<T>(IReadOnlyList<T> items, Func<T, string> kindOf)
    {
        if (items.Count == 0)
        {
            return ([], []);
        }

        var group = Order(kindOf(items[0]));
        var room = PerRun(kindOf(items[0]));
        var now = new List<T>();
        var later = new List<T>();
        foreach (var item in items)
        {
            if (now.Count < room && Order(kindOf(item)) == group)
            {
                now.Add(item);
            }
            else
            {
                later.Add(item);
            }
        }

        return (now, later);
    }

    public async Task<RegeneratedAsset> RegenerateAsync(Room room, Asset asset, AssetVersion current, string instruction, bool? newVisuals, CancellationToken cancellationToken)
    {
        if (regenerators.FirstOrDefault(regenerator => regenerator.Kinds.Contains(asset.Kind)) is not { } regenerator)
        {
            throw new StageFailure(StageFailure.UnknownKind, "Kairo can't regenerate this kind of asset yet.");
        }

        trail.Take();
        var result = await regenerator.RegenerateAsync(new AssetRegenerationContext(room, asset, current, current.Read(), instruction, newVisuals), cancellationToken);
        _trails[asset.Id] = trail.Take();
        return new RegeneratedAsset(
            asset.Id,
            current.Version,
            instruction,
            result.Content,
            result.Summary,
            result.Companions,
            result.DurationSeconds,
            result.Model,
            result.PromptVersion,
            result.Usage);
    }

    public async Task<Asset?> SaveAsync(Room room, RegeneratedAsset regenerated, string origin, Guid? createdBy, StageRun run, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.SingleOrDefaultAsync(row => row.Id == regenerated.AssetId && row.RoomId == room.Id, cancellationToken);
        if (asset is null || asset.Locked || asset.Status != AssetStatuses.Active || asset.CurrentVersion != regenerated.FromVersion)
        {
            return null;
        }

        if (_trails.TryGetValue(regenerated.AssetId, out var recorded))
        {
            trail.Restore(recorded);
        }

        var model = regenerated.Model ?? outcome.Model;
        var promptVersion = regenerated.PromptVersion ?? outcome.PromptVersion;
        var savedOutcome = outcome with { Model = model, PromptVersion = promptVersion };
        if (regenerated.Companions is { Count: > 0 } companions)
        {
            await store.SaveCompanionsAsync(room, asset, companions, run, savedOutcome, regenerated.Instruction, origin, createdBy, cancellationToken);
        }

        store.AddVersion(asset, regenerated.Content, origin, run.Id, model, promptVersion, createdBy, regenerated.Instruction);
        if (regenerated.DurationSeconds is { } seconds)
        {
            asset.DurationSeconds = seconds;
        }

        return asset;
    }
}

internal sealed class AssetRegenerateWork(
    CampaignRoomsDbContext db,
    AssetStore store,
    AssetReworker reworker) : IStageWork
{
    public string Kind => StageKinds.AssetRegenerate;

    public string Agent => StageAgents.Create;

    public string Stage => RoomStates.Creating;

    public string? NextKind(StageRun run, StageOutcome outcome) =>
        outcome.Output is RegeneratedBatch { Remaining.Count: > 0 } ? StageKinds.AssetRegenerate : StageKinds.Check;

    public string? NextInput(StageRun run, StageOutcome outcome) =>
        outcome.Output is RegeneratedBatch { Remaining.Count: > 0 } batch ? BatchInput(batch.Remaining) : null;

    public int AttemptsFor(StageRun run) =>
        Targets(run).Any(item => item.Kind == AssetKinds.Video) ? VideoWork.Attempts : StageRunner.MaxAttempts;

    public static string Input(Guid assetId, int version, string instruction, string kind) =>
        JsonSerializer.Serialize(new RegenerationInput(assetId, version, instruction, kind), JsonSerializerOptions.Web);

    public static string BatchInput(IEnumerable<RegenerationInput> items) =>
        JsonSerializer.Serialize(new RegenerationBatch([.. items]), JsonSerializerOptions.Web);

    public static RegenerationInput? Read(StageRun run) => IsBatch(run) ? null : Parse<RegenerationInput>(run);

    public static RegenerationBatch? ReadBatch(StageRun run) => IsBatch(run) ? Parse<RegenerationBatch>(run) : null;

    private static IReadOnlyList<RegenerationInput> Targets(StageRun run) =>
        ReadBatch(run)?.Assets ?? (Read(run) is { } single ? [single] : []);

    private static bool IsBatch(StageRun run)
    {
        try
        {
            if (run.Input is not { } json)
            {
                return false;
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("assets", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static T? Parse<T>(StageRun run)
        where T : class
    {
        try
        {
            return run.Input is { } json ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        if (ReadBatch(run) is { } batch)
        {
            return await RunBatchAsync(batch, room, cancellationToken);
        }

        var input = Read(run);
        var asset = input is null
            ? null
            : await db.Assets.AsNoTracking().SingleOrDefaultAsync(row => row.Id == input.AssetId && row.RoomId == room.Id && row.Status == AssetStatuses.Active, cancellationToken);
        if (input is null || asset is null || await store.CurrentAsync(asset, cancellationToken) is not { } current)
        {
            throw new StageFailure(StageFailure.AssetMissing, "That asset is no longer in this campaign.");
        }

        if (asset.Locked)
        {
            throw new StageFailure(StageFailure.Locked, "That asset is locked, so Kairo left it as it is.");
        }

        var result = await reworker.RegenerateAsync(room, asset, current, input.Instruction, null, cancellationToken);
        return new StageOutcome(result, result.Model, result.PromptVersion, result.Usage, result.Summary);
    }

    private async Task<StageOutcome> RunBatchAsync(RegenerationBatch batch, Room room, CancellationToken cancellationToken)
    {
        var (now, later) = AssetReworker.Budget(batch.Assets, item => item.Kind ?? "");
        var done = new List<RegeneratedAsset>();
        foreach (var item in now)
        {
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(row => row.Id == item.AssetId && row.RoomId == room.Id && row.Status == AssetStatuses.Active, cancellationToken);
            if (asset is null || asset.Locked || asset.CurrentVersion != item.Version || !reworker.CanRegenerate(asset.Kind))
            {
                continue;
            }

            if (await store.CurrentAsync(asset, cancellationToken) is not { } current)
            {
                continue;
            }

            done.Add(await reworker.RegenerateAsync(room, asset, current, item.Instruction, null, cancellationToken));
        }

        var summary = done.Count == 1 ? done[0].Summary : $"Regenerated {done.Count} sent-back assets.";
        return new StageOutcome(
            new RegeneratedBatch(done, later),
            done.Select(item => item.Model).FirstOrDefault(model => model is not null),
            done.Select(item => item.PromptVersion).FirstOrDefault(version => version is not null),
            MediaDesk.Sum([.. done.Select(item => item.Usage)]),
            summary);
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        IReadOnlyList<RegeneratedAsset> items = outcome.Output switch
        {
            RegeneratedBatch batch => batch.Assets,
            RegeneratedAsset single => [single],
            _ => [],
        };
        foreach (var item in items)
        {
            await reworker.SaveAsync(room, item, AssetOrigins.Regenerated, run.RequestedBy, run, outcome, cancellationToken);
        }
    }
}
