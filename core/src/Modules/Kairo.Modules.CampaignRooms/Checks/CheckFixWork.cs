using System.Text;
using Kairo.Contracts.Audit;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record FixedAsset(RegeneratedAsset Asset, string Kind, string[] Rules);

internal sealed record FixFailure(Guid AssetId, int Version, string Code);

internal sealed record FixOutcome(List<FixedAsset> Fixed, List<FixFailure> Failed);

internal sealed class CheckFixWork(
    CampaignRoomsDbContext db,
    CheckSettingsStore settingsStore,
    AssetStore store,
    AssetReworker reworker,
    CheckLedger ledger,
    IAuditLog audit) : IStageWork
{
    public const int Attempts = 2;
    public const string Lead = "Fix these problems that Brand Guardian found, and keep everything else as it is:";

    private static readonly string[] VisualRules = [CheckRules.ProductAccuracy, CheckRules.Visual];

    public string Kind => StageKinds.CheckFix;

    public string Agent => StageAgents.Check;

    public string Stage => RoomStates.Creating;

    public int MaxAttempts => Attempts;

    public string? NextKind(StageRun run, StageOutcome outcome) => StageKinds.Check;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(room.BrandId, cancellationToken);
        var failing = (await db.Assets.AsNoTracking()
                .Where(asset => asset.RoomId == room.Id && asset.Status == AssetStatuses.Active && !asset.Hidden && !asset.Locked && asset.CheckState == AssetCheckStates.Failed)
                .ToListAsync(cancellationToken))
            .Where(asset => reworker.CanRegenerate(asset.Kind))
            .ToList();
        var attempts = await ledger.FixAttemptsAsync(failing, cancellationToken);
        var candidates = failing
            .Where(asset => attempts[asset.Id] < settings.FixAttempts)
            .OrderBy(asset => AssetReworker.Order(asset.Kind))
            .ThenBy(asset => asset.Key, StringComparer.Ordinal)
            .ToList();
        var (now, _) = AssetReworker.Budget(candidates, asset => asset.Kind);
        var done = new List<FixedAsset>();
        var failed = new List<FixFailure>();
        foreach (var asset in now)
        {
            if (await store.CurrentAsync(asset, cancellationToken) is not { } current)
            {
                continue;
            }

            var issues = (await ledger.LatestAsync(asset.Id, current.Version, cancellationToken))?.ReadIssues() ?? [];
            try
            {
                var regenerated = await reworker.RegenerateAsync(
                    room,
                    asset,
                    current,
                    Instruction(issues),
                    asset.Kind == AssetKinds.Video ? issues.Any(issue => VisualRules.Contains(issue.Rule)) : null,
                    cancellationToken);
                done.Add(new FixedAsset(regenerated, asset.Kind, [.. issues.Select(issue => issue.Rule).Distinct()]));
            }
            catch (Exception exception) when (exception is AgentCallException or StageFailure)
            {
                failed.Add(new FixFailure(asset.Id, current.Version, exception is AgentCallException call ? call.Code : ((StageFailure)exception).Code));
            }
        }

        var summary = done.Count == 0 && failed.Count == 0
            ? "Nothing left to fix."
            : $"Fixed {done.Count} assets{(failed.Count > 0 ? $", and {failed.Count} couldn't be fixed this time" : "")}.";
        return new StageOutcome(
            new FixOutcome(done, failed),
            done.Select(item => item.Asset.Model).FirstOrDefault(model => model is not null),
            done.Select(item => item.Asset.PromptVersion).FirstOrDefault(version => version is not null),
            MediaDesk.Sum([.. done.Select(item => item.Asset.Usage)]),
            summary);
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var result = (FixOutcome)outcome.Output;
        foreach (var item in result.Fixed)
        {
            if (await reworker.SaveAsync(room, item.Asset, AssetOrigins.Fixed, run, outcome, cancellationToken) is not { } asset)
            {
                continue;
            }

            await audit.RecordAsync(
                new AuditRecord(
                    "asset.fixed",
                    RoomWorkflow.AuditTarget,
                    room.Id.ToString(),
                    new { assetId = asset.Id, version = item.Asset.FromVersion },
                    new { assetId = asset.Id, kind = item.Kind, version = asset.CurrentVersion, rules = item.Rules, runId = run.Id },
                    AuditActor.Agent(StageProgress.ActorName(Agent), Agent)),
                cancellationToken);
        }

        foreach (var failure in result.Failed)
        {
            if (await ledger.LatestAsync(failure.AssetId, failure.Version, cancellationToken) is { } latest)
            {
                latest.FailedFixes++;
            }
        }
    }

    public static string Instruction(IReadOnlyList<CheckIssue> issues)
    {
        var text = new StringBuilder(Lead);
        foreach (var issue in issues.OrderBy(issue => issue.Severity == CheckSeverities.Block ? 0 : 1))
        {
            var line = $" {issue.Message}";
            if (text.Length + line.Length > AssetLimits.Instruction)
            {
                break;
            }

            text.Append(line);
        }

        return issues.Count == 0 ? "Improve this asset so it follows the brand's rules." : text.ToString();
    }
}
