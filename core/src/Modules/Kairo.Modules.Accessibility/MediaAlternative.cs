using System.Globalization;
using System.Text;
using System.Text.Json;
using Kairo.Contracts.Media;

namespace Kairo.Modules.Accessibility;

internal sealed class MediaAlternative : IBrandOwned
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string TargetKey { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string Content { get; set; } = "{}";
    public int Revision { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? DescribedFileKey { get; set; }
}

internal sealed record MediaCue(decimal Start, decimal End, string Text);
internal sealed record AlternativeContent(string Language, string? AltText, string? Transcript, MediaCue[] Captions,
    string? VisualDescription, bool AudioConveysVisuals = false, bool SilentVideo = false, MediaCue[]? DescriptionCues = null);
internal sealed record AlternativeWrite(MediaTarget Target, int Revision, string SourceHash, AlternativeContent Content);
internal sealed record AlternativeView(MediaTarget Target, int Revision, string SourceHash, AlternativeContent Content,
    bool CanEdit, string Status, string[] Missing, Guid? ReviewedBy, DateTimeOffset? ReviewedAt,
    string? CaptionsUrl, string? DescribedMediaUrl, bool CanDraft = false);

internal static class AlternativeRules
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Key(MediaTarget target) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(target, Json))));

    public static Dictionary<string, string[]> Validate(AlternativeContent? content, decimal? duration)
    {
        Dictionary<string, string[]> errors = [];
        if (content is null) { errors["content"] = ["Supply the media alternatives."]; return errors; }
        if (content.Language is not ("en" or "hi")) errors["language"] = ["Choose English or Hindi."];
        foreach (var (name, text, maximum) in new[] { ("altText", content.AltText, 2000), ("transcript", content.Transcript, 40000), ("visualDescription", content.VisualDescription, 20000) })
            if (text?.Length > maximum || text?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true)
                errors[name] = [$"Use plain text of at most {maximum} characters."];
        if (content.Captions is null || content.Captions.Length > 500)
            errors["captions"] = ["Supply no more than 500 caption cues."];
        else
        {
            decimal previous = 0;
            foreach (var cue in content.Captions)
            {
                if (cue is null || cue.Start < previous || cue.End - cue.Start < .001m || cue.End > (duration ?? 3600)
                    || string.IsNullOrWhiteSpace(cue.Text) || cue.Text.Length > 2000 || cue.Text.Contains("-->") || cue.Text.Contains('\0'))
                { errors["captions"] = ["Use ordered, non-overlapping caption cues within the media duration, with plain caption text."]; break; }
                previous = cue.End;
            }
        }
        if (content.DescriptionCues is { Length: > 0 } descriptions)
        {
            if (descriptions.Length > 50) errors["descriptionCues"] = ["Use no more than 50 spoken visual descriptions."];
            decimal previous = 0;
            foreach (var cue in descriptions)
            {
                if (cue is null || cue.Start < previous || cue.End <= cue.Start || cue.End > (duration ?? 3600) || string.IsNullOrWhiteSpace(cue.Text) || cue.Text.Length > 2000)
                { errors["descriptionCues"] = ["Use ordered description cues within the video duration."]; break; }
                previous = cue.End;
            }
        }
        return errors;
    }

    public static string[] Missing(AlternativeContent content, string contentType, bool described)
    {
        List<string> missing = [];
        if (contentType.StartsWith("image/", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(content.AltText)) missing.Add("Image description");
        if (contentType.StartsWith("audio/", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(content.Transcript)) missing.Add("Audio transcript");
        if (contentType.StartsWith("video/", StringComparison.Ordinal))
        {
            if (!content.SilentVideo && content.Captions.Length == 0) missing.Add("Captions, including relevant sounds");
            if (string.IsNullOrWhiteSpace(content.Transcript)) missing.Add("Descriptive transcript");
            if (string.IsNullOrWhiteSpace(content.VisualDescription)) missing.Add("Visual description");
            if (!content.SilentVideo && !content.AudioConveysVisuals && !described) missing.Add("Audio-described video, or confirmation that existing audio conveys all essential visuals");
        }
        return [.. missing];
    }

    public static string Vtt(MediaCue[] cues)
    {
        static string Stamp(decimal seconds) => TimeSpan.FromMilliseconds((double)decimal.Round(seconds * 1000)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        static string Text(string value) => string.Join("\n", value.Replace("\r", "").Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)))
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        return "WEBVTT\n\n" + string.Join("\n\n", cues.Select((cue, i) => $"{i + 1}\n{Stamp(cue.Start)} --> {Stamp(cue.End)}\n{Text(cue.Text)}")) + "\n";
    }
}
