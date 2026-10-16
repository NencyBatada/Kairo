using System.Globalization;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Care.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Care.Recommendations;

internal sealed record RecommendationSweep(int Brands, int Drafted, int Refreshed);

internal sealed class RecommendationDrafter(CareDbContext db, KairoDatabase database, ICampaignTests tests, ICampaignChanges changes,
    IApprovers approvers, INotifications notifications, IAuditLog audit, ILiveUpdates live, TimeProvider time, ILogger<RecommendationDrafter> logger)
{
    public static readonly AuditActor Actor = new(AuditActorType.System, "Care", "care");
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);
    public const string ApprovalKind = "approval.needed";
    public const string ReadyKind = "report.ready";

    public static string Subject(Guid id) => $"recommendation:{id:N}";

    public static LiveEvent Changed(Guid roomId) => new("care.recommendations_changed", new { campaignId = roomId }, LiveAudience.Everyone);

    public static string Link(BrandSummary brand, Guid roomId) => $"/{brand.Handle}/campaigns/{roomId}/recommendations";

    public static string LockName(Guid roomId) => $"care-room:{roomId:N}";

    public async Task<(int Drafted, int Refreshed)> RunAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var (drafted, refreshed) = (0, 0);
        foreach (var test in await tests.ListRunningAsync(cancellationToken))
        {
            try
            {
                await ReleaseStuckAsync(test, cancellationToken);
                if (!test.Running && await DraftAsync(brand, test, cancellationToken)) drafted++;
                if (await RefreshGoogleAsync(test, cancellationToken)) refreshed++;
                await NotifyAsync(brand, test, cancellationToken);
            }
            catch (CampaignChangeException exception)
            {
                logger.LogWarning("Recommendations for campaign {RoomId} were skipped: {Reason}", test.RoomId, exception.Message);
            }
        }

        return (drafted, refreshed);
    }

    private async Task<bool> DraftAsync(BrandSummary brand, CampaignTest test, CancellationToken cancellationToken)
    {
        if (await db.Runs.AnyAsync(run => run.RoomId == test.RoomId, cancellationToken)) return false;
        var today = Today(brand);
        var ended = test.EndedAt is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone(brand)).DateTime) : today;
        var from = test.Start ?? ended;
        var to = test.End is { } end && end < ended ? end : ended;
        var drafts = test.Start is null ? [] : RecommendationRules.Draft(test.Kpi, brand.Currency, await changes.ReadAsync(test.RoomId, from, to, cancellationToken), from, to, today);
        return await SaveAsync(test.RoomId, async () =>
        {
            if (await db.Runs.AnyAsync(run => run.RoomId == test.RoomId, cancellationToken)) return false;
            var now = time.GetUtcNow();
            db.Runs.Add(new RecommendationRun { Id = Guid.NewGuid(), RoomId = test.RoomId, From = from, To = to, Drafted = drafts.Count, DraftedAt = now });
            foreach (var draft in drafts)
            {
                var recommendation = new Recommendation
                {
                    Id = Guid.NewGuid(), RoomId = test.RoomId, Source = RecommendationSources.Kairo, Type = draft.Type, CampaignType = draft.CampaignType,
                    State = RecommendationStates.Proposed, Title = draft.Title, ExpectedEffect = draft.ExpectedEffect, Reasoning = draft.Reasoning,
                    Change = Write(draft.Change), Baseline = Write(draft.Baseline), Evidence = Write(draft.Evidence), CreatedAt = now, UpdatedAt = now,
                };
                db.Recommendations.Add(recommendation);
                await ProposedAsync(recommendation, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            if (drafts.Count > 0)
                await notifications.NotifyAsync(new Notification(ReadyKind, LiveAudience.Roles(BrandRoles.Marketer | BrandRoles.Approver),
                    $"Recommendations ready for {test.Title}", $"The test ended and Kairo has {drafts.Count} recommendation{(drafts.Count == 1 ? "" : "s")} to review.",
                    Link(brand, test.RoomId), $"room:{test.RoomId:N}:recommendations"), cancellationToken);
            await live.PublishAsync(Changed(test.RoomId), cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<bool> RefreshGoogleAsync(CampaignTest test, CancellationToken cancellationToken)
    {
        var suggestions = await changes.GoogleRecommendationsAsync(test.RoomId, cancellationToken);
        return await SaveAsync(test.RoomId, async () =>
        {
            var now = time.GetUtcNow();
            var existing = await db.Recommendations.Where(row => row.RoomId == test.RoomId && row.Source == RecommendationSources.Google).ToListAsync(cancellationToken);
            var changed = false;
            foreach (var suggestion in suggestions)
            {
                var (title, effect, reasoning, evidence) = Describe(suggestion);
                if (existing.FirstOrDefault(row => row.GoogleResource == suggestion.ResourceName) is { } known)
                {
                    if (known.State == RecommendationStates.Proposed && (known.ExpectedEffect != effect || known.Evidence != evidence))
                    {
                        (known.ExpectedEffect, known.Evidence, known.UpdatedAt) = (effect, evidence, now);
                        known.Version++;
                        changed = true;
                    }

                    continue;
                }

                var recommendation = new Recommendation
                {
                    Id = Guid.NewGuid(), RoomId = test.RoomId, Source = RecommendationSources.Google, Type = suggestion.Type, CampaignType = suggestion.CampaignType,
                    State = RecommendationStates.Proposed, Title = title, ExpectedEffect = effect, Reasoning = reasoning,
                    Change = Write(new ChangeDetails(CampaignType: suggestion.CampaignType, GoogleResource: suggestion.ResourceName)), Evidence = evidence,
                    GoogleResource = suggestion.ResourceName, CreatedAt = now, UpdatedAt = now,
                };
                db.Recommendations.Add(recommendation);
                await ProposedAsync(recommendation, cancellationToken);
                changed = true;
            }

            var current = suggestions.Select(suggestion => suggestion.ResourceName).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in existing.Where(row => row.State == RecommendationStates.Proposed && !current.Contains(row.GoogleResource!)))
            {
                await ExpireAsync(gone, "Google Ads no longer suggests this.", Actor, cancellationToken);
                changed = true;
            }

            if (!changed) return false;
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(Changed(test.RoomId), cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task NotifyAsync(BrandSummary brand, CampaignTest test, CancellationToken cancellationToken)
    {
        var waiting = await db.Recommendations.AsNoTracking()
            .Where(row => row.RoomId == test.RoomId && row.State == RecommendationStates.Proposed && row.NotifiedAt == null).ToListAsync(cancellationToken);
        waiting = [.. waiting.Where(row => RecommendationRules.Visible(row.Source, row.Type, test.Running) && !RecommendationRules.Held(row.Source, test.Running))];
        if (waiting.Count == 0) return;
        await SaveAsync(test.RoomId, async () =>
        {
            foreach (var row in waiting)
            {
                var recommendation = await db.Recommendations.SingleAsync(item => item.Id == row.Id, cancellationToken);
                if (recommendation.State != RecommendationStates.Proposed || recommendation.NotifiedAt is not null) continue;
                var duty = RecommendationRules.DutyOf(recommendation.Source, recommendation.Type);
                var assigned = await approvers.ForAsync(brand.Id, duty, cancellationToken);
                if (RecommendationRules.NeedsBudgetOwner(recommendation.Source, recommendation.Type))
                {
                    var owners = await approvers.ForAsync(brand.Id, ApprovalDuty.BudgetOwner, cancellationToken);
                    assigned = [.. (assigned.Count > 0 ? assigned.IntersectBy(owners.Select(owner => owner.UserId), member => member.UserId) : owners)];
                }

                var audience = assigned.Count > 0 ? LiveAudience.Users([.. assigned.Select(member => member.UserId)]) : LiveAudience.Roles(BrandRoles.Approver);
                await notifications.NotifyAsync(new Notification(ApprovalKind, audience, $"Recommendation for {test.Title}",
                    recommendation.Title + ". " + recommendation.ExpectedEffect, Link(brand, test.RoomId), Subject(recommendation.Id)), cancellationToken);
                recommendation.NotifiedAt = time.GetUtcNow();
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task ReleaseStuckAsync(CampaignTest test, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - StuckAfter;
        if (!await db.Recommendations.AnyAsync(row => row.RoomId == test.RoomId && row.State == RecommendationStates.Approved && row.DecidedAt < cutoff, cancellationToken)) return;
        await SaveAsync(test.RoomId, async () =>
        {
            foreach (var stuck in await db.Recommendations.Where(row => row.RoomId == test.RoomId && row.State == RecommendationStates.Approved && row.DecidedAt < cutoff).ToListAsync(cancellationToken))
            {
                stuck.State = RecommendationStates.Proposed;
                stuck.Problem = "Kairo couldn't confirm this change was applied. Check the campaign in Google Ads, then approve again.";
                stuck.UpdatedAt = time.GetUtcNow();
                stuck.Version++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(Changed(test.RoomId), cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task ExpireAsync(Recommendation recommendation, string reason, AuditActor? actor, CancellationToken cancellationToken)
    {
        var before = new { recommendation.State };
        recommendation.State = RecommendationStates.Expired;
        recommendation.Problem = reason;
        recommendation.UpdatedAt = time.GetUtcNow();
        recommendation.Version++;
        await notifications.ResolveAsync(Subject(recommendation.Id), cancellationToken);
        await audit.RecordAsync(new AuditRecord("care.recommendation_expired", "recommendation", recommendation.Id.ToString(), before,
            new { recommendation.State, recommendation.RoomId, recommendation.Type, reason }, actor), cancellationToken);
    }

    private Task ProposedAsync(Recommendation recommendation, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditRecord("care.recommendation_proposed", "recommendation", recommendation.Id.ToString(), null,
            new { recommendation.RoomId, recommendation.Source, recommendation.Type, recommendation.Title }, Actor), cancellationToken);

    private async Task<bool> SaveAsync(Guid roomId, Func<Task<bool>> change, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(LockName(roomId), cancellationToken);
            var result = await change();
            await database.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static (string Title, string Effect, string Reasoning, string Evidence) Describe(GoogleRecommendation suggestion)
    {
        var name = suggestion.Type.Replace('_', ' ').ToLowerInvariant();
        var label = RecommendationRules.Label(suggestion.CampaignType);
        var impact = suggestion.Impact;
        var parts = new List<string>();
        var conversions = impact.PotentialConversions - impact.BaseConversions;
        var clicks = impact.PotentialClicks - impact.BaseClicks;
        var cost = impact.PotentialCost - impact.BaseCost;
        if (conversions != 0) parts.Add($"{Signed(conversions)} conversions");
        if (clicks != 0) parts.Add($"{Signed(clicks)} clicks");
        if (cost != 0) parts.Add($"{Signed(cost)} in cost");
        var effect = parts.Count == 0 ? "Google Ads didn't estimate the effect of this change." : $"Google Ads expects about {string.Join(", ", parts)} a week.";
        return (char.ToUpperInvariant(name[0]) + name[1..] + " for " + label, effect, $"Google Ads suggests this for the {label} campaign.",
            Write(new { impact, details = suggestion.Details }));
    }

    private static string Signed(decimal value) => (value > 0 ? "+" : "") + value.ToString("N0", CultureInfo.InvariantCulture);

    private DateOnly Today(BrandSummary brand) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone(brand)).DateTime);

    private static TimeZoneInfo Zone(BrandSummary brand) => TimeZoneInfo.TryFindSystemTimeZoneById(brand.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    public static string Write(object value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}

internal static class RecommendationJobs
{
    public static void MapRecommendationJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/care/recommendations", RunAsync);

    private static async Task<Ok<RecommendationSweep>> RunAsync(IBrandDirectory brands, IServiceScopeFactory scopes, ILogger<RecommendationDrafter> logger, IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var all = await brands.ListAllAsync(stopping);
        var (drafted, refreshed) = (0, 0);
        foreach (var brand in all)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, stopping);
                var result = await scope.ServiceProvider.GetRequiredService<RecommendationDrafter>().RunAsync(brand, stopping);
                drafted += result.Drafted;
                refreshed += result.Refreshed;
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                logger.LogError(exception, "Recommendations failed for brand {BrandId}.", brand.Id);
            }
        }

        return TypedResults.Ok(new RecommendationSweep(all.Count, drafted, refreshed));
    }
}
