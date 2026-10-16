using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record CheckIssueView(
    string Rule,
    string RuleName,
    string Severity,
    string Message,
    string? Field,
    int? Index,
    double? At,
    string? Evidence,
    bool Overridable);

internal sealed record CheckOverrideView(Guid By, DateTimeOffset At, string Reason);

internal sealed record CheckFixView(
    int FromVersion,
    int ToVersion,
    DateTimeOffset At,
    CheckIssueView[] IssuesFixed,
    AssetContentView Before,
    string? BeforeMediaUrl,
    AssetContentView After,
    string? AfterMediaUrl);

internal sealed record AssetCheckView(
    string State,
    int? Score,
    bool? Passed,
    int? SettingsVersion,
    DateTimeOffset? CheckedAt,
    CheckIssueView[] Issues,
    CheckOverrideView? Overridden,
    int FixAttempts,
    CheckFixView[] Fixes)
{
    public static AssetCheckView StateOnly(string state) => new(state, null, null, null, null, [], null, 0, []);
}

internal sealed record AssetSetCheckView(
    int? Score,
    int Passed,
    int Flagged,
    int Overridden,
    int Unchecked,
    int SettingsVersion,
    int PassScore,
    int FixAttempts,
    string? ApproveBlocker);

internal sealed record CheckOverrideRequest(int? Version, string? Reason);

internal sealed record AssetSendBackRequest(int? Version, string? Comment, List<Guid>? AssetIds);

internal sealed record AssetCheckRequest(int? Version);

internal sealed class CheckViews(CampaignRoomsDbContext db, CheckLedger ledger)
{
    public const int MaxFixes = 10;

    public async Task<Dictionary<Guid, AssetCheckView>> ForAsync(Guid roomId, Guid brandId, IReadOnlyList<(Asset Asset, AssetVersion Version)> current, CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            return [];
        }

        var latest = await ledger.LatestForRoomAsync(roomId, cancellationToken);
        var attempts = await ledger.FixAttemptsAsync([.. current.Select(entry => entry.Asset)], cancellationToken);
        var fixedIds = await db.AssetVersions.AsNoTracking()
            .Where(version => version.RoomId == roomId && version.Origin == AssetOrigins.Fixed)
            .Select(version => version.AssetId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var versions = fixedIds.Count == 0
            ? []
            : await db.AssetVersions.AsNoTracking().Where(version => fixedIds.Contains(version.AssetId)).ToListAsync(cancellationToken);
        var byAsset = versions.GroupBy(version => version.AssetId).ToDictionary(group => group.Key, group => group.ToDictionary(version => version.Version));
        var views = new Dictionary<Guid, AssetCheckView>();
        foreach (var (asset, version) in current)
        {
            var result = latest.GetValueOrDefault((asset.Id, version.Version));
            var fixes = byAsset.TryGetValue(asset.Id, out var history)
                ? history.Values
                    .Where(entry => entry.Origin == AssetOrigins.Fixed && entry.Version <= version.Version && history.ContainsKey(entry.Version - 1))
                    .OrderByDescending(entry => entry.Version)
                    .Take(MaxFixes)
                    .Select(entry => Fix(asset, brandId, history[entry.Version - 1], entry, latest.GetValueOrDefault((asset.Id, entry.Version - 1))))
                    .ToArray()
                : [];
            views[asset.Id] = new AssetCheckView(
                asset.CheckState,
                result?.Score,
                result?.Passed,
                result?.SettingsVersion,
                result?.CreatedAt,
                result is null ? [] : Issues(result.ReadIssues()),
                result is { OverriddenBy: { } by, OverriddenAt: { } at } ? new CheckOverrideView(by, at, result.OverrideReason ?? "") : null,
                attempts.GetValueOrDefault(asset.Id),
                fixes);
        }

        return views;
    }

    public static CheckIssueView[] Issues(IEnumerable<CheckIssue> issues) =>
    [
        .. issues.Select(issue => new CheckIssueView(
            issue.Rule,
            issue.RuleName,
            issue.Severity,
            issue.Message,
            issue.Field,
            issue.Index,
            issue.At,
            issue.Evidence,
            issue.Overridable)),
    ];

    public static bool CanOverride(AssetCheckView check) =>
        check.State == AssetCheckStates.Failed && check.Issues.Length > 0 && check.Issues.All(issue => issue.Overridable);

    public static AssetSetCheckView Summary(IReadOnlyList<AssetView> assets, EffectiveCheckSettings settings, string? blocker)
    {
        var scored = assets.Where(asset => asset.Check.Score is not null).ToList();
        return new AssetSetCheckView(
            scored.Count == 0 ? null : (int)Math.Round(scored.Average(asset => asset.Check.Score!.Value), MidpointRounding.AwayFromZero),
            assets.Count(asset => asset.CheckState == AssetCheckStates.Passed),
            assets.Count(asset => asset.CheckState == AssetCheckStates.Failed),
            assets.Count(asset => asset.CheckState == AssetCheckStates.Overridden),
            assets.Count(asset => asset.CheckState == AssetCheckStates.Unchecked),
            settings.Version,
            settings.PassScore,
            settings.FixAttempts,
            blocker);
    }

    private static CheckFixView Fix(Asset asset, Guid brandId, AssetVersion before, AssetVersion after, CheckResult? found)
    {
        var beforeContent = before.Read();
        var afterContent = after.Read();
        return new CheckFixView(
            before.Version,
            after.Version,
            after.CreatedAt,
            found is null ? [] : Issues(found.ReadIssues()),
            AssetViews.Content(beforeContent),
            beforeContent.Media is null ? null : AssetViews.MediaUrl(brandId, asset.RoomId, asset.Id, before.Version),
            AssetViews.Content(afterContent),
            afterContent.Media is null ? null : AssetViews.MediaUrl(brandId, asset.RoomId, asset.Id, after.Version));
    }
}
