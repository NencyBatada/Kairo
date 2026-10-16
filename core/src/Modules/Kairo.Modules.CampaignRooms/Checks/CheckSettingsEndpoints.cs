using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.CampaignRooms.Data;

namespace Kairo.Modules.CampaignRooms.Checks;

internal static class CheckSettingsEndpoints
{
    public const string Stale = "Someone else changed the check settings. Reload them and try again.";

    public static void MapCheckSettings(this IEndpointRouteBuilder api)
    {
        var settings = api.MapBrandApi().MapGroup("/check-settings");
        settings.MapGet("", GetAsync);
        settings.MapPut("", SaveAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Ok<CheckSettingsView>> GetAsync(
        CurrentMember member,
        CheckSettingsStore store,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(CheckSettingsView.Of(await store.GetAsync(member.BrandId, cancellationToken)));

    private static async Task<Results<Ok<CheckSettingsView>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        SaveCheckSettingsRequest request,
        CurrentMember member,
        KairoDatabase database,
        CampaignRoomsDbContext db,
        CheckSettingsStore store,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await database.LockAsync($"kairo.check-settings.{member.BrandId}", cancellationToken);

        var saved = await db.CheckSettings.SingleOrDefaultAsync(settings => settings.BrandId == member.BrandId, cancellationToken);
        var (valid, errors) = CheckSettingsRules.Check(request, saved?.CustomRules.Select(rule => rule.Id).ToList() ?? []);
        if (valid is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (request.Version != (saved?.Version ?? 0))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Stale);
        }

        var before = CheckSettingsView.Of(EffectiveCheckSettings.From(saved));
        if (saved is null)
        {
            saved = new CheckSettings { BrandId = member.BrandId };
            db.CheckSettings.Add(saved);
        }

        saved.Version++;
        saved.Rules = valid.Rules;
        saved.CustomRules = valid.CustomRules;
        saved.PassScore = valid.PassScore;
        saved.FixAttempts = valid.FixAttempts;
        saved.UpdatedBy = member.UserId;
        saved.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var after = CheckSettingsView.Of(await store.GetAsync(member.BrandId, cancellationToken));
        await audit.RecordAsync(
            new AuditRecord("check_settings.changed", "brand", member.BrandId.ToString(), before, after),
            cancellationToken);
        await live.PublishAsync(CampaignLiveEvents.CheckSettingsChanged(), cancellationToken);
        return TypedResults.Ok(after);
    }
}
