using Kairo.Contracts.Connections;
using Kairo.Modules.Measurement.Data;
using Kairo.Modules.Measurement.Feeds;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Measurement.Lab;

internal sealed class LabWork(MeasurementDbContext db, IEnumerable<ILabSource> sources, ImpactLabAgent agent)
{
    public const int HistoryDays = 365;
    public const int MostChangeDays = 180;
    public const int MostPicked = 50;

    public static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);

    public async Task<LabData> LoadAsync(LabAnalysis analysis, CancellationToken cancellationToken)
    {
        var spec = LabSourceSpec.Read(analysis.Source);
        var source = sources.FirstOrDefault(candidate => candidate.Handles(spec)) ?? throw new LabDataProblem(LabSourceRules.BadKind);
        var through = await source.ThroughAsync(spec, analysis.Id, cancellationToken)
            ?? throw new LabDataProblem(spec.Kind == LabKinds.File ? Sources.FileSource.UploadFirst : "Kairo hasn't loaded any data from this source yet.");
        if (through < analysis.Start)
        {
            throw new LabDataProblem($"The data only reaches {FeedClock.Iso(through)}, before the change started.");
        }

        var to = through < analysis.End ? through : analysis.End;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoadTimeout);
        LabData data;
        try
        {
            data = await source.LoadAsync(spec, analysis.BrandId, analysis.Id, analysis.Start.AddDays(-HistoryDays), to, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LabDataProblem("Loading the data took longer than a minute. Try a shorter change or a smaller source.");
        }

        if (data.From >= analysis.Start)
        {
            throw new LabDataProblem("The source has no data from before the change started.");
        }

        return data;
    }

    public static IReadOnlyList<string> Problems(LabSelectionRequest request, IReadOnlyCollection<string> keys, out string[] affected, out string[]? comparison)
    {
        var problems = new List<string>();
        affected = [.. (request.Affected ?? []).Select(key => key.Trim()).Distinct(StringComparer.Ordinal)];
        comparison = request.Comparison is null ? null : [.. request.Comparison.Select(key => key.Trim()).Distinct(StringComparer.Ordinal)];
        if (affected.Length is 0 or > MostPicked)
        {
            problems.Add($"Pick 1 to {MostPicked} series that the change affected.");
        }

        if (comparison is { Length: > MostPicked })
        {
            problems.Add($"Pick at most {MostPicked} comparison series.");
        }

        if (affected.Concat(comparison ?? []).Any(key => !keys.Contains(key)))
        {
            problems.Add("Pick series from the loaded data.");
        }

        if (comparison is not null && comparison.Intersect(affected, StringComparer.Ordinal).Any())
        {
            problems.Add("A series can't be both affected and a comparison.");
        }

        return problems;
    }

    public Task<LabCheckAnswer> CheckAsync(LabAnalysis analysis, IReadOnlyList<LabSeriesData> series, string[] affected, string[]? comparison, CancellationToken cancellationToken) =>
        agent.CheckAsync(
            new LabCheckRequest(
                series[0].Values.Length,
                analysis.HistoryDays!.Value,
                analysis.ChangeDays!.Value,
                LabViews.PlannedDays(analysis),
                [.. series.Select(item => new LabSeries(item.Key, item.Name, item.Values))],
                affected,
                comparison),
            cancellationToken);

    public async Task<LabDataset> DatasetAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var dataset = await db.LabDatasets.SingleOrDefaultAsync(row => row.AnalysisId == analysisId, cancellationToken);
        if (dataset is null)
        {
            dataset = new LabDataset { AnalysisId = analysisId };
            db.LabDatasets.Add(dataset);
        }

        return dataset;
    }

    public static void ClearSelection(LabAnalysis analysis)
    {
        analysis.Affected = "[]";
        analysis.Comparison = "[]";
        analysis.Candidates = "[]";
        analysis.Fit = null;
        analysis.Detectable = null;
        analysis.Placebos = 0;
        analysis.Warnings = "[]";
        ClearResult(analysis);
    }

    public static void ClearResult(LabAnalysis analysis)
    {
        analysis.Outcome = null;
        analysis.Conclusive = false;
        analysis.Result = null;
        analysis.Summary = null;
        analysis.Explanation = null;
        analysis.InconclusiveReason = null;
        analysis.NextAdvice = null;
        analysis.Method = null;
        analysis.Model = null;
        analysis.PromptVersion = null;
        analysis.Problem = null;
        analysis.FinishedAt = null;
        analysis.MemoryEntryId = null;
    }

    public static string ConnectFirst(ConnectedProduct product) => $"Connect {Name(product)} before loading this data.";

    public static string Reconnect(ConnectedProduct product) => $"Kairo lost access to {Name(product)}. Reconnect it and try again.";

    private static string Name(ConnectedProduct product) => product switch
    {
        ConnectedProduct.GoogleAds => "Google Ads",
        ConnectedProduct.Ga4 => "Google Analytics 4",
        ConnectedProduct.BigQuery => "BigQuery",
        _ => product.ToString(),
    };
}
