using Microsoft.Extensions.Configuration;

namespace Kairo.SharedKernel.Files;

public sealed class FileStoreOptions(IConfiguration configuration)
{
    public string? Project => configuration["Kairo:Files:Project"] is { Length: > 0 } project ? project : null;

    public string Root => configuration["Kairo:Files:Root"] is { Length: > 0 } root
        ? root
        : Path.Combine(Path.GetTempPath(), "kairo-files");

    public string BucketName(string bucket) => $"{Project}-{bucket}";
}
