using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.BrandMemory;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Opportunities.Data;
using Kairo.Modules.Opportunities.Radar;
using Kairo.Modules.Opportunities.Scoring;
using Kairo.Modules.Opportunities.Sense;
using Kairo.Modules.Opportunities.Topics;
using Kairo.SharedKernel.Agents;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Opportunities.Refresh;

internal sealed record BrandRefresh(Guid BrandId, string Outcome, string? Reason, int Created, int Updated, int Expired);

internal sealed class BrandRefresher(
    OpportunitiesDbContext db,
    KairoDatabase database,
    IConnections connections,
    IBrandKit brandKit,
    IBrandMemory brandMemory,
    ICatalogue catalogue,
    KeywordPlanner planner,
    SenseAgent sense,
    OpportunityOptions options,
    IAuditLog audit,
    ILiveUpdates live,
    INotifications notifications,
    IAgentProgress progress,
    TimeProvider time,
    ILogger<BrandRefresher> logger)
{
    public const string Running = "running";
    public const int MaxErrorLength = 1000;
    public const int RegionsAtOnce = 2;
    public const int MaxRegionKeywords = 1000;
    public const int MaxDismissals = 30;
    public const int ExplainKeywords = 5;
    public const int Steps = 4;
    public const string AuditTarget = "opportunity_refresh";

    public static readonly TimeSpan RunningFor = TimeSpan.FromMinutes(30);

    private bool _reported;

    public static string LockName(Guid brandId) => $"opportunities:{brandId:N}";

    public async Task<BrandRefresh> RefreshAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var run = new RefreshRun { Id = Guid.NewGuid(), Outcome = Running, StartedAt = time.GetUtcNow() };
        if (!await ClaimAsync(brand.Id, run, cancellationToken))
        {
            return new BrandRefresh(brand.Id, RefreshOutcomes.Skipped, RefreshReasons.Busy, 0, 0, 0);
        }

        string? externalId = null;
        try
        {
            var status = await connections.GetStatusAsync(ConnectedProduct.GoogleAds, cancellationToken);
            externalId = status.ExternalId;
            if (status.State != ConnectionState.Connected)
            {
                var reason = status.State == ConnectionState.NeedsReconnect ? RefreshReasons.NeedsReconnect : RefreshReasons.NotConnected;
                return await FinishAsync(run, RefreshOutcomes.Skipped, reason, null, cancellationToken);
            }

            var gathered = await GatherAsync(brand, run, cancellationToken);
            if (gathered.SkipReason is { } skip)
            {
                return await FinishAsync(run, RefreshOutcomes.Skipped, skip, null, cancellationToken);
            }

            return await SaveAsync(brand, run, gathered, cancellationToken);
        }
        catch (ConnectionUnavailableException unavailable)
        {
            var reason = unavailable.Reason == RefreshReasons.NotConnected ? RefreshReasons.NotConnected : RefreshReasons.NeedsReconnect;
            return await FinishAsync(run, RefreshOutcomes.Skipped, reason, null, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            await FailQuietlyAsync(run, null, "The refresh was interrupted before it finished.");
            throw;
        }
        catch (AdsCallException ads) when (ads.AccessLost)
        {
            logger.LogWarning(ads, "The opportunity refresh lost access to Google Ads for brand {BrandId}.", brand.Id);
            var outcome = await FailAsync(run, RefreshReasons.AccessLost, ads.Message, cancellationToken);
            if (externalId is not null)
            {
                await connections.ReportAccessLostAsync(ConnectedProduct.GoogleAds, externalId, cancellationToken);
            }

            return outcome;
        }
        catch (AdsCallException ads)
        {
            logger.LogError(ads, "Google Ads keyword planning failed for brand {BrandId}.", brand.Id);
            return await FailAsync(run, RefreshReasons.GoogleAds, ads.Message, cancellationToken);
        }
        catch (AgentCallException agent)
        {
            logger.LogError(agent, "The Sense agent failed for brand {BrandId}.", brand.Id);
            return await FailAsync(run, RefreshReasons.Agent, agent.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The opportunity refresh failed for brand {BrandId}.", brand.Id);
            return await FailAsync(run, null, exception.Message, cancellationToken);
        }
    }

    private async Task<bool> ClaimAsync(Guid brandId, RefreshRun run, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var since = run.StartedAt - RunningFor;
            if (!await database.TryLockAsync(LockName(brandId), cancellationToken)
                || await db.BrandRuns.AnyAsync(existing => existing.Outcome == Running && existing.StartedAt > since, cancellationToken))
            {
                await database.RollbackAsync(cancellationToken);
                return false;
            }

            db.RefreshRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
            await database.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<Gathered> GatherAsync(BrandSummary brand, RefreshRun run, CancellationToken cancellationToken)
    {
        var topics = await db.BrandTopics.AsNoTracking().ToListAsync(cancellationToken);
        var watched = topics.Where(topic => topic.Kind == TopicKinds.Watch).Select(topic => topic.Phrase).ToList();
        var ignored = topics.Where(topic => topic.Kind == TopicKinds.Ignore).Select(topic => topic.Phrase).ToList();
        var categories = await catalogue.ListInStockCategoriesAsync(cancellationToken);
        if (categories.Count == 0)
        {
            return Gathered.Skip(RefreshReasons.NoStock);
        }

        var seeds = Seeds(watched, KitCategories(await brandKit.GetApprovedAsync(cancellationToken)), categories);
        if (seeds.Count == 0)
        {
            return Gathered.Skip(RefreshReasons.NoSeeds);
        }

        await ReportAsync(run, AgentJobStatus.Running, "Reading search demand", 1, cancellationToken);
        var access = await connections.GetAccessAsync(ConnectedProduct.GoogleAds, cancellationToken);
        var market = await planner.MarketAsync(access, cancellationToken);
        var ideas = await planner.IdeasAsync(access, market.Country, seeds, cancellationToken);
        var candidates = ideas
            .Where(idea => !ignored.Any(phrase => TopicText.Mentions(phrase, idea.Text, [])))
            .Select(idea => (Idea: idea, Demand: new Demand(idea.Months)))
            .Where(entry => (entry.Demand.Growth >= OpportunityOptions.MinGrowth && entry.Demand.LastMonth >= OpportunityOptions.MinSearches)
                || watched.Any(phrase => TopicText.Mentions(phrase, entry.Idea.Text, [])))
            .OrderByDescending(entry => entry.Demand.LastMonth * (1 + Math.Max(entry.Demand.Growth, 0)))
            .Take(OpportunityOptions.MaxCandidates)
            .ToList();
        run.Keywords = candidates.Count;
        if (candidates.Count == 0)
        {
            return new Gathered([], [], null, null);
        }

        await ReportAsync(run, AgentJobStatus.Running, "Matching to your products", 2, cancellationToken);
        var dismissals = await DismissalsAsync(cancellationToken);
        var match = await sense.MatchAsync(
            new MatchRequest(
                brand.Name,
                new SenseMarket(market.Country.Name, options.Language),
                candidates.Select(entry => new SenseKeyword(entry.Idea.Text, entry.Demand.LastMonth, entry.Demand.Growth)).ToList(),
                categories.Select(category => new SenseCategory(category.Name, category.InStock, category.SampleTitles)).ToList(),
                dismissals.Take(MaxDismissals).Select(dismissal => new SenseDismissal(dismissal.Topic, dismissal.Past.Reason)).ToList(),
                watched),
            cancellationToken);

        var memory = await brandMemory.RelevantAsync(new MemoryQuery(Limit: MemoryQuery.MostEntries), cancellationToken);
        var byText = candidates.ToDictionary(entry => entry.Idea.Text, entry => entry.Idea, StringComparer.OrdinalIgnoreCase);
        var known = categories.Select(category => category.Name).ToHashSet(StringComparer.Ordinal);
        var now = time.GetUtcNow();
        var radar = new List<RadarTopic>();
        foreach (var topic in (match.Topics ?? []).OfType<MatchedTopic>())
        {
            var keywords = (topic.Keywords ?? []).OfType<string>().Where(byText.ContainsKey).Select(text => byText[text].Text).Distinct().ToList();
            var name = topic.Name?.Trim();
            if (string.IsNullOrEmpty(name) || keywords.Count == 0 || radar.Any(existing => existing.Key == TopicText.Key(name)))
            {
                continue;
            }

            if (ignored.Any(phrase => TopicText.Mentions(phrase, name, keywords)))
            {
                continue;
            }

            name = name.Length > 200 ? name[..200] : name;
            var matched = (topic.Categories ?? []).OfType<string>().Where(known.Contains).Distinct().ToList();
            var products = await catalogue.FindInStockAsync(matched, [name, .. keywords], OpportunityOptions.MaxProducts, cancellationToken);
            var demand = Demand.Sum(keywords.Select(text => byText[text].Months));
            var key = TopicText.Key(name);
            var breakdown = OpportunityScoring.Breakdown(
                demand, topic.Fit ?? 0, keywords, key, dismissals.Select(dismissal => dismissal.Past), now, matched, [.. products.Select(product => product.Id)], memory);
            radar.Add(new RadarTopic
            {
                Name = name,
                Key = key,
                Keywords = keywords,
                Demand = demand,
                Bids = BidRange.Of(keywords
                    .Select(text => byText[text])
                    .Where(idea => idea.Bids is not null)
                    .Select(idea => (idea.Bids!.Low, idea.Bids.High, new Demand(idea.Months).LastMonth))),
                Breakdown = breakdown,
                Score = OpportunityScoring.Score(breakdown),
                Categories = matched,
                ProductIds = products.Select(product => product.Id).ToList(),
                FitReason = topic.FitReason is { Length: > 500 } reason ? reason[..500] : topic.FitReason,
                Watched = watched.Any(phrase => TopicText.Mentions(phrase, name, keywords)),
                Model = match.Model,
                PromptVersion = match.PromptVersion,
            });
        }

        var existing = await db.BrandOpportunities.AsNoTracking().Where(o => o.Status == OpportunityStatuses.New || o.Status == OpportunityStatuses.Saved).ToListAsync(cancellationToken);
        var kept = OpportunityMerger.Keep(radar, existing);
        if (kept.Count > 0)
        {
            await ReportAsync(run, AgentJobStatus.Running, "Checking regions and reasons", 3, cancellationToken);
            await AddRegionsAsync(access, market, kept, cancellationToken);
            await AddReasonsAsync(brand, market, kept, cancellationToken);
        }

        return new Gathered(kept, radar, match.Model, null);
    }

    private async Task AddRegionsAsync(ConnectionAccess access, Market market, IReadOnlyList<RadarTopic> topics, CancellationToken cancellationToken)
    {
        var keywords = topics.SelectMany(topic => topic.Keywords).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRegionKeywords).ToList();
        var demand = new Dictionary<string, IReadOnlyList<KeywordDemand>>();
        await Parallel.ForEachAsync(
            market.Regions,
            new ParallelOptions { MaxDegreeOfParallelism = RegionsAtOnce, CancellationToken = cancellationToken },
            async (region, token) =>
            {
                try
                {
                    var history = await planner.HistoryAsync(access, region, keywords, token);
                    lock (demand)
                    {
                        demand[region.Id] = history;
                    }
                }
                catch (AdsCallException exception) when (!exception.AccessLost)
                {
                    logger.LogWarning(exception, "Google Ads had no regional demand for {Region}.", region.Name);
                }
            });

        foreach (var topic in topics)
        {
            var wanted = topic.Keywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var national = topic.Demand.Growth;
            topic.Regions = market.Regions
                .Where(region => demand.ContainsKey(region.Id))
                .Select(region =>
                {
                    var local = Demand.Sum(demand[region.Id].Where(keyword => wanted.Contains(keyword.Text)).Select(keyword => keyword.Months));
                    var growth = local.Months.Count > Demand.RecentMonths ? local.Growth : (double?)null;
                    return new RegionDemand
                    {
                        Id = region.Id,
                        Name = region.Name,
                        LastMonthSearches = local.LastMonth,
                        Growth = growth,
                        Rising = growth is { } value && value > national && value > 0 && local.LastMonth > 0,
                    };
                })
                .Where(region => region.LastMonthSearches > 0)
                .OrderByDescending(region => region.Rising)
                .ThenByDescending(region => region.Growth ?? double.MinValue)
                .ThenByDescending(region => region.LastMonthSearches)
                .ToList();
        }
    }

    private async Task AddReasonsAsync(BrandSummary brand, Market market, IReadOnlyList<RadarTopic> topics, CancellationToken cancellationToken)
    {
        var ids = topics.Select((topic, index) => (Id: $"t{index + 1}", Topic: topic)).ToList();
        ExplainAnswer answer;
        try
        {
            answer = await sense.ExplainAsync(
                new ExplainRequest(
                    brand.Name,
                    new SenseMarket(market.Country.Name, options.Language),
                    ids.Select(entry => new ExplainTopic(entry.Id, entry.Topic.Name, entry.Topic.Keywords.Take(ExplainKeywords).ToList(), entry.Topic.Demand.Growth, entry.Topic.Demand.Yoy)).ToList()),
                cancellationToken);
        }
        catch (AgentCallException exception)
        {
            logger.LogWarning(exception, "The Sense agent couldn't explain the opportunities for brand {BrandId}.", brand.Id);
            foreach (var topic in topics)
            {
                topic.Reasons = [];
                topic.ReasonsStatus = ReasonsStatuses.Unavailable;
            }

            return;
        }

        var explained = (answer.Topics ?? []).OfType<ExplainedTopic>().Where(topic => topic.Id is not null).GroupBy(topic => topic.Id!).ToDictionary(group => group.Key, group => group.First());
        foreach (var (id, topic) in ids)
        {
            if (!explained.TryGetValue(id, out var found))
            {
                topic.Reasons = [];
                topic.ReasonsStatus = ReasonsStatuses.Unavailable;
                continue;
            }

            var reasons = Reasons(found);
            topic.Reasons = reasons;
            topic.ReasonsStatus = reasons.Count > 0 ? ReasonsStatuses.Sourced : ReasonsStatuses.Unsourced;
        }
    }

    private async Task<BrandRefresh> SaveAsync(BrandSummary brand, RefreshRun run, Gathered gathered, CancellationToken cancellationToken)
    {
        await ReportAsync(run, AgentJobStatus.Running, "Saving opportunities", 4, cancellationToken);
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await db.BrandOpportunities
                .Where(o => o.Status == OpportunityStatuses.New || o.Status == OpportunityStatuses.Saved || o.ClosedAt > run.StartedAt - OpportunityScoring.FeedbackWindow)
                .ToListAsync(cancellationToken);
            var merged = OpportunityMerger.Merge(existing, gathered.Kept, time.GetUtcNow());
            db.Opportunities.AddRange(merged.Created);
            run.Created = merged.Created.Count;
            run.Updated = merged.Updated;
            run.Expired = merged.Expired;
            var outcome = await FinishAsync(run, RefreshOutcomes.Succeeded, null, null, cancellationToken);
            foreach (var opportunity in existing.Concat(merged.Created)
                .Where(o => o.Breakdown.MemoryEntryIds is { Count: > 0 } && gathered.Kept.Any(topic => ReferenceEquals(topic.Breakdown, o.Breakdown))))
            {
                await brandMemory.RecordUsesAsync(opportunity.Breakdown.MemoryEntryIds!, MemoryUsedIn.Opportunity, opportunity.Id, null, cancellationToken);
            }

            await audit.RecordAsync(
                new AuditRecord(
                    "opportunities.refreshed",
                    AuditTarget,
                    run.Id.ToString(),
                    After: new { run.Keywords, run.Created, run.Updated, run.Expired, gathered.Model },
                    Actor: AuditActor.Agent("Sense agent", "sense")),
                cancellationToken);
            await live.PublishAsync(OpportunityLiveEvents.Refreshed(run), cancellationToken);
            var fresh = merged.Created.Where(o => o.Score >= OpportunityOptions.CutOff || gathered.Topics.Any(t => t.Watched && t.Key == o.TopicKey)).OrderByDescending(o => o.Score).ToList();
            if (fresh.Count > 0)
            {
                await notifications.NotifyAsync(NewOpportunities(brand, fresh), cancellationToken);
            }

            await database.CommitAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<BrandRefresh> FinishAsync(RefreshRun run, string outcome, string? reason, string? error, CancellationToken cancellationToken)
    {
        run.Outcome = outcome;
        run.Reason = reason;
        run.Error = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
        run.FinishedAt = time.GetUtcNow();
        if (db.Entry(run).State == EntityState.Detached)
        {
            db.RefreshRuns.Attach(run);
            db.Entry(run).State = EntityState.Modified;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (!_reported)
        {
            return new BrandRefresh(run.BrandId, outcome, reason, run.Created, run.Updated, run.Expired);
        }

        var status = outcome == RefreshOutcomes.Failed ? AgentJobStatus.Failed : AgentJobStatus.Succeeded;
        await progress.ReportAsync(
            new AgentProgress(run.Id, AgentName.Sense, status, outcome == RefreshOutcomes.Succeeded ? "Opportunities refreshed" : "Refresh stopped")
            {
                Message = reason is null ? null : $"Outcome: {outcome} ({reason}).",
            },
            cancellationToken);
        return new BrandRefresh(run.BrandId, outcome, reason, run.Created, run.Updated, run.Expired);
    }

    private Task<BrandRefresh> FailAsync(RefreshRun run, string? reason, string error, CancellationToken cancellationToken) =>
        FinishAsync(run, RefreshOutcomes.Failed, reason, error, cancellationToken);

    private async Task FailQuietlyAsync(RefreshRun run, string? reason, string error)
    {
        try
        {
            db.ChangeTracker.Clear();
            await FailAsync(run, reason, error, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't record the interrupted opportunity refresh {RunId}.", run.Id);
        }
    }

    private Task ReportAsync(RefreshRun run, AgentJobStatus status, string step, int number, CancellationToken cancellationToken)
    {
        _reported = true;
        return progress.ReportAsync(new AgentProgress(run.Id, AgentName.Sense, status, step) { StepNumber = number, StepCount = Steps }, cancellationToken);
    }

    private async Task<List<(string Topic, PastDismissal Past)>> DismissalsAsync(CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - OpportunityScoring.FeedbackWindow;
        var dismissed = await db.BrandOpportunities.AsNoTracking()
            .Where(o => o.Status == OpportunityStatuses.Dismissed && o.ClosedAt > since)
            .OrderByDescending(o => o.ClosedAt)
            .ToListAsync(cancellationToken);
        return dismissed
            .Select(o => (o.Topic, new PastDismissal(o.TopicKey, o.Keywords, o.DismissReason ?? DismissReasons.Other, o.ClosedAt!.Value, o.LastMonthSearches)))
            .ToList();
    }

    private static List<string> Seeds(IEnumerable<string> watched, IEnumerable<string> kit, IReadOnlyList<CatalogueCategory> categories)
    {
        var leaves = categories.Select(category => category.Name.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "");
        return watched.Concat(kit).Concat(leaves)
            .Select(seed => seed.Trim())
            .Where(seed => seed.Length is > 1 and <= 80)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(OpportunityOptions.MaxSeeds)
            .ToList();
    }

    private static IEnumerable<string> KitCategories(ApprovedBrandKit? kit)
    {
        if (kit is null || kit.Content.ValueKind != JsonValueKind.Object
            || !kit.Content.TryGetProperty("productCategories", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray()
            .Select(category => category.ValueKind == JsonValueKind.Object && category.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
            .OfType<string>()
            .ToList();
    }

    private static List<SourcedReason> Reasons(ExplainedTopic topic) =>
        (topic.Reasons ?? [])
            .OfType<ExplainedReason>()
            .Select(reason => new SourcedReason
            {
                Text = Cut(reason.Text?.Trim() ?? "", 500),
                Sources = (reason.Sources ?? [])
                    .OfType<ExplainedSource>()
                    .Where(source => Uri.TryCreate(source.Url, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
                    .DistinctBy(source => source.Url)
                    .Take(5)
                    .Select(source => new ReasonSource { Title = Cut(string.IsNullOrWhiteSpace(source.Title) ? new Uri(source.Url!).Host : source.Title.Trim(), 300), Url = source.Url! })
                    .ToList(),
            })
            .Where(reason => reason.Text.Length > 0 && reason.Sources.Count > 0)
            .Take(5)
            .ToList();

    private static Notification NewOpportunities(BrandSummary brand, IReadOnlyList<Opportunity> fresh)
    {
        var top = fresh[0];
        var title = fresh.Count == 1 ? "1 new opportunity" : $"{fresh.Count} new opportunities";
        return new Notification(
            "opportunities.ready",
            LiveAudience.Roles(BrandRoles.Marketer),
            title,
            $"Top: {top.Topic}, scored {top.Score}. Kairo found it in this morning's refresh.",
            $"/{brand.Handle}/opportunities");
    }

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

    private sealed record Gathered(IReadOnlyList<RadarTopic> Kept, IReadOnlyList<RadarTopic> Topics, string? Model, string? SkipReason)
    {
        public static Gathered Skip(string reason) => new([], [], null, reason);
    }
}
