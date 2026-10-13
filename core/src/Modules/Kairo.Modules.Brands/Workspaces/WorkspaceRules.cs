using System.Globalization;
using System.Text.RegularExpressions;

namespace Kairo.Modules.Brands.Workspaces;

internal static partial class WorkspaceRules
{
    private static readonly HashSet<string> ReservedHandles =
        ["api", "health", "sign-in", "sign-out", "welcome", "settings", "new", "admin", "app", "help"];

    private static readonly HashSet<string> Currencies = LoadCurrencies();

    public static string? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter the brand's name.";
        }

        return name.Trim().Length > 100 ? "Use 100 characters or fewer." : null;
    }

    public static string? CheckHandle(string? handle)
    {
        if (string.IsNullOrEmpty(handle))
        {
            return "Choose a handle.";
        }

        if (!HandlePattern().IsMatch(handle))
        {
            return "Use 3 to 40 lowercase letters, digits and single hyphens, starting with a letter.";
        }

        return ReservedHandles.Contains(handle) ? "That handle is reserved." : null;
    }

    public static string? CheckTimeZone(string? timeZone) =>
        timeZone is not null
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _)
            ? null
            : "Choose a time zone from the list.";

    public static string? CheckCurrency(string? currency) =>
        currency is not null && Currencies.Contains(currency) ? null : "Choose a currency from the list.";

    public static Dictionary<string, string[]> Errors(params (string Field, string? Error)[] checks) =>
        checks
            .Where(check => check.Error is not null)
            .ToDictionary(check => check.Field, check => new[] { check.Error! });

    private static HashSet<string> LoadCurrencies()
    {
        var currencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                currencies.Add(new RegionInfo(culture.Name).ISOCurrencySymbol);
            }
            catch (ArgumentException)
            {
            }
        }

        return currencies;
    }

    [GeneratedRegex("^[a-z](?:[a-z0-9]|-(?=[a-z0-9])){2,39}$")]
    private static partial Regex HandlePattern();
}
