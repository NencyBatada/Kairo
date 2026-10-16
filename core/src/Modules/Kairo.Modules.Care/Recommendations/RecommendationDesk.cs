using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Identity;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Care.Data;

namespace Kairo.Modules.Care.Recommendations;

internal enum DeskError
{
    None,
    NotFound,
    Conflict,
    Forbidden,
    Google,
}

internal sealed record DeskResult(RecommendationView? View, DeskError Error = DeskError.None, string? Message = null)
{
    public static DeskResult Failed(DeskError error, string message) => new(null, error, message);
}

internal sealed record RecommendationView(
    Guid Id,
    int Version,
    string Source,
    string Type,
    string CampaignType,
    string State,
    string Title,
    string ExpectedEffect,
    string Reasoning,
    JsonElement Change,
    JsonElement Evidence,
    string Duty,
    bool Held,
    bool CanApprove,
    string? Problem,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DismissReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record CampaignTestView(string Stage, DateOnly? Start, DateOnly? End, DateTimeOffset? EndedAt, bool EndedEarly, bool Running);

internal sealed record RecommendationList(CampaignTestView Test, RecommendationView[] Recommendations);

internal sealed record ApproveRecommendation(int? Version);

internal sealed record DismissRecommendation(int? Version, string? Reason);

internal sealed class RecommendationDesk(CareDbContext db, KairoDatabase database, ICampaignTests tests, ICampaignChanges changes,
    IApprovers approvers, INotifications notifications, IAuditLog audit, ILiveUpdates live, IBrandDirectory brands, RecommendationDrafter drafter, TimeProvider time)
{
    public const int MaxReasonLength = 500;

    public async Task<RecommendationList?> ListAsync(Guid roomId, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await tests.GetAsync(roomId, cancellationToken) is not { } test) return null;
        var rows = await db.Recommendations.AsNoTracking().Where(row => row.RoomId == roomId).OrderByDescending(row => row.CreatedAt).ToListAsync(cancellationToken);
        var views = new List<RecommendationView>();
        foreach (var row in rows.Where(row => RecommendationRules.Visible(row.Source, row.Type, test.Running)))
            views.Add(await ViewAsync(row, test, member, cancellationToken));
        return new RecommendationList(new CampaignTestView(test.Stage, test.Start, test.End, test.EndedAt, test.EndedEarly, test.Running), [.. views]);
    }

    public async Task<DeskResult> ApproveAsync(Guid id, int version, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(id, version, cancellationToken) is not { } opened) return DeskResult.Failed(DeskError.NotFound, "Not found.");
        if (opened.Error is { } error) return error;
        var (recommendation, test) = (opened.Recommendation!, opened.Test!);
        if (RecommendationRules.Held(recommendation.Source, test.Running))
            return DeskResult.Failed(DeskError.Conflict, "Recommendations are held while the test runs. End the test early to use them.");
        if (!await MayApproveAsync(recommendation, member, cancellationToken))
            return DeskResult.Failed(DeskError.Forbidden, "You aren't an approver for this type of recommendation.");

        var claimed = await SaveAsync(recommendation, async row =>
        {
            if (row.State != RecommendationStates.Proposed || row.Version != version) return false;
            row.State = RecommendationStates.Approved;
            row.DecidedBy = member.UserId;
            row.DecidedAt = time.GetUtcNow();
            row.UpdatedAt = time.GetUtcNow();
            row.Version++;
            return true;
        }, cancellationToken);
        if (!claimed) return DeskResult.Failed(DeskError.Conflict, "This recommendation changed since you opened it. Refresh and try again.");

        string? expired;
        try
        {
            expired = await CheckAsync(recommendation, test, cancellationToken);
            if (expired is null) await ApplyAsync(recommendation, cancellationToken);
        }
        catch (CampaignChangeException exception)
        {
            await SaveAsync(recommendation, row =>
            {
                row.State = RecommendationStates.Proposed;
                row.DecidedBy = null;
                row.DecidedAt = null;
                row.Problem = exception.Message + " " + exception.Fix;
                row.UpdatedAt = time.GetUtcNow();
                row.Version++;
                return Task.FromResult(true);
            }, cancellationToken);
            return DeskResult.Failed(DeskError.Google, exception.Message + " " + exception.Fix);
        }

        if (expired is not null)
        {
            await SaveAsync(recommendation, async row =>
            {
                await drafter.ExpireAsync(row, expired, null, cancellationToken);
                return true;
            }, cancellationToken);
            return DeskResult.Failed(DeskError.Conflict, expired + " Kairo marked the recommendation as expired.");
        }

        await SaveAsync(recommendation, async row =>
        {
            row.State = RecommendationStates.Applied;
            row.Problem = null;
            row.UpdatedAt = time.GetUtcNow();
            row.Version++;
            await notifications.ResolveAsync(RecommendationDrafter.Subject(row.Id), cancellationToken);
            await audit.RecordAsync(new AuditRecord("care.recommendation_applied", "recommendation", row.Id.ToString(), new { State = RecommendationStates.Proposed },
                new { row.State, row.RoomId, row.Source, row.Type, row.Title, change = JsonDocument.Parse(row.Change).RootElement }), cancellationToken);
            return true;
        }, cancellationToken);
        return new DeskResult(await ViewAsync(recommendation, test, member, cancellationToken));
    }

    public async Task<DeskResult> DismissAsync(Guid id, int version, string? reason, CurrentMember member, CancellationToken cancellationToken)
    {
        if (await OpenAsync(id, version, cancellationToken) is not { } opened) return DeskResult.Failed(DeskError.NotFound, "Not found.");
        if (opened.Error is { } error) return error;
        var (recommendation, test) = (opened.Recommendation!, opened.Test!);
        await database.LockAsync(RecommendationDrafter.LockName(recommendation.RoomId), cancellationToken);
        await db.Entry(recommendation).ReloadAsync(cancellationToken);
        if (recommendation.State != RecommendationStates.Proposed || recommendation.Version != version)
            return DeskResult.Failed(DeskError.Conflict, "This recommendation changed since you opened it. Refresh and try again.");
        recommendation.State = RecommendationStates.Dismissed;
        recommendation.DismissReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        recommendation.DecidedBy = member.UserId;
        recommendation.DecidedAt = time.GetUtcNow();
        recommendation.UpdatedAt = time.GetUtcNow();
        recommendation.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.ResolveAsync(RecommendationDrafter.Subject(recommendation.Id), cancellationToken);
        await audit.RecordAsync(new AuditRecord("care.recommendation_dismissed", "recommendation", recommendation.Id.ToString(), new { State = RecommendationStates.Proposed },
            new { recommendation.State, recommendation.RoomId, recommendation.Source, recommendation.Type, recommendation.Title, reason = recommendation.DismissReason }), cancellationToken);
        await live.PublishAsync(RecommendationDrafter.Changed(recommendation.RoomId), cancellationToken);
        return new DeskResult(await ViewAsync(recommendation, test, member, cancellationToken));
    }

    private sealed record Opened(Recommendation? Recommendation, CampaignTest? Test, DeskResult? Error);

    private async Task<Opened?> OpenAsync(Guid id, int version, CancellationToken cancellationToken)
    {
        var recommendation = await db.Recommendations.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (recommendation is null || await tests.GetAsync(recommendation.RoomId, cancellationToken) is not { } test
            || !RecommendationRules.Visible(recommendation.Source, recommendation.Type, test.Running))
            return null;
        if (recommendation.State != RecommendationStates.Proposed)
            return new Opened(null, null, DeskResult.Failed(DeskError.Conflict, "This recommendation was already decided."));
        if (recommendation.Version != version)
            return new Opened(null, null, DeskResult.Failed(DeskError.Conflict, "This recommendation changed since you opened it. Refresh and try again."));
        return new Opened(recommendation, test, null);
    }

    private async Task<string?> CheckAsync(Recommendation recommendation, CampaignTest test, CancellationToken cancellationToken)
    {
        if (test.Stage is not (CampaignTestStages.Live or CampaignTestStages.ResultsReady or CampaignTestStages.Closed))
            return "The campaign is no longer running.";
        if (recommendation.Source == RecommendationSources.Google)
        {
            var current = await changes.GoogleRecommendationsAsync(recommendation.RoomId, cancellationToken);
            return current.Any(item => item.ResourceName == recommendation.GoogleResource) ? null : "Google Ads no longer suggests this.";
        }

        var zone = await brands.FindTimeZoneAsync(recommendation.BrandId, cancellationToken) is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var states = await changes.ReadAsync(recommendation.RoomId, today, today, cancellationToken);
        return RecommendationRules.Drift(Read<ChangeDetails>(recommendation.Change), Read<CampaignBaseline[]>(recommendation.Baseline), states, today);
    }

    private async Task ApplyAsync(Recommendation recommendation, CancellationToken cancellationToken)
    {
        var change = Read<ChangeDetails>(recommendation.Change);
        if (recommendation.Source == RecommendationSources.Google)
            await changes.ApplyGoogleAsync(recommendation.RoomId, recommendation.GoogleResource!, cancellationToken);
        else if (recommendation.Type == RecommendationTypes.BudgetMove)
            await changes.SetBudgetsAsync(recommendation.RoomId, change.Budgets!, cancellationToken);
        else
            await changes.SetTargetAsync(recommendation.RoomId, change.CampaignType!, change.BiddingStrategy!, change.Target!.Value, cancellationToken);
    }

    private async Task<bool> MayApproveAsync(Recommendation recommendation, CurrentMember member, CancellationToken cancellationToken)
    {
        if (!member.Has(BrandRoles.Approver)) return false;
        var assigned = await approvers.ForAsync(member.BrandId, RecommendationRules.DutyOf(recommendation.Source, recommendation.Type), cancellationToken);
        if (assigned.Count > 0 && assigned.All(approver => approver.UserId != member.UserId)) return false;
        if (!RecommendationRules.NeedsBudgetOwner(recommendation.Source, recommendation.Type)) return true;
        var owners = await approvers.ForAsync(member.BrandId, ApprovalDuty.BudgetOwner, cancellationToken);
        return owners.Any(owner => owner.UserId == member.UserId);
    }

    private async Task<RecommendationView> ViewAsync(Recommendation row, CampaignTest test, CurrentMember member, CancellationToken cancellationToken)
    {
        var held = RecommendationRules.Held(row.Source, test.Running);
        var canApprove = row.State == RecommendationStates.Proposed && !held && await MayApproveAsync(row, member, cancellationToken);
        return new RecommendationView(row.Id, row.Version, row.Source, row.Type, row.CampaignType, row.State, row.Title, row.ExpectedEffect, row.Reasoning,
            Read<JsonElement>(row.Change), Read<JsonElement>(row.Evidence), RecommendationRules.DutyKey(RecommendationRules.DutyOf(row.Source, row.Type)),
            held, canApprove, row.Problem, row.DecidedBy, row.DecidedAt, row.DismissReason, row.CreatedAt, row.UpdatedAt);
    }

    private async Task<bool> SaveAsync(Recommendation recommendation, Func<Recommendation, Task<bool>> change, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync(RecommendationDrafter.LockName(recommendation.RoomId), cancellationToken);
            await db.Entry(recommendation).ReloadAsync(cancellationToken);
            if (!await change(recommendation))
            {
                await database.RollbackAsync(cancellationToken);
                return false;
            }

            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(RecommendationDrafter.Changed(recommendation.RoomId), cancellationToken);
            await database.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)!;
}
