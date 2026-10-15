using Kairo.Modules.Catalogue.Format;

namespace Kairo.Modules.Catalogue.Imports;

internal sealed record FetchedFeed(byte[]? Body, string? ErrorCode, string? ErrorMessage);

internal static class FeedUrls
{
    public const int MaxLength = 2000;
    public const string BadUrl = "Use a full web address starting with http:// or https://.";

    public static string? Clean(string? url)
    {
        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && SafeWebClient.IsAllowed(uri) ? uri.AbsoluteUri : null;
    }
}

internal sealed class FeedFetcher(SafeWebClient web)
{
    public static readonly SafeFetchOptions Options = new(CatalogueFile.MaxBytes, TimeSpan.FromSeconds(120));

    public async Task<FetchedFeed> FetchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await web.FetchAsync(new Uri(url), Options, cancellationToken);
            return new FetchedFeed(response.Body, null, null);
        }
        catch (SafeFetchException exception)
        {
            return new FetchedFeed(null, exception.Code, MessageFor(exception.Code));
        }
    }

    public static string MessageFor(string code) => code switch
    {
        SafeFetchErrors.TooLarge => "The feed is larger than 50 MB.",
        SafeFetchErrors.Timeout => "The feed didn't finish downloading within 120 seconds.",
        SafeFetchErrors.BlockedAddress => "Kairo can't read this address. Use a public http or https link.",
        SafeFetchErrors.BadStatus => "The feed address answered with an error.",
        _ => "Kairo couldn't reach this address.",
    };
}
