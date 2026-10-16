using System.Text.RegularExpressions;
using Kairo.Contracts.Audit;
using Kairo.Contracts.CampaignRooms;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Media;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed record CheckedAsset(Guid AssetId, int Version, string Kind, int Score, bool Passed, List<CheckIssue> Issues);

internal sealed class CheckOutcome
{
    public List<CheckedAsset> Assets { get; init; } = [];

    public int SettingsVersion { get; init; }

    public int FixAttempts { get; init; }

    public string? Next { get; set; }

    public bool Done { get; set; }
}

internal sealed partial class CheckWork(
    CampaignRoomsDbContext db,
    CheckSettingsStore settingsStore,
    CopyDesk copyDesk,
    MediaDesk media,
    CheckAgent agent,
    AssetStore store,
    AssetReworker reworker,
    CheckLedger ledger,
    IAuditLog audit,
    TimeProvider time) : IStageWork
{
    public const int CallsAtOnce = 6;
    public const int ImagesPerRun = 8;
    public const int VideosPerRun = 1;
    public const int TextPerCall = 60;
    public const long MaxVideoBytes = 60L * 1024 * 1024;

    private static readonly string[] AgentImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public string Kind => StageKinds.Check;

    public string Agent => StageAgents.Check;

    public string Stage => RoomStates.Creating;

    public string? NextKind(StageRun run, StageOutcome outcome) => outcome.Output is CheckOutcome { Done: false, Next: { } next } ? next : null;

    public string? SignalAfterSave(StageRun run, StageOutcome outcome) =>
        outcome.Output is CheckOutcome { Done: true } ? CampaignSignals.AssetsChecked : null;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(room.BrandId, cancellationToken);
        var pending = await db.Assets.AsNoTracking()
            .Where(asset => asset.RoomId == room.Id && asset.Status == AssetStatuses.Active && !asset.Hidden && asset.CheckState == AssetCheckStates.Unchecked)
            .OrderBy(asset => asset.Key)
            .ToListAsync(cancellationToken);
        var chosen = pending.Where(asset => asset.Kind is not (AssetKinds.Image or AssetKinds.Video))
            .Concat(pending.Where(asset => asset.Kind == AssetKinds.Image).Take(ImagesPerRun))
            .Concat(pending.Where(asset => asset.Kind == AssetKinds.Video).Take(VideosPerRun))
            .ToList();
        var outcome = new CheckOutcome { SettingsVersion = settings.Version, FixAttempts = settings.FixAttempts };
        if (chosen.Count == 0)
        {
            return new StageOutcome(outcome, null, null, null, "Every asset is already checked.");
        }

        if (await copyDesk.ReadAsync(room, cancellationToken) is not { } context)
        {
            throw new StageFailure(StageFailure.NoBrief, "This campaign has no brief to check the assets against.");
        }

        var entries = new List<(Asset Asset, AssetVersion Version, AssetContent Content)>();
        foreach (var asset in chosen)
        {
            if (await store.CurrentAsync(asset, cancellationToken) is { } version)
            {
                entries.Add((asset, version, version.Read()));
            }
        }

        var findings = entries.ToDictionary(entry => entry.Asset.Id, entry => CodeChecks.For(entry.Asset, entry.Content));
        var calls = new List<Func<CancellationToken, Task<CheckAnswer?>>>();
        AddTextCalls(calls, entries.Where(entry => AssetKinds.IsText(entry.Asset.Kind)).ToList(), context, settings);
        await AddImageCallsAsync(calls, entries.Where(entry => entry.Asset.Kind == AssetKinds.Image).ToList(), context, settings, findings, cancellationToken);
        await AddVideoCallsAsync(calls, entries.Where(entry => entry.Asset.Kind == AssetKinds.Video).ToList(), context, settings, findings, cancellationToken);
        var answers = new List<CheckAnswer>();
        await Parallel.ForEachAsync(
            calls,
            new ParallelOptions { MaxDegreeOfParallelism = CallsAtOnce, CancellationToken = cancellationToken },
            async (call, token) =>
            {
                if (await call(token) is { } answer)
                {
                    lock (answers)
                    {
                        answers.Add(answer);
                    }
                }
            });
        foreach (var answer in answers)
        {
            foreach (var result in (answer.Results ?? []).OfType<CheckAssetFindings>())
            {
                if (!Guid.TryParse(result.AssetId, out var assetId) || !findings.TryGetValue(assetId, out var list))
                {
                    continue;
                }

                list.AddRange((result.Findings ?? [])
                    .OfType<CheckFinding>()
                    .Where(finding => !string.IsNullOrWhiteSpace(finding.Rule) && !string.IsNullOrWhiteSpace(finding.Message))
                    .Select(finding => new RawFinding(finding.Rule!.Trim(), finding.Message!, finding.Field, finding.Index, finding.At, finding.Evidence)));
            }
        }

        foreach (var (asset, version, _) in entries)
        {
            var scored = CheckScoring.Score(findings[asset.Id], settings);
            outcome.Assets.Add(new CheckedAsset(asset.Id, version.Version, asset.Kind, scored.Score, scored.Passed, scored.Issues));
        }

        var passed = outcome.Assets.Count(item => item.Passed);
        return new StageOutcome(
            outcome,
            answers.Select(answer => answer.Model).FirstOrDefault(model => model is not null),
            answers.Select(answer => answer.PromptVersion).FirstOrDefault(version => version is not null),
            MediaDesk.Sum([.. answers.Select(answer => answer.Usage)]),
            $"Checked {outcome.Assets.Count} assets: {passed} passed and {outcome.Assets.Count - passed} need attention.");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var checkedSet = (CheckOutcome)outcome.Output;
        var ids = checkedSet.Assets.Select(item => item.AssetId).ToList();
        var assets = await db.Assets.Where(asset => asset.RoomId == room.Id && ids.Contains(asset.Id)).ToDictionaryAsync(asset => asset.Id, cancellationToken);
        var now = time.GetUtcNow();
        var saved = new List<CheckedAsset>();
        foreach (var item in checkedSet.Assets)
        {
            if (!assets.TryGetValue(item.AssetId, out var asset)
                || asset.Status != AssetStatuses.Active
                || asset.CurrentVersion != item.Version
                || asset.CheckState != AssetCheckStates.Unchecked)
            {
                continue;
            }

            db.CheckResults.Add(new CheckResult
            {
                Id = Guid.NewGuid(),
                BrandId = room.BrandId,
                RoomId = room.Id,
                AssetId = asset.Id,
                AssetVersion = item.Version,
                Score = item.Score,
                Passed = item.Passed,
                Issues = CheckIssue.Write(item.Issues),
                SettingsVersion = checkedSet.SettingsVersion,
                StageRunId = run.Id,
                Model = outcome.Model,
                PromptVersion = outcome.PromptVersion,
                CreatedAt = now,
            });
            asset.CheckState = item.Passed ? AssetCheckStates.Passed : AssetCheckStates.Failed;
            saved.Add(item);
        }

        await db.SaveChangesAsync(cancellationToken);
        await DecideAsync(run, room, checkedSet, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord(
                "assets.checked",
                RoomWorkflow.AuditTarget,
                room.Id.ToString(),
                null,
                new
                {
                    runId = run.Id,
                    @checked = saved.Count,
                    passed = saved.Count(item => item.Passed),
                    failed = saved.Count(item => !item.Passed),
                    rules = saved.SelectMany(item => item.Issues).Select(issue => issue.Rule).Distinct().Order(StringComparer.Ordinal).ToArray(),
                    settingsVersion = checkedSet.SettingsVersion,
                    next = checkedSet.Done ? "review" : checkedSet.Next,
                },
                AuditActor.Agent(StageProgress.ActorName(Agent), Agent)),
            cancellationToken);
    }

    private async Task DecideAsync(StageRun run, Room room, CheckOutcome outcome, CancellationToken cancellationToken)
    {
        var earlier = await db.StageRuns.CountAsync(
            row => row.RoomId == room.Id && row.Kind == StageKinds.Check && row.Status == StageRunStatuses.Succeeded && row.QueuedAt >= room.StateEnteredAt && row.Id != run.Id,
            cancellationToken);
        if (earlier + 1 >= CheckResultLimits.MaxChecksPerEntry)
        {
            outcome.Done = true;
            return;
        }

        var visible = db.Assets.Where(asset => asset.RoomId == room.Id && asset.Status == AssetStatuses.Active && !asset.Hidden);
        if (await visible.AnyAsync(asset => asset.CheckState == AssetCheckStates.Unchecked, cancellationToken))
        {
            outcome.Next = StageKinds.Check;
            return;
        }

        var failed = await visible.Where(asset => asset.CheckState == AssetCheckStates.Failed && !asset.Locked).ToListAsync(cancellationToken);
        var fixable = failed.Where(asset => reworker.CanRegenerate(asset.Kind)).ToList();
        var attempts = await ledger.FixAttemptsAsync(fixable, cancellationToken);
        if (fixable.Any(asset => attempts[asset.Id] < outcome.FixAttempts))
        {
            outcome.Next = StageKinds.CheckFix;
            return;
        }

        outcome.Done = true;
    }

    private void AddTextCalls(
        List<Func<CancellationToken, Task<CheckAnswer?>>> calls,
        List<(Asset Asset, AssetVersion Version, AssetContent Content)> entries,
        CopyContext context,
        EffectiveCheckSettings settings)
    {
        var rules = Rules(settings, CheckTargets.Text);
        var custom = Custom(settings, CheckTargets.Text);
        foreach (var language in entries.GroupBy(entry => LanguageOf(entry.Asset.Language, context)))
        {
            foreach (var chunk in language.Chunk(TextPerCall))
            {
                var request = new CopyCheckRequest(
                    context.Brand,
                    language.Key,
                    context.BriefData(),
                    context.ProductData(),
                    context.BrandKit,
                    rules,
                    custom,
                    [.. chunk.Select(entry => new TextCheckAsset(entry.Asset.Id.ToString(), entry.Asset.Kind, TextRegenerator.Data(entry.Asset, entry.Content, context)))]);
                calls.Add(async token => await agent.CopyAsync(request, token));
            }
        }
    }

    private async Task AddImageCallsAsync(
        List<Func<CancellationToken, Task<CheckAnswer?>>> calls,
        List<(Asset Asset, AssetVersion Version, AssetContent Content)> entries,
        CopyContext context,
        EffectiveCheckSettings settings,
        Dictionary<Guid, List<RawFinding>> findings,
        CancellationToken cancellationToken)
    {
        var rules = Rules(settings, CheckTargets.Image);
        var custom = Custom(settings, CheckTargets.Image);
        var references = new Dictionary<string, MediaReferenceProduct?>(StringComparer.Ordinal);
        foreach (var (asset, _, content) in entries)
        {
            if (content.Media is not { } file || !AgentImageTypes.Contains(file.ContentType))
            {
                continue;
            }

            if (await media.ReadAsync(file, cancellationToken) is not { Length: > 0 } bytes)
            {
                findings[asset.Id].Add(Missing(asset.Kind));
                continue;
            }

            var request = new ImageCheckRequest(
                context.Brand,
                context.Language,
                context.BriefData(),
                context.ProductData(),
                context.BrandKit,
                rules,
                custom,
                asset.Id.ToString(),
                asset.Format,
                new MediaPhoto(file.ContentType, bytes),
                await ReferencesAsync(asset, context, references, cancellationToken));
            calls.Add(async token => await agent.ImageAsync(request, token));
        }
    }

    private async Task AddVideoCallsAsync(
        List<Func<CancellationToken, Task<CheckAnswer?>>> calls,
        List<(Asset Asset, AssetVersion Version, AssetContent Content)> entries,
        CopyContext context,
        EffectiveCheckSettings settings,
        Dictionary<Guid, List<RawFinding>> findings,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var rules = Rules(settings, CheckTargets.Video);
        var custom = Custom(settings, CheckTargets.Video);
        var references = new Dictionary<string, MediaReferenceProduct?>(StringComparer.Ordinal);
        var logo = await media.LogoAsync(context.BrandKit, cancellationToken);
        foreach (var (asset, _, content) in entries)
        {
            if (content.Media is not { ContentType: "video/mp4" } file)
            {
                continue;
            }

            if (await media.ReadAsync(file, cancellationToken) is not { Length: > 0 } bytes)
            {
                findings[asset.Id].Add(Missing(asset.Kind));
                continue;
            }

            if (bytes.LongLength > MaxVideoBytes)
            {
                findings[asset.Id].Add(new RawFinding(CheckRules.MediaSpec, "The video file is too large to check. Regenerate it."));
                continue;
            }

            var request = new VideoCheckRequest(
                context.Brand,
                LanguageOf(asset.Language, context),
                context.BriefData(),
                context.ProductData(),
                context.BrandKit,
                rules,
                custom,
                asset.Id.ToString(),
                new VideoCheckInput(file.ContentType, bytes),
                asset.DurationSeconds is > 0 and <= 120 ? asset.DurationSeconds : null,
                [.. (content.Script ?? []).Take(12).Select(line => new VideoCheckLine(CopyDesk.Cut(line.Voiceover, 300), CopyDesk.Cut(line.Caption, 120), Math.Max(0, line.Start), Math.Max(0, line.End)))],
                await ReferencesAsync(asset, context, references, cancellationToken),
                logo);
            var target = findings[asset.Id];
            calls.Add(async token =>
            {
                try
                {
                    return await agent.VideoAsync(request, token);
                }
                catch (AgentCallException exception) when (exception.Message.Contains(CheckAgent.BadVideo, StringComparison.Ordinal))
                {
                    lock (target)
                    {
                        target.Add(CodeChecks.BadVideo());
                    }

                    return null;
                }
            });
        }
    }

    private async Task<IReadOnlyList<MediaReferenceProduct>> ReferencesAsync(
        Asset asset,
        CopyContext context,
        Dictionary<string, MediaReferenceProduct?> cache,
        CancellationToken cancellationToken)
    {
        var product = context.Products.FirstOrDefault(entry => asset.ProductIds.Contains(entry.Product.Id.ToString()));
        if (product is null)
        {
            return [];
        }

        if (!cache.TryGetValue(product.Key, out var reference))
        {
            var photos = await media.PhotosAsync([product], 1, cancellationToken);
            reference = photos.Count == 0
                ? null
                : new MediaReferenceProduct(product.Key, CopyDesk.Cut(product.Product.Title, 300), CopyDesk.Cut(product.Product.Description, 2000), photos[0].Photos);
            cache[product.Key] = reference;
        }

        return reference is null ? [] : [reference];
    }

    private static RawFinding Missing(string kind) =>
        new(CheckRules.MediaSpec, kind == AssetKinds.Video ? "The video file is missing. Regenerate it." : "The image file is missing. Regenerate it.");

    private static IReadOnlyList<CheckRuleData> Rules(EffectiveCheckSettings settings, string target) =>
    [
        .. settings.On(target).Select(rule => new CheckRuleData(rule.Rule.Id, rule.Rule.Name, rule.Rule.Description, rule.Rule.AppliesTo)),
    ];

    private static IReadOnlyList<CustomRuleData> Custom(EffectiveCheckSettings settings, string target) =>
    [
        .. settings.CustomOn(target).Select(rule => new CustomRuleData(rule.Id, rule.Text, rule.AppliesTo)),
    ];

    private static string LanguageOf(string language, CopyContext context) =>
        LanguageCode().IsMatch(language) ? language : context.Language;

    [GeneratedRegex("^[a-z]{2}$")]
    private static partial Regex LanguageCode();
}
