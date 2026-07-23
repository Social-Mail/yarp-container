using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

namespace DotNetReverseProxy;

public static class SocialMailRateLimiter
{

    public static string CacheKey(this HttpContext context)
    {
        return ToCacheKey(context.Connection?.RemoteIpAddress?.ToString() ?? "0.0.0.0");
    }

    public static string ToCacheKey(string ipAddress)
    {
        return $"HttpStatus_Error_{ipAddress}";
    }


    public static void AddSocialMailRateLimiter(this IServiceCollection services)
    {
        services.AddSingleton<StripedCacheService>();

        var readRequestRegEx = new Regex("^(GET|HEAD|OPTIONS)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var skipIPs = (System.Environment.GetEnvironmentVariable("FORWARD_NO_RATE_LIMIT_IP_ADDRESSES") ?? "").Split(",", StringSplitOptions.RemoveEmptyEntries);

        var allowedIPs = new HashSet<string>(skipIPs.Select(ToCacheKey));

        var maxPenaltyPerSecond = int.TryParse(System.Environment.GetEnvironmentVariable("FORWARD_MAX_ERROR_PENALTY") ?? "60", out var n) ? n : 60;
        
        var noRateLimiterHeader = System.Environment.GetEnvironmentVariable("FORWARD_DISABLE_RATE_LIMITER_HEADER");
        var noRateLimiterHeaderValue = System.Environment.GetEnvironmentVariable("FORWARD_DISABLE_RATE_LIMITER_HEADER_VALUE");

        services.AddRateLimiter(rl => {
            rl.RejectionStatusCode  = StatusCodes.Status429TooManyRequests;
            rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    if(noRateLimiterHeader != null) {
                        if(httpContext.Request.Headers.TryGetValue(noRateLimiterHeader, out var h))
                        {
                            if(noRateLimiterHeaderValue == h.ToString())
                            {
                                return RateLimitPartition.GetNoLimiter("bypass");
                            }
                        }
                    }

                    var cacheKey = httpContext.CacheKey();

                    if (allowedIPs.Contains(cacheKey) || maxPenaltyPerSecond == 0)
                    {
                        return RateLimitPartition.GetNoLimiter("bypass");
                    }

                    var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();

                    var errorCount = cache.Get<int?>(cacheKey) ?? 0;
                    if (errorCount > maxPenaltyPerSecond)
                    {
                        Console.WriteLine($"RateLimited (Penalty): {cacheKey}");
                        return RateLimitPartition.GetTokenBucketLimiter(
                            partitionKey: cacheKey + "_penalty",
                            factory: _ => new TokenBucketRateLimiterOptions
                            {
                                TokenLimit = 1,                             // Hard limit of 1 request
                                TokensPerPeriod = 1,
                                ReplenishmentPeriod = TimeSpan.FromMinutes(1), // Refills only once per minute
                                AutoReplenishment = true,
                                QueueLimit = 0                              // Dropped instantly if exceeded
                            });
                    }

                    // 2. Read Endpoints Layer (Handles 100s of simultaneous browser requests)
                    if (readRequestRegEx.IsMatch(httpContext.Request.Method))
                    {
                        return RateLimitPartition.GetTokenBucketLimiter(
                            partitionKey: cacheKey + "_read",
                            factory: _ => new TokenBucketRateLimiterOptions
                            {
                                TokenLimit = 500,                           // Absorbs up to 500 requests instantly at startup
                                TokensPerPeriod = 50,                       // Refills 50 tokens every second (500 over 10s)
                                ReplenishmentPeriod = TimeSpan.FromSeconds(1), // Smooth, continuous replenishment
                                AutoReplenishment = true,
                                QueueLimit = 100,                           // Safely queue overflow during deep bursts
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                            });
                    }

                    // 3. Write Endpoints Layer
                    return RateLimitPartition.GetTokenBucketLimiter(
                        partitionKey: cacheKey + "_write",
                        factory: _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = 50,                            // Absorbs up to 50 concurrent state-changes
                            TokensPerPeriod = 5,                        // Refills 5 tokens every second (50 over 10s)
                            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                            AutoReplenishment = true,
                            QueueLimit = 20,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        });

                }
            );
        });

    }

    public static void UseSocialMailRateLimiter(this IApplicationBuilder app)
    {
        app.UseRateLimiter();
    }
}
