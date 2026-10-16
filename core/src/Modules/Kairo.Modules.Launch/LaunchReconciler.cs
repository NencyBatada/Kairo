using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Launch;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;

namespace Kairo.Modules.Launch;

internal sealed record BuildJournal(JsonElement[] Operations, string[] Keys, BuiltAd[] Ads);

internal sealed class LaunchReconciler(LaunchGoogle google, YouTubeUpload youtube, LaunchDbContext db)
{
    public async Task<bool> ReconcileAsync(LaunchRecord record, LaunchPlan plan, CancellationToken cancellationToken)
    {
        var step = record.AttemptingStep!;
        var resources = LaunchRunner.Read<Dictionary<string, string>>(record.Resources);
        if (step.StartsWith("upload-session:", StringComparison.Ordinal)) return false;
        if (step.StartsWith("upload:", StringComparison.Ordinal))
        {
            var key = step[7..];
            var asset = plan.Assets.Single(asset => asset.Id.ToString("N") == key);
            var uploads = LaunchRunner.Read<Dictionary<string, VideoUpload>>(record.Uploads);
            if (!uploads.TryGetValue(key, out var upload)) throw Unresolved();
            uploads[key] = await youtube.ReconcileAsync(plan, asset, upload, cancellationToken);
            record.Uploads = LaunchRunner.Write(uploads);
            return false;
        }

        if (step.StartsWith("media:", StringComparison.Ordinal))
        {
            var key = step[6..];
            var name = key == "logo" ? "logo" : $"asset {key} v{plan.Assets.Single(asset => asset.Id.ToString("N") == key).Version}";
            var rows = await google.SearchAsync(plan.GoogleAdsAccountId!, $"SELECT asset.resource_name, asset.name FROM asset WHERE asset.name = '{CampaignBuild.Marker(record.Id, name)}'", cancellationToken);
            if (rows.Length != 1) throw Unresolved();
            resources[step] = rows[0].GetProperty("asset").GetProperty("resourceName").GetString()!;
        }
        else if (step == "build")
        {
            if (record.PendingBuild is null) throw Unresolved();
            var journal = LaunchRunner.Read<BuildJournal>(record.PendingBuild);
            var temporary = new Dictionary<string, string>();
            var used = new HashSet<string>();
            for (var index = 0; index < journal.Operations.Length; index++)
            {
                var operation = journal.Operations[index].EnumerateObject().Single();
                var type = operation.Name[..^9];
                var expected = JsonNode.Parse(operation.Value.GetProperty("create").GetRawText())!.AsObject();
                Replace(expected, temporary);
                var table = Snake(type);
                var fields = Fields(type);
                var where = expected["name"] is { } name ? $"{table}.name = '{Escape(name.GetValue<string>())}'"
                    : expected["adGroup"] is { } group ? $"{table}.ad_group = '{Escape(group.GetValue<string>())}'"
                    : expected["campaign"] is { } campaign ? $"{table}.campaign = '{Escape(campaign.GetValue<string>())}'" : throw Unresolved();
                var rows = await google.SearchAsync(plan.GoogleAdsAccountId!, $"SELECT {string.Join(", ", fields.Select(field => table + "." + field))} FROM {table} WHERE {where}", cancellationToken);
                var matches = rows.Select(row => row.GetProperty(type)).Where(row => Matches(type, expected, row)
                    && !used.Contains(row.GetProperty("resourceName").GetString()!)).ToArray();
                if (matches.Length != 1) throw Unresolved();
                var resource = matches[0].GetProperty("resourceName").GetString()!;
                used.Add(resource);
                resources[journal.Keys[index]] = resource;
                if (operation.Value.GetProperty("create").TryGetProperty("resourceName", out var old)) temporary[old.GetString()!] = resource;
            }
            resources["built"] = "true";
            foreach (var ad in journal.Ads)
            foreach (var product in ad.ProductIds.Distinct())
                if (!await db.ProductAds.AnyAsync(row => row.RoomId == plan.RoomId && row.ProductId == product && row.AdResource == resources[ad.Key], cancellationToken))
                    db.ProductAds.Add(new ProductAdMap { Id = Guid.NewGuid(), RoomId = plan.RoomId, ProductId = product,
                        CampaignType = ad.CampaignType, CampaignResource = resources[ad.CampaignKey], AdResource = resources[ad.Key] });
        }
        else if (step == "activate")
        {
            var states = await CampaignStatesAsync(google, plan, resources, cancellationToken);
            if (states.All(status => status == "ENABLED")) return true;
            if (!states.All(status => status == "PAUSED")) throw Unresolved();
        }
        else throw Unresolved();
        record.Resources = LaunchRunner.Write(resources);
        return false;
    }

    public static async Task<string[]> CampaignStatesAsync(LaunchGoogle google, LaunchPlan plan, Dictionary<string, string> resources, CancellationToken cancellationToken)
    {
        var names = plan.Settings.Channels.Select(channel => resources.GetValueOrDefault("campaign:" + channel.CampaignType)).ToArray();
        if (names.Any(string.IsNullOrEmpty)) throw Unresolved();
        var rows = await google.SearchAsync(plan.GoogleAdsAccountId!, $"SELECT campaign.resource_name, campaign.status FROM campaign WHERE campaign.resource_name IN ({string.Join(',', names.Select(name => "'" + Escape(name!) + "'"))})", cancellationToken);
        if (rows.Length != names.Length || !rows.Select(row => row.GetProperty("campaign").GetProperty("resourceName").GetString()).ToHashSet().SetEquals(names)) throw Unresolved();
        return rows.Select(row => row.GetProperty("campaign").GetProperty("status").GetString()!).ToArray();
    }

    private static bool Matches(string type, JsonObject expected, JsonElement actual)
    {
        if (type == "adGroupAd")
        {
            var ad = expected["ad"]!.AsObject();
            var kind = ad.First(pair => pair.Key.EndsWith("Ad", StringComparison.Ordinal)).Key;
            return Text(actual, "adGroup") == expected["adGroup"]!.GetValue<string>()
                && Text(actual.GetProperty("ad"), "type") == Snake(kind).ToUpperInvariant()
                && (ad["name"] is null || Text(actual.GetProperty("ad"), "name") == ad["name"]!.GetValue<string>());
        }
        foreach (var property in expected)
        {
            if (property.Key == "resourceName" || !Fields(type).Any(field => field.Split('.')[0] == Snake(property.Key))) continue;
            if (!Subset(property.Value, actual.TryGetProperty(property.Key, out var value) ? value : default)) return false;
        }
        return true;
    }

    private static bool Subset(JsonNode? expected, JsonElement actual)
    {
        if (expected is JsonObject obj) return obj.All(pair => Subset(pair.Value, actual.ValueKind == JsonValueKind.Object && actual.TryGetProperty(pair.Key, out var value) ? value : default));
        if (expected is null) return true;
        var valueText = expected.ToString();
        if (actual.ValueKind == JsonValueKind.Undefined) return valueText is "false" or "0" or "";
        return actual.ToString().Equals(valueText, StringComparison.OrdinalIgnoreCase);
    }

    private static void Replace(JsonNode node, Dictionary<string, string> temporary)
    {
        if (node is JsonObject obj)
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
                if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && temporary.TryGetValue(text, out var replacement)) obj[key] = replacement;
                else if (obj[key] is { } child) Replace(child, temporary);
        if (node is JsonArray array) foreach (var child in array.OfType<JsonNode>()) Replace(child, temporary);
    }

    private static string[] Fields(string type) => type switch
    {
        "campaignBudget" => ["resource_name", "name", "period", "total_amount_micros"],
        "campaign" => ["resource_name", "name", "status", "campaign_budget", "advertising_channel_type"],
        "adGroup" => ["resource_name", "name", "campaign", "status", "type"],
        "campaignCriterion" => ["resource_name", "campaign", "negative", "location.geo_target_constant", "language.language_constant"],
        "adGroupCriterion" => ["resource_name", "ad_group", "negative", "keyword.text", "keyword.match_type", "listing_group.type", "listing_group.parent_ad_group_criterion", "listing_group.case_value.product_item_id.value", "audience.audience", "user_list.user_list"],
        "adGroupAd" => ["resource_name", "ad_group", "ad.name", "ad.type"],
        "adGroupAsset" => ["resource_name", "ad_group", "asset", "field_type"],
        "audience" => ["resource_name", "name"],
        "asset" => ["resource_name", "name", "call_to_action_asset.call_to_action"],
        _ => throw Unresolved(),
    };

    private static string Text(JsonElement value, string property) => LaunchPreflight.Text(value, property);
    private static string Snake(string value) => string.Concat(value.Select((character, index) => char.IsUpper(character) && index > 0 ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    public static LaunchException Unresolved() => new("reconciliation_required", "Google's last operation couldn't be confirmed uniquely.", "Inspect the recorded Google resources and resolve the uncertain operation before continuing.", uncertain: true);
}
