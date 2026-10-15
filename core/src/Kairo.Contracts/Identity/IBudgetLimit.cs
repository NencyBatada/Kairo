namespace Kairo.Contracts.Identity;

public interface IBudgetLimit
{
    Task<BudgetCap> GetAsync(Guid brandId, CancellationToken cancellationToken);
}

public sealed record BudgetCap(decimal? Amount, string Currency);
