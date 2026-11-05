using System.Net;
using System.Net.Sockets;

namespace Webhooks.Security;

/// <summary>
/// Which IP addresses a webhook may NEVER reach (SSRF): anything that is not a plain public unicast address.
/// IPv6 forms that EMBED an IPv4 address (IPv4-mapped <c>::ffff:a.b.c.d</c>, IPv4-compatible <c>::a.b.c.d</c>,
/// NAT64 <c>64:ff9b::/96</c>, 6to4 <c>2002::/16</c>) are judged by the embedded IPv4 address, so a private target can
/// not be smuggled in through them.
/// </summary>
public static class NetworkAddressPolicy
{
    /// <summary>True when connecting to <paramref name="address"/> must be refused.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IsBlockedV6(address);
        }

        return address.AddressFamily != AddressFamily.InterNetwork || IsBlockedV4(address.GetAddressBytes());
    }

    private static bool IsBlockedV4(ReadOnlySpan<byte> b)
    {
        var (a, c, d) = (b[0], b[1], b[2]);
        return a == 0                                              // 0.0.0.0/8 ("this network")
               || a == 10                                          // 10/8
               || (a == 100 && c is >= 64 and <= 127)              // 100.64/10 carrier-grade NAT
               || a == 127                                         // loopback
               || (a == 169 && c == 254)                           // link-local incl. cloud metadata 169.254.169.254
               || (a == 172 && c is >= 16 and <= 31)               // 172.16/12
               || (a == 192 && c == 0 && d == 0)                   // 192.0.0.0/24 IETF protocol assignments
               || (a == 192 && c == 0 && d == 2)                   // 192.0.2/24 TEST-NET-1
               || (a == 192 && c == 168)                           // 192.168/16
               || (a == 198 && c is 18 or 19)                      // 198.18/15 benchmarking
               || (a == 198 && c == 51 && d == 100)                // TEST-NET-2
               || (a == 203 && c == 0 && d == 113)                 // TEST-NET-3
               || a >= 224;                                        // multicast 224/4, reserved 240/4, broadcast
    }

    private static bool IsBlockedV6(IPAddress address)
    {
        // IPv4-mapped (::ffff:a.b.c.d) - judge the embedded IPv4.
        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedV4(address.MapToIPv4().GetAddressBytes());
        }

        Span<byte> b = stackalloc byte[16];
        if (!address.TryWriteBytes(b, out _))
        {
            return true;
        }

        // :: (unspecified) and ::1 (loopback).
        var leadingZeros = b[..12].IndexOfAnyExcept((byte)0) < 0;
        if (leadingZeros)
        {
            // ::/96 holds ::, ::1 and the deprecated IPv4-compatible ::a.b.c.d - none is a legitimate public target.
            return true;
        }

        // 64:ff9b::/96 NAT64 well-known prefix - embedded IPv4 in the last 4 bytes.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].IndexOfAnyExcept((byte)0) < 0)
        {
            return IsBlockedV4(b[12..16]);
        }

        // 6to4 2002::/16 - the IPv4 address sits in bytes 2..5.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsBlockedV4(b[2..6]);
        }

        // Teredo 2001:0::/32 tunnels to arbitrary IPv4 hosts; not a webhook target.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
        {
            return true;
        }

        return (b[0] & 0xFE) == 0xFC                                // fc00::/7 unique local
               || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80)           // fe80::/10 link-local
               || (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0)           // fec0::/10 site-local (deprecated)
               || b[0] == 0xFF                                      // ff00::/8 multicast
               || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8); // 2001:db8::/32 documentation
    }
}
