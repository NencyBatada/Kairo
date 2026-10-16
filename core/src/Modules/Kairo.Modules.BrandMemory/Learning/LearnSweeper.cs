using System.Text.Json;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Measurement;
using Kairo.Modules.BrandMemory.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandMemory.Learning;

internal sealed record LearnTally(int Saved, int Skipped, int Failed);

internal sealed record LearnSweep(int Brands, int Saved, int Skipped, int Failed);

internal sealed class LearnSweeper(
    BrandMemoryDbContext db,
    IImpactReports reports,
    ICampaignTests tests,
    ICampaignBriefs briefs,
    ICampaignChanges changes,
    ICatalogue catalogue,
    LearnWork work,
    ILogger<LearnSweeper> logger)
{
    public const int VisualLength = 1000;

    public async Task<LearnTally> RunAsync(BrandSummary brand, CancellationToken cancellationToken)
    {
        var ready = await reports.ListReadyAsync(DateTimeOffset.MinValue, cancellationToken);
        var runs = await db.LearnRuns.AsNoTracking().ToDictionaryAsync(run => run.RoomId, cancellationToken);
        var (saved, skipped, failed) = (0, 0, 0);
        foreach (var report in ready)
        {
            if (runs.TryGetValue(report.RoomId, out var run) && (run.Outcome != LearnOutcomes.Failed || run.Attempts >= LearnWork.MostAttempts))
            {
                continue;
            }

            if (await tests.GetAsync(report.RoomId, cancellationToken) is not { Stage: CampaignTestStages.ResultsReady })
            {
                continue;
            }

            try
            {
                var result = await work.LearnAsync(await InputAsync(brand, report, cancellationToken), cancellationToken);
                switch (result?.Outcome)
                {
                    case LearnOutcomes.Saved:
                        saved++;
                        break;
                    case LearnOutcomes.SkippedInconclusive:
                        skipped++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Learning from campaign {RoomId} failed.", report.RoomId);
                await work.FailAsync(report.RoomId, report.ReportId, exception.Message, cancellationToken);
                failed++;
            }
        }

        return new LearnTally(saved, skipped, failed);
    }

    public async Task<LearnInput> InputAsync(BrandSummary brand, ImpactSummary report, CancellationToken cancellationToken)
    {
        var brief = await briefs.GetAsync(report.RoomId, cancellationToken);
        var ids = (brief?.ProductIds ?? []).Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
        var known = (await catalogue.GetInStockAsync(ids, cancellationToken)).ToDictionary(product => product.Id);
        var products = ids.Take(LearnWork.MostProducts)
            .Select(id => known.TryGetValue(id, out var product)
                ? new LearnProduct(id.ToString(), product.Title, product.Category)
                : new LearnProduct(id.ToString(), id.ToString(), null))
            .ToList();
        var ads = ReportOutcomes.Proven(report.Outcome)
            ? await changes.AdResultsAsync(report.RoomId, report.StartDate, report.ThroughDate, cancellationToken)
            : [];
        return new LearnInput(
            report.RoomId,
            report.ReportId,
            new LearnRequest(
                brand.Name,
                report.Currency,
                new LearnCampaign(
                    report.Title,
                    report.Kpi,
                    brief?.Objective,
                    brief?.Audiences ?? [],
                    brief?.KeyMessages ?? [],
                    brief?.Languages ?? [],
                    products),
                new LearnResult(
                    report.Outcome,
                    report.Lift,
                    report.Low,
                    report.High,
                    report.Extra,
                    report.ExtraLow,
                    report.ExtraHigh,
                    report.CostPerExtra,
                    report.Spend,
                    (double)report.Results),
                [.. report.Regions.Select(region => new LearnRegion(region.Name, region.Test, region.Conclusive, region.Lift, region.Low, region.High, region.Extra))],
                [.. ads.Select(Ad)]));
    }

    public static LearnAd Ad(AdResult result)
    {
        var headlines = new List<string>();
        var descriptions = new List<string>();
        var visuals = new List<string>();
        foreach (var asset in result.Assets)
        {
            var content = asset.Content;
            headlines.AddRange(Texts(content, "headlines"));
            headlines.AddRange(Texts(content, "longHeadlines"));
            descriptions.AddRange(Texts(content, "descriptions"));
            if (Text(content, "title") is { } title)
            {
                headlines.Add(title);
            }

            if (asset.Kind is "image" or "video" or "footage")
            {
                var script = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("script", out var lines) && lines.ValueKind == JsonValueKind.Array
                    ? string.Join(" ", lines.EnumerateArray().Select(line => Text(line, "voiceover")).OfType<string>())
                    : "";
                visuals.Add(script.Length > 0 ? $"{asset.Kind} ({asset.Format ?? "any size"}): {script}" : $"{asset.Kind} ({asset.Format ?? "any size"})");
            }
        }

        var visual = string.Join("; ", visuals);
        return new LearnAd(
            result.Key,
            result.CampaignType,
            result.Language,
            [.. headlines.Distinct(StringComparer.Ordinal)],
            [.. descriptions.Distinct(StringComparer.Ordinal)],
            visual.Length == 0 ? null : visual.Length > VisualLength ? visual[..VisualLength] : visual,
            result.Impressions,
            result.Clicks,
            (double)result.Conversions,
            result.Cost);
    }

    private static IEnumerable<string> Texts(JsonElement content, string property) =>
        content.ValueKind == JsonValueKind.Object && content.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => value.Length > 0)
            : [];

    private static string? Text(JsonElement content, string property) =>
        content.ValueKind == JsonValueKind.Object && content.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

internal static class LearnJobs
{
    public static void MapLearnJobs(this IEndpointRouteBuilder jobs) => jobs.MapPost("/brand-memory/learn", RunAsync);

    private static async Task<Ok<LearnSweep>> RunAsync(IBrandDirectory brands, IServiceScopeFactory scopes, ILogger<LearnSweeper> logger, IHostApplicationLifetime lifetime)
    {
        var stopping = lifetime.ApplicationStopping;
        var all = await brands.ListAllAsync(stopping);
        var (saved, skipped, failed) = (0, 0, 0);
        foreach (var brand in all)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(brand.Id, stopping);
                var tally = await scope.ServiceProvider.GetRequiredService<LearnSweeper>().RunAsync(brand, stopping);
                saved += tally.Saved;
                skipped += tally.Skipped;
                failed += tally.Failed;
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                logger.LogError(exception, "Learning failed for brand {BrandId}.", brand.Id);
            }
        }

        return TypedResults.Ok(new LearnSweep(all.Count, saved, skipped, failed));
    }
}
