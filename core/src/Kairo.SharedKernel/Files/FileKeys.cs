using System.Text.RegularExpressions;

namespace Kairo.SharedKernel.Files;

public static partial class FileKeys
{
    public const string DraftPrefix = "drafts";

    public static string For(Guid brandId, string area, Guid id) => $"{brandId:N}/{area}/{id:N}";

    public static string Draft(Guid brandId, string area, Guid id) => $"{DraftPrefix}/{For(brandId, area, id)}";

    internal static void EnsureValid(string bucket, string key)
    {
        if (!Bucket().IsMatch(bucket))
        {
            throw new ArgumentException($"'{bucket}' isn't a valid bucket name.", nameof(bucket));
        }

        if (!Key().IsMatch(key))
        {
            throw new ArgumentException($"'{key}' isn't a valid file key.", nameof(key));
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex Bucket();

    [GeneratedRegex("^[a-z0-9-]{1,64}(/[a-z0-9-]{1,64}){0,7}$")]
    private static partial Regex Key();
}
