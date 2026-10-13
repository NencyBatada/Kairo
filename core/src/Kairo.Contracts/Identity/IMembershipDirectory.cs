using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Identity;

public interface IMembershipDirectory
{
    Task<IReadOnlyList<BrandMembership>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record BrandMembership(Guid BrandId, BrandRoles Roles);
