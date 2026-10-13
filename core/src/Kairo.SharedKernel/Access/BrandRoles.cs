namespace Kairo.SharedKernel.Access;

[Flags]
public enum BrandRoles
{
    None = 0,
    Admin = 1,
    Marketer = 2,
    Approver = 4,
    All = Admin | Marketer | Approver,
}

public enum BrandRole
{
    Admin,
    Marketer,
    Approver,
}

public static class BrandRoleConversions
{
    public static BrandRoles ToFlag(this BrandRole role) => role switch
    {
        BrandRole.Admin => BrandRoles.Admin,
        BrandRole.Marketer => BrandRoles.Marketer,
        BrandRole.Approver => BrandRoles.Approver,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static BrandRoles ToFlags(this IEnumerable<BrandRole> roles) =>
        roles.Aggregate(BrandRoles.None, (all, role) => all | role.ToFlag());

    public static BrandRole[] ToList(this BrandRoles roles) =>
        Enum.GetValues<BrandRole>().Where(role => roles.HasFlag(role.ToFlag())).ToArray();
}
