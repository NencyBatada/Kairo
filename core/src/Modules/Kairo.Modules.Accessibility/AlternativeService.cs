using System.Security.Cryptography;
using System.Text.Json;
using Kairo.Contracts.Audit;
using Kairo.Contracts.Media;
using Kairo.Modules.Accessibility.Data;
using Kairo.SharedKernel.Files;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.Accessibility;

internal sealed class AlternativeException(string message, int status = 409, string code = "media_alternative_conflict") : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal sealed class AlternativeService(AccessibilityDbContext db, KairoDatabase database, IEnumerable<IAccessibleMedia> sources,
    IFileStore files, CurrentMember member, IAuditLog audit, TimeProvider time, IConfiguration configuration,
    IServiceProvider services, AgentClient agent, AgentUsageContext usage)
{
    private bool DraftingEnabled => configuration.GetValue<bool>("Kairo:Accessibility:AiDraftingEnabled")
        && services.GetService<IAgentInvocations>() is not null;

    public async Task<(AccessibleMediaSource Source, string Hash)> SourceAsync(MediaTarget target, bool edit, CancellationToken ct)
    {
        if (target is null || string.IsNullOrEmpty(target.Kind) || target.Id == Guid.Empty || target.Version < 1)
            throw new AlternativeException("Choose an available media version.", 400, "invalid_media_target");
        if (target.Kind == "asset" && (target.CampaignId is null || target.CampaignId == Guid.Empty || target.ConversationId is not null || target.Name is not null)
            || target.Kind == "ask_preview" && (target.ConversationId is null || target.ConversationId == Guid.Empty || target.CampaignId is not null || string.IsNullOrEmpty(target.Name)))
            throw new AlternativeException("Use this item's original media reference.", 400, "invalid_media_target");
        var source = await (sources.SingleOrDefault(source => source.Kind == target.Kind)?.ResolveAsync(target, ct)
            ?? Task.FromResult<AccessibleMediaSource?>(null));
        if (source is null || source.ContentType is not ("image/jpeg" or "image/png" or "video/mp4" or "audio/mpeg"))
            throw new AlternativeException("This media version is unavailable. Refresh the item.", 404, "media_unavailable");
        if (edit && !source.CanEdit) throw new AlternativeException("Your role cannot edit these alternatives.", 403, "forbidden");
        try
        {
            await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, source.FileKey, ct);
            return (source, Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)));
        }
        catch (StoredFileMissingException) { throw new AlternativeException("The media file is unavailable.", 404, "media_unavailable"); }
    }

    public async Task<AlternativeView> DraftAsync(AlternativeWrite request, CancellationToken ct)
    {
        var (source, hash) = await SourceAsync(request.Target, true, ct);
        if (AlternativeRules.Validate(request.Content, source.DurationSeconds).Count > 0)
            throw new AlternativeException("Choose a supported alternative language and valid caption content.", 400, "invalid_media_alternative");
        if (!DraftingEnabled || source.CampaignId is null)
            throw new AlternativeException("AI drafting is unavailable until verified Operations accounting is enabled. You can enter alternatives manually.", 503, "accessibility_drafting_unavailable");
        if (hash != request.SourceHash) throw new AlternativeException("The media changed. Reload before drafting alternatives.");
        if ((await ReadAsync(request.Target, ct)).Revision != request.Revision)
            throw new AlternativeException("The alternatives changed. Reload before drafting them again.");
        await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, source.FileKey, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > 20_000_000) throw new AlternativeException("AI drafting supports media files up to 20 MB. Enter alternatives manually for this file.", 400, "media_too_large");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        using var invocation = usage.Enter(new(member.BrandId, source.CampaignId));
        AlternativeContent content;
        try
        {
            content = await agent.PostAsync<object, AlternativeContent>("/accessibility/draft", new
            {
                data = Convert.ToBase64String(buffer.ToArray()), source.ContentType, language = request.Content.Language,
            }, TimeSpan.FromMinutes(2), ct);
        }
        catch (AgentCallException error) when (error.Code == "generation_limit_exceeded")
        {
            throw new AlternativeException("The campaign generation limit was reached. Enter alternatives manually.", 429, "generation_limit_exceeded");
        }
        catch (AgentCallException)
        {
            throw new AlternativeException("AI drafting could not complete. The current alternatives are unchanged. Check campaign generation capacity or try again later.", 503, "accessibility_draft_failed");
        }
        return await SaveAsync(request with { Content = content with { AudioConveysVisuals = false, SilentVideo = false } }, false, ct);
    }

    public async Task<AlternativeView> ReadAsync(MediaTarget target, CancellationToken ct)
    {
        var (source, hash) = await SourceAsync(target, false, ct);
        var key = AlternativeRules.Key(target);
        var row = await db.Alternatives.AsNoTracking().SingleOrDefaultAsync(row => row.TargetKey == key, ct);
        await CheckDescriptionAsync(row, ct);
        return View(target, source, hash, row);
    }

    public async Task<AlternativeView> DescribeAsync(AlternativeWrite request, CancellationToken ct)
    {
        var view = await ReadAsync(request.Target, ct);
        var (source, hash) = await SourceAsync(request.Target, true, ct);
        if (!DraftingEnabled || source.CampaignId is null)
            throw new AlternativeException("Audio description generation requires verified Operations accounting. You can attach a described MP4 manually.", 503, "accessibility_drafting_unavailable");
        if (source.ContentType != "video/mp4" || view.Revision != request.Revision || hash != request.SourceHash || view.Content.DescriptionCues is not { Length: > 0 } || view.Content.Captions.Length == 0 || view.Content.SilentVideo)
            throw new AlternativeException("Save timed visual descriptions for this video before generating its described preview.");
        await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, source.FileKey, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > 20_000_000) throw new AlternativeException("Attach a described MP4 manually for source files larger than 20 MB.", 400, "media_too_large");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        using var invocation = usage.Enter(new(member.BrandId, source.CampaignId));
        DescribedResponse response;
        try
        {
            response = await agent.PostAsync<object, DescribedResponse>("/accessibility/describe", new
            {
                data = Convert.ToBase64String(buffer.ToArray()), language = view.Content.Language,
                descriptionCues = view.Content.DescriptionCues, captions = view.Content.Captions,
            }, TimeSpan.FromMinutes(3), ct);
        }
        catch (AgentCallException error) when (error.Code == "generation_limit_exceeded")
        { throw new AlternativeException("The campaign generation limit was reached.", 429, "generation_limit_exceeded"); }
        catch (AgentCallException)
        { throw new AlternativeException("Audio description could not complete. Use a quiet gap with enough time for each spoken description, or attach a described MP4 manually.", 503, "audio_description_failed"); }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(response.Data); }
        catch (FormatException) { throw new AlternativeException("The described preview was invalid. Your saved alternatives are unchanged.", 503, "audio_description_failed"); }
        return await UploadDescriptionAsync(request.Target, request.Revision, request.SourceHash, bytes, ct);
    }

    private sealed record DescribedResponse(string Data);

    public async Task<AlternativeView> SaveAsync(AlternativeWrite request, bool confirm, CancellationToken ct)
    {
        var key = AlternativeRules.Key(request.Target);
        await database.LockAsync($"media-alternative:{member.BrandId:N}:{key}", ct);
        var (source, hash) = await SourceAsync(request.Target, true, ct);
        if (hash != request.SourceHash) throw new AlternativeException("The media file changed. Reload and review its alternatives.");
        var errors = AlternativeRules.Validate(request.Content, source.DurationSeconds);
        if (errors.Count != 0) throw new AlternativeException(string.Join(" ", errors.Values.SelectMany(value => value)), 400, "invalid_media_alternative");
        var row = await db.Alternatives.SingleOrDefaultAsync(row => row.TargetKey == key, ct);
        await CheckDescriptionAsync(row, ct);
        if (request.Revision != (row?.Revision ?? 0)) throw new AlternativeException("The alternatives changed. Reload before saving.");
        if (row?.SourceHash != hash && row is not null) row.DescribedFileKey = null;
        var nextContent = JsonSerializer.Serialize(request.Content, AlternativeRules.Json);
        if (row is not null && row.Content != nextContent) row.DescribedFileKey = null;
        var missing = AlternativeRules.Missing(request.Content, source.ContentType, row?.DescribedFileKey is not null);
        if (confirm && missing.Length > 0) throw new AlternativeException($"Complete these alternatives first: {string.Join(", ", missing)}.", 400, "media_alternatives_incomplete");
        if (row is null)
        {
            row = new() { Id = Guid.NewGuid(), TargetKey = key };
            db.Alternatives.Add(row);
        }
        row.SourceHash = hash;
        row.Content = nextContent;
        row.Revision++;
        row.ReviewedBy = confirm ? member.UserId : null;
        row.ReviewedAt = confirm ? time.GetUtcNow() : null;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditRecord(confirm ? "media_alternative.confirmed" : "media_alternative.saved", "media_alternative", row.Id.ToString(),
            After: new { request.Target.Kind, request.Target.Id, row.Revision, row.SourceHash, Reviewed = confirm }), ct);
        return View(request.Target, source, hash, row);
    }

    public async Task<AlternativeView> UploadDescriptionAsync(MediaTarget target, int revision, string sourceHash, byte[] content, CancellationToken ct)
    {
        var key = AlternativeRules.Key(target);
        await database.LockAsync($"media-alternative:{member.BrandId:N}:{key}", ct);
        var (source, hash) = await SourceAsync(target, true, ct);
        if (source.ContentType != "video/mp4" || hash != sourceHash) throw new AlternativeException("Reload this video before attaching a described version.");
        var row = await db.Alternatives.SingleOrDefaultAsync(row => row.TargetKey == key, ct);
        if (row is null || row.Revision != revision || row.SourceHash != hash) throw new AlternativeException("Save and reload these alternatives before attaching a described version.");
        if (content.Length is < 12 or > 25_000_000 || !content.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            throw new AlternativeException("Supply an MP4 file of at most 25 MB.", 400, "invalid_described_media");
        var fileKey = FileKeys.For(member.BrandId, "accessible-media", row.Id) + "/" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await files.PutAsync(FileBuckets.GeneratedMedia, fileKey, content, "video/mp4", ct);
        row.DescribedFileKey = fileKey;
        row.Revision++;
        row.ReviewedAt = null;
        row.ReviewedBy = null;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditRecord("media_alternative.described", "media_alternative", row.Id.ToString(), After: new { row.Revision, row.SourceHash }), ct);
        return View(target, source, hash, row);
    }

    public async Task<(string? Text, string? Key)> FileAsync(MediaTarget target, string file, int? revision, string? sourceHash, CancellationToken ct)
    {
        var view = await ReadAsync(target, ct);
        if (revision != view.Revision || sourceHash != view.SourceHash)
            throw new AlternativeException("These media alternatives changed. Reload their latest version.", 404, "media_unavailable");
        if (view.Status == "missing" || view.Revision == 0) throw new AlternativeException("This alternative is unavailable.", 404, "media_unavailable");
        if (file == "captions" && view.Content.Captions.Length > 0) return (AlternativeRules.Vtt(view.Content.Captions), null);
        var key = AlternativeRules.Key(target);
        var row = await db.Alternatives.AsNoTracking().SingleAsync(row => row.TargetKey == key, ct);
        if (file == "described" && row.SourceHash == view.SourceHash && row.DescribedFileKey is { } described) return (null, described);
        throw new AlternativeException("This alternative is unavailable.", 404, "media_unavailable");
    }

    private async Task CheckDescriptionAsync(MediaAlternative? row, CancellationToken ct)
    {
        if (row?.DescribedFileKey is not { } key) return;
        try { await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, key, ct); }
        catch (StoredFileMissingException) { row.DescribedFileKey = null; }
    }

    private AlternativeView View(MediaTarget target, AccessibleMediaSource source, string hash, MediaAlternative? row)
    {
        var current = row?.SourceHash == hash;
        var content = current ? JsonSerializer.Deserialize<AlternativeContent>(row!.Content, AlternativeRules.Json)! : new(source.Language, null, null, [], null);
        var missing = AlternativeRules.Missing(content, source.ContentType, current && row?.DescribedFileKey is not null);
        var reviewed = current && row?.ReviewedAt is not null && missing.Length == 0;
        var url = MediaAccessibilityLinks.For(member.BrandId, target);
        var fileVersion = $"&revision={row?.Revision ?? 0}&sourceHash={hash}";
        return new(target, row?.Revision ?? 0, hash, content, source.CanEdit, reviewed ? "reviewed" : current ? "draft" : "missing", missing,
            current ? row?.ReviewedBy : null, current ? row?.ReviewedAt : null,
            current && content.Captions.Length > 0 ? url + "&file=captions" + fileVersion : null,
            current && row?.DescribedFileKey is not null ? url + "&file=described" + fileVersion : null, DraftingEnabled && source.CampaignId is not null);
    }
}
