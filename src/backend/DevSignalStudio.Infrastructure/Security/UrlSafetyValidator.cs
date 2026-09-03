using System.Net;
using System.Net.Sockets;

namespace DevSignalStudio.Infrastructure.Security;

public sealed class UrlSafetyValidator
{
    public async Task<Uri> ValidateAsync(
        string value,
        bool allowLoopback,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Only absolute http and https URLs are supported.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("URLs containing embedded credentials are not allowed.");
        }

        string host = uri.DnsSafeHost;
        bool localName = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
        if (localName && !allowLoopback)
        {
            throw new InvalidOperationException("Local and private hosts are not allowed for remote content sources.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(cancellationToken);
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"The host '{host}' did not resolve to an IP address.");
        }

        foreach (IPAddress address in addresses)
        {
            if (IsLoopback(address))
            {
                if (!allowLoopback)
                {
                    throw new InvalidOperationException("Loopback URLs are not allowed for remote content sources.");
                }
                continue;
            }

            if (IsPrivateOrSpecial(address))
            {
                throw new InvalidOperationException(
                    "The URL resolved to a private, link-local, multicast, or special-use address.");
            }
        }

        return uri;
    }

    private static bool IsLoopback(IPAddress address)
    {
        IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(normalized);
    }

    private static bool IsPrivateOrSpecial(IPAddress address)
    {
        IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (normalized.Equals(IPAddress.Any) || normalized.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (normalized.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = normalized.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => true,
                100 when bytes[1] is >= 64 and <= 127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                >= 224 => true,
                _ => false
            };
        }

        if (normalized.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = normalized.GetAddressBytes();
            bool uniqueLocal = (bytes[0] & 0xFE) == 0xFC;
            return normalized.IsIPv6LinkLocal ||
                normalized.IsIPv6Multicast ||
                normalized.IsIPv6SiteLocal ||
                uniqueLocal;
        }

        return true;
    }
}
