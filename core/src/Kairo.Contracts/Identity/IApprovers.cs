namespace Kairo.Contracts.Identity;

public enum ApprovalDuty
{
    Brief,
    Assets,
    Launch,
    BudgetOwner,
    BudgetMoves,
    TargetChanges,
    GoogleSuggestions,
}

public interface IApprovers
{
    Task<IReadOnlyList<BrandMember>> ForAsync(Guid brandId, ApprovalDuty duty, CancellationToken cancellationToken);
}
