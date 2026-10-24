using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kairo.Contracts.Operations;

namespace Kairo.Modules.Operations;

internal sealed record ReserveCall(Guid RequestId, string Model, string Operation, string Region, int TextCalls = 0,
    int Images = 0, int VideoClips = 0, decimal? RequestedVideoSeconds = null, long? SpeechCharacters = null, bool? VideoAudio = null, string? VideoResolution = null);
internal sealed record ReportCall(string Outcome, string? ResolvedModel = null, long? InputTokens = null,
    long? OutputTokens = null, long? ThoughtTokens = null, long? CachedTokens = null, long? ImageTokens = null,
    int? GeneratedImages = null, decimal? VideoSeconds = null, long? SpeechCharacters = null, decimal? SpeechSeconds = null);
internal sealed class UsageFailure(string code, int status) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

internal static class UsagePolicy
{
    public const string LimitExceeded = "generation_limit_exceeded";
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    public static GenerationCapacity Limits(IConfiguration configuration) => new(
        Limit(configuration, "TextCalls", 100), Limit(configuration, "Images", 50), Limit(configuration, "VideoClips", 10));

    private static int Limit(IConfiguration configuration, string name, int fallback)
    {
        var value = configuration[$"Kairo:Operations:Limits:{name}"];
        if (value is null) return fallback;
        return int.TryParse(value, out var number) && number >= 0 && number <= 1_000_000
            ? number : throw new InvalidOperationException($"Invalid campaign generation limit: {name}.");
    }

    public static void Validate(ReserveCall value)
    {
        if (value.RequestId == Guid.Empty || value.Model is not { Length: > 0 and <= 100 }
            || value.Region is not { Length: > 0 and <= 40 }
            || value.Model.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '.' and not '_')
            || value.Region.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')) Invalid();
        var valid = value.Operation switch
        {
            "text" => value.TextCalls == 1 && value.Images == 0 && value.VideoClips == 0 && value.RequestedVideoSeconds is null && value.SpeechCharacters is null,
            "image" => value.TextCalls == 0 && value.Images is >= 1 and <= 4 && value.VideoClips == 0 && value.RequestedVideoSeconds is null && value.SpeechCharacters is null,
            "video" => value.TextCalls == 0 && value.Images == 0 && value.VideoClips is >= 1 and <= 4 && value.RequestedVideoSeconds is > 0 and <= 120 && value.SpeechCharacters is null && value.VideoAudio is not null && value.VideoResolution is "720p" or "1080p" or "4k",
            "speech" => value.TextCalls == 0 && value.Images == 0 && value.VideoClips == 0 && value.RequestedVideoSeconds is null && value.SpeechCharacters is >= 0 and <= 100_000,
            _ => false,
        };
        if (!valid) Invalid();
    }

    public static void Validate(ReportCall value, ReserveCall reservation)
    {
        if (value.Outcome is not ("succeeded" or "failed" or "uncertain") || value.ResolvedModel?.Length > 100
            || new long?[] { value.InputTokens, value.OutputTokens, value.ThoughtTokens, value.CachedTokens, value.ImageTokens, value.SpeechCharacters }.Any(count => count is < 0 or > 1_000_000_000)
            || value.GeneratedImages is < 0 || value.GeneratedImages > reservation.Images
            || value.VideoSeconds is < 0 || value.VideoSeconds > reservation.RequestedVideoSeconds * reservation.VideoClips
            || value.SpeechSeconds is < 0 or > 86_400 || value.SpeechCharacters != reservation.SpeechCharacters
            || value.CachedTokens > value.InputTokens || value.ImageTokens > value.OutputTokens) Invalid();
    }

    public static void Enforce(GenerationCapacity consumed, ReserveCall requested, GenerationCapacity limits)
    {
        if ((long)consumed.TextCalls + requested.TextCalls > limits.TextCalls
            || (long)consumed.Images + requested.Images > limits.Images
            || (long)consumed.VideoClips + requested.VideoClips > limits.VideoClips)
            throw new UsageFailure(LimitExceeded, 429);
    }

    private static void Invalid() => throw new UsageFailure("invalid_usage", 400);
}
