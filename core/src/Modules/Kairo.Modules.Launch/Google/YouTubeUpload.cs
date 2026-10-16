using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kairo.Contracts.Connections;
using Kairo.Contracts.Launch;
using Kairo.SharedKernel.Files;

namespace Kairo.Modules.Launch.Google;

internal sealed record VideoUpload(string Session, string? VideoId = null);

internal sealed class YouTubeUpload(LaunchGoogle google, IFileStore files)
{
    public async Task CheckChannelAsync(LaunchPlan plan, CancellationToken cancellationToken)
    {
        using var response = await google.SendAsync(ConnectedProduct.YouTube, plan.YouTubeChannelId!, HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/channels?part=id&mine=true", null, false, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var ids = body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        if (ids.Length != 1 || ids[0] != plan.YouTubeChannelId)
            throw new LaunchException("youtube_channel", "The YouTube sign-in doesn't identify the approved upload channel.", "Reconnect YouTube while signed in as the approved channel.");
    }

    public async Task<string> StartAsync(LaunchPlan plan, LaunchAsset asset, CancellationToken cancellationToken)
    {
        using var response = await google.SendAsync(ConnectedProduct.YouTube, plan.YouTubeChannelId!, HttpMethod.Post,
            "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status",
            JsonContent.Create(new
            {
                snippet = new { title = plan.Title[..Math.Min(100, plan.Title.Length)], description = CampaignBuild.Marker(plan.RoomId, $"video {asset.Id:N} v{asset.Version}") },
                status = new { privacyStatus = "unlisted", containsSyntheticMedia = asset.SyntheticMedia, selfDeclaredMadeForKids = false },
            }), true, cancellationToken, request =>
            {
                request.Headers.Add("X-Upload-Content-Type", asset.ContentType);
                request.Headers.Add("X-Upload-Content-Length", asset.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });
        var session = response.Headers.Location?.AbsoluteUri;
        if (session is null || !AllowedSession(session)) throw new LaunchException("upload_session", "YouTube didn't provide a valid upload session.", "Retry the video upload.");
        return session;
    }

    public async Task<string> UploadAsync(LaunchPlan plan, LaunchAsset asset, string session, CancellationToken cancellationToken)
    {
        if (!AllowedSession(session)) throw new LaunchException("upload_session", "The saved upload session isn't valid.", "Start a new approved video upload session.");
        await using var stream = await files.OpenAsync(FileBuckets.GeneratedMedia, asset.FileKey!, cancellationToken);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(asset.ContentType!);
        content.Headers.ContentLength = asset.Bytes;
        using var response = await google.SendAsync(ConnectedProduct.YouTube, plan.YouTubeChannelId!, HttpMethod.Put, session, content, true, cancellationToken);
        if ((int)response.StatusCode == 308) throw new LaunchException("upload_incomplete", "The video upload isn't complete yet.", "Resume the saved upload session.", transient: true, uncertain: true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.TryGetProperty("id", out var id) && !string.IsNullOrEmpty(id.GetString()) ? id.GetString()!
            : throw new LaunchException("upload_response", "YouTube didn't confirm the uploaded video ID.", "Check the saved upload session before retrying.", uncertain: true);
    }

    public async Task ReadyAsync(LaunchPlan plan, string videoId, CancellationToken cancellationToken)
    {
        using var response = await google.SendAsync(ConnectedProduct.YouTube, plan.YouTubeChannelId!, HttpMethod.Get,
            $"https://www.googleapis.com/youtube/v3/videos?part=snippet,status,processingDetails&id={Uri.EscapeDataString(videoId)}", null, false, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        if (items.Length != 1 || LaunchPreflight.Text(items[0].GetProperty("snippet"), "channelId") != plan.YouTubeChannelId
            || LaunchPreflight.Text(items[0].GetProperty("status"), "privacyStatus") != "unlisted")
            throw new LaunchException("video_unavailable", "The uploaded video isn't available as unlisted on the approved channel.", "Check YouTube access and the video's visibility before retrying.");
        if (!items[0].TryGetProperty("processingDetails", out var processing) || LaunchPreflight.Text(processing, "processingStatus") != "succeeded")
            throw new LaunchException("video_processing", "YouTube is still processing the approved video.", "Retry after YouTube finishes processing.", transient: true);
    }

    public static bool AllowedSession(string session) => Uri.TryCreate(session, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host is "www.googleapis.com" or "upload.youtube.com";
}
