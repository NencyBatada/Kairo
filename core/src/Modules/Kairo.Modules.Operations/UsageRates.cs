namespace Kairo.Modules.Operations;

internal static class UsageRates
{
    public const string Version = "google-standard-usd/2026-10-18.1";

    public static (decimal? Cost, string? Version) Estimate(ReserveCall call, ReportCall report, DateTimeOffset at)
    {
        if (report.Outcome != "succeeded" || at < new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero)) return (null, null);
        if (call.Operation == "speech")
        {
            decimal? rate = call.Model switch
            {
                var value when value.Contains("-Standard-", StringComparison.Ordinal) || value.Contains("-Wavenet-", StringComparison.Ordinal) => 4,
                var value when value.Contains("-Neural2-", StringComparison.Ordinal) => 16,
                var value when value.Contains("-Chirp3-HD-", StringComparison.Ordinal) => 30,
                _ => null,
            };
            return (rate * report.SpeechCharacters / 1_000_000, rate is null ? null : Version);
        }
        if (call.Operation == "video")
        {
            decimal? rate = (call.Model, call.VideoAudio, call.VideoResolution) switch
            {
                ("veo-3.1-generate-001", false, "720p" or "1080p") => 0.20m,
                ("veo-3.1-generate-001", true, "720p" or "1080p") => 0.40m,
                ("veo-3.1-generate-001", false, "4k") => 0.40m,
                ("veo-3.1-generate-001", true, "4k") => 0.60m,
                _ => null,
            };
            return (rate * report.VideoSeconds, rate is null ? null : Version);
        }
        decimal? input;
        decimal? output;
        decimal? cached;
        if (call.Model == "gemini-3.8-flash")
        {
            var introductory = at < new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
            (input, output, cached) = introductory ? (0.75m, 3.75m, 0.075m) : (1.5m, 7.5m, 0.15m);
            if (call.Region != "global") (input, output, cached) = (input * 1.1m, output * 1.1m, cached * 1.1m);
        }
        else if (call.Model == "gemini-3.5-flash")
        {
            (input, output, cached) = call.Region == "global" ? (1.5m, 9m, 0.15m) : (1.65m, 9.9m, 0.165m);
        }
        else if (call.Model == "gemini-2.5-flash-image")
            (input, output, cached) = (0.3m, 2.5m, null);
        else return (null, null);
        if (report.InputTokens is null || report.OutputTokens is null || report.ThoughtTokens is null || report.CachedTokens is null)
            return (null, Version);
        if (report.CachedTokens > 0 && cached is null) return (null, Version);
        decimal? cost;
        if (call.Operation == "image")
        {
            if (report.ImageTokens is null || call.Model != "gemini-2.5-flash-image") return (null, Version);
            cost = ((report.InputTokens - report.CachedTokens) * input + report.CachedTokens * (cached ?? 0)
                + (report.OutputTokens - report.ImageTokens + report.ThoughtTokens) * output + report.ImageTokens * 30) / 1_000_000;
        }
        else
            cost = ((report.InputTokens - report.CachedTokens) * input + report.CachedTokens * (cached ?? 0)
                + (report.OutputTokens + report.ThoughtTokens) * output) / 1_000_000;
        return (cost, Version);
    }
}
