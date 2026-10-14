namespace Kairo.SharedKernel.Files;

public interface IFileStore
{
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken);
}

public static class FileBuckets
{
    public const string BrandFiles = "brand-files";
}

public sealed class StoredFileMissingException(string bucket, string key)
    : Exception($"{bucket}/{key} isn't in the file store.");

public sealed class FileStoreException(string message) : Exception(message);
