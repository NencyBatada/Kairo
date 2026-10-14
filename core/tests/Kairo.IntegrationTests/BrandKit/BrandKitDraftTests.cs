using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.IntegrationTests.Api;
using Kairo.IntegrationTests.Live;

namespace Kairo.IntegrationTests.BrandKit;

public sealed class BrandKitDraftTests(SqlServer sqlServer) : IAsyncDisposable
{
    private const string StaleDraft = "Someone else changed the draft. Reload it and try again.";

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_workspace_has_no_brand_kit()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var kit = await BrandKits.GetAsync(admin.Browser, brandId);

        Assert.Equal(JsonValueKind.Null, kit.GetProperty("approved").ValueKind);
        Assert.Equal(JsonValueKind.Null, kit.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task Saving_creates_the_first_draft_with_cleaned_content()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var draft = await BrandKits.SaveAsync(
            admin.Browser,
            brandId,
            new
            {
                voice = new { summary = "  Warm.  ", traits = new[] { " bold ", "", "  " } },
                colours = new[] { new { hex = "#e4572e", name = "Ember", role = "primary" } },
            },
            0);

        Assert.Equal(1, draft.GetProperty("number").GetInt32());
        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal("manual", draft.GetProperty("origin").GetString());
        Assert.Equal(1, draft.GetProperty("revision").GetInt32());
        Assert.Equal(admin.UserId, draft.GetProperty("createdBy").GetGuid());
        Assert.Equal("Warm.", BrandKits.Summary(draft));
        var content = draft.GetProperty("content");
        Assert.Equal(new[] { "bold" }, content.GetProperty("voice").GetProperty("traits").EnumerateArray().Select(trait => trait.GetString()).ToArray());
        Assert.Equal("#E4572E", content.GetProperty("colours")[0].GetProperty("hex").GetString());
        Assert.Equal("primary", content.GetProperty("colours")[0].GetProperty("role").GetString());
        Assert.Equal(1, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Null_lists_are_saved_as_empty()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var draft = await BrandKits.SaveAsync(
            admin.Browser,
            brandId,
            new { voice = new { summary = "Warm.", traits = (string[]?)null }, colours = (object[]?)null, rules = (object?)null },
            0);

        Assert.Empty(draft.GetProperty("content").GetProperty("voice").GetProperty("traits").EnumerateArray());
        Assert.Empty(draft.GetProperty("content").GetProperty("colours").EnumerateArray());
    }

    [Fact]
    public async Task Each_save_raises_the_revision_and_a_stale_one_is_refused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);
        var second = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 1);

        var stale = await BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content("third"), 1);

        Assert.Equal(2, second.GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(StaleDraft, await BrandKits.ProblemTitleAsync(stale));
        Assert.Equal("second", BrandKits.Summary((await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft")));
    }

    [Fact]
    public async Task A_revision_from_an_earlier_draft_cannot_approve_or_overwrite_a_newer_one()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);
        await BrandKits.ApproveAsync(admin.Browser, brandId, 1);
        var newer = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);

        var staleApprove = await admin.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 1 }, Token);
        var staleSave = await BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content("third"), 1);

        Assert.NotEqual(1, newer.GetProperty("revision").GetInt32());
        Assert.Equal(new[] { HttpStatusCode.Conflict, HttpStatusCode.Conflict }, new[] { staleApprove.StatusCode, staleSave.StatusCode });
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Equal("first", BrandKits.Summary(kit.GetProperty("approved")));
        Assert.Equal("second", BrandKits.Summary(kit.GetProperty("draft")));
    }

    [Fact]
    public async Task A_first_draft_must_start_from_revision_0()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content(), 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Two_admins_saving_the_same_revision_at_once_leave_one_winner()
    {
        var (admin, brandId) = await WorkspaceAsync();
        var other = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "admin");
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);

        var responses = await Task.WhenAll(
            BrandKits.PutDraftAsync(admin.Browser, brandId, BrandKits.Content("from one"), 1),
            BrandKits.PutDraftAsync(other.Browser, brandId, BrandKits.Content("from two"), 1));

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(2, await BrandKits.DraftRevisionAsync(admin.Browser, brandId));
    }

    [Fact]
    public async Task Invalid_content_lists_its_problems_by_field_and_saves_nothing()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var response = await BrandKits.PutDraftAsync(
            admin.Browser,
            brandId,
            new { colours = new[] { new { hex = "red", name = "Red", role = "primary" } } },
            0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new[] { "Use a colour code like #E4572E." }, await BrandKits.ErrorsAsync(response, "content.colours[0].hex"));
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task Approving_makes_the_draft_the_approved_version()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("first"), 0);

        var approved = await BrandKits.ApproveAsync(admin.Browser, brandId, 1);

        Assert.Equal("approved", approved.GetProperty("status").GetString());
        Assert.Equal(admin.UserId, approved.GetProperty("approvedBy").GetGuid());
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Equal(1, kit.GetProperty("approved").GetProperty("number").GetInt32());
        Assert.Equal(JsonValueKind.Null, kit.GetProperty("draft").ValueKind);
    }

    [Fact]
    public async Task A_second_approval_supersedes_the_first()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));

        var draft = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);
        var kitWithDraft = await BrandKits.GetAsync(admin.Browser, brandId);
        await BrandKits.ApproveAsync(admin.Browser, brandId, draft.GetProperty("revision").GetInt32());

        Assert.Equal(2, draft.GetProperty("number").GetInt32());
        Assert.Equal("first", BrandKits.Summary(kitWithDraft.GetProperty("approved")));
        var kit = await BrandKits.GetAsync(admin.Browser, brandId);
        Assert.Equal(2, kit.GetProperty("approved").GetProperty("number").GetInt32());
        Assert.Equal("second", BrandKits.Summary(kit.GetProperty("approved")));
    }

    [Fact]
    public async Task Approving_needs_a_draft_at_the_current_revision()
    {
        var (admin, brandId) = await WorkspaceAsync();

        var noDraft = await admin.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 0 }, Token);
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        var stale = await admin.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 9 }, Token);

        Assert.Equal(HttpStatusCode.NotFound, noDraft.StatusCode);
        Assert.Equal("There's no draft.", await BrandKits.ProblemTitleAsync(noDraft));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await BrandKits.GetAsync(admin.Browser, brandId)).GetProperty("approved").ValueKind);
    }

    [Fact]
    public async Task Discarding_removes_the_draft_and_its_number_is_not_reused()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        var discarded = await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token);
        var again = await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token);
        var next = await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);

        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(2, next.GetProperty("number").GetInt32());
    }

    [Theory]
    [InlineData("marketer")]
    [InlineData("approver")]
    public async Task Only_admins_can_change_the_brand_kit(string role)
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        var member = await Workspaces.JoinAsync(_api, admin.Browser, brandId, role);

        var save = await BrandKits.PutDraftAsync(member.Browser, brandId, BrandKits.Content(), 1);
        var approve = await member.Browser.PostAsJsonAsync($"{BrandKits.Draft(brandId)}/approve", new { revision = 1 }, Token);
        var discard = await member.Browser.DeleteAsync(BrandKits.Draft(brandId), Token);
        var read = await member.Browser.GetAsync(BrandKits.Kit(brandId), Token);

        Assert.Equal(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK },
            new[] { save.StatusCode, approve.StatusCode, discard.StatusCode, read.StatusCode });
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_brand_kit()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content(), 0);
        var (outsider, _) = await WorkspaceAsync();

        var response = await outsider.Browser.GetAsync(BrandKits.Kit(brandId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Changes_are_audited_and_published_live()
    {
        var (admin, brandId) = await WorkspaceAsync();
        await BrandKits.ApproveNewAsync(admin.Browser, brandId, BrandKits.Content("first"));
        await BrandKits.SaveAsync(admin.Browser, brandId, BrandKits.Content("second"), 0);
        (await admin.Browser.DeleteAsync(BrandKits.Draft(brandId), Token)).EnsureSuccessStatusCode();

        var actions = await Workspaces.AuditActionsAsync(admin.Browser, brandId);
        var events = (await OutboxRows.ReadAsync(sqlServer.KairoConnectionString, brandId))
            .Where(row => row.Type == "brand_kit.changed")
            .Select(row => JsonDocument.Parse(row.Payload).RootElement.GetProperty("status").GetString())
            .ToArray();

        Assert.Equal(2, actions.Count(action => action == "brand_kit.draft_saved"));
        Assert.Contains("brand_kit.approved", actions);
        Assert.Contains("brand_kit.draft_discarded", actions);
        Assert.Equal(new[] { "draft", "approved", "draft", "discarded" }, events);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private async Task<(Member Admin, Guid BrandId)> WorkspaceAsync()
    {
        var admin = await Workspaces.SignInAsync(_api);
        return (admin, await Workspaces.CreateAsync(admin.Browser));
    }
}
