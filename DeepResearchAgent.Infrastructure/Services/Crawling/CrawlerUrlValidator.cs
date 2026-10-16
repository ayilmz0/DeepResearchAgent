
using System.Net;
using System.Net.Sockets;

namespace DeepResearchAgent.Infrastructure.Services.Crawling;

public static class CrawlerUrlValidator
{
    public static async Task ValidateAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Geçersiz URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Yalnızca HTTP ve HTTPS URL'leri desteklenir.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "Kimlik bilgisi içeren URL'lere izin verilmez.");
        }

        await ResolveSafeAddressesAsync(
            uri.DnsSafeHost,
            cancellationToken);
    }

    public static async Task<IPAddress[]> ResolveSafeAddressesAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        host = host.Trim('[', ']');

        if (string.IsNullOrWhiteSpace(host) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Yerel host adlarına erişime izin verilmez.");
        }

        IPAddress[] addresses;

        try
        {
            if (IPAddress.TryParse(host, out var literalAddress))
            {
                addresses = [literalAddress];
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(
                    host,
                    cancellationToken);
            }
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                "URL'nin sunucu adresi çözümlenemedi.", ex);
        }

        if (addresses.Length == 0 ||
            addresses.Any(IsUnsafeAddress))
        {
            throw new InvalidOperationException(
                "Yerel, özel veya ayrılmış IP adreslerine erişime izin verilmez.");
        }

        return addresses;
    }

    private static bool IsUnsafeAddress(IPAddress address)
    {
        // IPv4-mapped IPv6 adreslerini önce IPv4'e dönüştür.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.IPv6Loopback))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            // IPv4 özel, yerel, dokümantasyon ve ayrılmış aralıklar.
            return b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)
                || (b[0] == 192 && b[1] == 88 && b[2] == 99)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                || b[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();

            // Genel IPv6 unicast alanı: 2000::/3.
            // Bu aralığın dışındaki adresleri varsayılan olarak reddet.
            var isGlobalUnicastRange =
                b[0] >= 0x20 && b[0] <= 0x3F;

            if (!isGlobalUnicastRange)
            {
                return true;
            }

            // 2001::/23: özel amaçlı adresler için kısıtlayıcı politika.
            var isSpecial2001Range =
                b[0] == 0x20 &&
                b[1] == 0x01 &&
                (b[2] & 0xFE) == 0x00;

            // Dokümantasyon: 2001:db8::/32.
            var isDocumentationRange =
                b[0] == 0x20 &&
                b[1] == 0x01 &&
                b[2] == 0x0D &&
                b[3] == 0xB8;

            // 6to4: 2002::/16.
            var isSixToFourRange =
                b[0] == 0x20 &&
                b[1] == 0x02;

            // IPv4/IPv6 çeviri önekleri.
            var isIpv4TranslationRange =
                b[0] == 0x00 &&
                b[1] == 0x64 &&
                b[2] == 0xFF &&
                b[3] == 0x9B;

            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC
                || isSpecial2001Range
                || isDocumentationRange
                || isSixToFourRange
                || isIpv4TranslationRange;
        }

        // Tanınmayan adres ailelerini varsayılan olarak reddet.
        return true;
    }
}
