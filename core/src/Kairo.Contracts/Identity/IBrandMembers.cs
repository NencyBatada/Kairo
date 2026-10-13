using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Identity;

public interface IBrandMembers
{
    Task<IReadOnlyList<BrandMember>> ListAsync(CancellationToken cancellationToken);

    Task<BrandMember?> FindAsync(Guid brandId, Guid userId, CancellationToken cancellationToken);
}

public sealed record BrandMember(Guid UserId, string Email, string Name, BrandRoles Roles);
