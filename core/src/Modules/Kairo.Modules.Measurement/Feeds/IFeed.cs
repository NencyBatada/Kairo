using Kairo.Contracts.Connections;

namespace Kairo.Modules.Measurement.Feeds;

internal interface IFeed
{
    string Name { get; }

    ConnectedProduct Product { get; }

    Task<bool> IsSetUpAsync(CancellationToken cancellationToken);

    string TargetOf(ConnectionStatus status);

    Task<FeedResult> RunAsync(Guid brandId, ConnectionAccess access, bool firstRun, CancellationToken cancellationToken);
}

internal sealed record FeedResult(FeedWindow Window, int Rows);

internal sealed record FeedOutcome(Guid BrandId, string Feed, string Status, int Rows);

internal sealed record DailyRun(int Brands, IReadOnlyList<FeedOutcome> Runs);
