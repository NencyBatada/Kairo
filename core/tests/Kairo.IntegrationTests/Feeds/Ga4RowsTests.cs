using System.Text.Json;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

public sealed class Ga4RowsTests
{
    [Fact]
    public void A_report_row_becomes_a_ga4_region_daily_row()
    {
        var row = JsonDocument.Parse(
            """{"dimensionValues":[{"value":"20261007"},{"value":"India"},{"value":"Goa"}],"metricValues":[{"value":"12"},{"value":"1.5"},{"value":"2"},{"value":"999.5"}]}""")
            .RootElement;

        Assert.Equal(
            """{"date":"2026-10-07","property_id":"777","country":"India","region":"Goa","currency":"INR","sessions":12,"key_events":1.5,"transactions":2,"purchase_revenue":"999.5"}""",
            Ga4Rows.Of(row, new Ga4Property("777", "Asia/Kolkata", "INR")).ToJsonString());
    }
}
