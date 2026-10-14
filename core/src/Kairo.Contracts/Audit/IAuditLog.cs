namespace Kairo.Contracts.Audit;

public interface IAuditLog
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

public sealed record AuditRecord(
    string Action,
    string TargetType,
    string TargetId,
    object? Before = null,
    object? After = null,
    AuditActor? Actor = null);

public enum AuditActorType
{
    User,
    Agent,
    System,
}

public sealed record AuditActor(AuditActorType Type, string Name, string? Id = null, string? Email = null)
{
    public static AuditActor Agent(string name, string? id = null) => new(AuditActorType.Agent, name, id);
}
