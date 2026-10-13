namespace Kairo.SharedKernel.Access;

public sealed class CurrentMember
{
    public Guid BrandId { get; private set; }

    public Guid UserId { get; private set; }

    public BrandRoles Roles { get; private set; }

    public bool Has(BrandRoles role) => (Roles & role) != BrandRoles.None;

    internal void Set(Guid brandId, Guid userId, BrandRoles roles)
    {
        BrandId = brandId;
        UserId = userId;
        Roles = roles;
    }
}
