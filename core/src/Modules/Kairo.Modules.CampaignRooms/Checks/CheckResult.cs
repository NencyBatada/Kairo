using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kairo.Modules.CampaignRooms.Checks;

internal sealed class CheckResult : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public Guid AssetId { get; set; }

    public int AssetVersion { get; set; }

    public int Score { get; set; }

    public bool Passed { get; set; }

    public string Issues { get; set; } = "[]";

    public int SettingsVersion { get; set; }

    public Guid? StageRunId { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int FailedFixes { get; set; }

    public Guid? OverriddenBy { get; set; }

    public DateTimeOffset? OverriddenAt { get; set; }

    public string? OverrideReason { get; set; }

    public List<CheckIssue> ReadIssues() => CheckIssue.Parse(Issues);
}

internal sealed class CheckIssue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Rule { get; set; } = "";

    public string RuleName { get; set; } = "";

    public string Severity { get; set; } = CheckSeverities.Block;

    public string Message { get; set; } = "";

    public string? Field { get; set; }

    public int? Index { get; set; }

    public double? At { get; set; }

    public string? Evidence { get; set; }

    public bool Overridable { get; set; } = true;

    public static List<CheckIssue> Parse(string json) => JsonSerializer.Deserialize<List<CheckIssue>>(json, Json) ?? [];

    public static string Write(IEnumerable<CheckIssue> issues) => JsonSerializer.Serialize(issues.ToList(), Json);
}

internal static class CheckResultLimits
{
    public const int Message = 300;
    public const int Evidence = 200;
    public const int MaxIssues = 40;
    public const int MinReason = 3;
    public const int MaxReason = 500;
    public const int MaxSendBack = 50;
    public const int MaxChecksPerEntry = 20;
}
