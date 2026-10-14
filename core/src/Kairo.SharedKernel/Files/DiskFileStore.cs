namespace Kairo.SharedKernel.Files;

internal sealed class DiskFileStore(FileStoreOptions options) : IFileStore
{
    public async Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathOf(bucket, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    public Task<Stream> OpenAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        var path = PathOf(bucket, key);
        if (!File.Exists(path))
        {
            throw new StoredFileMissingException(bucket, key);
        }

        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    private string PathOf(string bucket, string key)
    {
        FileKeys.EnsureValid(bucket, key);
        return Path.Combine([options.Root, bucket, .. key.Split('/')]);
    }
}
