using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Kairo.SharedKernel.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kairo.Api.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void Queued_work_resumes_its_original_trace_and_attaches_correlation()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == KairoTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var saved = KairoTelemetry.CaptureParent();
        var brand = Guid.NewGuid();
        var room = Guid.NewGuid();
        var run = Guid.NewGuid();
        using var stage = KairoTelemetry.StartStage("brief", saved, brand, room, run);
        Assert.NotNull(stage);
        Assert.Equal(parent.TraceId, stage.TraceId);
        Assert.Equal(parent.SpanId, stage.ParentSpanId);
        Assert.Equal(room.ToString(), stage.GetTagItem("kairo.campaign_id"));
        Assert.Equal(brand.ToString(), stage.GetTagItem("kairo.brand_id"));
        Assert.Equal(run.ToString(), stage.GetTagItem("kairo.run_id"));
    }

    [Fact]
    public void Cloud_logs_retain_safe_metadata_without_rendering_private_values()
    {
        var brand = Guid.NewGuid();
        var state = new KeyValuePair<string, object?>[]
        {
            new("{OriginalFormat}", "Kairo operation {Event} for brand {BrandId}: {Prompt}"),
            new("Event", "launch.failed"), new("BrandId", brand),
            new("Prompt", "private prompt"), new("Authorization", "private token"), new("Attempt", 2),
        };
        var entry = new LogEntry<KeyValuePair<string, object?>[]>(LogLevel.Error, "Kairo", new EventId(1), state,
            new InvalidOperationException("private exception"), (_, _) => "private formatted message");
        using var writer = new StringWriter();
        new CloudLogFormatter().Write(entry, null, writer);
        var text = writer.ToString();
        Assert.DoesNotContain("private", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("launch.failed", json.RootElement.GetProperty("event").GetString());
        Assert.Equal(brand, json.RootElement.GetProperty("BrandId").GetGuid());
        Assert.Equal(2, json.RootElement.GetProperty("Attempt").GetInt32());
        Assert.Equal("ERROR", json.RootElement.GetProperty("severity").GetString());
    }

    [Fact]
    public void Metrics_keep_campaign_identifiers_out_of_dimensions_and_reset_queue_age()
    {
        var samples = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        {
            if (instrument.Meter.Name == KairoTelemetry.SourceName) meter.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => samples.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();
        KairoTelemetry.StageFinished("brief", "failed", 2.5);
        KairoTelemetry.QueueSnapshot(0, -10);
        listener.RecordObservableInstruments();
        Assert.Contains(samples, sample => sample.Name == "kairo.stage.duration" && sample.Value == 2.5);
        Assert.Contains(samples, sample => sample.Name == "kairo.stage.queue.age" && sample.Value == 0);
        Assert.All(samples.SelectMany(sample => sample.Tags), tag => Assert.DoesNotContain("Id", tag.Key));
    }

    [Theory]
    [InlineData("https://googleads.googleapis.com/v25/search", 429, "integration.quota_exceeded")]
    [InlineData("https://googleads.googleapis.com/v25/search", 403, "integration.failed")]
    [InlineData("https://example.com", 429, null)]
    [InlineData("https://googleads.googleapis.com/v25/search", 200, null)]
    public async Task Google_failures_are_reported_without_reading_response_bodies(string url, int status, string? expected)
    {
        var logger = new CapturingLogger();
        using var handler = new IntegrationTelemetryHandler(logger) { InnerHandler = new StubHandler(status) };
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(expected, logger.Event);
    }

    private sealed class StubHandler(int status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private") });
    }

    private sealed class CapturingLogger : ILogger<IntegrationTelemetryHandler>
    {
        public string? Event { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                Event = fields.Single(field => field.Key == "Event").Value?.ToString();
        }
    }
}
