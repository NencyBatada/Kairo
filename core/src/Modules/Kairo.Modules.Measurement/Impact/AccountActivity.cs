using System.Globalization;
using System.Net;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.Modules.Measurement.Impact;

internal sealed record AccountSpend(decimal BeforePerDay, decimal DuringPerDay)
{
    public bool Changed => BeforePerDay > 0 ? Math.Abs(DuringPerDay / BeforePerDay - 1) > AccountActivity.ChangeShare : DuringPerDay > 0;
}

internal sealed class AccountActivity(BigQueryClient bigQuery, IServiceAccessToken serviceToken, AnalyticsOptions options)
{
    public const int BeforeDays = 28;
    public const decimal ChangeShare = 0.5m;

    public static string Query(string table) => $"""
        SELECT IF(date < @start, 'before', 'during'), SUM(cost)
        FROM {table}
        WHERE date BETWEEN @from AND @to AND customer_id = @customer AND campaign_id NOT IN UNNEST(@exclude)
        GROUP BY 1
        """;

    public async Task<AccountSpend?> SpendAsync(Guid brandId, string customer, IReadOnlyList<string> exclude, DateOnly start, DateOnly through, CancellationToken cancellationToken)
    {
        if (options.Project is not { } project || through < start) return null;
        var token = await serviceToken.GetAsync(cancellationToken);
        var table = $"`{project}.{AnalyticsOptions.DatasetFor(brandId)}.{TableSchemas.AdsCampaignDaily.Name}`";
        IReadOnlyList<string?[]> rows;
        try
        {
            rows = await bigQuery.QueryAsync(project, token, Query(table), options.Location,
            [
                QueryParameter.Date("start", start), QueryParameter.Date("from", start.AddDays(-BeforeDays)), QueryParameter.Date("to", through),
                new QueryParameter("customer", "STRING", customer), new QueryParameter("exclude", "STRING", Values: [.. exclude.DefaultIfEmpty("")]),
            ], cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.Status == HttpStatusCode.NotFound)
        {
            return null;
        }

        decimal Sum(string period) => rows.Where(row => row[0] == period)
            .Sum(row => decimal.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0m);
        var days = through.DayNumber - start.DayNumber + 1;
        return new AccountSpend(decimal.Round(Sum("before") / BeforeDays, 2), decimal.Round(Sum("during") / days, 2));
    }
}
