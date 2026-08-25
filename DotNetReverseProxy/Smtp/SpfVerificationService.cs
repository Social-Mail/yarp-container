using DotNetReverseProxy.Spf;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Smtp;

public class SpfVerificationService
{
    private readonly IMemoryCache cache;

    public SpfVerificationService(IMemoryCache cache)
    {
        this.cache = cache;
    }

    async Task<SpfValidator> GetSpf(string domain)
    {
        return null;
    }

    internal async Task VerifyAsync(
        string? from,
        string remoteAddress,
        string hostNameAppearsAs,
        string clientHostName)
    {
        
    }
}
