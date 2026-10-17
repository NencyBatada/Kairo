namespace Kairo.SharedKernel.Access;

public sealed class CurrentMember
{
    public Guid BrandId { get; private set; }

    public Guid UserId { get; private set; }

    public BrandRoles Roles { get; private set; }

    public bool Has(BrandRoles role) => (Roles & role) != BrandRoles.None;

    public async Task<bool> LoadAsync(Guid brandId, Guid userId, IBrandAccess access, CancellationToken cancellationToken)
    {
        if (await access.FindRolesAsync(brandId, userId, cancellationToken) is not { } roles) return false;
        Set(brandId, userId, roles);
        return true;
    }

    internal void Set(Guid brandId, Guid userId, BrandRoles roles)
    {
        BrandId = brandId;
        UserId = userId;
        Roles = roles;
    }
}
