using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Contracts.Approvals;
using Kairo.Contracts.Audit;
using Kairo.Contracts.BrandKit;
using Kairo.Contracts.Brands;
using Kairo.Contracts.CampaignRooms;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;
using Kairo.Contracts.Live;
using Kairo.Contracts.Notifications;
using Kairo.Modules.Launch.Data;
using Kairo.Modules.Launch.Google;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.Launch;

internal sealed class LostLaunchLeaseException : Exception;

internal sealed class LaunchRunner(LaunchDbContext db, KairoDatabase database, ILaunchPlans plans, LaunchPreflight preflight,
    LaunchGoogle google, YouTubeUpload youtube, IFileStore files, IBrandKit kits, ICampaignLifecycle lifecycle,
    ILiveUpdates live, IAuditLog audit, INotifications notifications, IBrandDirectory brands, TimeProvider time, IServiceScopeFactory scopes)
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    public static readonly AuditActor Actor = new(AuditActorType.System, "Launch service", "launch");

    public async Task RunAsync(Guid id, Guid claim, CancellationToken cancellationToken)
    {
        var record = await db.Launches.SingleAsync(row => row.Id == id, cancellationToken);
        var plan = JsonSerializer.Deserialize<LaunchPlan>(record.Plan, JsonSerializerOptions.Web)!;
        try
        {
            if (record.AttemptingStep is not null)
                throw new LaunchException("reconciliation_required", "The last launch step has an uncertain outcome.", "Reconcile the recorded Google operation before creating anything else.", uncertain: true);
            await VerifyAsync(record, cancellationToken);
            if (await lifecycle.AdvanceAsync(plan.RoomId, CampaignSignals.LaunchStarted, Actor, cancellationToken) != CampaignAdvance.Moved)
                throw new LaunchException("wrong_state", "This campaign isn't waiting to launch.", "Open the current campaign state before trying again.");
            var languages = await preflight.CheckAsync(plan, cancellationToken);
            var resources = Read<Dictionary<string, string>>(record.Resources);
            var uploads = Read<Dictionary<string, VideoUpload>>(record.Uploads);
            foreach (var asset in plan.Assets.Where(asset => asset.Kind is "image" or "video"))
            {
                var key = asset.Id.ToString("N");
                if (resources.ContainsKey("media:" + key)) continue;
                if (asset.Kind == "video")
                {
                    await youtube.CheckChannelAsync(plan, cancellationToken);
                    if (!uploads.ContainsKey(key))
                        await StepAsync(record, claim, $"upload-session:{key}", async () =>
                        {
                            uploads[key] = new VideoUpload(await youtube.StartAsync(plan, asset, cancellationToken));
                            record.Uploads = Write(uploads);
                        }, cancellationToken);
                    if (uploads[key].VideoId is null)
                        await StepAsync(record, claim, $"upload:{key}", async () =>
                        {
                            uploads[key] = uploads[key] with { VideoId = await youtube.UploadAsync(plan, asset, uploads[key].Session, cancellationToken) };
                            record.Uploads = Write(uploads);
                        }, cancellationToken);
                    await youtube.ReadyAsync(plan, uploads[key].VideoId!, cancellationToken);
                }

                await StepAsync(record, claim, $"media:{key}", async () =>
                {
                    var body = new JsonObject { ["name"] = CampaignBuild.Marker(plan.RoomId, $"asset {key} v{asset.Version}") };
                    if (asset.Kind == "image") body["imageAsset"] = new JsonObject { ["data"] = Convert.ToBase64String(await ReadFileAsync(asset.FileKey!, cancellationToken)) };
                    else body["youtubeVideoAsset"] = new JsonObject { ["youtubeVideoId"] = uploads[key].VideoId };
                    if (asset.SyntheticMedia) body["syntheticContentInfo"] = Attestation();
                    resources["media:" + key] = (await google.MutateAsync(plan.GoogleAdsAccountId!, [new JsonObject { ["assetOperation"] = new JsonObject { ["create"] = body } }], false, cancellationToken))[0];
                    record.Resources = Write(resources);
                }, cancellationToken);
            }

            if (plan.Settings.Channels.Any(channel => channel.CampaignType == "demand_gen") && !resources.ContainsKey("media:logo"))
                await StepAsync(record, claim, "media:logo", async () =>
                {
                    var logo = plan.Logo ?? throw new LaunchException("missing_logo", "The approved logo is missing.", "Approve a Brand Kit logo before launching.");
                    var file = await kits.OpenFileAsync(logo.FileId, cancellationToken);
                    if (file is null || Convert.ToHexStringLower(SHA256.HashData(file.Content)) != logo.Digest)
                        throw new LaunchException("logo_changed", "The approved logo changed or is missing.", "Approve the current logo in a new launch plan.");
                    resources["media:logo"] = (await google.MutateAsync(plan.GoogleAdsAccountId!, [new { assetOperation = new { create = new { name = CampaignBuild.Marker(plan.RoomId, "logo"), imageAsset = new { data = Convert.ToBase64String(file.Content) } } } }], false, cancellationToken))[0];
                    record.Resources = Write(resources);
                }, cancellationToken);
            var media = resources.Where(pair => pair.Key.StartsWith("media:", StringComparison.Ordinal)).ToDictionary(pair => pair.Key[6..], pair => pair.Value);
            var build = new CampaignBuild(plan, media, languages).Build();
            if (!resources.ContainsKey("built"))
            {
                await google.MutateAsync(plan.GoogleAdsAccountId!, build.Operations, true, cancellationToken);
                await StepAsync(record, claim, "build", async () =>
                {
                    var names = await google.MutateAsync(plan.GoogleAdsAccountId!, build.Operations, false, cancellationToken);
                    for (var index = 0; index < names.Length; index++) resources[build.Keys[index]] = names[index];
                    resources["built"] = "true";
                    record.Resources = Write(resources);
                    foreach (var ad in build.Ads)
                    foreach (var product in ad.ProductIds.Distinct())
                        db.ProductAds.Add(new ProductAdMap { Id = Guid.NewGuid(), RoomId = plan.RoomId, ProductId = product,
                            CampaignType = ad.CampaignType, CampaignResource = resources[ad.CampaignKey], AdResource = resources[ad.Key] });
                }, cancellationToken);
            }

            var activate = plan.Settings.Channels.Select(channel => (object)new
            {
                campaignOperation = new { update = new { resourceName = resources[$"campaign:{channel.CampaignType}"], status = "ENABLED" }, updateMask = "status" },
            }).ToArray();
            await google.MutateAsync(plan.GoogleAdsAccountId!, activate, true, cancellationToken);
            await StepAsync(record, claim, "activate", async () =>
            {
                await google.MutateAsync(plan.GoogleAdsAccountId!, activate, false, cancellationToken);
                record.Status = "live";
                record.ActivatedAt = time.GetUtcNow();
                record.ClaimedUntil = null;
                record.ClaimToken = null;
                await lifecycle.AdvanceAsync(plan.RoomId, CampaignSignals.LaunchFinished, Actor, cancellationToken);
                await audit.RecordAsync(new AuditRecord("launch.finished", "campaign_room", plan.RoomId.ToString(), null, new { record.Id, record.Hash }, Actor), cancellationToken);
                await NotifyAsync(record, "launch.finished", "Campaign launched", "All approved campaigns are enabled in Google Ads.", cancellationToken);
            }, cancellationToken);
        }
        catch (LostLaunchLeaseException) { }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var error = exception as LaunchException ?? (exception is ConnectionUnavailableException
                ? new LaunchException("connection_missing", "An approved Google account needs reconnecting.", "Reconnect the account and retry.")
                : new LaunchException("launch_interrupted", "Kairo couldn't finish this launch step.", "Check the saved launch progress before retrying.", uncertain: record.AttemptingStep is not null));
            await using var failureScope = scopes.CreateAsyncScope();
            await failureScope.ServiceProvider.GetRequiredService<KairoDatabase>().UseBrandAsync(record.BrandId, cancellationToken);
            await failureScope.ServiceProvider.GetRequiredService<LaunchFailureRecorder>().RecordAsync(record.Id, claim, error, cancellationToken);
        }
    }

    private async Task StepAsync(LaunchRecord record, Guid claim, string step, Func<Task> operation, CancellationToken cancellationToken)
    {
        await SaveAsync(record, claim, () => { record.Step = step; record.AttemptingStep = step; return Task.CompletedTask; }, true, cancellationToken);
        try
        {
            await SaveAsync(record, claim, async () =>
            {
                await operation();
                record.AttemptingStep = null;
                await audit.RecordAsync(new AuditRecord("launch.step_finished", "campaign_room", record.RoomId.ToString(), null, new { step }, Actor), cancellationToken);
            }, true, cancellationToken);
        }
        catch (Exception exception) when (exception is not (LaunchException or LostLaunchLeaseException or OperationCanceledException))
        {
            throw new LaunchException("checkpoint_failed", "Kairo couldn't confirm the last launch step locally.", "Reconcile the saved Google operation before retrying.", uncertain: true);
        }
    }

    private async Task SaveAsync(LaunchRecord record, Guid claim, Func<Task> change, bool verify, CancellationToken cancellationToken)
    {
        await database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.LockAsync($"room:{record.RoomId:N}", cancellationToken);
            await database.LockAsync($"launch:{record.RoomId:N}", cancellationToken);
            await db.Entry(record).ReloadAsync(cancellationToken);
            if (record.ClaimToken != claim || record.ClaimedUntil <= time.GetUtcNow()) throw new LostLaunchLeaseException();
            if (verify) await VerifyAsync(record, cancellationToken);
            record.ClaimedUntil = time.GetUtcNow() + Lease;
            await change();
            await db.SaveChangesAsync(cancellationToken);
            await live.PublishAsync(new LiveEvent("launch.changed", new { campaignId = record.RoomId, record.Status, record.Step }, LiveAudience.Everyone), cancellationToken);
            await live.PublishAsync(new LiveEvent("campaign.changed", new { campaignId = record.RoomId }, LiveAudience.Everyone), cancellationToken);
            await database.CommitAsync(cancellationToken);
        }
        catch
        {
            await database.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task VerifyAsync(LaunchRecord record, CancellationToken cancellationToken)
    {
        var current = await plans.GetAsync(record.RoomId, cancellationToken);
        if (ApprovalHash.Of(record.Plan) != record.Hash || current is null || ApprovalHash.Of(Write(current)) != record.Hash
            || !await plans.IsApprovedAsync(record.RoomId, cancellationToken))
            throw new LaunchException("approval_changed", "The launch plan no longer matches its approvals.", "Send the campaign back and approve its current plan.");
    }

    private async Task NotifyAsync(LaunchRecord record, string kind, string title, string message, CancellationToken cancellationToken)
    {
        var brand = (await brands.GetAsync([record.BrandId], cancellationToken)).Single();
        await notifications.NotifyAsync(new Notification(kind, LiveAudience.Roles(BrandRoles.Marketer), title, message,
            $"/{brand.Handle}/campaigns/{record.RoomId}/launch", $"launch:{record.RoomId:N}:{kind}"), cancellationToken);
    }

    private async Task<byte[]> ReadFileAsync(string key, CancellationToken cancellationToken)
    {
        await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, key, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length is 0 or > 10_000_000) throw new LaunchException("invalid_media", "An approved media file has an invalid size.", "Regenerate and approve the asset again.");
        return buffer.ToArray();
    }

    public static JsonObject Attestation() => new() { ["advertiserAttestation"] = new JsonObject { ["source"] = "ADVERTISER_ATTESTED", ["status"] = "IS_SYNTHETIC" } };
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)!;
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
