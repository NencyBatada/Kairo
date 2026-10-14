namespace Kairo.Modules.Audit.Data;

internal sealed class AuditEntry : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string ActorType { get; set; } = "";

    public string? ActorId { get; set; }

    public string ActorName { get; set; } = "";

    public string? ActorEmail { get; set; }

    public string Action { get; set; } = "";

    public string TargetType { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string? Before { get; set; }

    public string? After { get; set; }

    public string? TraceId { get; set; }
}
