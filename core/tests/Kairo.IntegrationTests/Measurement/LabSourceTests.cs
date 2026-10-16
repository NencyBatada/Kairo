using System.Text;
using Kairo.Modules.Measurement.Feeds;
using Kairo.Modules.Measurement.Lab;
using Kairo.Modules.Measurement.Lab.Sources;

namespace Kairo.IntegrationTests.Measurement;

public sealed class LabSourceTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void A_good_file_becomes_dense_daily_series()
    {
        var file = Parse("﻿Series,Date,Value\nRunning,2026-10-01,4\nrunning,2026-10-03,2.5\n\n\"Walking, men\",2026-10-02,1\n");

        Assert.Empty(file.Errors);
        var data = file.Data!;
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), 3), (data.From, data.To, data.Days));
        Assert.Equal(new[] { "running", "walking, men" }, data.Series.Select(series => series.Key).ToArray());
        Assert.Equal(new[] { 4, 0, 2.5 }, data.Series[0].Values);
        Assert.Equal(new[] { 0, 1d, 0 }, data.Series[1].Values);
    }

    [Theory]
    [InlineData("", "the file is empty")]
    [InlineData("date,region,value\n", "row 1: the header must name date, series and value")]
    [InlineData("date,series,value\n", "the file has no rows")]
    [InlineData("date,series,value\n2026-10-01,A,0\n", "every value in the file is 0")]
    [InlineData("date,series,value\n2026-10-17,A,1\n", "row 2: date can't be after today")]
    [InlineData("date,series,value\n2026-10-01,A,1\n2026-10-01,a,2\n", "row 3: date and series already appear in row 2")]
    [InlineData("date,series,value\n2026-10-01,A,1,2\n", "row 2: expected 3 values but found 4")]
    [InlineData("date,series,value\n2026-10-01,A,1e5\n", "row 2: value must be a number, 0 or more")]
    [InlineData("date,series,value\n2020-01-01,A,1\n2026-10-01,A,1\n", "the file covers more than 3 years")]
    [InlineData("date,series,value\n\"2026-10-01,A,1\n", "row 2: a quoted value is never closed")]
    public void Bad_files_say_what_is_wrong(string text, string error)
    {
        Assert.Equal(new[] { error }, Parse(text).Errors);
    }

    [Fact]
    public void Text_that_is_not_utf8_is_refused()
    {
        var file = LabCsv.Parse(new MemoryStream([0x64, 0xFF, 0xFE]), Today);

        Assert.Equal(new[] { SalesCsv.NotText }, file.Errors);
    }

    [Fact]
    public void The_builder_keeps_the_200_largest_series_and_trims_empty_leading_days()
    {
        var from = new DateOnly(2026, 1, 1);
        var builder = new LabSeriesBuilder(from, from.AddDays(9));
        for (var index = 0; index < 250; index++)
        {
            builder.Add($"s{index}", $"Series {index}", from.AddDays(3), index + 1);
        }

        builder.Add("s0", null, from.AddDays(20), 100);
        builder.Add("negative", "Negative", from.AddDays(4), -5);
        builder.Add("  ", "Blank", from.AddDays(4), 5);

        var data = builder.Build("INR");

        Assert.Equal(LabSeriesBuilder.MostSeries, data.Series.Count);
        Assert.DoesNotContain(data.Series, series => series.Key is "s0" or "s49" or "negative");
        Assert.Contains(data.Series, series => series.Key == "s50");
        Assert.Equal((from.AddDays(3), 7, "INR"), (data.From, data.Days, data.Currency));
        Assert.All(data.Series, series => Assert.Equal(7, series.Values.Length));
        Assert.Throws<LabDataProblem>(() => new LabSeriesBuilder(from, from).Build(null));
    }

    [Theory]
    [InlineData("google_ads", "state", "conversions", "region_name", "SUM(conversions)")]
    [InlineData("google_ads", "campaign", "conversion_value", "campaign_id", "SUM(conversions_value)")]
    [InlineData("ga4", "city", "purchase_revenue", "city", "SUM(purchase_revenue)")]
    [InlineData("ga4", "state", "key_events", "region", "SUM(key_events)")]
    public void Nightly_queries_read_one_metric_by_day_and_key(string kind, string grain, string metric, string key, string sum)
    {
        var sql = LabQueries.Nightly(new LabSourceSpec(kind, metric, grain), "`p.d.t`");

        Assert.Contains($"SELECT CAST(date AS STRING), {key}, ", sql);
        Assert.Contains(sum, sql);
        Assert.Contains($"AND {key} IS NOT NULL AND {key} != '(not set)'", sql);
        Assert.Contains("GROUP BY 1, 2", sql);
    }

    [Fact]
    public void Brand_table_queries_use_the_checked_column_names_and_count_timestamps_in_india()
    {
        var table = new TableInfo("asia-south1", [new("Ordered_At", "TIMESTAMP", "NULLABLE"), new("Units", "NUMERIC", "NULLABLE"), new("Line", "STRING", "NULLABLE")]);

        var sql = LabTableRules.Query("acme-data.shop.orders", LabTableRules.Field(table, "date", "ordered_at"), LabTableRules.Field(table, "value", "units"),
            LabTableRules.Field(table, "series", "line"));

        Assert.Contains("SELECT CAST(DATE(`Ordered_At`, 'Asia/Kolkata') AS STRING) AS date, CAST(`Line` AS STRING) AS series, CAST(SUM(`Units`) AS FLOAT64) AS value", sql);
        Assert.Contains("FROM `acme-data.shop.orders`", sql);
        Assert.Equal("The table has no column named nope.", Assert.Throws<LabDataProblem>(() => LabTableRules.Field(table, "date", "nope")).Message);
        Assert.Throws<LabDataProblem>(() => LabTableRules.Field(table, "value", "line"));
        var repeated = new TableInfo("US", [new("tags", "STRING", "REPEATED")]);
        Assert.Throws<LabDataProblem>(() => LabTableRules.Field(repeated, "series", "tags"));
    }

    [Fact]
    public void Live_product_reports_ask_for_the_right_fields()
    {
        var window = new FeedWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(
            "SELECT segments.date, segments.product_type_l1, metrics.clicks, metrics.conversions, metrics.conversions_value FROM shopping_performance_view "
            + "WHERE segments.date BETWEEN '2026-01-01' AND '2026-01-31'",
            AdsQueries.Products(window, true));
        Assert.Contains("segments.product_item_id, segments.product_title", AdsQueries.Products(window, false));
    }

    [Theory]
    [InlineData("google_ads", "category", "clicks", true)]
    [InlineData("ga4", "product", "items_purchased", true)]
    [InlineData("ga4", "product", "transactions", false)]
    [InlineData("ga4", "campaign", "transactions", false)]
    [InlineData("google_ads", "series", "clicks", false)]
    public void Sources_offer_only_their_own_metrics_and_grains(string kind, string grain, string metric, bool allowed)
    {
        var (spec, errors) = LabSourceRules.Check(new LabSourceRequest(kind, metric, grain, null, null, null, null));

        Assert.Equal(allowed, spec is not null);
        Assert.Equal(allowed, errors.Count == 0);
    }

    private static LabFile Parse(string text) => LabCsv.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)), Today);
}
