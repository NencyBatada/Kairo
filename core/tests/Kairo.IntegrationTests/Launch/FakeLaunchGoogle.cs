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
    public string Currency { get; set; } = "INR";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public int UploadSessions { get; private set; }
    public int VideoUploads { get; private set; }
    public JsonElement? UploadMetadata { get; private set; }
    public bool VideoReady { get; set; } = true;

    public HttpResponseMessage? Respond(Uri uri, string body)
    {
        if (uri.Host == "googleads.googleapis.com" && uri.AbsolutePath.EndsWith("/googleAds:search", StringComparison.Ordinal))
        {
            using var json = JsonDocument.Parse(body);
            var query = json.RootElement.GetProperty("query").GetString()!;
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
        if (building && FailBuild || activating && FailActivation) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = new { message = "Fixture failure" } }) };
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
                foreach (var replacement in temporary) create = JsonNode.Parse(create.ToJsonString().Replace(replacement.Key, replacement.Value, StringComparison.Ordinal))!.AsObject();
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

        return Json(new { mutateOperationResponses = results });
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
