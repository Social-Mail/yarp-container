using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net;

namespace DotNetReverseProxy.RateLimiter;

public class BannedIPs
{
    private readonly IMemoryCache cache;
    private readonly KernelIpBlocker ipBlocker;

    public BannedIPs(IMemoryCache cache, KernelIpBlocker ipBlocker)
    {
        this.cache = cache;
        this.ipBlocker = ipBlocker;
    }

    public void Add(IPAddress ip)
    {
        cache.Set($"banned-{ip}", ip, TimeSpan.FromMinutes(5));
        this.ipBlocker.BanFor15Minutes(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString());
        // KernelIpBlocker.BanFor15Minutes( ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString());
        //cache.GetOrCreate($"banned-{ip}", (x) =>
        //{
        //    x.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        //    return ip;
        //});
    }

    public bool IsBanned(IPAddress ip)
    {
        return cache.TryGetValue($"banned-{ip}", out var _v);
    }

}
