using Kairo.Contracts.Live;
using Kairo.Modules.Catalogue.Imports;

namespace Kairo.Modules.Catalogue;

internal static class CatalogueLiveEvents
{
    public static LiveEvent Imported(CatalogueImport import) =>
        new("catalogue.imported", new { importId = import.Id, status = import.Status }, LiveAudience.Everyone);

    public static LiveEvent ProductChanged(Guid productId) =>
        new("catalogue.product_changed", new { productId }, LiveAudience.Everyone);
}
