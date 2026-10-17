using System.Text.Json;
using Kairo.Contracts.Ask;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Live;

namespace Kairo.Modules.BrandKit.Kit;

internal sealed class KitAskResource(KitVersions versions, IAuditLog audit, ILiveUpdates live) : IAskResource
{
    public string Kind => "brand_kit";
    public BrandRoles Editors => BrandRoles.Admin;
    public async Task<AskDocument?> ReadAsync(AskTarget target, CancellationToken ct)
    {
        var draft = await versions.DraftAsync(ct);
        var approved = await versions.ApprovedAsync(ct);
        var content = KitJson.Read(draft?.Content ?? approved?.Content ?? "{}");
        return new("Brand Kit", "brand-kit", AskJson.Element(content), AskJson.Revision(new { draft = draft?.Revision, approved = approved?.Revision }));
    }
    public async Task<AskChange> PreviewAsync(AskTarget target, JsonElement changes, CancellationToken ct)
    {
        var before = (await ReadAsync(target, ct))!.Data;
        var merged = AskJson.Merge(before, changes, "voice", "colours", "logo", "keyMessages", "productCategories", "rules", "guidelineFileIds");
        var check = KitContentRules.Check(AskJson.Read<KitContent>(merged));
        if (!check.IsValid) throw new AskResourceException(JsonSerializer.Serialize(check.Errors), 400);
        var files = await versions.FileProblemsAsync(check.Content, ct);
        if (files.Count > 0) throw new AskResourceException(JsonSerializer.Serialize(files), 400);
        var payload = new SaveDraftRequest(check.Content, (await versions.DraftAsync(ct))?.Revision ?? 0);
        return new(before, AskJson.Element(check.Content), AskJson.Element(payload), ["Saves a draft. The approved Brand Kit stays in use until separately approved."]);
    }
    public async Task ApplyAsync(AskTarget target, string revision, JsonElement payload, CancellationToken ct)
    {
        await versions.LockAsync(ct);
        AskJson.Unchanged(revision, await ReadAsync(target, ct));
        AskJson.Result(await KitEndpoints.SaveDraftAsync(AskJson.Read<SaveDraftRequest>(payload), versions, audit, live, ct));
    }
}
