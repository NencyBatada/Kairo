using Kairo.Contracts.Audit;
using Kairo.Modules.Notifications.Data;
using Kairo.Modules.Notifications.Kinds;

namespace Kairo.Modules.Notifications.EmailSettings;

internal static class EmailSettingsEndpoints
{
    public static void MapEmailSettings(this IEndpointRouteBuilder api)
    {
        var settings = api.MapBrandApi().MapGroup("/notifications/email-settings");
        settings.MapGet("", GetAsync);
        settings.MapPut("", SaveAsync);
    }

    private static async Task<Ok<EmailSettings>> GetAsync(
        CurrentMember member,
        NotificationsDbContext db,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await LoadAsync(member, db, cancellationToken));

    private static async Task<Results<Ok<EmailSettings>, ValidationProblem>> SaveAsync(
        SaveEmailSettingsRequest request,
        CurrentMember member,
        NotificationsDbContext db,
        IAuditLog audit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var changes = request.Kinds ?? [];
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < changes.Count; i++)
        {
            var kind = NotificationKinds.Find(changes[i].Kind);
            if (kind is null)
            {
                errors[$"kinds[{i}].kind"] = [$"'{changes[i].Kind}' isn't a notification kind."];
            }
            else if (!kind.Email)
            {
                errors[$"kinds[{i}].kind"] = [$"{kind.Name} is shown only in the app, so it has no email to turn on or off."];
            }
            else if (!kind.CanTurnOff && !changes[i].Email)
            {
                errors[$"kinds[{i}].email"] = [$"{kind.Name} emails can't be turned off."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var before = await LoadAsync(member, db, cancellationToken);
        var optOuts = await db.EmailOptOuts
            .Where(optOut => optOut.UserId == member.UserId)
            .ToListAsync(cancellationToken);
        foreach (var change in changes.GroupBy(change => change.Kind).Select(group => group.Last()))
        {
            var existing = optOuts.SingleOrDefault(optOut => optOut.Kind == change.Kind);
            if (change.Email && existing is not null)
            {
                db.EmailOptOuts.Remove(existing);
            }
            else if (!change.Email && existing is null)
            {
                db.EmailOptOuts.Add(new EmailOptOut { UserId = member.UserId, Kind = change.Kind, CreatedAt = time.GetUtcNow() });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var after = await LoadAsync(member, db, cancellationToken);
        await audit.RecordAsync(
            new AuditRecord("notifications.email_settings_changed", "user", member.UserId.ToString(), before, after),
            cancellationToken);
        return TypedResults.Ok(after);
    }

    private static async Task<EmailSettings> LoadAsync(CurrentMember member, NotificationsDbContext db, CancellationToken cancellationToken)
    {
        var off = (await db.EmailOptOuts
                .AsNoTracking()
                .Where(optOut => optOut.UserId == member.UserId)
                .Select(optOut => optOut.Kind)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var kinds = NotificationKinds.All
            .Where(kind => kind.Email && member.Has(kind.UsualRecipients))
            .Select(kind => new EmailSetting(kind.Key, kind.Name, kind.Description, !kind.CanTurnOff || !off.Contains(kind.Key), kind.CanTurnOff))
            .ToList();
        return new EmailSettings(kinds);
    }
}

internal sealed record EmailSetting(string Kind, string Name, string Description, bool Email, bool CanTurnOff);

internal sealed record EmailSettings(IReadOnlyList<EmailSetting> Kinds);

internal sealed record EmailSettingChange(string Kind, bool Email);

internal sealed record SaveEmailSettingsRequest(IReadOnlyList<EmailSettingChange>? Kinds);
