namespace Kairo.Modules.Measurement.Feeds;

internal static class FeedNames
{
    public const string GoogleAds = "google-ads";
    public const string Ga4 = "ga4";
    public const string SalesTable = "sales-table";
}

internal static class RunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

internal static class SkipReasons
{
    public const string NotConnected = "not_connected";
    public const string NeedsReconnect = "needs_reconnect";
    public const string AlreadyRunning = "already_running";
}

internal static class FailReasons
{
    public const string AccessLost = "access_lost";
    public const string Interrupted = "interrupted";
}

internal static class FeedStates
{
    public const string NotSetUp = "not_set_up";
    public const string NeedsConnection = "needs_connection";
    public const string Waiting = "waiting";
    public const string Running = "running";
    public const string Failing = "failing";
    public const string Ok = "ok";
}
