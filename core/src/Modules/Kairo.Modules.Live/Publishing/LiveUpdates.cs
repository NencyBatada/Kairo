using System.Text.Json;
using System.Text.RegularExpressions;
using Kairo.Contracts.Live;
using Kairo.Modules.Live.Data;

namespace Kairo.Modules.Live.Publishing;

internal sealed partial class LiveUpdates(LiveDbContext db, TimeProvider time) : ILiveUpdates
{
    public async Task PublishAsync(LiveEvent liveEvent, CancellationToken cancellationToken)
    {
        if (!EventType().IsMatch(liveEvent.Type))
        {
            throw new ArgumentException($"'{liveEvent.Type}' isn't a lowercase, dotted event type.", nameof(liveEvent));
        }

        var audience = liveEvent.Audience;
        db.Outbox.Add(new OutboxEvent
        {
            Type = liveEvent.Type,
            Payload = JsonSerializer.Serialize(liveEvent.Payload, JsonSerializerOptions.Web),
            AudienceKind = audience.Kind,
            AudienceRoles = audience.RoleFilter,
            AudienceUsers = audience.Kind == LiveAudienceKind.Users ? JsonSerializer.Serialize(audience.UserFilter) : null,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    [GeneratedRegex("^[a-z_]+(\\.[a-z_]+)+$")]
    private static partial Regex EventType();
}
