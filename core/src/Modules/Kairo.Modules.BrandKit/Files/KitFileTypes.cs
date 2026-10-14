using System.Text;

namespace Kairo.Modules.BrandKit.Files;

internal static class KitFileTypes
{
    public const long MaxBytes = 20 * 1024 * 1024;
    public const string Pdf = "application/pdf";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static readonly byte[] PdfStart = "%PDF-"u8.ToArray();
    private static readonly byte[] PngStart = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegStart = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] ZipStart = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] IcoStart = [0x00, 0x00, 0x01, 0x00];

    public static KitFileKind? ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        null or "" or "guideline" => KitFileKind.Guideline,
        "logo" => KitFileKind.Logo,
        _ => null,
    };

    public static string? Detect(byte[] content, string? fileName)
    {
        var head = content.AsSpan();
        if (head.StartsWith(PdfStart))
        {
            return Pdf;
        }

        if (head.StartsWith(PngStart))
        {
            return "image/png";
        }

        if (head.StartsWith(JpegStart))
        {
            return "image/jpeg";
        }

        if (head.Length >= 12 && head[..4].SequenceEqual(Riff) && head[8..12].SequenceEqual(Webp))
        {
            return "image/webp";
        }

        if (head.StartsWith(ZipStart) && (fileName ?? "").EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return Docx;
        }

        return LooksLikeSvg(head) ? "image/svg+xml" : null;
    }

    public static string? DetectLogo(byte[] content)
    {
        if (content.AsSpan().StartsWith(IcoStart))
        {
            return "image/x-icon";
        }

        return Detect(content, null) is { } contentType && IsImage(contentType) ? contentType : null;
    }

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.Ordinal);

    public static string CleanName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (name.Length == 0)
        {
            return "file";
        }

        return name.Length > 200 ? name[..200] : name;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> head)
    {
        var text = Encoding.UTF8.GetString(head[..Math.Min(head.Length, 1024)]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var opens = text.StartsWith("<?xml", StringComparison.Ordinal)
            || text.StartsWith("<svg", StringComparison.Ordinal)
            || text.StartsWith("<!--", StringComparison.Ordinal);
        return opens && text.Contains("<svg", StringComparison.Ordinal);
    }
}
