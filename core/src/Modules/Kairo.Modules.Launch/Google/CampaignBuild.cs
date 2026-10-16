using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Launch;

namespace Kairo.Modules.Launch.Google;

internal sealed record BuiltAd(string Key, string CampaignKey, string CampaignType, string[] ProductIds);
internal sealed record CampaignMutations(object[] Operations, string[] Keys, BuiltAd[] Ads);

internal sealed class CampaignBuild(LaunchPlan plan, IReadOnlyDictionary<string, string> media, IReadOnlyDictionary<string, string> languages)
{
    private readonly List<object> _operations = [];
    private readonly List<string> _keys = [];
    private readonly List<BuiltAd> _ads = [];
    private int _next = -1;
    private string Account => plan.GoogleAdsAccountId!;
    public static string Marker(Guid roomId, string key) => $"Kairo {roomId:N} {key}";
    private string Temporary(string type) => $"customers/{Account}/{type}/{_next--}";

    public CampaignMutations Build()
    {
        foreach (var channel in plan.Settings.Channels)
        {
            var campaignKey = $"campaign:{channel.CampaignType}";
            var budget = Temporary("campaignBudgets");
            Add("campaignBudget", $"budget:{channel.CampaignType}", new JsonObject
            {
                ["resourceName"] = budget, ["name"] = Marker(plan.RoomId, $"budget {channel.CampaignType}"),
                ["totalAmountMicros"] = Micros(channel.Budget), ["period"] = "CUSTOM", ["deliveryMethod"] = "STANDARD", ["explicitlyShared"] = false,
            });
            var campaign = Temporary("campaigns");
            var body = new JsonObject
            {
                ["resourceName"] = campaign, ["name"] = Marker(plan.RoomId, channel.CampaignType),
                ["advertisingChannelType"] = channel.CampaignType.ToUpperInvariant(), ["status"] = "PAUSED", ["campaignBudget"] = budget,
                ["startDateTime"] = plan.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00",
                ["endDateTime"] = plan.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59",
                ["containsEuPoliticalAdvertising"] = "DOES_NOT_CONTAIN_EU_POLITICAL_ADVERTISING",
                ["geoTargetTypeSetting"] = new JsonObject { ["positiveGeoTargetType"] = "PRESENCE", ["negativeGeoTargetType"] = "PRESENCE" },
            };
            body[channel.BiddingStrategy switch { "maximize_clicks" => "targetSpend", "target_cpa" => "targetCpa", "target_roas" => "targetRoas", _ => throw new InvalidOperationException("Unsupported bidding strategy.") }] = channel.BiddingStrategy switch
            {
                "target_cpa" => new JsonObject { ["targetCpaMicros"] = Micros(channel.Target!.Value) },
                "target_roas" => new JsonObject { ["targetRoas"] = channel.Target!.Value },
                _ => new JsonObject(),
            };
            if (channel.CampaignType == "search") body["networkSettings"] = new JsonObject { ["targetGoogleSearch"] = true, ["targetSearchNetwork"] = false, ["targetContentNetwork"] = false };
            if (channel.CampaignType == "shopping") body["shoppingSetting"] = new JsonObject { ["merchantId"] = plan.MerchantAccountId, ["campaignPriority"] = 0, ["feedLabel"] = plan.Settings.SalesCountry };
            Add("campaign", campaignKey, body);
            foreach (var (region, negative) in plan.Regions.Select(region => (region, false)).Concat(plan.Holdouts.Select(region => (region, true))))
                Add("campaignCriterion", $"geo:{channel.CampaignType}:{region.Id}:{negative}", new JsonObject { ["campaign"] = campaign, ["negative"] = negative, ["location"] = new JsonObject { ["geoTargetConstant"] = $"geoTargetConstants/{region.Id}" } });
            foreach (var language in plan.Languages)
                Add("campaignCriterion", $"language:{channel.CampaignType}:{language}", new JsonObject { ["campaign"] = campaign, ["language"] = new JsonObject { ["languageConstant"] = $"languageConstants/{languages[language]}" } });
            switch (channel.CampaignType)
            {
                case "search": Search(campaign, campaignKey); break;
                case "shopping": Shopping(campaign, campaignKey); break;
                case "demand_gen": DemandGen(campaign, campaignKey); break;
            }
        }

        if (_operations.Count > 5000) throw new LaunchException("plan_too_large", "The campaign contains too many launch operations.", "Reduce the campaign's asset or targeting count and approve again.");
        return new CampaignMutations([.. _operations], [.. _keys], [.. _ads]);
    }

    private void Search(string campaign, string campaignKey)
    {
        foreach (var asset in plan.Assets.Where(asset => asset.Kind == "search_ad"))
        {
            var key = $"search:{asset.Id:N}";
            var group = Group(campaign, key, "SEARCH_STANDARD");
            var keywords = plan.Assets.Where(candidate => candidate.Kind == "keyword_set" && candidate.Language == asset.Language && candidate.AdGroup == asset.AdGroup);
            foreach (var keywordAsset in keywords)
            {
                if (!keywordAsset.Content.TryGetProperty("keywords", out var words)) continue;
                var index = 0;
                foreach (var keyword in words.EnumerateArray())
                    Add("adGroupCriterion", $"keyword:{asset.Id:N}:{keywordAsset.Id:N}:{index++}", new JsonObject { ["adGroup"] = group, ["status"] = "ENABLED", ["keyword"] = new JsonObject { ["text"] = LaunchPreflight.Text(keyword, "text"), ["matchType"] = LaunchPreflight.Text(keyword, "match").ToUpperInvariant() } });
            }

            var rsa = new JsonObject { ["headlines"] = Texts(asset.Content, "headlines"), ["descriptions"] = Texts(asset.Content, "descriptions") };
            foreach (var path in new[] { "path1", "path2" })
                if (asset.Content.TryGetProperty(path, out var value) && value.ValueKind == JsonValueKind.String) rsa[path] = value.GetString();
            Ad(key, campaignKey, "search", group, new JsonObject { ["finalUrls"] = Urls(asset), ["responsiveSearchAd"] = rsa }, asset.ProductIds);
            foreach (var image in plan.Assets.Where(image => image.Kind == "image" && image.Format is "landscape" or "square" && image.ProductIds.Intersect(asset.ProductIds).Any()))
                Add("adGroupAsset", $"search-image:{asset.Id:N}:{image.Id:N}", new JsonObject { ["adGroup"] = group, ["asset"] = media[image.Id.ToString("N")], ["fieldType"] = "AD_IMAGE" });
            Segments(group, key);
        }
    }

    private void Shopping(string campaign, string campaignKey)
    {
        foreach (var asset in plan.Assets.Where(asset => asset.Kind == "shopping_title"))
        {
            var key = $"shopping:{asset.Id:N}";
            var group = Group(campaign, key, "SHOPPING_PRODUCT_ADS");
            var root = $"customers/{Account}/adGroupCriteria/{group.Split('/')[^1]}~{_next--}";
            Add("adGroupCriterion", $"root:{key}", new JsonObject { ["resourceName"] = root, ["adGroup"] = group, ["listingGroup"] = new JsonObject { ["type"] = "SUBDIVISION" } });
            Add("adGroupCriterion", $"product:{key}", new JsonObject { ["adGroup"] = group, ["listingGroup"] = new JsonObject { ["type"] = "UNIT", ["parentAdGroupCriterion"] = root, ["caseValue"] = new JsonObject { ["productItemId"] = new JsonObject { ["value"] = LaunchPreflight.Text(asset.Content, "offerId") } } } });
            Add("adGroupCriterion", $"other:{key}", new JsonObject { ["adGroup"] = group, ["negative"] = true, ["listingGroup"] = new JsonObject { ["type"] = "UNIT", ["parentAdGroupCriterion"] = root, ["caseValue"] = new JsonObject { ["productItemId"] = new JsonObject() } } });
            Ad(key, campaignKey, "shopping", group, new JsonObject { ["shoppingProductAd"] = new JsonObject() }, asset.ProductIds);
            Segments(group, key);
        }
    }

    private void DemandGen(string campaign, string campaignKey)
    {
        var audience = Temporary("audiences");
        Add("audience", "audience:demand_gen", new JsonObject
        {
            ["resourceName"] = audience, ["name"] = Marker(plan.RoomId, "audience"),
            ["dimensions"] = new JsonArray(new JsonObject { ["audienceSegments"] = new JsonObject { ["segments"] = new JsonArray((plan.Settings.AudienceIds ?? []).Select(id => (JsonNode)new JsonObject { ["userList"] = new JsonObject { ["userList"] = $"customers/{Account}/userLists/{id}" } }).ToArray()) } }),
        });
        foreach (var text in plan.Assets.Where(asset => asset.Kind == "demand_gen_text"))
        {
            var key = $"demand_gen:{text.Id:N}";
            var group = Group(campaign, key, null, grouped: true);
            Add("adGroupCriterion", $"audience:{key}", new JsonObject { ["adGroup"] = group, ["audience"] = new JsonObject { ["audience"] = audience } });
            var imageAd = new JsonObject
            {
                ["headlines"] = Texts(text.Content, "headlines"), ["descriptions"] = Texts(text.Content, "descriptions"),
                ["businessName"] = LaunchPreflight.Text(text.Content, "businessName"), ["callToActionText"] = LaunchPreflight.Text(text.Content, "callToAction"),
                ["marketingImages"] = Images("landscape"), ["squareMarketingImages"] = Images("square"), ["portraitMarketingImages"] = Images("portrait"),
                ["logoImages"] = new JsonArray(new JsonObject { ["asset"] = media["logo"] }),
            };
            Ad(key, campaignKey, "demand_gen", group, new JsonObject { ["name"] = Marker(plan.RoomId, key), ["finalUrls"] = Urls(text), ["demandGenMultiAssetAd"] = imageAd }, text.ProductIds);
            var videos = plan.Assets.Where(asset => asset.Kind == "video" && asset.Language == text.Language).ToArray();
            var action = Temporary("assets");
            if (videos.Length > 0)
                Add("asset", $"action:{text.Id:N}", new JsonObject { ["resourceName"] = action, ["name"] = Marker(plan.RoomId, $"action:{text.Id:N}"),
                    ["callToActionAsset"] = new JsonObject { ["callToAction"] = LaunchPreflight.Text(text.Content, "callToAction").ToUpperInvariant() } });
            foreach (var video in videos)
            {
                var videoAd = new JsonObject
                {
                    ["headlines"] = Texts(text.Content, "headlines"), ["longHeadlines"] = Texts(text.Content, "longHeadlines"), ["descriptions"] = Texts(text.Content, "descriptions"),
                    ["businessName"] = new JsonObject { ["text"] = LaunchPreflight.Text(text.Content, "businessName") },
                    ["logoImages"] = new JsonArray(new JsonObject { ["asset"] = media["logo"] }),
                    ["videos"] = new JsonArray(new JsonObject { ["asset"] = media[video.Id.ToString("N")] }),
                    ["callToActions"] = new JsonArray(new JsonObject { ["asset"] = action }),
                };
                var videoKey = $"video:{text.Id:N}:{video.Id:N}";
                Ad(videoKey, campaignKey, "demand_gen", group, new JsonObject { ["name"] = Marker(plan.RoomId, videoKey), ["finalUrls"] = Urls(text), ["demandGenVideoResponsiveAd"] = videoAd }, video.ProductIds);
            }
        }
    }

    private string Group(string campaign, string key, string? type, bool grouped = false)
    {
        var group = Temporary("adGroups");
        var body = new JsonObject { ["resourceName"] = group, ["name"] = Marker(plan.RoomId, key), ["campaign"] = campaign, ["status"] = "ENABLED" };
        if (type is not null) body["type"] = type;
        if (grouped)
        {
            body["audienceSetting"] = new JsonObject { ["useAudienceGrouped"] = true };
            body["optimizedTargetingEnabled"] = false;
        }

        Add("adGroup", $"group:{key}", body);
        return group;
    }

    private void Segments(string group, string key)
    {
        foreach (var id in plan.Settings.AudienceIds ?? [])
            Add("adGroupCriterion", $"segment:{key}:{id}", new JsonObject { ["adGroup"] = group, ["userList"] = new JsonObject { ["userList"] = $"customers/{Account}/userLists/{id}" } });
    }

    private void Ad(string key, string campaignKey, string type, string group, JsonObject body, string[] products)
    {
        var ad = new JsonObject { ["adGroup"] = group, ["status"] = "ENABLED", ["ad"] = body };
        if (type == "demand_gen")
        {
            body["syntheticContentInfo"] = LaunchRunner.Attestation();
            ad["adGroupAdAssetAutomationSettings"] = new JsonArray(new JsonObject
            {
                ["assetAutomationType"] = "GENERATE_ANIMATED_IMAGES_FROM_OTHER_ASSETS", ["assetAutomationStatus"] = "OPTED_OUT",
            });
        }

        Add("adGroupAd", $"ad:{key}", ad);
        _ads.Add(new BuiltAd($"ad:{key}", campaignKey, type, products));
    }

    private void Add(string type, string key, JsonObject body)
    {
        _operations.Add(new JsonObject { [type + "Operation"] = new JsonObject { ["create"] = body } });
        _keys.Add(key);
    }

    private JsonArray Images(string format) => new(plan.Assets.Where(asset => asset.Kind == "image" && asset.Format == format).Select(asset => (JsonNode)new JsonObject { ["asset"] = media[asset.Id.ToString("N")] }).ToArray());
    private JsonArray Urls(LaunchAsset asset) => new((LaunchPreflight.Text(asset.Content, "finalUrl") is { Length: > 0 } url ? new[] { url } : plan.LandingPages.Take(1)).Select(url => JsonValue.Create(url)).ToArray());
    private static JsonArray Texts(JsonElement content, string property) => new(content.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Select(item => (JsonNode)new JsonObject { ["text"] = item.GetString() }).ToArray() : []);
    public static string Micros(decimal amount) => checked((long)decimal.Round(amount * 1_000_000m, 0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
