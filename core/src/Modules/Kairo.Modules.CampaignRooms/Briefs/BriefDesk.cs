using System.Globalization;
using Kairo.Contracts.Brands;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Identity;
using Kairo.Modules.CampaignRooms.Rooms;

namespace Kairo.Modules.CampaignRooms.Briefs;

internal sealed record BriefProductOption(string Id, string Title, decimal? Price, string? Currency, string? Link, bool InStock);

internal sealed record BriefRegionOption(string? Id, string Name, long? LastMonthSearches);

internal sealed record BriefDeskContext(
    BrandSummary Brand,
    RoomEvidence Evidence,
    DateOnly Today,
    DateOnly? LatestEnd,
    IReadOnlyList<BriefRegionOption> Regions,
    IReadOnlyList<BriefProductOption> Products,
    BudgetCap Limit,
    bool MerchantCenterLinked,
    bool YouTubeConnected)
{
    public BriefSituation Situation() => new(
        Today,
        LatestEnd,
        Limit.Amount,
        Limit.Currency,
        Products.ToDictionary(product => product.Id, product => product.Title),
        Products.Where(product => product.InStock).Select(product => product.Id).ToHashSet(),
        MerchantCenterLinked,
        YouTubeConnected);

    public BriefProductOption? Product(string id) => Products.FirstOrDefault(product => product.Id == id);

    public string? Region(string name) =>
        Regions.Select(region => region.Name).FirstOrDefault(known => string.Equals(known, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal sealed class BriefDesk(
    ICatalogue catalogue,
    IBrandDirectory brands,
    IBudgetLimit limits,
    IConnections connections,
    TimeProvider time)
{
    public const string Market = "India";

    public async Task<BriefDeskContext> ReadAsync(Room room, BriefContent? content, bool withConnections, CancellationToken cancellationToken)
    {
        var evidence = RoomEvidence.Read(room.Evidence);
        var brand = (await brands.GetAsync([room.BrandId], cancellationToken)).Single();
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(brand.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        DateOnly? latestEnd = evidence.Trigger?.ConditionEndsAt is { } ends
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ends.AddTicks(-1), zone).DateTime)
            : null;

        var regions = evidence.RegionsInPlay()
            .Select(region => new BriefRegionOption(region.Id, region.Name!, region.LastMonthSearches))
            .ToList();
        foreach (var name in content?.Regions ?? [])
        {
            if (!regions.Any(region => string.Equals(region.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                regions.Add(new BriefRegionOption(null, name, null));
            }
        }

        if (regions.Count == 0)
        {
            regions.Add(new BriefRegionOption(null, Market, null));
        }

        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in evidence.Products ?? [])
        {
            if (product.Id != Guid.Empty && !string.IsNullOrWhiteSpace(product.Title))
            {
                titles.TryAdd(product.Id.ToString(), product.Title.Trim());
            }
        }

        var ids = titles.Keys.Concat(content?.ProductIds ?? [])
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var stocked = (await catalogue.GetInStockAsync(ids, cancellationToken)).ToDictionary(product => product.Id);
        var products = ids
            .Select(id => stocked.TryGetValue(id, out var product)
                ? new BriefProductOption(id.ToString(), product.Title, product.SalePrice ?? product.Price, product.Currency, product.Link, true)
                : new BriefProductOption(id.ToString(), titles.GetValueOrDefault(id.ToString(), "Unknown product"), null, null, null, false))
            .ToList();

        var limit = await limits.GetAsync(room.BrandId, cancellationToken);
        var merchant = withConnections && await ConnectedAsync(ConnectedProduct.MerchantCenter, cancellationToken);
        var youTube = withConnections && await ConnectedAsync(ConnectedProduct.YouTube, cancellationToken);
        return new BriefDeskContext(brand, evidence, today, latestEnd, regions, products, limit, merchant, youTube);
    }

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? ParseDay(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private async Task<bool> ConnectedAsync(ConnectedProduct product, CancellationToken cancellationToken) =>
        (await connections.GetStatusAsync(product, cancellationToken)).State == ConnectionState.Connected;
}
