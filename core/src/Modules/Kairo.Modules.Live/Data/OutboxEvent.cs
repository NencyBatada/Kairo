using System.Text.Json;
using Kairo.Contracts.Live;

namespace Kairo.Modules.Live.Data;

internal sealed class OutboxEvent : IBrandOwned
{
    public long Id { get; set; }

    public Guid BrandId { get; set; }

    public string Type { get; set; } = "";

    public string Payload { get; set; } = "{}";

    public LiveAudienceKind AudienceKind { get; set; }

    public BrandRoles AudienceRoles { get; set; }

    public string? AudienceUsers { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public LiveAudience Audience() => AudienceKind switch
    {
        LiveAudienceKind.Roles => LiveAudience.Roles(AudienceRoles),
        LiveAudienceKind.Users => LiveAudience.Users(JsonSerializer.Deserialize<Guid[]>(AudienceUsers ?? "[]") ?? []),
        _ => LiveAudience.Everyone,
    };
}
