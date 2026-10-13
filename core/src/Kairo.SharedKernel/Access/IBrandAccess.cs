namespace Kairo.SharedKernel.Access;

public interface IBrandAccess
{
    Task<BrandRoles?> FindRolesAsync(Guid brandId, Guid userId, CancellationToken cancellationToken);
}
