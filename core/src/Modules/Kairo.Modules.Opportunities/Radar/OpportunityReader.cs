using Kairo.Contracts.Catalogue;
using Kairo.Modules.Opportunities.Data;
using Kairo.Modules.Opportunities.Scoring;
using Kairo.Modules.Opportunities.Topics;

namespace Kairo.Modules.Opportunities.Radar;

internal sealed class OpportunityReader(OpportunitiesDbContext db, ICatalogue catalogue)
{
    public async Task<Shelf> ShelfAsync(IReadOnlyCollection<Opportunity> opportunities, CancellationToken cancellationToken)
    {
        var topics = await db.BrandTopics.AsNoTracking().ToListAsync(cancellationToken);
        var ids = opportunities.SelectMany(opportunity => opportunity.MatchedProductIds).ToHashSet();
        var products = await catalogue.GetInStockAsync(ids, cancellationToken);
        return new Shelf(products.ToDictionary(product => product.Id), topics);
    }

    public async Task<OpportunityDetail> DetailAsync(Opportunity opportunity, CancellationToken cancellationToken)
    {
        var shelf = await ShelfAsync([opportunity], cancellationToken);
        return OpportunityDetail.Of(opportunity, shelf.InStock(opportunity), shelf.Watched(opportunity));
    }
}

internal sealed class Shelf(IReadOnlyDictionary<Guid, CatalogueProduct> products, IReadOnlyList<WatchTopic> topics)
{
    public IReadOnlyList<CatalogueProduct> InStock(Opportunity opportunity) =>
        opportunity.MatchedProductIds.Where(products.ContainsKey).Select(id => products[id]).ToList();

    public bool Watched(Opportunity opportunity) => Mentioned(opportunity, TopicKinds.Watch);

    public bool Ignored(Opportunity opportunity) => Mentioned(opportunity, TopicKinds.Ignore);

    private bool Mentioned(Opportunity opportunity, string kind) =>
        topics.Any(topic => topic.Kind == kind && TopicText.Mentions(topic.Phrase, opportunity.Topic, opportunity.Keywords));
}
