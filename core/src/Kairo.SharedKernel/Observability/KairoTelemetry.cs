using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Kairo.SharedKernel.Observability;

public static class KairoTelemetry
{
    public const string SourceName = "Kairo";
    public static readonly ActivitySource Activities = new(SourceName);
    private static readonly Meter Metrics = new(SourceName);
    private static readonly Counter<long> Events = Metrics.CreateCounter<long>("kairo.operations");
    private static readonly Histogram<double> StageDuration = Metrics.CreateHistogram<double>("kairo.stage.duration", "s");
    private static readonly Histogram<double> AgentDuration = Metrics.CreateHistogram<double>("kairo.agent.duration", "s");
    private static double _queueAge;
    private static long _queueCount;
    private static readonly ObservableGauge<double> QueueAge = Metrics.CreateObservableGauge("kairo.stage.queue.age", () => Volatile.Read(ref _queueAge), "s");
    private static readonly ObservableGauge<long> QueueCount = Metrics.CreateObservableGauge("kairo.stage.queue.count", () => Interlocked.Read(ref _queueCount));

    public static string? CaptureParent() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    public static Activity? StartStage(string kind, string? parent, Guid brandId, Guid campaignId, Guid runId)
    {
        var context = ActivityContext.TryParse(parent, null, out var parsed) ? parsed : default;
        var activity = Activities.StartActivity("stage " + kind, ActivityKind.Consumer, context);
        activity?.SetTag("kairo.brand_id", brandId.ToString());
        activity?.SetTag("kairo.campaign_id", campaignId.ToString());
        activity?.SetTag("kairo.run_id", runId.ToString());
        activity?.SetTag("kairo.stage", kind);
        return activity;
    }

    public static void StageFinished(string kind, string outcome, double seconds) =>
        StageDuration.Record(Math.Max(0, seconds), new("stage", kind), new("outcome", outcome));

    public static void AgentFinished(string operation, string outcome, double seconds) =>
        AgentDuration.Record(Math.Max(0, seconds), new("operation", operation), new("outcome", outcome));

    public static void QueueSnapshot(long count, double age)
    {
        Interlocked.Exchange(ref _queueCount, count);
        Volatile.Write(ref _queueAge, Math.Max(0, age));
    }

    public static void Event(ILogger logger, string name, Guid? brandId = null, Guid? campaignId = null, Guid? runId = null)
    {
        Events.Add(1, new KeyValuePair<string, object?>("event", name));
        logger.LogWarning("Kairo operation {Event} for brand {BrandId}, campaign {CampaignId}, run {RunId}", name, brandId, campaignId, runId);
    }

    public static IApplicationBuilder UseKairoTelemetry(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (Guid.TryParse(context.Request.RouteValues["brandId"]?.ToString(), out var brand))
            Activity.Current?.SetTag("kairo.brand_id", brand.ToString());
        if (Guid.TryParse(context.Request.RouteValues["campaignId"]?.ToString(), out var campaign))
            Activity.Current?.SetTag("kairo.campaign_id", campaign.ToString());
        await next(context);
    });
}
