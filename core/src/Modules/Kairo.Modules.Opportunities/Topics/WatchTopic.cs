namespace Kairo.Modules.Opportunities.Topics;

internal sealed class WatchTopic : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public string Phrase { get; set; } = "";

    public string Kind { get; set; } = TopicKinds.Watch;

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal static class TopicKinds
{
    public const string Watch = "watch";
    public const string Ignore = "ignore";

    public static readonly string[] All = [Watch, Ignore];
}
