namespace Kairo.Contracts.Audit;

public interface IAuditLog
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

public sealed record AuditRecord(string Action, string TargetType, string TargetId, object? Before = null, object? After = null);
