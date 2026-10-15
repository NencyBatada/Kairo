using Kairo.Modules.Catalogue.Imports;
using Kairo.SharedKernel.Persistence;

namespace Kairo.IntegrationTests.Catalogue;

internal static class CatalogueLocks
{
    public static async Task<IAsyncDisposable> HoldAsync(string connectionString, Guid brandId)
    {
        var database = new KairoDatabase(connectionString);
        await database.BeginTransactionAsync(CancellationToken.None);
        Assert.True(await database.TryLockAsync(CatalogueImporter.LockName(brandId), CancellationToken.None));
        return database;
    }
}
