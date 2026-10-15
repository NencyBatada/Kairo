using Kairo.Contracts.Audit;
using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record FeedRequest(string? Url);

internal static class SourceEndpoints
{
    public const string AuditTarget = "catalogue_source";
    public const string NoFeed = "There's no feed link to refresh.";

    public static async Task<Results<Ok<SourceView>, NoContent>> GetAsync(CatalogueDbContext db, CancellationToken cancellationToken)
    {
        if (await db.BrandSources.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is { } source)
        {
            return TypedResults.Ok(SourceView.Of(source));
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<FeedSetView>, ValidationProblem, ProblemHttpResult>> SetFeedAsync(
        FeedRequest request,
        CatalogueDbContext db,
        CatalogueImporter importer,
        FeedFetcher fetcher,
        CurrentMember member,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (FeedUrls.Clean(request.Url) is not { } url)
        {
            return UrlProblem(FeedUrls.BadUrl);
        }

        if (!await importer.TryLockAsync(cancellationToken))
        {
            return ImportEndpoints.BusyProblem();
        }

        var fetched = await fetcher.FetchAsync(url, cancellationToken);
        if (fetched.Body is not { } body)
        {
            return UrlProblem(fetched.ErrorMessage!);
        }

        var import = await importer.ImportAsync(new ImportInput(SourceKinds.Feed, null, url, member.UserId), body, cancellationToken);
        if (import.Status == ImportStatuses.Failed)
        {
            return UrlProblem(import.ErrorMessage!);
        }

        var source = await db.BrandSources.SingleOrDefaultAsync(cancellationToken);
        var before = source is null ? null : SourceView.Of(source);
        if (source is null)
        {
            source = new CatalogueSource { BrandId = member.BrandId };
            db.Sources.Add(source);
        }

        var now = time.GetUtcNow();
        var succeeded = FeedRefresher.Succeeded(import);
        source.Kind = SourceKinds.Feed;
        source.FeedUrl = url;
        source.SetBy = member.UserId;
        source.SetAt = now;
        source.LastCheckedAt = now;
        source.LastSucceededAt = succeeded ? now : null;
        source.FailuresInRow = succeeded ? 0 : 1;
        await db.SaveChangesAsync(cancellationToken);

        var after = SourceView.Of(source);
        await audit.RecordAsync(new AuditRecord("catalogue.feed_set", AuditTarget, member.BrandId.ToString(), before, after), cancellationToken);
        return TypedResults.Ok(new FeedSetView(after, ImportView.Of(import)));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> RemoveFeedAsync(
        CatalogueDbContext db,
        CatalogueImporter importer,
        CurrentMember member,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (!await importer.TryLockAsync(cancellationToken))
        {
            return ImportEndpoints.BusyProblem();
        }

        if (await db.BrandSources.SingleOrDefaultAsync(cancellationToken) is { Kind: SourceKinds.Feed } source)
        {
            var before = SourceView.Of(source);
            db.Sources.Remove(source);
            await db.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(new AuditRecord("catalogue.feed_removed", AuditTarget, member.BrandId.ToString(), before), cancellationToken);
        }

        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<ImportView>, ProblemHttpResult>> RefreshAsync(
        CatalogueDbContext db,
        FeedRefresher refresher,
        CurrentMember member,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (await db.BrandSources.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is not { Kind: SourceKinds.Feed, FeedUrl: { } url })
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: NoFeed);
        }

        if (await refresher.RefreshAsync(url, member.UserId, cancellationToken) is not { } import)
        {
            return ImportEndpoints.BusyProblem();
        }

        var view = ImportView.Of(import);
        await audit.RecordAsync(new AuditRecord("catalogue.feed_refreshed", ImportEndpoints.AuditTarget, import.Id.ToString(), After: view), cancellationToken);
        return TypedResults.Ok(view);
    }

    private static ValidationProblem UrlProblem(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [message] });
}
