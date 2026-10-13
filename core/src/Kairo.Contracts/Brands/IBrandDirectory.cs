namespace Kairo.Contracts.Brands;

public interface IBrandDirectory
{
    Task<IReadOnlyList<BrandSummary>> GetAsync(IReadOnlyCollection<Guid> brandIds, CancellationToken cancellationToken);
}

public sealed record BrandSummary(Guid Id, string Handle, string Name);
