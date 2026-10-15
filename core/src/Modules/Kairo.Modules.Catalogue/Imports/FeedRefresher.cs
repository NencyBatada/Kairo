using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Catalogue.Data;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed class FeedRefresher(
    CatalogueDbContext db,
    CatalogueImporter importer,
    FeedFetcher fetcher,
    INotifications notifications,
    IBrandDirectory brands,
    TimeProvider time)
{
    public const int AlertAfter = 3;
    public const string AlertKind = "connection.problem";
    public const string AlertTitle = "Your product feed isn't refreshing";
    public const string AlertBody = "Kairo couldn't refresh your product feed 3 times in a row.";

    public async Task<CatalogueImport?> RefreshAsync(string feedUrl, Guid? startedBy, CancellationToken cancellationToken)
    {
        if (!await importer.TryLockAsync(cancellationToken))
        {
            return null;
        }

        var input = new ImportInput(SourceKinds.Feed, null, feedUrl, startedBy);
        var fetched = await fetcher.FetchAsync(feedUrl, cancellationToken);
        var import = fetched.Body is { } body
            ? await importer.ImportAsync(input, body, cancellationToken)
            : await importer.FailAsync(input, fetched.ErrorCode!, fetched.ErrorMessage!, cancellationToken);
        await RecordAsync(import, cancellationToken);
        return import;
    }

    public static bool Succeeded(CatalogueImport import) =>
        import.Status is ImportStatuses.Succeeded or ImportStatuses.Unchanged;

    private async Task RecordAsync(CatalogueImport import, CancellationToken cancellationToken)
    {
        if (await db.BrandSources.SingleOrDefaultAsync(cancellationToken) is not { Kind: SourceKinds.Feed } source)
        {
            return;
        }

        var now = time.GetUtcNow();
        source.LastCheckedAt = now;
        if (Succeeded(import))
        {
            source.LastSucceededAt = now;
            source.FailuresInRow = 0;
        }
        else
        {
            source.FailuresInRow++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (source.FailuresInRow == AlertAfter)
        {
            var brand = (await brands.GetAsync([source.BrandId], cancellationToken)).Single();
            await notifications.NotifyAsync(
                new Notification(AlertKind, LiveAudience.Roles(BrandRoles.Admin), AlertTitle, AlertBody, $"/{brand.Handle}/brand-kit/catalogue"),
                cancellationToken);
        }
    }
}
