using System.Diagnostics;
using System.Net.Mail;
using System.Text.Json;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Notifications.Data;

namespace Kairo.Modules.Notifications.Publishing;

internal sealed class OperationsAlerts(NotificationsDbContext db, KairoDatabase database, IConfiguration configuration, TimeProvider time) : IOperationsAlerts
{
    public const string RecipientSetting = "Kairo:Operations:Email";
    private static readonly HashSet<string> Kinds = ["launch.failed", "stage.failed_twice", "mass_pause.held", "feed.stale", "connection.expired"];

    public async Task QueueAsync(OperationsAlert alert, CancellationToken cancellationToken)
    {
        if (alert.EventId == Guid.Empty || !Kinds.Contains(alert.Kind)) throw new ArgumentException("Unknown operations event.", nameof(alert));
        var recipient = configuration[RecipientSetting];
        if (string.IsNullOrWhiteSpace(recipient)) return;
        if (recipient.Length > 320 || !MailAddress.TryCreate(recipient, out var address) || !string.Equals(address.Address, recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Operations email needs a valid mailbox address.");
        var brand = database.BrandId ?? throw new InvalidOperationException("Operations alerts need a workspace.");
        var key = $"{alert.Kind}:{alert.EventId:N}";
        var marker = $"\"key\":\"{key}\"";
        var own = !database.HasTransaction;
        if (own) await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync($"operations:{brand:N}:{key}", cancellationToken);
            if (!await db.OutboundEmails.AnyAsync(row => row.BrandId == brand && row.Type == EmailType.Operations && row.Payload != null && row.Payload.Contains(marker), cancellationToken))
            {
                var now = time.GetUtcNow();
                db.OutboundEmails.Add(new OutboundEmail
                {
                    Type = EmailType.Operations,
                    Address = recipient,
                    Payload = JsonSerializer.Serialize(new OperationsDetails(key, alert.Kind, brand, alert.CampaignId, alert.RunId, Activity.Current?.TraceId.ToString(), alert.IsVerification), JsonSerializerOptions.Web),
                    Status = EmailStatus.Pending,
                    NextAttemptAt = now,
                    CreatedAt = now,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            if (own) await database.CommitAsync(cancellationToken);
        }
        catch
        {
            if (own)
            {
                await database.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
            throw;
        }
    }
}

internal sealed record OperationsDetails(string Key, string Kind, Guid BrandId, Guid? CampaignId, Guid? RunId, string? TraceId, bool IsVerification = false)
{
    public static OperationsDetails Read(string? payload) =>
        JsonSerializer.Deserialize<OperationsDetails>(payload ?? "", JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The operations email has no details.");
}
