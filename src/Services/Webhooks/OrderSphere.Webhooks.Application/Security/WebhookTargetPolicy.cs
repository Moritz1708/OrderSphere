using System.Net;
using System.Net.Sockets;

namespace OrderSphere.Webhooks.Application.Security;

/// <summary>
/// Decides which targets a webhook may be delivered to. The subscriber chooses the URL and the
/// worker that posts to it runs inside the service network, so an unrestricted URL would let a
/// customer make the platform call internal services or cloud metadata endpoints (SSRF).
/// <para>
/// The policy is applied twice. <see cref="IsAllowedUrl"/> rejects obviously internal targets
/// when a subscription is saved. <see cref="IsBlockedAddress"/> is applied to every resolved
/// address when the worker connects; that check is authoritative, because a public host name
/// can resolve — or later re-resolve — to an internal address.
/// </para>
/// </summary>
public static class WebhookTargetPolicy
{
    private static readonly string[] InternalSuffixes =
        [".localhost", ".local", ".internal", ".home.arpa"];

    // Azure platform endpoint (DHCP, DNS, health probes); reachable from inside a VNet.
    private static readonly IPAddress AzureWireServer = IPAddress.Parse("168.63.129.16");

    private static ReadOnlySpan<byte> Nat64Prefix => [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>
    /// Syntax check for a subscription URL: absolute <c>https</c>, no user info, and a host that is
    /// neither a blocked IP literal nor a name that only resolves inside the platform network.
    /// </summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        switch (uri.HostNameType)
        {
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
                return IPAddress.TryParse(uri.DnsSafeHost, out var address) && !IsBlockedAddress(address);

            case UriHostNameType.Dns:
                var host = uri.IdnHost.TrimEnd('.');
                // Single-label names ("localhost", Aspire service names such as "ordersphere-catalog")
                // resolve through service discovery or the cluster's DNS search domains.
                return host.Contains('.')
                    && !InternalSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));

            default:
                return false;
        }
    }

    /// <summary>
    /// True for addresses a webhook must never connect to: loopback, private (RFC 1918), CGNAT,
    /// link-local (including cloud metadata at 169.254.169.254), unspecified, multicast and
    /// reserved ranges, IPv6 unique-local/site-local/link-local, and IPv4 addresses embedded in
    /// IPv6 (mapped, NAT64, 6to4).
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedIPv4(address),
            AddressFamily.InterNetworkV6 => IsBlockedIPv6(address),
            _ => true
        };
    }

    private static bool IsBlockedIPv4(IPAddress address)
    {
        if (address.Equals(AzureWireServer))
            return true;

        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);

        return b[0] switch
        {
            0 => true,                                   // 0.0.0.0/8 "this network"
            10 => true,                                  // 10.0.0.0/8 private
            100 => b[1] is >= 64 and <= 127,             // 100.64.0.0/10 carrier-grade NAT
            127 => true,                                 // 127.0.0.0/8 loopback
            169 => b[1] == 254,                          // 169.254.0.0/16 link-local, cloud metadata
            172 => b[1] is >= 16 and <= 31,              // 172.16.0.0/12 private
            192 => b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2), // 192.168/16 private, 192.0.0/24, 192.0.2/24
            198 => b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100), // 198.18/15 benchmarking, 198.51.100/24
            203 => b[1] == 0 && b[2] == 113,             // 203.0.113.0/24 documentation
            >= 224 => true,                              // multicast, reserved, broadcast
            _ => false
        };
    }

    private static bool IsBlockedIPv6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6UniqueLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        Span<byte> b = stackalloc byte[16];
        address.TryWriteBytes(b, out _);

        // 64:ff9b::/96 (NAT64) carries the IPv4 target in the last four bytes.
        if (b[..12].SequenceEqual(Nat64Prefix))
            return IsBlockedIPv4(new IPAddress(b[12..]));

        // 2002::/16 (6to4) carries the IPv4 relay in bytes 2..5.
        if (b[0] == 0x20 && b[1] == 0x02)
            return IsBlockedIPv4(new IPAddress(b[2..6]));

        // ::/96 (deprecated IPv4-compatible) — nothing public lives there.
        return b[..12].IndexOfAnyExcept((byte)0) < 0;
    }
}
