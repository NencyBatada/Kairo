namespace Kairo.Modules.Opportunities.Scoring;

internal static class TopicText
{
    public const int MaxKeyLength = 200;

    public static string Key(string text)
    {
        var key = string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return key.Length > MaxKeyLength ? key[..MaxKeyLength] : key;
    }

    public static bool Mentions(string phrase, string topic, IEnumerable<string> keywords)
    {
        var wanted = Key(phrase);
        return wanted.Length > 0 && (Key(topic).Contains(wanted, StringComparison.Ordinal) || keywords.Any(keyword => Key(keyword).Contains(wanted, StringComparison.Ordinal)));
    }

    public static double Overlap(IReadOnlyCollection<string> first, IReadOnlyCollection<string> second)
    {
        var left = first.Select(Key).ToHashSet(StringComparer.Ordinal);
        var right = second.Select(Key).ToHashSet(StringComparer.Ordinal);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var shared = left.Count(right.Contains);
        return (double)shared / (left.Count + right.Count - shared);
    }

    public static bool Similar(string firstKey, IReadOnlyCollection<string> firstKeywords, string secondKey, IReadOnlyCollection<string> secondKeywords, double threshold) =>
        firstKey == secondKey || Overlap(firstKeywords, secondKeywords) >= threshold;
}
