using Kairo.Contracts.Brands;
using Kairo.Contracts.Identity;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.Notifications.Delivery;

internal sealed class EmailDispatcher(
    NotificationsDbContext db,
    KairoDatabase database,
    IBrandMembers members,
    IBrandDirectory brands,
    IEmailSender sender,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<EmailDispatcher> logger)
{
    public const int BatchSize = 10;

    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var claimed = await ClaimAsync(cancellationToken);
            foreach (var id in claimed)
            {
                await SendAsync(id, cancellationToken);
            }

            return claimed.Count;
        }
    }

    private Task<List<long>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var leaseEnd = now + Lease;
        return db.Database.SqlQuery<long>($"""
            UPDATE TOP (10) notifications.OutboundEmails WITH (READPAST, UPDLOCK, ROWLOCK)
            SET ClaimedUntil = {leaseEnd}, Attempts = Attempts + 1
            OUTPUT inserted.Id AS Value
            WHERE Status = N'Pending' AND NextAttemptAt <= {now} AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
            """).ToListAsync(cancellationToken);
    }

    private async Task SendAsync(long id, CancellationToken cancellationToken)
    {
        var email = await db.OutboundEmails.Include(row => row.Notification).SingleAsync(row => row.Id == id, cancellationToken);
        var notification = email.Notification!;
        var kind = NotificationKinds.Find(notification.Kind);
        var member = await members.FindAsync(email.BrandId, email.UserId, cancellationToken);
        var workspace = (await brands.GetAsync([email.BrandId], cancellationToken)).SingleOrDefault();

        if (kind is null || member is null || workspace is null)
        {
            email.Status = EmailStatus.Skipped;
        }
        else
        {
            try
            {
                var message = EmailTemplate.Render(notification, kind, workspace, member, configuration[NotificationSettings.PublicOrigin] ?? "");
                var result = await sender.SendAsync(message, cancellationToken);
                email.Status = result == SendResult.Sent ? EmailStatus.Sent : EmailStatus.Logged;
                email.SentAt = time.GetUtcNow();
                email.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Fail(email, exception);
            }
        }

        email.ClaimedUntil = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Fail(OutboundEmail email, Exception exception)
    {
        var wait = exception is EmailSendException { MayPass: false } ? null : EmailRetries.After(email.Attempts);
        email.LastError = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
        if (wait is { } delay)
        {
            email.NextAttemptAt = time.GetUtcNow() + delay;
            logger.LogWarning(exception, "Notification email {EmailId} failed on attempt {Attempts}. Trying again in {Delay}.", email.Id, email.Attempts, delay);
            return;
        }

        email.Status = EmailStatus.Failed;
        logger.LogError(exception, "Gave up on notification email {EmailId} after {Attempts} attempts.", email.Id, email.Attempts);
    }
}
