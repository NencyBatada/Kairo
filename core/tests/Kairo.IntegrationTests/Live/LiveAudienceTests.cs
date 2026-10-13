using Kairo.Contracts.Live;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Live;

public sealed class LiveAudienceTests
{
    private static readonly Guid Priya = Guid.NewGuid();
    private static readonly Guid Arjun = Guid.NewGuid();

    [Fact]
    public void Everyone_includes_every_member()
    {
        Assert.True(LiveAudience.Everyone.Includes(Priya, BrandRoles.Marketer));
        Assert.True(LiveAudience.Everyone.Includes(Arjun, BrandRoles.Approver));
    }

    [Fact]
    public void A_role_audience_includes_members_holding_any_of_its_roles()
    {
        var audience = LiveAudience.Roles(BrandRoles.Admin | BrandRoles.Approver);

        Assert.True(audience.Includes(Priya, BrandRoles.Approver));
        Assert.True(audience.Includes(Priya, BrandRoles.Admin | BrandRoles.Marketer));
        Assert.False(audience.Includes(Priya, BrandRoles.Marketer));
    }

    [Fact]
    public void A_user_audience_includes_only_the_people_it_names()
    {
        var audience = LiveAudience.Users(Priya);

        Assert.True(audience.Includes(Priya, BrandRoles.Marketer));
        Assert.False(audience.Includes(Arjun, BrandRoles.All));
    }

    [Fact]
    public void An_audience_says_what_kind_it_is()
    {
        Assert.Equal(LiveAudienceKind.Everyone, LiveAudience.Everyone.Kind);
        Assert.Equal((LiveAudienceKind.Roles, BrandRoles.Admin), (LiveAudience.Roles(BrandRoles.Admin).Kind, LiveAudience.Roles(BrandRoles.Admin).RoleFilter));
        Assert.Equal([Priya, Arjun], LiveAudience.Users(Priya, Arjun).UserFilter);
    }
}
