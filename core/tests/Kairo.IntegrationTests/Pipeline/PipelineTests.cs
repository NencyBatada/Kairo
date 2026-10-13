using System.Net;
using System.Net.Http.Json;
using Kairo.IntegrationTests.Persistence;
using Kairo.SharedKernel.Access;

namespace Kairo.IntegrationTests.Pipeline;

public sealed class PipelineTests(SqlServer sqlServer) : IAsyncLifetime
{
    private readonly FakeBrandAccess _access = new();
    private readonly Guid _brand = Guid.NewGuid();
    private readonly Guid _otherBrand = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _marketer = Guid.NewGuid();
    private PlumbingDatabase _database = null!;
    private PipelineApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await PlumbingDatabase.CreateAsync(sqlServer);
        _access.Add(_brand, _admin, BrandRoles.Admin | BrandRoles.Marketer);
        _access.Add(_otherBrand, _admin, BrandRoles.Admin);
        _access.Add(_brand, _marketer, BrandRoles.Marketer);
        _app = await PipelineApp.StartAsync(_database.ConnectionString, _access);
    }

    [Fact]
    public async Task A_change_without_the_request_header_is_refused()
    {
        var without = await _app.SendAsync(HttpMethod.Post, "/api/ping", withHeader: false);
        var with = await _app.SendAsync(HttpMethod.Post, "/api/ping");

        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);
        Assert.Equal(HttpStatusCode.OK, with.StatusCode);
    }

    [Fact]
    public async Task Reads_do_not_need_the_request_header()
    {
        var response = await _app.SendAsync(HttpMethod.Get, "/api/ping", withHeader: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_successful_change_is_saved()
    {
        var saved = await _app.SendAsync(HttpMethod.Post, Notes(_brand), _admin, new { text = "first" });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["first"], await NotesAsync(_brand));
    }

    [Fact]
    public async Task A_change_that_ends_in_an_error_status_is_rolled_back()
    {
        var refused = await _app.SendAsync(HttpMethod.Post, $"{Notes(_brand)}/then-refuse", _admin, new { text = "discarded" });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Empty(await NotesAsync(_brand));
    }

    [Fact]
    public async Task A_change_that_ends_in_an_error_inside_a_result_union_is_rolled_back()
    {
        var refused = await _app.SendAsync(HttpMethod.Post, $"{Notes(_brand)}/then-refuse-in-union", _admin, new { text = "discarded" });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Empty(await NotesAsync(_brand));
    }

    [Fact]
    public async Task A_change_that_throws_is_rolled_back()
    {
        var failed = await _app.SendAsync(HttpMethod.Post, $"{Notes(_brand)}/then-throw", _admin, new { text = "discarded" });

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Empty(await NotesAsync(_brand));
    }

    [Fact]
    public async Task Workspace_endpoints_need_a_signed_in_user()
    {
        var response = await _app.SendAsync(HttpMethod.Get, Notes(_brand));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task People_outside_the_workspace_get_not_found()
    {
        var stranger = await _app.SendAsync(HttpMethod.Get, Notes(_brand), Guid.NewGuid());
        var unknown = await _app.SendAsync(HttpMethod.Get, Notes(Guid.NewGuid()), _admin);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Members_without_the_role_an_endpoint_needs_are_forbidden()
    {
        var marketer = await _app.SendAsync(HttpMethod.Get, $"/api/brands/{_brand}/admin-only", _marketer);
        var admin = await _app.SendAsync(HttpMethod.Get, $"/api/brands/{_brand}/admin-only", _admin);

        Assert.Equal(HttpStatusCode.Forbidden, marketer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task The_workspace_in_the_route_scopes_the_data()
    {
        await _app.SendAsync(HttpMethod.Post, Notes(_brand), _admin, new { text = "mine" });
        await _app.SendAsync(HttpMethod.Post, Notes(_otherBrand), _admin, new { text = "theirs" });

        Assert.Equal(["mine"], await NotesAsync(_brand));
        Assert.Equal(["theirs"], await NotesAsync(_otherBrand));
    }

    [Fact]
    public async Task Endpoints_can_see_the_current_member()
    {
        var response = await _app.SendAsync(HttpMethod.Get, $"/api/brands/{_brand}/whoami", _marketer);

        var member = await response.Content.ReadFromJsonAsync<MemberEcho>(TestContext.Current.CancellationToken);
        Assert.Equal(new MemberEcho(_brand, _marketer, "Marketer"), member);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    private static string Notes(Guid brandId) => $"/api/brands/{brandId}/notes";

    private async Task<string[]> NotesAsync(Guid brandId)
    {
        var response = await _app.SendAsync(HttpMethod.Get, Notes(brandId), _admin);
        return (await response.Content.ReadFromJsonAsync<string[]>(TestContext.Current.CancellationToken))!;
    }
}
