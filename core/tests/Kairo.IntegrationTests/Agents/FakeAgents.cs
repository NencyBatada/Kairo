using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kairo.IntegrationTests.Agents;

public sealed record AgentRequest(string Url, string? Authorization, JsonElement Body, string? TraceParent = null, string? BrandId = null, string? RunId = null);

public sealed class FakeAgents : HttpMessageHandler
{
    public ConcurrentQueue<AgentRequest> Requests { get; } = new();

    public Func<string, JsonElement, HttpResponseMessage> Respond { get; set; } =
        (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);

    public int Failures { get; set; }

    public TimeSpan Delay { get; set; }

    public static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    public static HttpResponseMessage Text(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var parsed = document.RootElement.Clone();
        Requests.Enqueue(new AgentRequest(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), parsed,
            request.Headers.TryGetValues("traceparent", out var trace) ? trace.Single() : null,
            request.Headers.TryGetValues("X-Kairo-brand-Id", out var brand) ? brand.Single() : null,
            request.Headers.TryGetValues("X-Kairo-run-Id", out var run) ? run.Single() : null));
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (Failures > 0)
        {
            Failures--;
            return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = JsonContent.Create(new { error = "model_failed" }) };
        }

        return Respond(request.RequestUri.AbsolutePath, parsed);
    }
}
