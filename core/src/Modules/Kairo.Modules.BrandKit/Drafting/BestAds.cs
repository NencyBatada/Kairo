using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Contracts.Connections;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed record BestAd(string Id, IReadOnlyList<string> Headlines, IReadOnlyList<string> Descriptions);

internal sealed partial class BestAds(IConnections connections, IHttpClientFactory http, IConfiguration configuration, ILogger<BestAds> logger)
{
    public const string ClientName = "kairo-brand-kit-google";
    public const int Max = 10;
    public const int MaxHeadlines = 20;
    public const int MaxDescriptions = 10;

    public const string Query =
        "SELECT ad_group_ad.ad.id, ad_group_ad.ad.type, "
        + "ad_group_ad.ad.responsive_search_ad.headlines, ad_group_ad.ad.responsive_search_ad.descriptions, "
        + "ad_group_ad.ad.responsive_display_ad.headlines, ad_group_ad.ad.responsive_display_ad.descriptions, "
        + "ad_group_ad.ad.final_urls, metrics.conversions, metrics.clicks "
        + "FROM ad_group_ad "
        + "WHERE segments.date DURING LAST_90_DAYS AND ad_group_ad.status = 'ENABLED' AND metrics.impressions > 0 "
        + "ORDER BY metrics.conversions DESC, metrics.clicks DESC "
        + "LIMIT 10";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly string[] AdKinds = ["responsiveSearchAd", "responsiveDisplayAd"];

    private string ApiVersion => configuration["Kairo:Google:AdsApiVersion"] is { Length: > 0 } version ? version : "v25";

    public async Task<IReadOnlyList<BestAd>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await connections.GetStatusAsync(ConnectedProduct.GoogleAds, cancellationToken);
            if (status.State != ConnectionState.Connected)
            {
                return [];
            }

            var access = await connections.GetAccessAsync(ConnectedProduct.GoogleAds, cancellationToken);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://googleads.googleapis.com/{ApiVersion}/customers/{access.ExternalId}/googleAds:searchStream")
            {
                Content = JsonContent.Create(new { query = Query }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
            if (access.ManagerId is { } manager)
            {
                request.Headers.Add("login-customer-id", manager);
            }

            using var response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Ads answered {Status} when Kairo read the best ads, so drafting goes on without them.", (int)response.StatusCode);
                return [];
            }

            return Parse(text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Couldn't read the best ads, so drafting goes on without them.");
            return [];
        }
    }

    public static IReadOnlyList<BestAd> Parse(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        var root = document.RootElement;
        List<JsonElement> batches = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];
        var ads = new List<BestAd>();
        foreach (var batch in batches)
        {
            if (!Child(batch, "results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var row in results.EnumerateArray())
            {
                if (!Child(row, "adGroupAd", out var adGroupAd) || !Child(adGroupAd, "ad", out var ad))
                {
                    continue;
                }

                var id = Child(ad, "id", out var value) ? value.ToString() : "";
                var headlines = Texts(ad, "headlines");
                var descriptions = Texts(ad, "descriptions");
                if (AdId().IsMatch(id) && (headlines.Count > 0 || descriptions.Count > 0) && ads.All(known => known.Id != id))
                {
                    ads.Add(new BestAd(id, headlines.Take(MaxHeadlines).ToList(), descriptions.Take(MaxDescriptions).ToList()));
                }
            }
        }

        return ads.Take(Max).ToList();
    }

    private static List<string> Texts(JsonElement ad, string field)
    {
        var texts = new List<string>();
        foreach (var kind in AdKinds)
        {
            if (!Child(ad, kind, out var body) || !Child(body, field, out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (Child(asset, "text", out var text) && text.ValueKind == JsonValueKind.String
                    && text.GetString()?.Trim() is { Length: > 0 } line && !texts.Contains(line))
                {
                    texts.Add(line);
                }
            }
        }

        return texts;
    }

    private static bool Child(JsonElement element, string name, out JsonElement child)
    {
        child = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out child);
    }

    [GeneratedRegex("^[0-9]{1,20}$")]
    private static partial Regex AdId();
}
