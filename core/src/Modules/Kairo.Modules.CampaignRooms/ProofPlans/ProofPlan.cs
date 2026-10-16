using System.Text.Json;

namespace Kairo.Modules.CampaignRooms.ProofPlans;

internal sealed class ProofPlan : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public int Version { get; set; }

    public int BriefVersion { get; set; }

    public Guid? StageRunId { get; set; }

    public string? Method { get; set; }

    public string Content { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    public bool ChangedAfterLock { get; set; }

    public bool ApprovedWithWarnings { get; set; }

    public string? ChangeReason { get; set; }

    public Guid? ChangedBy { get; set; }

    public ProofPlanContent Read() => JsonSerializer.Deserialize<ProofPlanContent>(Content, JsonSerializerOptions.Web) ?? new ProofPlanContent();

    public void Write(ProofPlanContent content) => Content = JsonSerializer.Serialize(content, JsonSerializerOptions.Web);
}

internal sealed class ProofPlanContent
{
    public string Unit { get; set; } = ProofUnits.City;

    public string Kpi { get; set; } = "orders";

    public bool InsideArea { get; set; } = true;

    public bool ChosenByUser { get; set; }

    public List<PlanRegion> TestRegions { get; set; } = [];

    public List<PlanRegion> Holdouts { get; set; } = [];

    public List<PlanRegion> Candidates { get; set; } = [];

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? WindowDays { get; set; }

    public double? MinDetectableLift { get; set; }

    public List<PlanLift> Lifts { get; set; } = [];

    public int PlaceboWindows { get; set; }

    public double? HoldoutShare { get; set; }

    public decimal? HoldoutCost { get; set; }

    public string? Currency { get; set; }

    public int DataDays { get; set; }

    public List<PlanWarning> Warnings { get; set; } = [];
}

internal sealed record PlanRegion(
    string Id,
    string Name,
    string? State,
    double? Latitude,
    double? Longitude,
    bool InArea,
    double? Score = null,
    double? Share = null);

internal sealed record PlanLift(int Days, double? Lift, int Placebos);

internal sealed record PlanWarning(string Code, string Message);

internal static class ProofUnits
{
    public const string City = "city";
    public const string State = "state";
}

internal sealed record HoldoutChoice(string[] HoldoutIds);
