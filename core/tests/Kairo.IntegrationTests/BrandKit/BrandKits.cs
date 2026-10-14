using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kairo.IntegrationTests.BrandKit;

internal static class BrandKits
{
    public static readonly byte[] Pdf = "%PDF-1.7\n%Acme brand guidelines\n"u8.ToArray();

    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static string Kit(Guid brandId) => $"/api/brands/{brandId}/brand-kit";

    public static string Draft(Guid brandId) => $"{Kit(brandId)}/draft";

    public static object Content(string summary = "Friendly and practical.") => new
    {
        voice = new { summary, traits = new[] { "warm" } },
        colours = new[] { new { hex = "#e4572e", name = "Ember", role = "primary" } },
        rules = new { bannedWords = new[] { "cheap" } },
    };

    public static Task<HttpResponseMessage> PutDraftAsync(HttpClient browser, Guid brandId, object content, int revision) =>
        browser.PutAsJsonAsync(Draft(brandId), new { content, revision }, Token);

    public static async Task<JsonElement> SaveAsync(HttpClient admin, Guid brandId, object content, int revision)
    {
        var response = await PutDraftAsync(admin, brandId, content, revision);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    public static async Task<JsonElement> ApproveAsync(HttpClient admin, Guid brandId, int revision)
    {
        var response = await admin.PostAsJsonAsync($"{Draft(brandId)}/approve", new { revision }, Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Token);
    }

    public static async Task<JsonElement> ApproveNewAsync(HttpClient admin, Guid brandId, object content)
    {
        var draft = await SaveAsync(admin, brandId, content, await DraftRevisionAsync(admin, brandId));
        return await ApproveAsync(admin, brandId, draft.GetProperty("revision").GetInt32());
    }

    public static async Task<JsonElement> GetAsync(HttpClient browser, Guid brandId) =>
        await browser.GetFromJsonAsync<JsonElement>(Kit(brandId), Token);

    public static async Task<int> DraftRevisionAsync(HttpClient browser, Guid brandId)
    {
        var draft = (await GetAsync(browser, brandId)).GetProperty("draft");
        return draft.ValueKind == JsonValueKind.Null ? 0 : draft.GetProperty("revision").GetInt32();
    }

    public static string Summary(JsonElement version) =>
        version.GetProperty("content").GetProperty("voice").GetProperty("summary").GetString()!;

    public static MultipartFormDataContent File(byte[] bytes, string fileName, string? kind = null)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (kind is not null)
        {
            form.Add(new StringContent(kind), "kind");
        }

        return form;
    }

    public static async Task<Guid> UploadAsync(HttpClient admin, Guid brandId, byte[] bytes, string fileName, string? kind = null)
    {
        var response = await admin.PostAsync($"{Kit(brandId)}/files", File(bytes, fileName, kind), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
    }

    public static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString()!;

    public static async Task<string[]> ErrorsAsync(HttpResponseMessage response, string field)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()!).ToArray();
    }
}
