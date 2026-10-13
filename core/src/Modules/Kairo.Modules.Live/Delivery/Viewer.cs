using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Delivery;

internal sealed class Viewer(Guid brandId, Guid userId, BrandRoles roles, string? sessionKey)
{
    public Guid BrandId => brandId;

    public Guid UserId => userId;

    public string? SessionKey => sessionKey;

    public BrandRoles Roles { get; set; } = roles;

    public bool CanSee(OutboxEvent row) => row.Audience().Includes(UserId, Roles);
}
