using System.Text.Json;
using Kairo.Contracts.Catalogue;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;

namespace Kairo.Modules.Launch.Google;

internal sealed class LaunchPreflight(LaunchGoogle google, ICatalogue catalogue, TimeProvider time)
{
    public async Task<Dictionary<string, string>> CheckAsync(LaunchPlan plan, CancellationToken cancellationToken)
    {
        if (google.Version != "v25" || plan.ExecutionVersion != "google-ads/v25/launch-1") Fail("version_changed", "This launch uses an unsupported execution version.", "Review the plan with a supported Google Ads version and approve it again.");
        var account = plan.GoogleAdsAccountId!;
        var customers = await google.SearchAsync(account, "SELECT customer.id, customer.currency_code, customer.time_zone FROM customer LIMIT 1", cancellationToken);
        if (customers.Length != 1 || !customers[0].TryGetProperty("customer", out var customer)) Fail("account_unavailable", "Google Ads account details are unavailable.", "Reconnect the approved Google Ads account.");
        customer = customers[0].GetProperty("customer");
        if (Text(customer, "currencyCode") != plan.Currency || !SameZone(Text(customer, "timeZone"), plan.TimeZone)) Fail("account_settings", "The account currency or time zone differs from the approved plan.", "Correct the workspace settings and approve a new launch plan.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(plan.TimeZone)).DateTime);
        if (plan.Start < today || plan.End < today) Fail("dates_passed", "The approved start date has passed.", "Send the campaign back and approve current dates.");
        var regions = plan.Regions.Concat(plan.Holdouts).ToArray();
        if (regions.Length == 0 || regions.Any(region => !long.TryParse(region.Id, out var id) || id <= 0)) Fail("unresolved_region", "Some approved regions have no Google Ads location.", "Resolve every region and approve the updated plan.");
        var locations = await google.SearchAsync(account, $"SELECT geo_target_constant.id, geo_target_constant.name, geo_target_constant.status FROM geo_target_constant WHERE geo_target_constant.id IN ({string.Join(',', regions.Select(region => region.Id))})", cancellationToken);
        foreach (var region in regions)
        {
            if (!locations.Any(row => row.TryGetProperty("geoTargetConstant", out var location) && Text(location, "id") == region.Id
                && string.Equals(Text(location, "name"), region.Name, StringComparison.OrdinalIgnoreCase) && Text(location, "status") == "ENABLED"))
                Fail("unresolved_region", $"Google Ads couldn't verify {region.Name}.", "Choose the correct Google Ads location for that region and approve the plan again.");
        }

        var languages = await google.SearchAsync(account, "SELECT language_constant.id, language_constant.code FROM language_constant", cancellationToken);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in plan.Languages)
        {
            var found = languages.FirstOrDefault(row => row.TryGetProperty("languageConstant", out var value) && Text(value, "code") == language);
            if (found.ValueKind == JsonValueKind.Undefined) Fail("language_missing", "An approved language isn't supported by Google Ads.", "Choose supported campaign languages and approve again.");
            ids[language] = Text(found.GetProperty("languageConstant"), "id");
        }

        if (plan.Settings.Channels.Any(channel => channel.CampaignType == "demand_gen") && (plan.Settings.AudienceIds ?? []).Length == 0)
            Fail("audience_missing", "Demand Gen needs an approved audience list.", "Choose a Google Ads user list in the launch settings and approve again.");
        foreach (var id in plan.Settings.AudienceIds ?? [])
        {
            if (!long.TryParse(id, out _)) Fail("audience_missing", "An audience ID isn't valid.", "Choose an existing Google Ads user list.");
            var lists = await google.SearchAsync(account, $"SELECT user_list.id, user_list.membership_status FROM user_list WHERE user_list.id = {id}", cancellationToken);
            if (lists.Length != 1 || Text(lists[0].GetProperty("userList"), "membershipStatus") != "OPEN") Fail("audience_missing", "An approved audience list isn't available.", "Choose an available Google Ads user list and approve again.");
        }

        var products = plan.Assets.SelectMany(asset => asset.ProductIds).Distinct().ToArray();
        var productIds = products.Select(id => Guid.TryParse(id, out var value) ? value : Guid.Empty).ToArray();
        if (productIds.Contains(Guid.Empty) || (await catalogue.GetInStockAsync(productIds, cancellationToken)).Count != productIds.Length)
            Fail("product_unavailable", "An approved ad uses a product that is no longer in stock.", "Send the assets back and approve ads for available products.");
        foreach (var channel in plan.Settings.Channels)
        {
            if (channel.CampaignType == "search" && !plan.Assets.Any(asset => asset.Kind == "search_ad")) Fail("missing_search_ads", "The plan has no Search ads.", "Generate and approve the missing Search ads.");
            if (channel.CampaignType == "search")
            {
                foreach (var ad in plan.Assets.Where(asset => asset.Kind == "search_ad"))
                    if (!plan.Assets.Any(asset => asset.Kind == "keyword_set" && asset.AdGroup == ad.AdGroup && asset.Language == ad.Language
                        && asset.Content.TryGetProperty("keywords", out var keywords) && keywords.ValueKind == JsonValueKind.Array && keywords.GetArrayLength() > 0))
                        Fail("missing_keywords", "A Search ad group has no approved keywords.", "Generate and approve keywords for every Search ad group.");
            }
            if (channel.CampaignType == "shopping")
            {
                var links = await google.SearchAsync(account, "SELECT product_link.merchant_center.merchant_center_id FROM product_link WHERE product_link.type = 'MERCHANT_CENTER'", cancellationToken);
                if (!links.Any(row => row.TryGetProperty("productLink", out var link) && link.TryGetProperty("merchantCenter", out var merchant) && Text(merchant, "merchantCenterId") == plan.MerchantAccountId))
                    Fail("merchant_not_linked", "Merchant Center isn't linked to the approved Google Ads account.", "Link the approved Merchant Center account in Google Ads and retry.");
                if (!plan.Assets.Any(asset => asset.Kind == "shopping_title" && !string.IsNullOrWhiteSpace(Text(asset.Content, "offerId")))) Fail("missing_products", "The Shopping plan has no Merchant Center product IDs.", "Import the Merchant Center offer IDs and approve the assets again.");
            }

            if (channel.CampaignType == "demand_gen" && (plan.Logo is null || !plan.Assets.Any(asset => asset.Kind == "demand_gen_text") || !plan.Assets.Any(asset => asset.Kind == "image" && asset.Format == "square") || !plan.Assets.Any(asset => asset.Kind == "image" && asset.Format == "landscape")))
                Fail("missing_demand_gen_assets", "Demand Gen needs approved text, landscape and square images, and a Brand Kit logo.", "Complete and approve the Demand Gen assets and logo.");
            if (channel.BiddingStrategy != "maximize_clicks")
            {
                var conversions = await google.SearchAsync(account, "SELECT conversion_action.id FROM conversion_action WHERE conversion_action.status = 'ENABLED' AND conversion_action.primary_for_goal = TRUE LIMIT 1", cancellationToken);
                if (conversions.Length == 0) Fail("conversion_tracking", "This bidding strategy needs active conversion tracking.", "Set up a primary conversion action or approve a different bid strategy.");
            }
        }

        if (plan.Assets.Any(asset => asset.DisclosureBlocker is not null || asset.CheckState is not ("passed" or "overridden"))) Fail("assets_changed", "The approved assets have unresolved checks.", "Review the assets and approve them again.");
        return ids;
    }

    public static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var result) ? result.ToString() : "";
    private static bool SameZone(string left, string right)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(left).HasSameRules(TimeZoneInfo.FindSystemTimeZoneById(right)); }
        catch (TimeZoneNotFoundException) { return false; }
    }

    private static void Fail(string code, string message, string fix) => throw new LaunchException(code, message, fix);
}
