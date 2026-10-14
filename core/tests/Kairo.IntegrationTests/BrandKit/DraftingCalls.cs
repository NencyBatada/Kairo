using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.BrandKit;

internal static class DraftingCalls
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Path(Guid brandId) => $"/api/brands/{brandId}/brand-kit/drafting";

    public static Task<HttpResponseMessage> StartAsync(HttpClient browser, Guid brandId, string? websiteUrl, bool replaceDraft = false) =>
        browser.PostAsJsonAsync(Path(brandId), new { websiteUrl, replaceDraft }, Token);

    public static async Task<Guid> StartedAsync(HttpClient admin, Guid brandId, string websiteUrl, bool replaceDraft = false)
    {
        var response = await StartAsync(admin, brandId, websiteUrl, replaceDraft);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> LatestAsync(HttpClient browser, Guid brandId)
    {
        var response = await browser.GetAsync($"{Path(brandId)}/latest", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }
}
