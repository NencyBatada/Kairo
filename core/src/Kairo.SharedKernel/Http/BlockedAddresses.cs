using System.Net;
using System.Net.Sockets;

namespace Kairo.SharedKernel.Http;

public static class BlockedAddresses
{
    private static readonly (byte[] Prefix, int Bits)[] V4Ranges =
    [
        ([0, 0, 0, 0], 8),
        ([10, 0, 0, 0], 8),
        ([100, 64, 0, 0], 10),
        ([127, 0, 0, 0], 8),
        ([169, 254, 0, 0], 16),
        ([172, 16, 0, 0], 12),
        ([192, 0, 0, 0], 24),
        ([192, 168, 0, 0], 16),
        ([198, 18, 0, 0], 15),
        ([224, 0, 0, 0], 4),
        ([240, 0, 0, 0], 4),
    ];

    private static readonly byte[] Nat64 = [0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlocked(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return V4Ranges.Any(range => InRange(bytes, range.Prefix, range.Bits));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC)
        {
            return true;
        }

        if (InRange(bytes, Nat64, 96))
        {
            return IsBlocked(new IPAddress(bytes[12..]));
        }

        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return IsBlocked(new IPAddress(bytes[2..6]));
        }

        return bytes[..12].All(part => part == 0);
    }

    private static bool InRange(byte[] address, byte[] prefix, int bits)
    {
        for (var bit = 0; bit < bits; bit++)
        {
            var mask = 0x80 >> (bit % 8);
            if ((address[bit / 8] & mask) != (prefix[bit / 8] & mask))
            {
                return false;
            }
        }

        return true;
    }
}
