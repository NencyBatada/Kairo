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

    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        await using (await database.EnterSystemScopeAsync(cancellationToken))
        {
            var lease = time.GetUtcNow() + Lease;
            var claimed = await ClaimAsync(lease, cancellationToken);
            foreach (var id in claimed)
            {
                await SendAsync(id, lease, cancellationToken);
            }

            return claimed.Count;
        }
    }

    private Task<List<long>> ClaimAsync(DateTimeOffset lease, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Database.SqlQuery<long>($"""
            UPDATE TOP (10) notifications.OutboundEmails WITH (READPAST, UPDLOCK, ROWLOCK)
            SET ClaimedUntil = {lease}, Attempts = Attempts + 1
            OUTPUT inserted.Id AS Value
            WHERE Status = N'Pending' AND NextAttemptAt <= {now} AND (ClaimedUntil IS NULL OR ClaimedUntil < {now})
            """).ToListAsync(cancellationToken);
    }

    private async Task SendAsync(long id, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() + (SendTimeout * 2) > lease)
        {
            await Ours(id, lease)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.ClaimedUntil, (DateTimeOffset?)null)
                    .SetProperty(row => row.Attempts, row => row.Attempts - 1), cancellationToken);
            return;
        }

        var email = await db.OutboundEmails.AsNoTracking().Include(row => row.Notification).SingleAsync(row => row.Id == id, cancellationToken);
        if (email.ClaimedUntil != lease)
        {
            return;
        }

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

        var recorded = await Ours(id, lease)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, email.Status)
                .SetProperty(row => row.SentAt, email.SentAt)
                .SetProperty(row => row.LastError, email.LastError)
                .SetProperty(row => row.NextAttemptAt, email.NextAttemptAt)
                .SetProperty(row => row.ClaimedUntil, (DateTimeOffset?)null), email.SentAt is null ? cancellationToken : CancellationToken.None);
        if (recorded == 0)
        {
            logger.LogWarning("Notification email {EmailId} was claimed by another sender before its result was recorded.", id);
        }
    }

    private IQueryable<OutboundEmail> Ours(long id, DateTimeOffset lease) =>
        db.OutboundEmails.Where(row => row.Id == id && row.ClaimedUntil == lease);

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
