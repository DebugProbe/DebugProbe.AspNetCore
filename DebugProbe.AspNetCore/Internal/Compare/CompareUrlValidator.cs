using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Internal.Compare;

internal static class CompareUrlValidator
{
    public static async Task<(bool IsValid, Uri? BaseUri, string Error)> ValidateCompareBaseUrlAsync(string baseUrl, DebugProbeOptions options)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
        {
            return (false, null, "Invalid compare server URL");
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return (false, null, "Compare server URL must use http or https");
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return (false, null, "Compare server URL cannot include credentials");
        }

        System.Net.IPAddress[] addresses;

        try
        {
            addresses = System.Net.IPAddress.TryParse(parsed.Host, out var ipAddress)
                ? [ipAddress]
                : await System.Net.Dns.GetHostAddressesAsync(parsed.DnsSafeHost);
        }
        catch
        {
            return (false, null, "Failed to resolve compare server host");
        }

        if (options.AllowLocalCompareTargets != true)
        {
            if (IsLocalHostName(parsed.Host))
            {
                return (false, null, "Compare server URL cannot target localhost");
            }

            if (addresses.Length == 0 || addresses.Any(IsPrivateOrLocalAddress))
            {
                return (false, null, "Compare server URL cannot target local or private network addresses");
            }
        }

        return (true, new Uri(parsed.GetLeftPart(UriPartial.Authority)), string.Empty);
    }

    private static bool IsLocalHostName(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateOrLocalAddress(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address) ||
            System.Net.IPAddress.Any.Equals(address) ||
            System.Net.IPAddress.None.Equals(address) ||
            System.Net.IPAddress.Broadcast.Equals(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 0 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127 ||
                   bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();

            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal ||
                   address.IsIPv6Multicast ||
                   System.Net.IPAddress.IPv6Any.Equals(address) ||
                   System.Net.IPAddress.IPv6None.Equals(address) ||
                   bytes[0] is >= 0xfc and <= 0xfd;
        }

        return true;
    }
}
