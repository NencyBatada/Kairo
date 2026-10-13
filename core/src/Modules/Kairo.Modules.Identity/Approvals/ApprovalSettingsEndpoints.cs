using Kairo.Contracts.Audit;
using Kairo.Modules.Identity.Data;
using Kairo.Modules.Identity.Members;

namespace Kairo.Modules.Identity.Approvals;

internal static class ApprovalSettingsEndpoints
{
    private const decimal LargestBudgetLimit = 999_999_999_999_999.9999m;

    public static void MapApprovalSettings(this IEndpointRouteBuilder api)
    {
        var settings = api.MapBrandApi().MapGroup("/approval-settings");
        settings.MapGet("", GetAsync);
        settings.MapPut("", SaveAsync).RequireBrandRole(BrandRoles.Admin);
    }

    private static async Task<Ok<ApprovalSettings>> GetAsync(IdentityDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await LoadAsync(db, cancellationToken));

    private static async Task<Results<Ok<ApprovalSettings>, ValidationProblem>> SaveAsync(
        SaveApprovalSettingsRequest request,
        CurrentMember member,
        KairoDatabase database,
        IdentityDbContext db,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        (string Field, ApprovalDuty Duty, Guid[]? People)[] duties =
        [
            ("briefApprovers", ApprovalDuty.Brief, request.BriefApprovers),
            ("assetApprovers", ApprovalDuty.Assets, request.AssetApprovers),
            ("launchApprovers", ApprovalDuty.Launch, request.LaunchApprovers),
            ("budgetOwners", ApprovalDuty.BudgetOwner, request.BudgetOwners),
        ];

        await TeamRules.LockAsync(database, member.BrandId, cancellationToken);

        var approvers = (await db.Memberships
                .Where(membership => (membership.Roles & BrandRoles.Approver) == BrandRoles.Approver)
                .Select(membership => membership.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var errors = new Dictionary<string, string[]>();
        foreach (var (field, _, people) in duties)
        {
            if (people is not { Length: > 0 })
            {
                errors[field] = ["Choose at least one person."];
            }
            else if (people.Any(person => !approvers.Contains(person)))
            {
                errors[field] = ["Only members with the approver role can hold this duty."];
            }
        }

        if (request.BudgetLimit is < 0 or > LargestBudgetLimit)
        {
            errors["budgetLimit"] = ["Enter an amount of zero or more, or leave it empty."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var before = await LoadAsync(db, cancellationToken);
        var desired = duties
            .SelectMany(duty => duty.People!.Distinct().Select(person => (duty.Duty, UserId: person)))
            .ToHashSet();
        var current = await db.DutyAssignments.ToListAsync(cancellationToken);
        db.DutyAssignments.RemoveRange(current.Where(assignment => !desired.Contains((assignment.Duty, assignment.UserId))));
        foreach (var (duty, userId) in desired.Where(wanted => !current.Any(a => a.Duty == wanted.Duty && a.UserId == wanted.UserId)))
        {
            db.DutyAssignments.Add(new DutyAssignment { Duty = duty, UserId = userId });
        }

        var limit = await db.BudgetLimits.SingleAsync(cancellationToken);
        limit.Amount = request.BudgetLimit;
        await db.SaveChangesAsync(cancellationToken);

        var after = await LoadAsync(db, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("approval_settings.changed", "brand", member.BrandId.ToString(), before, after),
            cancellationToken);
        return TypedResults.Ok(after);
    }

    private static async Task<ApprovalSettings> LoadAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        var assignments = await db.DutyAssignments.AsNoTracking().ToListAsync(cancellationToken);
        var limit = await db.BudgetLimits.AsNoTracking().SingleAsync(cancellationToken);

        Guid[] On(ApprovalDuty duty) =>
            assignments.Where(assignment => assignment.Duty == duty).Select(assignment => assignment.UserId).Order().ToArray();

        return new ApprovalSettings(
            On(ApprovalDuty.Brief),
            On(ApprovalDuty.Assets),
            On(ApprovalDuty.Launch),
            On(ApprovalDuty.BudgetOwner),
            limit.Amount,
            limit.Currency);
    }
}

internal sealed record ApprovalSettings(
    Guid[] BriefApprovers,
    Guid[] AssetApprovers,
    Guid[] LaunchApprovers,
    Guid[] BudgetOwners,
    decimal? BudgetLimit,
    string Currency);

internal sealed record SaveApprovalSettingsRequest(
    Guid[] BriefApprovers,
    Guid[] AssetApprovers,
    Guid[] LaunchApprovers,
    Guid[] BudgetOwners,
    decimal? BudgetLimit);
