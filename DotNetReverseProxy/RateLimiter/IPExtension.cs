using System;
using System.Net;
using System.Net.Sockets;

namespace DotNetReverseProxy.RateLimiter;

public static class IpExtensions
{
    public static bool IsLocalOrDocker(this IPAddress ip)
    {
        if (ip == null) return false;

        // 1. Instantly clear out localhost / loopback
        if (IPAddress.IsLoopback(ip)) return true;

        if(ip.IsIPv4MappedToIPv6)
           ip = ip.MapToIPv4();

        // 2. Handle IPv4
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // GetAddressBytes() returns a 4-byte array for IPv4
            Span<byte> bytes = stackalloc byte[4];
            ip.TryWriteBytes(bytes, out var n);
            byte first = bytes[0];
            byte second = bytes[1];

            if (first == 10) return true;                                      // 10.x.x.x (LAN/VPC)
            if (first == 172 && second >= 16 && second <= 31) return true;     // 172.16.x.x - 172.31.x.x (Docker / LAN)
            if (first == 192 && second == 168) return true;                    // 192.168.x.x (Home Wi-Fi/Office LAN)
            if (first == 169 && second == 254) return true;                    // 169.254.x.x (Link-Local)
            if (first == 100 && second >= 64 && second <= 127) return true;    // 100.64.x.x - 100.127.x.x (CGNAT)

            return false;
        }

        // 3. Handle IPv6
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // GetAddressBytes() returns a 16-byte array for IPv6
            Span<byte> bytes = stackalloc byte[16];
            ip.TryWriteBytes(bytes, out var n);
            // byte[] bytes = ip.GetAddressBytes();

            // Unique Local Addresses (fc00::/7) -> starts with 0xFC or 0xFD
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            // Link-Local Addresses (fe80::/10) -> starts with 0xFE and next 2 bits are 10 (0x80)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;

            return false;
        }

        return false;
    }
}