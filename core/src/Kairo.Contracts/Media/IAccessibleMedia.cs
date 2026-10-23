namespace Kairo.Contracts.Media;

public sealed record MediaTarget(string Kind, Guid Id, int Version, Guid? CampaignId = null, Guid? ConversationId = null, string? Name = null);

public sealed record AccessibleMediaSource(string FileKey, string ContentType, string Language, bool CanEdit, decimal? DurationSeconds = null, Guid? CampaignId = null);

public interface IAccessibleMedia
{
    string Kind { get; }
    Task<AccessibleMediaSource?> ResolveAsync(MediaTarget target, CancellationToken cancellationToken);
}

public static class MediaAccessibilityLinks
{
    public static string For(Guid brandId, MediaTarget target) =>
        $"/api/brands/{brandId}/media-accessibility?kind={Uri.EscapeDataString(target.Kind)}&id={target.Id}&version={target.Version}"
        + (target.CampaignId is { } campaign ? $"&campaignId={campaign}" : "")
        + (target.ConversationId is { } conversation ? $"&conversationId={conversation}" : "")
        + (target.Name is { } name ? $"&name={Uri.EscapeDataString(name)}" : "");
}
