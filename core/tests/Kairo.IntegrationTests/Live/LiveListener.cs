using System.Net;
using System.Text;
using System.Text.Json;
using Kairo.IntegrationTests.Api;

namespace Kairo.IntegrationTests.Live;

internal sealed record LiveMessage(string Type, string? Id, JsonElement Data);

internal sealed class LiveListener : IAsyncDisposable
{
    private static readonly TimeSpan RealWait = TimeSpan.FromMilliseconds(50);

    private readonly KairoApiFactory _api;
    private readonly HttpResponseMessage _response;
    private readonly StreamReader? _reader;
    private Task<LiveMessage?>? _pending;

    private LiveListener(KairoApiFactory api, HttpResponseMessage response, StreamReader? reader)
    {
        _api = api;
        _response = response;
        _reader = reader;
    }

    public HttpStatusCode Status => _response.StatusCode;

    public string? ContentType => _response.Content.Headers.ContentType?.MediaType;

    public static async Task<LiveListener> OpenAsync(KairoApiFactory api, HttpClient browser, Guid brandId, string? lastEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/brands/{brandId}/live");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        var response = await browser.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var reader = response.IsSuccessStatusCode
            ? new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), Encoding.UTF8)
            : null;
        return new LiveListener(api, response, reader);
    }

    public async Task<LiveMessage> NextAsync(int maxSteps = 100) =>
        await NextOrEndAsync(TimeSpan.FromSeconds(1), maxSteps) ?? throw new InvalidOperationException("The stream ended.");

    public async Task<LiveMessage?> NextOrEndAsync(TimeSpan step, int maxSteps = 100)
    {
        var steps = 0;
        while (true)
        {
            _pending ??= ReadMessageAsync();
            if (await Task.WhenAny(_pending, Task.Delay(RealWait, TestContext.Current.CancellationToken)) == _pending)
            {
                var message = await _pending;
                _pending = null;
                if (message is { Type: "heartbeat" })
                {
                    continue;
                }

                return message;
            }

            if (++steps > maxSteps)
            {
                throw new TimeoutException($"Nothing arrived within {maxSteps} steps of {step}.");
            }

            _api.Clock.Advance(step);
        }
    }

    public ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        _response.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<LiveMessage?> ReadMessageAsync()
    {
        if (_reader is null)
        {
            return null;
        }

        string? type = null;
        string? id = null;
        var data = new StringBuilder();
        while (true)
        {
            var line = await _reader.ReadLineAsync(TestContext.Current.CancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                if (type is null && id is null && data.Length == 0)
                {
                    continue;
                }

                using var parsed = JsonDocument.Parse(data.Length == 0 ? "{}" : data.ToString());
                return new LiveMessage(type ?? "message", id, parsed.RootElement.Clone());
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    type = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    data.Append(value);
                    break;
            }
        }
    }
}
