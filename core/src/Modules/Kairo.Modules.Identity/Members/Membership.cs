namespace Kairo.Modules.Identity.Members;

internal sealed class Membership : IBrandOwned
{
    public Guid BrandId { get; set; }

    public Guid UserId { get; set; }

    public BrandRoles Roles { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
