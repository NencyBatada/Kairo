using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Notifications;

namespace Kairo.IntegrationTests.Api;

public sealed class InvitationTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_invited_person_sees_the_invitation_when_they_sign_in()
    {
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);

        await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer", "approver");

        var mine = await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token);
        var invitation = Assert.Single(mine.EnumerateArray());
        Assert.Equal("Acme Shoes", invitation.GetProperty("workspaceName").GetString());
        Assert.Equal("Neha Rao", invitation.GetProperty("invitedBy").GetString());
        Assert.Equal(["marketer", "approver"], invitation.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Equal("invitation.sent", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Inviting_queues_an_email_to_the_invited_address_each_time_it_is_sent()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var address = $"priya-{Guid.NewGuid():N}@example.com";

        await Workspaces.InviteAsync(owner.Browser, brandId, address.ToUpperInvariant(), "marketer");
        await Workspaces.InviteAsync(owner.Browser, brandId, address, "approver");

        var emails = await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId, "Invitation");
        Assert.Equal(2, emails.Count);
        Assert.All(emails, email => Assert.Equal((address, "Pending"), (email.Address, email.Status)));
    }

    [Fact]
    public async Task A_refused_invitation_queues_no_email()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PostAsJsonAsync($"/api/brands/{brandId}/invitations", new { email = "not-an-email", roles = new[] { "marketer" } }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await NotificationRows.EmailsAsync(sqlServer.KairoConnectionString, brandId, "Invitation"));
    }

    [Fact]
    public async Task Accepting_makes_them_a_member_with_the_invited_roles()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        var accepted = await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(brandId, (await accepted.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid());
        var mine = await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token);
        Assert.Equal(["marketer"], Assert.Single(mine.EnumerateArray()).GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Empty((await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
        Assert.Equal("invitation.accepted", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Someone_else_cannot_accept_an_invitation_meant_for_another_email()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, $"priya-{Guid.NewGuid():N}@example.com", "admin");
        var intruder = await Workspaces.SignInAsync(_api);

        var response = await intruder.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await intruder.Browser.GetFromJsonAsync<JsonElement>("/api/brands", Token)).EnumerateArray());
        Assert.Empty((await intruder.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
    }

    [Fact]
    public async Task Emails_match_whatever_their_case_and_spacing()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);

        await Workspaces.InviteAsync(owner.Browser, brandId, $"  {invitee.Account.Email.ToUpperInvariant()} ", "marketer");

        Assert.Single((await invitee.Browser.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
    }

    [Fact]
    public async Task An_invitation_expires_after_fourteen_days()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        _api.Clock.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        var signedInAgain = await _api.SignInAsync(invitee.Account);

        Assert.Empty((await signedInAgain.GetFromJsonAsync<JsonElement>("/api/me/invitations", Token)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await signedInAgain.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token)).StatusCode);
    }

    [Fact]
    public async Task A_revoked_invitation_cannot_be_accepted()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        var revoked = await owner.Browser.PostAsync($"/api/brands/{brandId}/invitations/{invitationId}/revoke", null, Token);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token)).StatusCode);
        Assert.Equal("invitation.revoked", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task A_declined_invitation_is_gone()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var invitee = await Workspaces.SignInAsync(_api);
        var invitationId = await Workspaces.InviteAsync(owner.Browser, brandId, invitee.Account.Email, "marketer");

        var declined = await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/decline", null, Token);

        Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await invitee.Browser.PostAsync($"/api/me/invitations/{invitationId}/accept", null, Token)).StatusCode);
        Assert.Empty((await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/invitations", Token)).EnumerateArray());
        Assert.Equal("invitation.declined", (await Workspaces.LatestAuditEntryAsync(owner.Browser, brandId)).GetProperty("action").GetString());
    }

    [Fact]
    public async Task Members_cannot_be_invited_again()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var member = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await owner.Browser.PostAsJsonAsync(
            $"/api/brands/{brandId}/invitations",
            new { email = member.Account.Email, roles = new[] { "approver" } },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("errors");
        Assert.Equal("That person is already a member.", errors.GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task Inviting_again_replaces_the_pending_invitation()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var email = $"priya-{Guid.NewGuid():N}@example.com";

        var first = await Workspaces.InviteAsync(owner.Browser, brandId, email, "marketer");
        var second = await Workspaces.InviteAsync(owner.Browser, brandId, email, "approver");

        Assert.Equal(first, second);
        var pending = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/invitations", Token);
        Assert.Equal(["approver"], Assert.Single(pending.EnumerateArray()).GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Theory]
    [InlineData("not-an-email", "marketer")]
    [InlineData("Priya <priya@example.com>", "marketer")]
    [InlineData("priya@example.com", null)]
    public async Task Invitations_need_a_valid_email_and_a_role(string email, string? role)
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);

        var response = await owner.Browser.PostAsJsonAsync(
            $"/api/brands/{brandId}/invitations",
            new { email, roles = role is null ? Array.Empty<string>() : [role] },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_invite()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        var marketer = await Workspaces.JoinAsync(_api, owner.Browser, brandId, "marketer");

        var response = await marketer.Browser.PostAsJsonAsync(
            $"/api/brands/{brandId}/invitations",
            new { email = "someone@example.com", roles = new[] { "marketer" } },
            Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
