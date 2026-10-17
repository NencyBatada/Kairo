using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.IntegrationTests.Agents;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Ask;
using Microsoft.Extensions.DependencyInjection;

namespace Kairo.IntegrationTests.Ask;

internal static class AskHarness
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
    public static string Root(Guid brand) => $"/api/brands/{brand}/ask/conversations";
    public static async Task<Guid> CreateAsync(HttpClient browser, Guid brand)
    {
        using var response = await browser.PostAsync(Root(brand), null, Token);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }
    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    public static async Task<HttpResponseMessage> ToolAsync(KairoApiFactory api, JsonElement invocation, object body)
    {
        var client = api.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/brands/{invocation.GetProperty("brandId").GetGuid()}/ask/invocations/{invocation.GetProperty("messageId").GetGuid()}/tools")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Kairo-Ask-Token", invocation.GetProperty("token").GetString());
        try { return await client.SendAsync(request, Token); }
        finally { client.Dispose(); }
    }
    public static void AnswerWith(KairoApiFactory api, Func<JsonElement, Task> callback)
    {
        var previous = api.Agents.Respond;
        api.Agents.Respond = (path, invocation) =>
        {
            if (path != "/ask/turn") return previous(path, invocation);
            callback(invocation).GetAwaiter().GetResult();
            return FakeAgents.Json(new { model = "test-pro", promptVersion = "ask/1" });
        };
    }
    public static async Task<(Guid Conversation, Guid Proposal, JsonElement View)> ProposeAsync(KairoApiFactory api, HttpClient browser, Guid brand, AskTarget target, object changes, bool preview = true)
    {
        Guid proposal = default;
        AnswerWith(api, async invocation =>
        {
            using var read = await ToolAsync(api, invocation, new { operation = "read", target });
            var evidence = await JsonAsync(read);
            using var proposed = await ToolAsync(api, invocation, new { operation = "propose", target, changes });
            proposal = (await JsonAsync(proposed)).GetProperty("id").GetGuid();
            using var answer = await ToolAsync(api, invocation, new { operation = "answer", text = "Inspect the preview and confirm before applying the change.", evidenceIds = new[] { evidence.GetProperty("id").GetString() } });
            await JsonAsync(answer);
        });
        var conversation = await CreateAsync(browser, brand);
        using var response = await browser.PostAsJsonAsync($"{Root(brand)}/{conversation}/messages", new SendMessage(Guid.NewGuid(), "Please change this draft.", target), Token);
        var message = await JsonAsync(response);
        Assert.Equal("completed", message.GetProperty("status").GetString());
        if (preview) await PreviewAsync(api);
        var view = await browser.GetFromJsonAsync<JsonElement>($"{Root(brand)}/{conversation}/proposals/{proposal}", Token);
        Assert.Equal(message.GetProperty("id").GetGuid(), view.GetProperty("messageId").GetGuid());
        return (conversation, proposal, view);
    }
    public static async Task PreviewAsync(KairoApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AskJobs>().RunDueAsync(Token);
    }
    public static Task<HttpResponseMessage> ConfirmAsync(HttpClient browser, Guid brand, Guid conversation, Guid proposal) =>
        browser.PostAsync($"{Root(brand)}/{conversation}/proposals/{proposal}/confirm", null, Token);
}
