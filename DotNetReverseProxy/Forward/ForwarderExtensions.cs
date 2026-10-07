using DotNetReverseProxy.RateLimiter;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetReverseProxy;

public static class ForwarderExtensions
{

    public static IServiceCollection AddForwarder(this IServiceCollection services)
    {
        services.AddSingleton<BannedIPs>();
        services.AddSingleton<ConcurrentIPCache>();
        services.AddSingleton<Forwarder>();
        return services;
    }

}

