using Kairo.Modules.Identity.Data;

namespace Kairo.Modules.Identity.Members;

internal static class TeamRules
{
    public const string LastAdmin = "A workspace needs at least one admin. Make someone else an admin first.";

    public static Task LockAsync(KairoDatabase database, Guid brandId, CancellationToken cancellationToken) =>
        database.LockAsync($"kairo.team.{brandId}", cancellationToken);

    public static async Task<bool> IsLastAdminAsync(IdentityDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var admins = await db.Memberships
            .Where(membership => (membership.Roles & BrandRoles.Admin) == BrandRoles.Admin)
            .Select(membership => membership.UserId)
            .ToListAsync(cancellationToken);
        return admins is [var only] && only == userId;
    }

    public static async Task<string?> SoleDutyAsync(IdentityDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var assignments = await db.DutyAssignments.ToListAsync(cancellationToken);
        foreach (var duty in Enum.GetValues<ApprovalDuty>())
        {
            var holders = assignments.Where(assignment => assignment.Duty == duty).Select(assignment => assignment.UserId).ToList();
            if (holders is [var only] && only == userId)
            {
                return $"This member is the only {duty.Describe()}. Give that duty to someone else first.";
            }
        }

        return null;
    }
}
