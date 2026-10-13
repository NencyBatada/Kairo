using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed class AuditLogTests(SqlServer sqlServer) : IAsyncDisposable
{
    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admins_see_who_did_what_newest_first()
    {
        var owner = await Workspaces.SignInAsync(_api, "Neha Rao");
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name = "Acme Footwear" }, Token);

        var page = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log", Token);

        var entries = page.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["brand.updated", "brand.created"], entries.Select(entry => entry.GetProperty("action").GetString()));
        Assert.All(entries, entry => Assert.Equal("Neha Rao", entry.GetProperty("actorName").GetString()));
        Assert.Equal("Acme Shoes", entries[0].GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Acme Footwear", entries[0].GetProperty("after").GetProperty("name").GetString());
        Assert.Equal("INR", entries[1].GetProperty("after").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task The_log_pages_with_a_cursor()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var brandId = await Workspaces.CreateAsync(owner.Browser);
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            await owner.Browser.PatchAsJsonAsync($"/api/brands/{brandId}", new { name }, Token);
        }

        var first = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=3", Token);
        var before = first.GetProperty("nextBefore").GetString();
        var second = await owner.Browser.GetFromJsonAsync<JsonElement>($"/api/brands/{brandId}/audit-log?limit=3&before={before}", Token);

        Assert.Equal(3, first.GetProperty("entries").GetArrayLength());
        Assert.Equal("brand.created", Assert.Single(second.GetProperty("entries").EnumerateArray()).GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
    }

    [Fact]
    public async Task Each_workspace_sees_only_its_own_entries()
    {
        var owner = await Workspaces.SignInAsync(_api);
        var first = await Workspaces.CreateAsync(owner.Browser);
        var second = await Workspaces.CreateAsync(owner.Browser);
        await owner.Browser.PatchAsJsonAsync($"/api/brands/{second}", new { name = "Second" }, Token);

        Assert.Equal(["brand.created"], await Workspaces.AuditActionsAsync(owner.Browser, first));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();
}
