using Kairo.SharedKernel.Access;

namespace Kairo.Contracts.Live;

public enum LiveAudienceKind
{
    Everyone,
    Roles,
    Users,
}

public sealed class LiveAudience
{
    private LiveAudience(LiveAudienceKind kind, BrandRoles roleFilter, IReadOnlyList<Guid> userFilter)
    {
        Kind = kind;
        RoleFilter = roleFilter;
        UserFilter = userFilter;
    }

    public static LiveAudience Everyone { get; } = new(LiveAudienceKind.Everyone, BrandRoles.None, []);

    public LiveAudienceKind Kind { get; }

    public BrandRoles RoleFilter { get; }

    public IReadOnlyList<Guid> UserFilter { get; }

    public static LiveAudience Roles(BrandRoles roles) => new(LiveAudienceKind.Roles, roles, []);

    public static LiveAudience Users(params Guid[] users) => new(LiveAudienceKind.Users, BrandRoles.None, users);

    public bool Includes(Guid userId, BrandRoles roles) => Kind switch
    {
        LiveAudienceKind.Everyone => true,
        LiveAudienceKind.Roles => (roles & RoleFilter) != BrandRoles.None,
        LiveAudienceKind.Users => UserFilter.Contains(userId),
        _ => false,
    };
}
