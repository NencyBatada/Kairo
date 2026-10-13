using Kairo.Contracts.Identity;
using Kairo.IntegrationTests.Api;
using Kairo.SharedKernel.Access;
using Kairo.SharedKernel.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Notifications;

public sealed class BrandMembersTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listing_returns_the_current_workspace_s_members_with_their_email_and_roles()
    {
        var owner = await Workspaces.SignInAsync(_api, "Priya Owner");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");
        await Workspaces.CreateAsync((await Workspaces.SignInAsync(_api)).Browser);

        var members = await ListAsync(brandId);

        Assert.Equal(2, members.Count);
        var listedOwner = Assert.Single(members, member => member.UserId == owner.UserId);
        Assert.Equal((owner.Account.Email, "Priya Owner", BrandRoles.All), (listedOwner.Email, listedOwner.Name, listedOwner.Roles));
        Assert.Equal(BrandRoles.Marketer, Assert.Single(members, member => member.UserId == marketer.UserId).Roles);
    }

    [Fact]
    public async Task Listing_needs_a_workspace()
    {
        await using var scope = _api.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IBrandMembers>().ListAsync(Token));
    }

    [Fact]
    public async Task Finding_works_for_any_workspace_without_a_workspace_set()
    {
        var owner = await Workspaces.SignInAsync(_api, "Arjun Owner");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await using var scope = _api.Services.CreateAsyncScope();
        var members = scope.ServiceProvider.GetRequiredService<IBrandMembers>();

        var found = await members.FindAsync(brandId, owner.UserId, Token);

        Assert.Equal(new BrandMember(owner.UserId, owner.Account.Email, "Arjun Owner", BrandRoles.All), found);
        Assert.Null(await members.FindAsync(brandId, Guid.NewGuid(), Token));
        Assert.Null(await members.FindAsync(Guid.NewGuid(), owner.UserId, Token));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<IReadOnlyList<BrandMember>> ListAsync(Guid brandId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brandId, Token);
        return await scope.ServiceProvider.GetRequiredService<IBrandMembers>().ListAsync(Token);
    }
}
