using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kairo.IntegrationTests.Launch;

public sealed class FakeLaunchGoogle
{
    private long _next = 100;
    public List<JsonElement> Mutations { get; } = [];
    public Dictionary<string, JsonObject> Resources { get; } = [];
    public bool MerchantLinked { get; set; } = true;
    public bool FailBuild { get; set; }
    public bool FailActivation { get; set; }
    public bool LoseBuildResponse { get; set; }
    public bool LoseActivationResponse { get; set; }
    public bool TimeoutBuild { get; set; }
    public bool HideResources { get; set; }
    public int TransientReads { get; set; }
    public bool Disapproved { get; set; }
    public long Impressions { get; set; } = 10;
    public string Currency { get; set; } = "INR";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public int UploadSessions { get; private set; }
    public int VideoUploads { get; private set; }
    public JsonElement? UploadMetadata { get; private set; }
    public bool VideoReady { get; set; } = true;
    public bool InterruptUpload { get; set; }
    public bool LoseUploadResponse { get; set; }
    public int UploadStatusQueries { get; private set; }
    public long LastUploadOffset { get; private set; }
    private bool _uploaded;

    public HttpResponseMessage? RespondRequest(HttpRequestMessage request, string body)
    {
        if (request.RequestUri!.AbsolutePath != "/upload-session/1") return null;
        var range = request.Content!.Headers.ContentRange;
        if (range is { HasRange: false })
        {
            UploadStatusQueries++;
            if (_uploaded) return Json(new { id = "video-1" });
            var pending = new HttpResponseMessage((HttpStatusCode)308);
            pending.Headers.TryAddWithoutValidation("Range", "bytes=0-1");
            return pending;
        }
        LastUploadOffset = range?.From ?? 0;
        VideoUploads++;
        if (InterruptUpload) { InterruptUpload = false; throw new HttpRequestException("Interrupted upload after two bytes."); }
        _uploaded = true;
        if (LoseUploadResponse) { LoseUploadResponse = false; throw new HttpRequestException("Lost upload completion response."); }
        return Json(new { id = "video-1" });
    }

    public HttpResponseMessage? Respond(Uri uri, string body)
    {
        if (uri.Host == "googleads.googleapis.com" && uri.AbsolutePath.EndsWith("/googleAds:search", StringComparison.Ordinal))
        {
            using var json = JsonDocument.Parse(body);
            var query = json.RootElement.GetProperty("query").GetString()!;
            if (TransientReads > 0) { TransientReads--; return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }
            if (query.Contains("customer.currency_code", StringComparison.Ordinal)) return Json(new { results = new[] { new { customer = new { id = "1111111111", currencyCode = Currency, timeZone = TimeZone } } } });
            if (query.Contains("FROM geo_target_constant", StringComparison.Ordinal)) return Json(new
            {
                results = new[] { ("1007785", "Mumbai"), ("1007788", "Pune"), ("1007790", "Nagpur"), ("1007791", "Nashik"), ("1007809", "Chennai") }
                    .Select(region => new { geoTargetConstant = new { id = region.Item1, name = region.Item2, status = "ENABLED" } }),
            });
            if (query.Contains("FROM language_constant", StringComparison.Ordinal)) return Json(new { results = new[] { new { languageConstant = new { id = "1000", code = "en" } }, new { languageConstant = new { id = "1023", code = "hi" } } } });
            if (query.Contains("FROM product_link", StringComparison.Ordinal)) return Json(new { results = MerchantLinked ? new object[] { new { productLink = new { merchantCenter = new { merchantCenterId = "555" } } } } : [] });
            if (query.Contains("FROM conversion_action", StringComparison.Ordinal)) return Json(new { results = new[] { new { conversionAction = new { id = "42" } } } });
            if (query.Contains("FROM user_list", StringComparison.Ordinal)) return Json(new { results = new[] { new { userList = new { id = "7", membershipStatus = "OPEN" } } } });
            return Lookup(query);
        }

        if (uri.Host == "googleads.googleapis.com" && uri.AbsolutePath.EndsWith("/googleAds:mutate", StringComparison.Ordinal)) return Mutate(body);
        if (uri.AbsolutePath == "/upload/youtube/v3/videos" && uri.Query.Contains("uploadType=resumable", StringComparison.Ordinal))
        {
            UploadSessions++;
            UploadMetadata = JsonDocument.Parse(body).RootElement.Clone();
            var response = Json(new { });
            response.Headers.Location = new Uri("https://www.googleapis.com/upload-session/1");
            return response;
        }

        if (uri.AbsolutePath == "/upload-session/1")
        {
            VideoUploads++;
            return Json(new { id = "video-1" });
        }

        if (uri.AbsolutePath == "/youtube/v3/videos") return Json(new { items = new[] { new { id = "video-1", snippet = new { channelId = "UC-acme" }, status = new { privacyStatus = "unlisted" }, processingDetails = new { processingStatus = VideoReady ? "succeeded" : "processing" } } } });
        return null;
    }

    private HttpResponseMessage Mutate(string body)
    {
        var json = JsonDocument.Parse(body).RootElement.Clone();
        Mutations.Add(json);
        var operations = json.GetProperty("mutateOperations").EnumerateArray().ToArray();
        var building = operations.Any(operation => operation.TryGetProperty("campaignOperation", out var op) && op.TryGetProperty("create", out _));
        var activating = operations.Any(operation => operation.TryGetProperty("campaignOperation", out var op) && op.TryGetProperty("update", out _));
        if (building && FailBuild || activating && FailActivation) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = new { message = "Fixture failure", details = new[] { new { errors = new[] { new { errorCode = new { policyFindingError = "POLICY_FINDING" } } } } } } }) };
        if (json.GetProperty("validateOnly").GetBoolean()) return Json(new { });
        var temporary = new Dictionary<string, string>();
        var results = new List<object>();
        foreach (var operation in operations)
        {
            var kind = operation.EnumerateObject().Single();
            var type = kind.Name[..^"Operation".Length];
            string name;
            if (kind.Value.TryGetProperty("update", out var update))
            {
                name = update.GetProperty("resourceName").GetString()!;
                Resources[name]["status"] = update.GetProperty("status").GetString();
            }
            else
            {
                var create = JsonNode.Parse(kind.Value.GetProperty("create").GetRawText())!.AsObject();
                var old = create["resourceName"]?.GetValue<string>();
                Replace(create, temporary);
                var collection = type switch { "campaignBudget" => "campaignBudgets", "adGroupCriterion" => "adGroupCriteria", "campaignCriterion" => "campaignCriteria", "adGroupAsset" => "adGroupAssets", _ => type + "s" };
                name = $"customers/1111111111/{collection}/{_next++}";
                if (type == "adGroupAd") name = $"customers/1111111111/adGroupAds/{create["adGroup"]!.GetValue<string>().Split('/')[^1]}~{_next++}";
                create["resourceName"] = name;
                if (old is not null) temporary[old] = name;
                Resources[name] = create;
            }

            results.Add(new Dictionary<string, object> { [type + "Result"] = new { resourceName = name } });
        }

        if (building && LoseBuildResponse)
        {
            LoseBuildResponse = false;
            throw new HttpRequestException("The response was lost after commit.");
        }
        if (building && TimeoutBuild) { TimeoutBuild = false; throw new TaskCanceledException("Simulated HTTP timeout after commit."); }
        if (activating && LoseActivationResponse)
        {
            LoseActivationResponse = false;
            throw new HttpRequestException("The activation response was lost after commit.");
        }

        return Json(new { mutateOperationResponses = results });
    }

    private HttpResponseMessage Lookup(string query)
    {
        var table = System.Text.RegularExpressions.Regex.Match(query, @"FROM (\w+)").Groups[1].Value;
        var type = table switch { "campaign_budget" => "campaignBudget", "ad_group" => "adGroup", "ad_group_ad" => "adGroupAd", "campaign_criterion" => "campaignCriterion", "ad_group_criterion" => "adGroupCriterion", "ad_group_asset" => "adGroupAsset", _ => table };
        var collection = type switch { "campaignBudget" => "campaignBudgets", "adGroupCriterion" => "adGroupCriteria", "campaignCriterion" => "campaignCriteria", "adGroupAsset" => "adGroupAssets", _ => type + "s" };
        var name = System.Text.RegularExpressions.Regex.Match(query, @"\.name = '([^']+)'").Groups[1].Value;
        var group = System.Text.RegularExpressions.Regex.Match(query, @"\.ad_group = '([^']+)'").Groups[1].Value;
        var campaign = System.Text.RegularExpressions.Regex.Match(query, @"\.campaign = '([^']+)'").Groups[1].Value;
        var rows = new List<object>();
        if (HideResources) return Json(new { results = rows });
        foreach (var pair in Resources.Where(pair => pair.Key.Contains("/" + collection + "/", StringComparison.Ordinal)))
        {
            var value = pair.Value.DeepClone().AsObject();
            if (name.Length > 0 && value["name"]?.ToString() != name || group.Length > 0 && value["adGroup"]?.ToString() != group || campaign.Length > 0 && value["campaign"]?.ToString() != campaign) continue;
            if (query.Contains("campaign.resource_name IN", StringComparison.Ordinal) && type == "campaign" && !query.Contains("'" + pair.Key + "'", StringComparison.Ordinal)) continue;
            if (type == "adGroupAd")
            {
                var ad = value["ad"]!.AsObject();
                var kind = ad.First(property => property.Key.EndsWith("Ad", StringComparison.Ordinal)).Key;
                ad["type"] = System.Text.RegularExpressions.Regex.Replace(kind, "([a-z])([A-Z])", "$1_$2").ToUpperInvariant();
                if (query.Contains("policy_summary", StringComparison.Ordinal)) value["policySummary"] = JsonSerializer.SerializeToNode(new { approvalStatus = Disapproved ? "DISAPPROVED" : "APPROVED", policyTopicEntries = Disapproved ? new[] { new { topic = "DESTINATION_NOT_WORKING" } } : [] });
            }
            var row = new Dictionary<string, object> { [type] = value };
            if (query.Contains("metrics.impressions", StringComparison.Ordinal)) row["metrics"] = new { impressions = Impressions.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            rows.Add(row);
        }
        return Json(new { results = rows });
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static void Replace(JsonNode node, Dictionary<string, string> temporary)
    {
        if (node is JsonObject obj)
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && temporary.TryGetValue(text, out var replacement)) obj[key] = replacement;
                else if (obj[key] is { } child) Replace(child, temporary);
        if (node is JsonArray array) foreach (var child in array.OfType<JsonNode>()) Replace(child, temporary);
    }
}
