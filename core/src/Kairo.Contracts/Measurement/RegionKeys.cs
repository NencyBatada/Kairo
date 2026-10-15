using System.Globalization;
using System.Text;

namespace Kairo.Contracts.Measurement;

public static class RegionKeys
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["bangalore"] = "bengaluru",
        ["bombay"] = "mumbai",
        ["calcutta"] = "kolkata",
        ["madras"] = "chennai",
        ["gurgaon"] = "gurugram",
        ["orissa"] = "odisha",
        ["pondicherry"] = "puducherry",
    };

    public static string Of(string name)
    {
        var text = new StringBuilder();
        foreach (var letter in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(letter))
            {
                text.Append(char.ToLowerInvariant(letter));
            }
            else if (text.Length > 0 && text[^1] != '-')
            {
                text.Append('-');
            }
        }

        var key = text.ToString().Trim('-');
        return Aliases.GetValueOrDefault(key, key);
    }
}
