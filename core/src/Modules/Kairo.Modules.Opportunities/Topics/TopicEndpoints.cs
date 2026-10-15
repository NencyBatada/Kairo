using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;
using Kairo.Modules.Opportunities.Data;

namespace Kairo.Modules.Opportunities.Topics;

internal sealed record TopicRequest(string? Phrase, string? Kind);

internal sealed record TopicView(Guid Id, string Phrase, string Kind, DateTimeOffset CreatedAt)
{
    public static TopicView Of(WatchTopic topic) => new(topic.Id, topic.Phrase, topic.Kind, topic.CreatedAt);
}

internal static class TopicEndpoints
{
    public const string AuditTarget = "opportunity_topic";
    public const int MinPhraseLength = 2;
    public const int MaxPhraseLength = 100;
    public const int MaxTopics = 200;
    public const string Duplicate = "That topic is already on your list.";

    public static async Task<Ok<TopicView[]>> ListAsync(OpportunitiesDbContext db, CancellationToken cancellationToken)
    {
        var topics = await db.BrandTopics.AsNoTracking().OrderBy(topic => topic.Kind).ThenBy(topic => topic.Phrase).ToListAsync(cancellationToken);
        return TypedResults.Ok(topics.Select(TopicView.Of).ToArray());
    }

    public static async Task<Results<Created<TopicView>, ValidationProblem, ProblemHttpResult>> AddAsync(
        TopicRequest request,
        OpportunitiesDbContext db,
        KairoDatabase database,
        CurrentMember member,
        IAuditLog audit,
        ILiveUpdates live,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var phrase = string.Join(' ', (request.Phrase ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var errors = new Dictionary<string, string[]>();
        if (phrase.Length is < MinPhraseLength or > MaxPhraseLength)
        {
            errors["phrase"] = [$"Use {MinPhraseLength} to {MaxPhraseLength} characters."];
        }

        if (request.Kind is null || !TopicKinds.All.Contains(request.Kind))
        {
            errors["kind"] = ["Use watch or ignore."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await database.LockAsync($"opportunity_topics:{member.BrandId:N}", cancellationToken);
        var existing = await db.BrandTopics.Select(topic => topic.Phrase).ToListAsync(cancellationToken);
        if (existing.Contains(phrase, StringComparer.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Duplicate);
        }

        if (existing.Count >= MaxTopics)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"You can keep up to {MaxTopics} topics.");
        }

        var topic = new WatchTopic
        {
            Id = Guid.NewGuid(),
            Phrase = phrase,
            Kind = request.Kind!,
            CreatedBy = member.UserId,
            CreatedAt = time.GetUtcNow(),
        };
        db.WatchTopics.Add(topic);
        await db.SaveChangesAsync(cancellationToken);
        var view = TopicView.Of(topic);
        await audit.RecordAsync(new AuditRecord("opportunity_topic.added", AuditTarget, topic.Id.ToString(), After: view), cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.TopicsChanged(), cancellationToken);
        return TypedResults.Created($"/api/brands/{member.BrandId}/opportunities/topics/{topic.Id}", view);
    }

    public static async Task<Results<NoContent, NotFound>> RemoveAsync(
        Guid topicId,
        OpportunitiesDbContext db,
        IAuditLog audit,
        ILiveUpdates live,
        CancellationToken cancellationToken)
    {
        if (await db.BrandTopics.SingleOrDefaultAsync(topic => topic.Id == topicId, cancellationToken) is not { } topic)
        {
            return TypedResults.NotFound();
        }

        db.WatchTopics.Remove(topic);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord("opportunity_topic.removed", AuditTarget, topic.Id.ToString(), Before: TopicView.Of(topic)), cancellationToken);
        await live.PublishAsync(OpportunityLiveEvents.TopicsChanged(), cancellationToken);
        return TypedResults.NoContent();
    }
}
