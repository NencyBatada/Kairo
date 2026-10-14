using System.Text.Json;
using Kairo.Modules.BrandKit.Kit;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed record DraftingRequest(string? WebsiteUrl, bool ReplaceDraft);

internal sealed record DraftRunPage(string Url, string Title);

internal sealed record DraftRunAd(string Id, string Headline);

internal sealed record DraftRunError(string Code, string Message);

internal sealed record DraftRunView(
    Guid Id,
    DraftRunStatus Status,
    string WebsiteUrl,
    DraftRunPage[] Pages,
    DraftRunAd[] Ads,
    DraftRunError? Error,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? FinishedAt,
    int? Version)
{
    public const string AuditTarget = "brand_kit_draft_run";

    public static DraftRunView Of(DraftRun run) => new(
        run.Id,
        run.Status,
        run.WebsiteUrl,
        JsonSerializer.Deserialize<DraftRunPage[]>(run.Pages, KitJson.Options) ?? [],
        JsonSerializer.Deserialize<DraftRunAd[]>(run.Ads, KitJson.Options) ?? [],
        run.ErrorCode is null ? null : new DraftRunError(run.ErrorCode, run.ErrorMessage ?? ""),
        run.RequestedBy,
        run.RequestedAt,
        run.FinishedAt,
        run.VersionNumber);
}
