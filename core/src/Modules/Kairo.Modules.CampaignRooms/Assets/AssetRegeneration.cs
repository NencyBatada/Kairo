using System.Text.Json;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Assets;

internal interface IAssetRegenerator
{
    IReadOnlyCollection<string> Kinds { get; }

    Task<AssetRegeneration> RegenerateAsync(AssetRegenerationContext context, CancellationToken cancellationToken);
}

internal sealed record AssetRegenerationContext(Room Room, Asset Asset, AssetVersion Current, AssetContent Content, string Instruction);

internal sealed record AssetRegeneration(AssetContent Content, string? Model, string? PromptVersion, AgentUsage? Usage, string Summary);

internal sealed record RegenerationInput(Guid AssetId, int Version, string Instruction);

internal sealed record RegeneratedAsset(Guid AssetId, int FromVersion, string Instruction, AssetContent Content, string Summary);

internal sealed class AssetRegenerateWork(
    CampaignRoomsDbContext db,
    AssetStore store,
    IEnumerable<IAssetRegenerator> regenerators) : IStageWork
{
    public string Kind => StageKinds.AssetRegenerate;

    public string Agent => StageAgents.Create;

    public string Stage => RoomStates.Creating;

    public string? NextKind(StageRun run, StageOutcome outcome) => StageKinds.Check;

    public static string Input(Guid assetId, int version, string instruction) =>
        JsonSerializer.Serialize(new RegenerationInput(assetId, version, instruction), JsonSerializerOptions.Web);

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        var input = run.Input is { } json ? JsonSerializer.Deserialize<RegenerationInput>(json, JsonSerializerOptions.Web) : null;
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

        if (regenerators.FirstOrDefault(regenerator => regenerator.Kinds.Contains(asset.Kind)) is not { } regenerator)
        {
            throw new StageFailure(StageFailure.UnknownKind, "Kairo can't regenerate this kind of asset yet.");
        }

        var result = await regenerator.RegenerateAsync(new AssetRegenerationContext(room, asset, current, current.Read(), input.Instruction), cancellationToken);
        return new StageOutcome(
            new RegeneratedAsset(asset.Id, current.Version, input.Instruction, result.Content, result.Summary),
            result.Model,
            result.PromptVersion,
            result.Usage,
            result.Summary);
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var regenerated = (RegeneratedAsset)outcome.Output;
        var asset = await db.Assets.SingleOrDefaultAsync(row => row.Id == regenerated.AssetId && row.RoomId == room.Id, cancellationToken);
        if (asset is null || asset.Locked || asset.Status != AssetStatuses.Active || asset.CurrentVersion != regenerated.FromVersion)
        {
            return;
        }

        store.AddVersion(asset, regenerated.Content, AssetOrigins.Regenerated, run.Id, outcome.Model, outcome.PromptVersion, run.RequestedBy, regenerated.Instruction);
    }
}
