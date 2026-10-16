using System.Text.Json;
using Kairo.Modules.CampaignRooms.Assets;
using Kairo.SharedKernel.Agents;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed record MediaBrief(string Title, string Objective, IReadOnlyList<string> KeyMessages, IReadOnlyList<string> Audiences, IReadOnlyList<string> Memory);

internal sealed record MediaPhoto(string MimeType, byte[] Data);

internal sealed record MediaReferenceProduct(string Id, string Title, string Description, IReadOnlyList<MediaPhoto> Photos);

internal sealed record ImageShot(string Id, string ProductId, string Style, IReadOnlyList<string> Formats);

internal sealed record ImagesRequest(
    MediaBrief Brief,
    JsonElement? BrandKit,
    IReadOnlyList<MediaReferenceProduct> Products,
    IReadOnlyList<ImageShot> Shots,
    string? Instruction = null);

internal sealed record GeneratedImage(string ShotId, string ProductId, string Style, string Format, int Width, int Height, string MimeType, byte[] Data, string Prompt);

internal sealed record ImageFailure(string ShotId, string Format, string Reason);

internal sealed record ImagesAnswer(
    IReadOnlyList<GeneratedImage>? Images,
    IReadOnlyList<ImageFailure>? Failures,
    string? Model,
    string? PromptVersion,
    AgentUsage? Usage = null);

internal sealed record FootageProduct(string Id, string Title);

internal sealed record FootageFrame(string? ProductId, string MimeType, byte[] Data);

internal sealed record FootageRequest(
    MediaBrief Brief,
    JsonElement? BrandKit,
    IReadOnlyList<FootageProduct> Products,
    IReadOnlyList<FootageFrame> Frames,
    int Clips,
    int ClipSeconds,
    string? Instruction);

internal sealed record FootageClip(int Index, string Prompt, string MimeType, byte[] Data, double Seconds);

internal sealed record FootageAnswer(
    IReadOnlyList<FootageClip>? Clips,
    string? Model,
    string? PlannerModel,
    string? PromptVersion,
    AgentUsage? Usage = null);

internal sealed record VideoClip(string MimeType, byte[] Data, double Seconds);

internal sealed record VideoScriptLine(string Voiceover, string Caption);

internal sealed record VideoRequest(
    MediaBrief Brief,
    JsonElement? BrandKit,
    string BusinessName,
    string Language,
    IReadOnlyList<VideoClip> Clips,
    MediaPhoto? Logo,
    string? Background,
    int Seconds,
    IReadOnlyList<VideoScriptLine>? Script,
    string? Instruction);

internal sealed record MediaFile(string MimeType, byte[] Data, double Seconds);

internal sealed record TimedScriptLine(string Voiceover, string Caption, double Start, double End);

internal sealed record VideoAnswer(
    MediaFile? Video,
    MediaFile? Voiceover,
    int Width,
    int Height,
    string? Language,
    string? Voice,
    IReadOnlyList<TimedScriptLine>? Script,
    string? Model,
    string? PromptVersion,
    AgentUsage? Usage = null);

internal sealed class MediaAgent(AgentClient agents, ProvenanceTrail trail)
{
    public const string ImagesRoute = "create/images";
    public const string FootageRoute = "create/footage";
    public const string VideoRoute = "create/video";

    public static readonly TimeSpan ImagesTimeout = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan FootageTimeout = TimeSpan.FromMinutes(6);
    public static readonly TimeSpan VideoTimeout = TimeSpan.FromSeconds(150);

    public Task<ImagesAnswer> ImagesAsync(ImagesRequest request, CancellationToken cancellationToken)
    {
        trail.Called(ImagesRoute, request);
        return agents.PostAsync<ImagesRequest, ImagesAnswer>(ImagesRoute, request, ImagesTimeout, cancellationToken);
    }

    public Task<FootageAnswer> FootageAsync(FootageRequest request, CancellationToken cancellationToken)
    {
        trail.Called(FootageRoute, request);
        return agents.PostAsync<FootageRequest, FootageAnswer>(FootageRoute, request, FootageTimeout, cancellationToken);
    }

    public Task<VideoAnswer> VideoAsync(VideoRequest request, CancellationToken cancellationToken)
    {
        trail.Called(VideoRoute, request, request.Language);
        return agents.PostAsync<VideoRequest, VideoAnswer>(VideoRoute, request, VideoTimeout, cancellationToken);
    }
}
