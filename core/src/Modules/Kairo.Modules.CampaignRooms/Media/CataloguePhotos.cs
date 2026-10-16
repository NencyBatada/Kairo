using Kairo.SharedKernel.Http;
using Microsoft.Extensions.Logging;

namespace Kairo.Modules.CampaignRooms.Media;

internal sealed class CataloguePhotos(SafeWebClient web, ILogger<CataloguePhotos> logger)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<MediaPhoto?> FetchAsync(string? link, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var url) || !SafeWebClient.IsAllowed(url))
        {
            return null;
        }

        try
        {
            var response = await web.FetchAsync(url, new SafeFetchOptions(MaxBytes, Timeout), cancellationToken);
            return TypeOf(response.Body) is { } type ? new MediaPhoto(type, response.Body) : null;
        }
        catch (SafeFetchException exception)
        {
            logger.LogInformation("Couldn't fetch the catalogue photo {Url}: {Code}", url, exception.Code);
            return null;
        }
    }

    public async Task<IReadOnlyList<MediaPhoto>> FetchFirstAsync(IEnumerable<string?> links, int count, CancellationToken cancellationToken)
    {
        var photos = new List<MediaPhoto>();
        foreach (var link in links.Where(link => !string.IsNullOrWhiteSpace(link)).Distinct())
        {
            if (photos.Count == count)
            {
                break;
            }

            if (await FetchAsync(link, cancellationToken) is { } photo)
            {
                photos.Add(photo);
            }
        }

        return photos;
    }

    public static string? TypeOf(ReadOnlySpan<byte> body)
    {
        if (body.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (body.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (body.Length >= 12 && body[..4].SequenceEqual("RIFF"u8) && body[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
