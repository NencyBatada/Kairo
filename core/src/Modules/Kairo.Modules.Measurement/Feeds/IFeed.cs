using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal interface IFeed
{
    string Name { get; }

    ConnectedProduct Product { get; }

    IReadOnlyList<TableSchema> Tables { get; }

    Task<bool> IsSetUpAsync(CancellationToken cancellationToken);

    string TargetOf(ConnectionStatus status);

    Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, FeedHistory history, CancellationToken cancellationToken);
}

internal sealed record FeedHistory(bool FirstRun, IReadOnlySet<string> Loaded)
{
    public const char Separator = ' ';

    public static FeedHistory Of(IFeed feed, IReadOnlyList<string?> succeeded)
    {
        if (succeeded.Count == 0)
        {
            return new FeedHistory(true, new HashSet<string>());
        }

        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tables in succeeded)
        {
            loaded.UnionWith(tables is null
                ? feed.Tables.Select(table => table.Name).Where(name => !TableSchemas.AddedLater.Contains(name))
                : tables.Split(Separator, StringSplitOptions.RemoveEmptyEntries));
        }

        return new FeedHistory(false, loaded);
    }

    public static string Record(IFeed feed) => string.Join(Separator, feed.Tables.Select(table => table.Name));

    public bool Backfills(TableSchema table) => FirstRun || !Loaded.Contains(table.Name);

    public FeedWindow WindowFor(IFeed feed, DateOnly today) => FeedWindow.For(today, feed.Tables.Any(Backfills));

    public FeedWindow? Span(TableSchema table, FeedWindow month, DateOnly today) =>
        Backfills(table) ? month : month.Within(FeedWindow.For(today, false));
}

internal sealed record FeedResult(FeedWindow Window, int Rows);

internal sealed record FeedOutcome(Guid BrandId, string Feed, string Status, int Rows);

internal sealed record DailyRun(int Brands, IReadOnlyList<FeedOutcome> Runs);
