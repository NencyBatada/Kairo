using System.Globalization;

namespace Kairo.Modules.Live.Delivery;

internal sealed record LivePosition(long? AfterId, DateTimeOffset? Since)
{
    private static readonly long LatestMark = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    public static string Mark(DateTimeOffset at) => "t" + at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    public static LivePosition? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value[0] == 't')
        {
            return long.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds <= LatestMark
                ? new LivePosition(null, DateTimeOffset.FromUnixTimeMilliseconds(milliseconds))
                : null;
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? new LivePosition(id, null) : null;
    }
}
