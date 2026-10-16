namespace Kairo.Modules.Measurement.Lift;

internal sealed class LiftReading : IBrandOwned
{
    public Guid Id { get; set; }

    public Guid BrandId { get; set; }

    public Guid RoomId { get; set; }

    public bool Early { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly ThroughDate { get; set; }

    public string Kpi { get; set; } = "";

    public string Unit { get; set; } = "";

    public int Days { get; set; }

    public int PlannedDays { get; set; }

    public string Status { get; set; } = "";

    public string? Reason { get; set; }

    public bool Conclusive { get; set; }

    public double? Lift { get; set; }

    public double? Low { get; set; }

    public double? High { get; set; }

    public double? Actual { get; set; }

    public double? Expected { get; set; }

    public double? Extra { get; set; }

    public double? ExtraLow { get; set; }

    public double? ExtraHigh { get; set; }

    public int Placebos { get; set; }

    public double? Spread { get; set; }

    public string? Revenue { get; set; }

    public string Regions { get; set; } = "[]";

    public string Daily { get; set; } = "[]";

    public string Method { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

internal static class LiftStatuses
{
    public const string Ok = "ok";
    public const string InsufficientData = "insufficient_data";
}

internal static class LiftReasons
{
    public const string NoTestDays = "no_test_days";
    public const string NoData = "no_data";
}
