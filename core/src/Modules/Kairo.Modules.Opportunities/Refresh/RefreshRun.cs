namespace Kairo.Modules.Opportunities.Refresh;

internal sealed class RefreshRun : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Outcome { get; set; } = "";

    public string? Reason { get; set; }

    public int Keywords { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    public int Expired { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }
}

internal static class RefreshOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
    public const string Error = "error";
}

internal static class RefreshReasons
{
    public const string NotConnected = "not_connected";
    public const string NeedsReconnect = "needs_reconnect";
    public const string NoSeeds = "no_seeds";
    public const string NoStock = "no_stock";
    public const string Busy = "busy";
    public const string AccessLost = "access_lost";
    public const string GoogleAds = "google_ads";
    public const string Agent = "agent";
}
