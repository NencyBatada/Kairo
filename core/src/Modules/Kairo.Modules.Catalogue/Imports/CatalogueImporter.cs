using System.Security.Cryptography;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Data;
using Kairo.Modules.Catalogue.Format;
using Kairo.Modules.Catalogue.Products;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record ImportInput(string Kind, string? FileName, string? FeedUrl, Guid? StartedBy, bool AllowLargeRemoval = false);

internal sealed class CatalogueImporter(
    CatalogueDbContext db,
    KairoDatabase database,
    IFileStore store,
    IBrandDirectory brands,
    ILiveUpdates live,
    TimeProvider time)
{
    public const int SaveBatch = 1000;
    public const int LargeRemovalFloor = 20;
    public const string NoValidRows = "no_valid_rows";
    public const string LargeRemoval = "large_removal";

    private Guid BrandId => database.BrandId ?? throw new InvalidOperationException("No brand is set for this import.");

    public static string LockName(Guid brandId) => $"catalogue:{brandId:N}";

    public Task<bool> TryLockAsync(CancellationToken cancellationToken) =>
        database.TryLockAsync(LockName(BrandId), cancellationToken);

    public async Task<CatalogueImport> ImportAsync(ImportInput input, byte[] body, CancellationToken cancellationToken)
    {
        var import = Start(input);
        import.ContentHash = Convert.ToHexStringLower(SHA256.HashData(body));
        var last = await db.Imports.AsNoTracking()
            .Where(i => i.Status == ImportStatuses.Succeeded)
            .OrderByDescending(i => i.StartedAt)
            .ThenByDescending(i => i.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (last is not null && last.ContentHash == import.ContentHash && last.Kind == input.Kind && last.FeedUrl == input.FeedUrl)
        {
            import.Status = ImportStatuses.Unchanged;
            return await FinishAsync(import, cancellationToken);
        }

        import.StorageKey = FileKeys.For(BrandId, "imports", import.Id);
        await store.PutAsync(FileBuckets.CatalogueImports, import.StorageKey, body, "application/octet-stream", cancellationToken);

        ParsedCatalogue parsed;
        try
        {
            var currency = (await brands.GetAsync([BrandId], cancellationToken)).Single().Currency;
            parsed = CatalogueFile.Parse(body, currency);
        }
        catch (CatalogueFileException exception)
        {
            return await FailAsync(import, exception.Code, exception.Message, cancellationToken);
        }

        import.Rows = parsed.Rows;
        import.Valid = parsed.Offers.Count;
        import.Invalid = parsed.Invalid;
        import.Problems = ImportProblems.Write(parsed.Problems);
        await ApplyAsync(import, parsed.Offers, input, cancellationToken);
        return await FinishAsync(import, cancellationToken);
    }

    public Task<CatalogueImport> FailAsync(ImportInput input, string code, string message, CancellationToken cancellationToken) =>
        FailAsync(Start(input), code, message, cancellationToken);

    private async Task ApplyAsync(CatalogueImport import, IReadOnlyList<ProductOffer> offers, ImportInput input, CancellationToken cancellationToken)
    {
        var existing = await db.Products.AsNoTracking().ToListAsync(cancellationToken);
        var byOffer = existing.ToDictionary(product => product.OfferId, StringComparer.OrdinalIgnoreCase);
        var offered = offers.Select(offer => offer.OfferId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = existing.Count(product => product.Status == ProductStatuses.Active);
        var removals = existing.Where(product => product.Status == ProductStatuses.Active && !offered.Contains(product.OfferId)).ToList();

        if (offers.Count == 0)
        {
            Suspicious(import, NoValidRows, "No product in this import could be read, so Kairo changed nothing.");
            return;
        }

        if (!input.AllowLargeRemoval && active >= LargeRemovalFloor && removals.Count * 2 > active)
        {
            Suspicious(import, LargeRemoval, LargeRemovalMessage(input.Kind, removals.Count, active));
            return;
        }

        var now = import.StartedAt;
        var pending = 0;
        foreach (var offer in offers)
        {
            if (byOffer.TryGetValue(offer.OfferId, out var product))
            {
                var before = product.StockState;
                if (!product.Apply(offer, import.Id, now))
                {
                    import.Unchanged++;
                    continue;
                }

                db.Products.Update(product);
                import.Updated++;
                RecordStock(product, before, import.Id, now);
            }
            else
            {
                db.Products.Add(Product.From(offer, import.Id, now));
                import.Added++;
            }

            pending = await SaveWhenFullAsync(pending + 1, cancellationToken);
        }

        foreach (var product in removals)
        {
            var before = product.StockState;
            product.Remove(import.Id, now);
            db.Products.Update(product);
            import.Removed++;
            RecordStock(product, before, import.Id, now);
            pending = await SaveWhenFullAsync(pending + 1, cancellationToken);
        }

        await SaveAsync(cancellationToken);
        import.Status = ImportStatuses.Succeeded;
    }

    private void RecordStock(Product product, string? before, Guid importId, DateTimeOffset now)
    {
        if (product.StockState is null || before == product.StockState)
        {
            return;
        }

        db.StockEvents.Add(new StockEvent
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            From = before,
            To = product.StockState,
            ImportId = importId,
            At = now,
        });
    }

    private async Task<int> SaveWhenFullAsync(int pending, CancellationToken cancellationToken)
    {
        if (pending < SaveBatch)
        {
            return pending;
        }

        await SaveAsync(cancellationToken);
        return 0;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private static void Suspicious(CatalogueImport import, string code, string message)
    {
        import.Status = ImportStatuses.Suspicious;
        import.ErrorCode = code;
        import.ErrorMessage = message;
    }

    private static string LargeRemovalMessage(string kind, int removals, int active) =>
        $"This import would remove {removals} of your {active} products, so Kairo changed nothing. "
        + (kind == SourceKinds.File ? "If that's right, import the file again and allow large removals." : "Check the feed, then refresh it.");

    private Task<CatalogueImport> FailAsync(CatalogueImport import, string code, string message, CancellationToken cancellationToken)
    {
        import.Status = ImportStatuses.Failed;
        import.ErrorCode = code;
        import.ErrorMessage = message;
        return FinishAsync(import, cancellationToken);
    }

    private async Task<CatalogueImport> FinishAsync(CatalogueImport import, CancellationToken cancellationToken)
    {
        import.FinishedAt = time.GetUtcNow();
        db.Imports.Add(import);
        await db.SaveChangesAsync(cancellationToken);
        await live.PublishAsync(CatalogueLiveEvents.Imported(import), cancellationToken);
        return import;
    }

    private CatalogueImport Start(ImportInput input) => new()
    {
        Id = Guid.NewGuid(),
        Kind = input.Kind,
        FileName = input.FileName,
        FeedUrl = input.FeedUrl,
        StartedBy = input.StartedBy,
        StartedAt = time.GetUtcNow(),
    };
}
