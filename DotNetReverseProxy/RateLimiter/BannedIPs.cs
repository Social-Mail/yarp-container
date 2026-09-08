using Microsoft.Extensions.Caching.Memory;
using System;
using System.Net;

namespace DotNetReverseProxy.RateLimiter;

public class BannedIPs
{
    private readonly IMemoryCache cache;

    public BannedIPs(IMemoryCache cache)
    {
        this.cache = cache;
    }

    public void Add(IPAddress ip)
    {
        cache.GetOrCreate($"banned-{ip}", (x) =>
        {
            x.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return ip;
        });
    }

    public bool IsBanned(IPAddress ip)
    {
        return cache.TryGetValue($"banned-{ip}", out var _v);
    }

}
