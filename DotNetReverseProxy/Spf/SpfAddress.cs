using System.Net;

namespace DotNetReverseProxy.Spf;

public class SpfAddress
{
    public static SpfAddress From(string ip, string? suffix) => ip.Contains(':')
        ? new SpfAddress { IPv6 = ip, Prefix = suffix }
        : new SpfAddress { IPv4 = ip, Prefix = suffix };

    public string? IPv4 { get; set; }

    public string? IPv6 { get; set; }

    public string? Prefix { get; set; }

    public IPNetwork ToNetwork()
    {
        if (IPv4 != null)
        {
            return IPNetwork.Parse($"{IPv4}/{Prefix ?? "32"}");
        }
        return IPNetwork.Parse($"{IPv6}/{Prefix ?? "128"}");
    }
}
