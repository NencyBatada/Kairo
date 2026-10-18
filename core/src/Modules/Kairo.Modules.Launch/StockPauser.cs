using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Microsoft.Extensions.Logging;
using Kairo.SharedKernel.Observability;

namespace Kairo.Modules.Launch;

internal static class PauseStates
{
    public const string Held = "held";
    public const string Paused = "paused";
    public const string Resumed = "resumed";
    public const string Undone = "undone";
    public const string Dismissed = "dismissed";
    public const string Cancelled = "cancelled";
}

internal sealed class ClaimedStock
{
    public Guid BrandId { get; set; }
}

internal sealed record StockAd(LaunchRecord Launch, LaunchPlan Plan, string CampaignType, string CampaignResource, string AdResource, string[] ProductIds);

internal sealed class StockPauser(LaunchDbContext db, KairoDatabase database, IServiceScopeFactory scopes, TimeProvider time, ILogger<StockPauser> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        while (true)
        {
            var now = time.GetUtcNow();
            var until = now + LaunchRunner.Lease;
            var claim = Guid.NewGuid();
            List<ClaimedStock> claimed;
            await using (await database.EnterSystemScopeAsync(cancellationToken))
            {
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO launch.StockWatches (BrandId, NextCheckAt)
                    SELECT DISTINCT launches.BrandId, {now} FROM launch.Launches launches
                    WHERE launches.Status = N'live'
                      AND NOT EXISTS (SELECT 1 FROM launch.StockWatches watches WITH (UPDLOCK, HOLDLOCK) WHERE watches.BrandId = launches.BrandId)
                    """, cancellationToken);
                claimed = await db.Database.SqlQuery<ClaimedStock>($"""
                    UPDATE TOP (4) watches WITH (READPAST, UPDLOCK, ROWLOCK)
                    SET ClaimToken = {claim}, ClaimedUntil = {until}
                    OUTPUT inserted.BrandId
                    FROM launch.StockWatches watches
                    WHERE watches.NextCheckAt <= {now} AND (watches.ClaimedUntil IS NULL OR watches.ClaimedUntil < {now})
                      AND EXISTS (SELECT 1 FROM launch.Launches launches WHERE launches.BrandId = watches.BrandId AND launches.Status = N'live')
                    """).ToListAsync(cancellationToken);
            }

            if (claimed.Count == 0) return processed;
            foreach (var row in claimed)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(row.BrandId, cancellationToken);
                    await scope.ServiceProvider.GetRequiredService<StockPauseWorker>().RunAsync(claim, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Stock check for brand {BrandId} stopped; its lease will expire before retry.", row.BrandId);
                }
            }
            processed += claimed.Count;
        }
    }
}

internal sealed class StockPauseWorker(LaunchDbContext db, KairoDatabase database, LaunchGoogle google, ICatalogue catalogue, TimeProvider time,
    INotifications notifications, IBrandDirectory brands, IAuditLog audit, ILiveUpdates live, ILogger<StockPauseWorker> logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);
    public const int HoldPercent = 30;
    private static readonly string[] Paused = ["out_of_stock", "removed"];

    public async Task RunAsync(Guid claim, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var ads = await RunningAdsAsync(now, cancellationToken);
        var rooms = ads.Select(ad => ad.Launch.RoomId).Distinct().ToList();
        var pauses = await db.AdPauses.AsNoTracking().Where(row => rooms.Contains(row.RoomId)).ToListAsync(cancellationToken);
        var ids = ads.SelectMany(ad => ad.ProductIds).Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
        var stock = await catalogue.GetStockAsync(ids, cancellationToken);
        var fresh = stock.UpdatedAt is { } updated && now - updated <= StaleAfter;
        var plan = fresh ? Decide(ads, pauses, stock) : new StockDecision([], [], []);
        var held = Hold(plan.Pause);
        var applied = new List<StockAd>();
        var resumed = new List<AdPause>();
        foreach (var launch in ads.GroupBy(ad => ad.Launch.Id))
        {
            var pause = plan.Pause.Where(ad => ad.Launch.Id == launch.Key && !held.ContainsKey(ad.AdResource)).ToArray();
            var resume = plan.Resume.Where(row => row.LaunchId == launch.Key).ToArray();
            if (pause.Length + resume.Length == 0) continue;
            var account = launch.First().Plan.GoogleAdsAccountId!;
            try
            {
                await SetStatusAsync(google, account, [.. pause.Select(ad => (ad.AdResource, "PAUSED")), .. resume.Select(row => (row.AdResource, "ENABLED"))], cancellationToken);
                applied.AddRange(pause);
                resumed.AddRange(resume);
            }
            catch (Exception exception) when (exception is LaunchException or ConnectionUnavailableException)
            {
                logger.LogWarning(exception, "Stock changes for launch {LaunchId} couldn't be sent to Google; the next check retries them.", launch.Key);
            }
        }

        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync($"launch-stock:{database.BrandId:N}", cancellationToken);
            var watch = await db.StockWatches.SingleAsync(cancellationToken);
            if (watch.ClaimToken != claim || watch.ClaimedUntil <= time.GetUtcNow()) { await database.RollbackAsync(cancellationToken); return; }
            watch.ClaimToken = null;
            watch.ClaimedUntil = null;
            watch.CheckedAt = now;
            watch.NextCheckAt = now + StockPauser.Interval;
            watch.FeedUpdatedAt = stock.UpdatedAt;
            var brand = (await brands.GetAsync([database.BrandId!.Value], cancellationToken)).Single();
            if (!fresh && ads.Count > 0)
            {
                if (watch.StaleSince is null)
                {
                    watch.StaleSince = now;
                    await database.AfterCommitAsync(_ => { KairoTelemetry.Event(logger, "feed.stale", brand.Id); return Task.CompletedTask; }, cancellationToken);
                    await notifications.NotifyAsync(new Notification("connection.problem", LiveAudience.Roles(BrandRoles.Admin),
                        "Stock pauses are on hold", "Kairo hasn't had a stock update for 3 hours, so it isn't pausing or resuming ads for stock. Check the product feed.",
                        $"/{brand.Handle}/brand-kit/catalogue"), cancellationToken);
                    await audit.RecordAsync(new AuditRecord("launch.stock_feed_stale", "brand", brand.Id.ToString(), null, new { stock.UpdatedAt }, LaunchRunner.Actor), cancellationToken);
                }
            }
            else
            {
                watch.StaleSince = null;
            }

            var changed = new HashSet<Guid>();
            foreach (var ad in applied)
            {
                db.AdPauses.Add(Row(ad, PauseStates.Paused, null, now));
                await audit.RecordAsync(new AuditRecord("launch.ad_paused_stock", "campaign_room", ad.Launch.RoomId.ToString(), null,
                    new { ad.AdResource, ad.CampaignType, ad.ProductIds }, LaunchRunner.Actor), cancellationToken);
                await notifications.NotifyAsync(new Notification("stock.pause_changed", LiveAudience.Roles(BrandRoles.Marketer),
                    "Ad paused for stock", $"Kairo paused a {Label(ad.CampaignType)} ad in {ad.Plan.Title} because its products sold out.",
                    Link(brand, ad.Launch.RoomId)), cancellationToken);
                changed.Add(ad.Launch.RoomId);
            }

            foreach (var row in resumed)
            {
                var current = await db.AdPauses.SingleAsync(pause => pause.Id == row.Id, cancellationToken);
                if (current.State != PauseStates.Paused) continue;
                current.State = PauseStates.Resumed;
                current.ResumedAt = now;
                current.Version++;
                var title = ads.First(ad => ad.Launch.Id == row.LaunchId).Plan.Title;
                await audit.RecordAsync(new AuditRecord("launch.ad_resumed_stock", "campaign_room", row.RoomId.ToString(), null,
                    new { row.AdResource, row.CampaignType }, LaunchRunner.Actor), cancellationToken);
                await notifications.NotifyAsync(new Notification("stock.pause_changed", LiveAudience.Roles(BrandRoles.Marketer),
                    "Ad resumed after restock", $"Kairo resumed a {Label(row.CampaignType)} ad in {title} because its products are back in stock.",
                    Link(brand, row.RoomId)), cancellationToken);
                changed.Add(row.RoomId);
            }

            foreach (var row in plan.Cancel)
            {
                var current = await db.AdPauses.SingleAsync(pause => pause.Id == row.Id, cancellationToken);
                if (current.State != PauseStates.Held) continue;
                current.State = PauseStates.Cancelled;
                current.Version++;
                changed.Add(row.RoomId);
            }

            foreach (var batch in held.Values.GroupBy(entry => entry.BatchId))
            {
                var first = batch.First();
                await database.AfterCommitAsync(_ => { KairoTelemetry.Event(logger, "mass_pause.held", brand.Id, first.Ad.Launch.RoomId, batch.Key); return Task.CompletedTask; }, cancellationToken);
                foreach (var entry in batch) db.AdPauses.Add(Row(entry.Ad, PauseStates.Held, batch.Key, now));
                await audit.RecordAsync(new AuditRecord("launch.mass_pause_held", "campaign_room", first.Ad.Launch.RoomId.ToString(), null,
                    new { batchId = batch.Key, first.Ad.CampaignResource, ads = batch.Select(entry => entry.Ad.AdResource).ToArray(), first.Total }, LaunchRunner.Actor), cancellationToken);
                await notifications.NotifyAsync(new Notification("mass_pause.held", LiveAudience.Roles(BrandRoles.Admin | BrandRoles.Marketer),
                    "Stock pauses need confirming",
                    $"{batch.Count()} of {first.Total} ads in the {Label(first.Ad.CampaignType)} campaign for {first.Ad.Plan.Title} would pause at once because products sold out. Confirm or dismiss the pauses.",
                    Link(brand, first.Ad.Launch.RoomId), Subject(batch.Key)), cancellationToken);
                changed.Add(first.Ad.Launch.RoomId);
            }

            foreach (var room in changed)
                await live.PublishAsync(new LiveEvent("launch.stock_changed", new { campaignId = room }, LiveAudience.Everyone), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch { await database.RollbackAsync(CancellationToken.None); throw; }
    }

    public static async Task SetStatusAsync(LaunchGoogle google, string account, (string Resource, string Status)[] changes, CancellationToken cancellationToken)
    {
        var operations = changes.Select(change => (object)new
        {
            adGroupAdOperation = new { update = new { resourceName = change.Resource, status = change.Status }, updateMask = "status" },
        }).ToArray();
        await google.MutateAsync(account, operations, true, cancellationToken);
        await google.MutateAsync(account, operations, false, cancellationToken);
    }

    public static string Subject(Guid batchId) => $"stock-batch:{batchId:N}";

    public static string Link(BrandSummary brand, Guid roomId) => $"/{brand.Handle}/campaigns/{roomId}/performance";

    public static string Label(string campaignType) => campaignType switch
    {
        "search" => "Search",
        "demand_gen" => "Demand Gen",
        _ => "Shopping",
    };

    private async Task<List<StockAd>> RunningAdsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var launches = await db.Launches.AsNoTracking().Where(row => row.Status == "live").ToListAsync(cancellationToken);
        var running = launches.Select(record => (Record: record, Plan: LaunchRunner.Read<LaunchPlan>(record.Plan)))
            .Where(launch => launch.Plan.GoogleAdsAccountId is not null
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(launch.Plan.TimeZone)).DateTime) <= launch.Plan.End)
            .ToDictionary(launch => launch.Record.RoomId);
        var rooms = running.Keys.ToList();
        var maps = await db.ProductAds.AsNoTracking()
            .Where(row => rooms.Contains(row.RoomId) && (row.CampaignType == "search" || row.CampaignType == "demand_gen"))
            .ToListAsync(cancellationToken);
        return [.. maps.GroupBy(row => (row.RoomId, row.AdResource)).Select(group =>
        {
            var launch = running[group.Key.RoomId];
            var first = group.First();
            return new StockAd(launch.Record, launch.Plan, first.CampaignType, first.CampaignResource, first.AdResource,
                [.. group.Select(row => row.ProductId).Distinct().Order(StringComparer.Ordinal)]);
        })];
    }

    private sealed record StockDecision(List<StockAd> Pause, List<AdPause> Resume, List<AdPause> Cancel);

    private sealed record HeldAd(StockAd Ad, Guid BatchId, int Total);

    private static StockDecision Decide(List<StockAd> ads, List<AdPause> pauses, CatalogueStock stock)
    {
        var products = stock.Products.ToDictionary(product => product.Id);
        var decision = new StockDecision([], [], []);
        foreach (var ad in ads)
        {
            var states = ad.ProductIds.Select(id => Guid.TryParse(id, out var productId) ? products.GetValueOrDefault(productId) : null).ToArray();
            var soldOut = states.Length > 0 && states.All(state => state is not null && Paused.Contains(state.State));
            var rows = pauses.Where(row => row.RoomId == ad.Launch.RoomId && row.AdResource == ad.AdResource).ToArray();
            var open = rows.FirstOrDefault(row => row.State is PauseStates.Held or PauseStates.Paused);
            if (open is null && soldOut)
            {
                var since = states.Max(state => state!.Since);
                var decided = rows.Where(row => row.State is PauseStates.Undone or PauseStates.Dismissed).Max(row => row.DecidedAt);
                if (decided is null || since > decided) decision.Pause.Add(ad);
            }
            else if (open is { State: PauseStates.Paused } && !soldOut) decision.Resume.Add(open);
            else if (open is { State: PauseStates.Held } && !soldOut) decision.Cancel.Add(open);
        }
        return decision;
    }

    private static Dictionary<string, HeldAd> Hold(List<StockAd> pause)
    {
        var held = new Dictionary<string, HeldAd>();
        foreach (var campaign in pause.GroupBy(ad => (ad.Launch.Id, ad.CampaignResource)))
        {
            var launch = campaign.First().Launch;
            var total = CampaignAds(launch, campaign.Key.CampaignResource);
            var count = campaign.Count();
            if (count * 100 <= total * HoldPercent) continue;
            var batch = Guid.NewGuid();
            foreach (var ad in campaign) held[ad.AdResource] = new HeldAd(ad, batch, Math.Max(total, count));
        }
        return held;
    }

    private static int CampaignAds(LaunchRecord launch, string campaignResource)
    {
        var resources = LaunchRunner.Read<Dictionary<string, string>>(launch.Resources);
        var key = resources.FirstOrDefault(pair => pair.Value == campaignResource && pair.Key.StartsWith("campaign:", StringComparison.Ordinal)).Key;
        if (key is null || launch.PendingBuild is null) return 0;
        return LaunchRunner.Read<BuildJournal>(launch.PendingBuild).Ads.Count(ad => ad.CampaignKey == key);
    }

    private static AdPause Row(StockAd ad, string state, Guid? batch, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        RoomId = ad.Launch.RoomId,
        LaunchId = ad.Launch.Id,
        CampaignType = ad.CampaignType,
        CampaignResource = ad.CampaignResource,
        AdResource = ad.AdResource,
        ProductIds = LaunchRunner.Write(ad.ProductIds),
        State = state,
        BatchId = batch,
        CreatedAt = now,
        PausedAt = state == PauseStates.Paused ? now : null,
    };
}
