namespace Kairo.Modules.Identity.Members;

internal sealed class BudgetLimit : IBrandOwned
{
    public Guid BrandId { get; set; }

    public decimal? Amount { get; set; }

    public string Currency { get; set; } = "";
}
