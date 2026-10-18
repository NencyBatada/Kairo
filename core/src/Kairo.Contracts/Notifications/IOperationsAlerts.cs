namespace Kairo.Contracts.Notifications;

public interface IOperationsAlerts
{
    Task QueueAsync(OperationsAlert alert, CancellationToken cancellationToken);
}

public sealed record OperationsAlert(Guid EventId, string Kind, Guid? CampaignId = null, Guid? RunId = null, bool IsVerification = false);
