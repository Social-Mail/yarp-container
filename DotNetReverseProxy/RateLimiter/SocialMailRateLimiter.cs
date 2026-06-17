using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace DotNetReverseProxy;

public static class SocialMailRateLimiter
{

    private static readonly TimeSpan TrackExpiration = TimeSpan.FromMinutes(15);

    private static string CacheKey(this HttpContext context)
    {
        return $"HttpStatus_Error_{context.Connection?.RemoteIpAddress?.ToString() ?? "0.0.0.0"}";
    }

    public static void RegisterStatus(this HttpContext context, TimeSpan ts, Exception? ex)
    {
        var cache = context.RequestServices.GetService<StripedCacheService>()!;
        var cacheKey = context.CacheKey();
        var request = context.Request;
        var response = context.Response;
        var status = response.StatusCode;
        var userAgent = request.Headers.UserAgent.ToString();
        var duration = ts.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture) + "ms";
        var time = DateTime.UtcNow;
        var error = ex?.ToString();
        if (context.Response.StatusCode >= 400)
        {
            var v = cache.Update<int?>(cacheKey, (x) => x + 1, 1, TrackExpiration);
            JsonLogger.Instance.LogError(new
            {
                status,
                userAgent,
                url = request.GetDisplayUrl(),
                ip = context.Connection.RemoteIpAddress?.ToString(),
                error,
                duration
            });
        }
        else
        {
            JsonLogger.Instance.LogError(new
            {
                status,
                userAgent,
                url = request.GetDisplayUrl(),
                ip = context.Connection.RemoteIpAddress?.ToString(),
                error,
                duration
            });

            var currentCount = cache.Get<int?>(cacheKey);
            if (currentCount == null)
            {
                return;
            }
            if (currentCount <= 1)
            {
                cache.Remove(cacheKey);
                return;
            }
            cache.Update<int?>(cacheKey, (x) => x - 1, 1, TrackExpiration);
        }
    }

    public static void AddSocialMailRateLimiter(this IServiceCollection services)
    {
        services.AddSingleton<StripedCacheService>();

        var readRequestRegEx = new Regex("^(GET|HEAD|OPTIONS)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        services.AddRateLimiter(rl => {
            rl.RejectionStatusCode  = StatusCodes.Status429TooManyRequests;
            rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var cacheKey = httpContext.CacheKey();

                    var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();

                    var errorCount = cache.Get<int?>(cacheKey) ?? 0;
                    if (errorCount > 60)
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
