using System.Text.Json;

namespace Kairo.Contracts.BrandKit;

public interface IBrandKit
{
    Task<ApprovedBrandKit?> GetApprovedAsync(CancellationToken cancellationToken);

    Task<BrandKitFileContent?> OpenFileAsync(Guid fileId, CancellationToken cancellationToken);
}

public sealed record ApprovedBrandKit(int Version, JsonElement Content, IReadOnlyList<BrandKitFile> Files);

public sealed record BrandKitFile(Guid Id, string Kind, string FileName, string ContentType);

public sealed record BrandKitFileContent(Guid Id, string Kind, string ContentType, byte[] Content);
