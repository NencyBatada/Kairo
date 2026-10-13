using System.Net.Http.Headers;
using System.Text;
using Kairo.IntegrationTests.Api;
using Kairo.Modules.Measurement.Feeds;

namespace Kairo.IntegrationTests.Feeds;

internal static class SalesFiles
{
    public const string Header = "date,region,orders,revenue\n";

    public static MultipartFormDataContent Csv(string text, string fileName = "sales.csv")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    public static string[] RowsOf(FakeBigQuery bigQuery, Guid brandId) => bigQuery.Rows(AnalyticsOptions.DatasetFor(brandId), "sales_daily")
        .Select(row => string.Join(
            ' ',
            row["date"]!.GetValue<string>(),
            row["region"]!.GetValue<string>(),
            row["orders"]!.GetValue<long>(),
            row["revenue"]!.GetValue<string>(),
            row["source"]!.GetValue<string>()))
        .Order(StringComparer.Ordinal)
        .ToArray();
}
