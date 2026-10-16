using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Stages;

internal interface IStageWork
{
    string Kind { get; }

    string Agent { get; }

    string Stage { get; }

    string? FollowUp => null;

    string? NextKind(StageRun run, StageOutcome outcome) => FollowUp;

    Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken);

    Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken);
}

internal sealed record StageOutcome(object Output, string? Model, string? PromptVersion, AgentUsage? Usage, string Summary);

internal sealed class StageFailure(string code, string message) : Exception(message)
{
    public const string NoProducts = "no_products";
    public const string UnknownKind = "unknown_kind";
    public const string BadAnswer = "bad_answer";
    public const string Crashed = "crashed";
    public const string NoBrief = "no_brief";
    public const string Locked = "locked";
    public const string AssetMissing = "asset_missing";

    public string Code { get; } = code;
}
