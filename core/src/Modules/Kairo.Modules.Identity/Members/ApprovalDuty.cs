using Kairo.Contracts.Identity;

namespace Kairo.Modules.Identity.Members;

internal sealed class DutyAssignment : IBrandOwned
{
    public Guid BrandId { get; set; }

    public ApprovalDuty Duty { get; set; }

    public Guid UserId { get; set; }
}

internal static class ApprovalDuties
{
    public static string Describe(this ApprovalDuty duty) => duty switch
    {
        ApprovalDuty.Brief => "brief approver",
        ApprovalDuty.Assets => "asset approver",
        ApprovalDuty.Launch => "launch approver",
        ApprovalDuty.BudgetOwner => "budget owner",
        ApprovalDuty.BudgetMoves => "approver for budget moves",
        ApprovalDuty.TargetChanges => "approver for target CPA and ROAS changes",
        ApprovalDuty.GoogleSuggestions => "approver for Google Ads recommendations",
        _ => throw new ArgumentOutOfRangeException(nameof(duty), duty, null),
    };
}
