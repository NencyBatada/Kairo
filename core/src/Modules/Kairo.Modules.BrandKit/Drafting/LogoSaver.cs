using Kairo.Modules.BrandKit.Files;
using Kairo.SharedKernel.Files;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.BrandKit.Drafting;

internal sealed class LogoSaver(SafeWebClient web, IFileStore store, TimeProvider time, ILogger<LogoSaver> logger)
{
    public const long MaxBytes = 2 * 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<KitFile?> DownloadAsync(LogoCandidate logo, Guid brandId, Guid uploadedBy, CancellationToken cancellationToken)
    {
        try
        {
            var response = await web.FetchAsync(logo.Url, new SafeFetchOptions(MaxBytes, Timeout), cancellationToken);
            if (KitFileTypes.DetectLogo(response.Body) is not { } contentType)
            {
                logger.LogInformation("The logo at {Url} isn't a PNG, JPEG, WebP, SVG or ICO image.", logo.Url);
                return null;
            }

            var id = Guid.NewGuid();
            var file = new KitFile
            {
                Id = id,
                BrandId = brandId,
                Kind = KitFileKind.Logo,
                FileName = FileNameOf(logo.Url),
                ContentType = contentType,
                Size = response.Body.LongLength,
                StorageKey = FileKeys.For(brandId, "logos", id),
                UploadedBy = uploadedBy,
                UploadedAt = time.GetUtcNow(),
            };
            await store.PutAsync(FileBuckets.BrandFiles, file.StorageKey, response.Body, contentType, cancellationToken);
            return file;
        }
        catch (SafeFetchException exception)
        {
            logger.LogInformation("Couldn't download the logo at {Url}: {Reason}.", logo.Url, exception.Code);
            return null;
        }
        catch (FileStoreException exception)
        {
            logger.LogWarning(exception, "Couldn't store the logo from {Url}.", logo.Url);
            return null;
        }
    }

    public static string FileNameOf(Uri url)
    {
        var name = Path.GetFileName(Uri.UnescapeDataString(url.AbsolutePath));
        return KitFileTypes.CleanName(string.IsNullOrWhiteSpace(name) ? "logo" : name);
    }
}
