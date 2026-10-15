using System.Text.Json;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Measurement;
using Kairo.Contracts.Opportunities;
using Kairo.Contracts.Triggers;
using Kairo.Modules.CampaignRooms.Data;
using Kairo.Modules.CampaignRooms.Rooms;
using Kairo.Modules.CampaignRooms.Stages;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.ProofPlans;

internal sealed class ProofPlanWork(
    CampaignRoomsDbContext db,
    ProofPlanAgent agent,
    IRegionHistory history,
    IKeywordDemand demand,
    IRegionPlaces places,
    IBrandDirectory brands,
    TimeProvider time,
    ILogger<ProofPlanWork> logger) : IStageWork
{
    public const int HistoryDays = 365;
    public const int RecentDays = 56;
    public const int MinCities = 3;
    public const int SearchMonths = 12;
    public const string Country = "india";
    public const string StateFallback = "state_fallback";

    public string Kind => StageKinds.ProofPlan;

    public string Agent => StageAgents.Plan;

    public string Stage => RoomStates.BriefDraft;

    public async Task<StageOutcome> RunAsync(StageRun run, Room room, CancellationToken cancellationToken)
    {
        var brief = await db.Briefs.AsNoTracking()
            .Where(row => row.RoomId == room.Id)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new StageFailure(StageFailure.NoBrief, "Kairo needs the brief before it can design the test.");
        var latest = await ProofPlanReader.LatestAsync(db, room.Id, cancellationToken);
        if (latest?.LockedAt is not null)
        {
            throw new StageFailure(StageFailure.Locked, "The test plan is locked, so Kairo can't change it.");
        }

        var choice = Choice(run.Input);
        var brand = (await brands.GetAsync([room.BrandId], cancellationToken)).Single();
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(brand.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        var evidence = RoomEvidence.Read(room.Evidence);
        var scope = BriefScope.Of(brief, evidence, zone, time.GetUtcNow());
        var to = scope.HistoryEnd;
        var from = to.AddDays(1 - HistoryDays);

        var unit = choice is not null && latest is not null ? latest.Read().Unit : null;
        RegionHistory? regions = null;
        if (unit != ProofUnits.State)
        {
            regions = await history.DailyAsync(room.BrandId, RegionUnit.City, from, to, cancellationToken);
            unit ??= Enough(regions, Area(scope.Regions, ProofUnits.City)) ? ProofUnits.City : ProofUnits.State;
        }

        if (unit == ProofUnits.State)
        {
            regions = await history.DailyAsync(room.BrandId, RegionUnit.State, from, to, cancellationToken);
        }

        var area = Area(scope.Regions, unit!);
        var selected = Select(regions!, area);
        var searches = await SearchesAsync(room.Evidence, selected, cancellationToken);
        var answer = await agent.DesignAsync(
            new ProofPlanRequest(
                regions!.Days,
                scope.Days,
                ProofPlanAgent.TargetLift,
                choice?.HoldoutIds,
                [.. selected.Select(entry => new ProofRegion(
                    entry.Series.Id,
                    entry.InArea,
                    entry.Series.Orders,
                    entry.Series.Conversions,
                    entry.Series.Revenue,
                    searches.GetValueOrDefault(entry.Series.Id)))]),
            cancellationToken);

        var content = Content(answer, selected, scope, unit!, regions.Currency ?? brand.Currency, choice is not null);
        return new StageOutcome(
            new ProofPlanDraft(brief.Version, content, answer.Method),
            null,
            answer.Method,
            null,
            "Designed the test plan");
    }

    public async Task SaveAsync(StageRun run, Room room, StageOutcome outcome, CancellationToken cancellationToken)
    {
        var draft = (ProofPlanDraft)outcome.Output;
        var latest = await db.ProofPlans.Where(plan => plan.RoomId == room.Id).MaxAsync(plan => (int?)plan.Version, cancellationToken) ?? 0;
        var plan = new ProofPlan
        {
            Id = Guid.NewGuid(),
            BrandId = room.BrandId,
            RoomId = room.Id,
            Version = latest + 1,
            BriefVersion = draft.BriefVersion,
            StageRunId = run.Id,
            Method = draft.Method,
            CreatedAt = time.GetUtcNow(),
        };
        plan.Write(draft.Content);
        db.ProofPlans.Add(plan);
    }

    public static HoldoutChoice? Choice(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HoldoutChoice>(input, JsonSerializerOptions.Web) is { HoldoutIds.Length: > 0 } choice ? choice : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private AreaKeys Area(IReadOnlyList<string> names, string unit)
    {
        var keys = names.Select(RegionKeys.Of).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (unit == ProofUnits.State)
        {
            foreach (var name in names)
            {
                if (places.Find(name)?.State is { } state)
                {
                    keys.Add(RegionKeys.Of(state));
                }
            }
        }

        return new AreaKeys(keys.Count == 0 || keys.Contains(Country), keys);
    }

    private static bool InArea(RegionSeries series, AreaKeys area) =>
        area.Everywhere || area.Keys.Contains(series.Id) || (series.State is { } state && area.Keys.Contains(RegionKeys.Of(state)));

    private static double Recent(RegionSeries series)
    {
        var values = series.Orders ?? series.Conversions;
        return values is null ? 0 : values.Skip(Math.Max(0, values.Length - RecentDays)).Sum();
    }

    private static bool Enough(RegionHistory cities, AreaKeys area)
    {
        var usable = cities.Regions.Where(series => Recent(series) > 0).ToList();
        return usable.Count >= MinCities && usable.Any(series => InArea(series, area));
    }

    private static List<Selected> Select(RegionHistory regions, AreaKeys area)
    {
        var ranked = regions.Regions
            .Select(series => new Selected(series, InArea(series, area), Recent(series)))
            .OrderByDescending(entry => entry.InArea)
            .ThenByDescending(entry => entry.Volume)
            .ThenBy(entry => entry.Series.Id, StringComparer.Ordinal)
            .Take(ProofPlanAgent.MaxRegions)
            .ToList();
        return ranked;
    }

    private async Task<Dictionary<string, double[]>> SearchesAsync(string evidence, List<Selected> selected, CancellationToken cancellationToken)
    {
        var keywords = Keywords(evidence);
        var geo = selected.Where(entry => entry.Series.GeoTargetId is not null && entry.Volume > 0).ToList();
        if (keywords.Count == 0 || geo.Count == 0)
        {
            return [];
        }

        IReadOnlyList<RegionSearches> found;
        try
        {
            found = await demand.MonthlyAsync(keywords, [.. geo.Select(entry => entry.Series.GeoTargetId!)], cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Search demand by region wasn't available for the test plan.");
            return [];
        }

        if (found.SelectMany(region => region.Months).Select(month => (month.Year, month.Month)).DefaultIfEmpty().Max() is not { Year: > 0 } last)
        {
            return [];
        }

        var end = new DateOnly(last.Year, last.Month, 1);
        var months = Enumerable.Range(0, SearchMonths).Select(index => end.AddMonths(index - SearchMonths + 1)).ToList();
        var byGeo = found.ToDictionary(region => region.GeoTargetId);
        var searches = new Dictionary<string, double[]>();
        foreach (var entry in geo)
        {
            if (byGeo.TryGetValue(entry.Series.GeoTargetId!, out var region))
            {
                var counts = region.Months.ToDictionary(month => (month.Year, month.Month), month => (double)month.Searches);
                searches[entry.Series.Id] = [.. months.Select(month => counts.GetValueOrDefault((month.Year, month.Month)))];
            }
        }

        return searches;
    }

    private static List<string> Keywords(string evidence)
    {
        try
        {
            using var document = JsonDocument.Parse(evidence);
            return document.RootElement.TryGetProperty("keywords", out var keywords) && keywords.ValueKind == JsonValueKind.Array
                ? [.. keywords.EnumerateArray().Where(keyword => keyword.ValueKind == JsonValueKind.String).Select(keyword => keyword.GetString()!)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private ProofPlanContent Content(ProofPlanAnswer answer, List<Selected> selected, BriefScope scope, string unit, string currency, bool chosen)
    {
        var byId = selected.ToDictionary(entry => entry.Series.Id, StringComparer.Ordinal);
        var scores = answer.Candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        PlanRegion? View(string id)
        {
            if (!byId.TryGetValue(id, out var entry))
            {
                return null;
            }

            var place = places.Find(entry.Series.Name);
            var candidate = scores.GetValueOrDefault(id);
            return new PlanRegion(
                id,
                entry.Series.Name,
                entry.Series.State ?? (unit == ProofUnits.City ? place?.State : null),
                place?.Latitude,
                place?.Longitude,
                entry.InArea,
                candidate?.Score,
                candidate?.Share);
        }

        List<PlanWarning> warnings = unit == ProofUnits.State
            ? [new PlanWarning(StateFallback, "Kairo doesn't have enough history by city here, so the test compares whole states.")]
            : [];
        warnings.AddRange(answer.Warnings.Select(warning => new PlanWarning(warning.Code, warning.Message)));
        return new ProofPlanContent
        {
            Unit = unit,
            Kpi = answer.Kpi,
            InsideArea = answer.InsideArea,
            ChosenByUser = chosen,
            TestRegions = [.. answer.TestIds.Select(View).OfType<PlanRegion>()],
            Holdouts = [.. answer.HoldoutIds.Select(View).OfType<PlanRegion>()],
            Candidates = [.. answer.Candidates.Select(candidate => View(candidate.Id)).OfType<PlanRegion>()],
            StartDate = answer.WindowDays is null ? null : scope.Start,
            EndDate = answer.WindowDays is { } days ? scope.Start.AddDays(days - 1) : null,
            WindowDays = answer.WindowDays,
            MinDetectableLift = answer.MinDetectableLift,
            Lifts = [.. answer.Lifts.Select(lift => new PlanLift(lift.Days, lift.Lift, lift.Placebos))],
            PlaceboWindows = answer.PlaceboWindows,
            HoldoutShare = answer.HoldoutShare,
            HoldoutCost = answer.HoldoutCost is { } cost ? Math.Round((decimal)cost, 2) : null,
            Currency = currency,
            DataDays = answer.DataDays,
            Warnings = warnings,
        };
    }

    private sealed record AreaKeys(bool Everywhere, HashSet<string> Keys);

    private sealed record Selected(RegionSeries Series, bool InArea, double Volume);
}

internal sealed record ProofPlanDraft(int BriefVersion, ProofPlanContent Content, string? Method);
