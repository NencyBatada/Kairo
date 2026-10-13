using Kairo.Contracts.Connections;

namespace Kairo.Modules.Brands.Connections;

internal sealed record ProductInfo(ConnectedProduct Product, string Slug, string Name, string[] Scopes);

internal static class Products
{
    public static readonly ProductInfo[] All =
    [
        new(ConnectedProduct.GoogleAds, "google-ads", "Google Ads", ["https://www.googleapis.com/auth/adwords"]),
        new(ConnectedProduct.YouTube, "youtube", "YouTube",
            ["https://www.googleapis.com/auth/youtube.readonly", "https://www.googleapis.com/auth/youtube.upload"]),
        new(ConnectedProduct.MerchantCenter, "merchant-center", "Merchant Center", ["https://www.googleapis.com/auth/content"]),
        new(ConnectedProduct.Ga4, "ga4", "Google Analytics", ["https://www.googleapis.com/auth/analytics.readonly"]),
        new(ConnectedProduct.BigQuery, "bigquery", "BigQuery", ["https://www.googleapis.com/auth/bigquery"]),
    ];

    public static ProductInfo? FindBySlug(string? slug) => All.FirstOrDefault(product => product.Slug == slug);

    public static ProductInfo Get(ConnectedProduct product) => All.Single(info => info.Product == product);
}
