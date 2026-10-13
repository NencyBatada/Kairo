using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kairo.IntegrationTests.Api;

public sealed class OpenApiDocumentTests(SqlServer sqlServer) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly KairoApiFactory _api = new(sqlServer.KairoConnectionString);

    [Fact]
    public async Task The_committed_document_matches_the_api()
    {
        var actual = Normalize(await _api.CreateBrowser().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
        var path = Path.Combine(FindCoreDirectory(), "src", "Kairo.Api", "openapi.json");

        if (Environment.GetEnvironmentVariable("KAIRO_UPDATE_OPENAPI") == "1")
        {
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            return;
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run the tests with KAIRO_UPDATE_OPENAPI=1 to write it.");
        Assert.Equal(Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)), actual);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

    private static string FindCoreDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kairo.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the core folder.");
    }
}
