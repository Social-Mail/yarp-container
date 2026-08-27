using Amazon.Runtime.Internal.Util;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Net;

namespace DotNetReverseProxy.RateLimiter;

public class RemoteIPCacheStore
{
    private static readonly TimeSpan TrackExpiration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentIPCache ipCache;

    public RemoteIPCacheStore(ILoggerFactory? externalLoggerFactory = null)
    {
        ipCache = new ();
    }

    public void ReportNoError(IPAddress? ip)
    {
        if(ip == null)
        {
            return;
        }

        var n = ipCache.GetOrUpdate(ip, (x) => 0, (x, y) => y - 1);
        
    }
}
