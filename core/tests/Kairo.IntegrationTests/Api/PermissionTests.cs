using System.Net;
using System.Net.Http.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class PermissionTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    [Fact]
    public async Task Each_role_can_do_only_what_the_PRD_allows()
    {
        var admin = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(admin.Browser);
        var marketer = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "marketer");
        var approver = await Workspaces.JoinAsync(_api, admin.Browser, brandId, "approver");
        var someone = Guid.NewGuid();
        var brand = $"/api/brands/{brandId}";
        var emptyDuties = new { briefApprovers = Array.Empty<Guid>(), assetApprovers = Array.Empty<Guid>(), launchApprovers = Array.Empty<Guid>(), budgetOwners = Array.Empty<Guid>() };

        (HttpMethod Method, string Path, object? Body, bool OnlyAdmins)[] endpoints =
        [
            (HttpMethod.Get, brand, null, false),
            (HttpMethod.Patch, brand, new { }, true),
            (HttpMethod.Get, $"{brand}/members", null, false),
            (HttpMethod.Put, $"{brand}/members/{someone}/roles", new { roles = new[] { "marketer" } }, true),
            (HttpMethod.Delete, $"{brand}/members/{someone}", null, true),
            (HttpMethod.Get, $"{brand}/invitations", null, true),
            (HttpMethod.Post, $"{brand}/invitations", new { email = "not-an-email", roles = Array.Empty<string>() }, true),
            (HttpMethod.Post, $"{brand}/invitations/{someone}/revoke", null, true),
            (HttpMethod.Get, $"{brand}/approval-settings", null, false),
            (HttpMethod.Put, $"{brand}/approval-settings", emptyDuties, true),
            (HttpMethod.Get, $"{brand}/audit-log", null, true),
            (HttpMethod.Get, $"{brand}/audit-log/export", null, true),
            (HttpMethod.Get, $"{brand}/audit-log/actions", null, true),
            (HttpMethod.Get, $"{brand}/connections", null, false),
            (HttpMethod.Get, $"{brand}/connections/google-ads/connect", null, true),
            (HttpMethod.Get, $"{brand}/connections/google-ads/accounts", null, true),
            (HttpMethod.Post, $"{brand}/connections/google-ads/account", new { externalId = "1" }, true),
            (HttpMethod.Post, $"{brand}/connections/google-ads/check", null, true),
            (HttpMethod.Delete, $"{brand}/connections/google-ads", null, true),
            (HttpMethod.Get, $"{brand}/analytics/feeds", null, false),
            (HttpMethod.Get, $"{brand}/analytics/sales/uploads", null, false),
            (HttpMethod.Get, $"{brand}/analytics/sales/source", null, false),
            (HttpMethod.Put, $"{brand}/analytics/sales/source", new { table = "bad" }, true),
            (HttpMethod.Delete, $"{brand}/analytics/sales/source", null, true),
        ];

        var wrong = new List<string>();
        foreach (var (method, path, body, onlyAdmins) in endpoints)
        {
            foreach (var (name, member, isAdmin) in new[] { ("admin", admin, true), ("marketer", marketer, false), ("approver", approver, false) })
            {
                var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
                var status = (await member.Browser.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
                var forbidden = status == HttpStatusCode.Forbidden;
                if (forbidden != (onlyAdmins && !isAdmin))
                {
                    wrong.Add($"{method} {path} as {name} returned {(int)status}");
                }
            }
        }

        Assert.Empty(wrong);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
