using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record ScoredCheck(List<CheckIssue> Issues, int Score, bool Passed);

internal static class CheckScoring
{
    public const string CustomName = "Custom rule";

    public static ScoredCheck Score(IEnumerable<RawFinding> findings, EffectiveCheckSettings settings)
    {
        var seen = new HashSet<(string, string?, int?, string)>();
        var issues = new List<CheckIssue>();
        foreach (var finding in findings)
        {
            var severity = settings.Severity(finding.Rule);
            var message = Cut(finding.Message, CheckResultLimits.Message);
            if (severity == CheckSeverities.Off || message.Length == 0 || !seen.Add((finding.Rule, finding.Field, finding.Index, message.ToLowerInvariant())))
            {
                continue;
            }

            issues.Add(new CheckIssue
            {
                Rule = finding.Rule,
                RuleName = NameOf(finding.Rule, settings),
                Severity = severity,
                Message = message,
                Field = finding.Field,
                Index = finding.Index,
                At = finding.At,
                Evidence = finding.Evidence is { } evidence && Cut(evidence, CheckResultLimits.Evidence) is { Length: > 0 } cut ? cut : null,
                Overridable = settings.Overridable(finding.Rule),
            });
        }

        issues = [.. issues.OrderBy(issue => issue.Severity == CheckSeverities.Block ? 0 : 1).Take(CheckResultLimits.MaxIssues)];
        var blocking = issues.Count(issue => issue.Severity == CheckSeverities.Block);
        var warnings = issues.Count(issue => issue.Severity == CheckSeverities.Warn);
        var score = CheckScore.Of(blocking, warnings);
        return new ScoredCheck(issues, score, CheckScore.Passes(blocking, score, settings.PassScore));
    }

    public static string NameOf(string rule, EffectiveCheckSettings settings) =>
        CheckRules.Find(rule)?.Name
        ?? settings.CustomRules.FirstOrDefault(custom => custom.Id == rule)?.Text
        ?? CustomName;

    public static string Cut(string? text, int length)
    {
        var tidy = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return tidy.Length > length ? tidy[..length].TrimEnd() : tidy;
    }
}

internal sealed class CheckLedger(CampaignRoomsDbContext db)
{
    public async Task<Dictionary<Guid, int>> FixAttemptsAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        var ids = assets.Select(asset => asset.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var versions = await db.AssetVersions.AsNoTracking()
            .Where(version => ids.Contains(version.AssetId))
            .Select(version => new { version.AssetId, version.Version, version.Origin })
            .ToListAsync(cancellationToken);
        var results = await db.CheckResults.AsNoTracking()
            .Where(result => ids.Contains(result.AssetId))
            .Select(result => new { result.AssetId, result.AssetVersion, result.FailedFixes, result.CreatedAt })
            .ToListAsync(cancellationToken);
        return assets.ToDictionary(
            asset => asset.Id,
            asset => versions
                    .Where(version => version.AssetId == asset.Id && version.Version <= asset.CurrentVersion)
                    .OrderByDescending(version => version.Version)
                    .TakeWhile(version => version.Origin == AssetOrigins.Fixed)
                    .Count()
                + (results
                    .Where(result => result.AssetId == asset.Id && result.AssetVersion == asset.CurrentVersion)
                    .OrderByDescending(result => result.CreatedAt)
                    .FirstOrDefault()?.FailedFixes ?? 0));
    }

    public Task<CheckResult?> LatestAsync(Guid assetId, int version, CancellationToken cancellationToken) =>
        db.CheckResults
            .Where(result => result.AssetId == assetId && result.AssetVersion == version)
            .OrderByDescending(result => result.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Dictionary<(Guid AssetId, int Version), CheckResult>> LatestForRoomAsync(Guid roomId, CancellationToken cancellationToken)
    {
        var results = await db.CheckResults.AsNoTracking().Where(result => result.RoomId == roomId).ToListAsync(cancellationToken);
        return results
            .GroupBy(result => (result.AssetId, result.AssetVersion))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(result => result.CreatedAt).First());
    }
}
