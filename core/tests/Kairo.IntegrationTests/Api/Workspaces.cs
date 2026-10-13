using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.Api;

public sealed record Member(HttpClient Browser, Guid UserId, GoogleUser Account);

internal static class Workspaces
{
    public static string NewHandle() => $"w{Guid.NewGuid():N}"[..20];

    public static object NewWorkspace(string handle, string name = "Acme Shoes") =>
        new { name, handle, timeZone = "Asia/Kolkata", currency = "INR" };

    public static async Task<Member> SignInAsync(KairoApiFactory api, string name = "Test User")
    {
        var account = GoogleUser.Create(name);
        var browser = await api.SignInAsync(account);
        return new Member(browser, await UserIdAsync(browser), account);
    }

    public static async Task<Guid> UserIdAsync(HttpClient browser) =>
        (await browser.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("id").GetGuid();

    public static async Task<Guid> CreateAsync(HttpClient browser, string? handle = null)
    {
        var response = await browser.PostAsJsonAsync("/api/brands", NewWorkspace(handle ?? NewHandle()));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
