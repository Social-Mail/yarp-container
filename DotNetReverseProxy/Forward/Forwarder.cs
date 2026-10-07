using DotNetReverseProxy.Forward;
using DotNetReverseProxy.HostLookup;
using DotNetReverseProxy.RateLimiter;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NeuroSpeech;
using NeuroSpeech.Acme;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace DotNetReverseProxy;

public class Forwarder: IMiddleware
{
    private static readonly TimeSpan TrackExpiration = TimeSpan.FromMinutes(15);

    private readonly IHttpForwarder forwarder;
    private readonly ForwarderRequestConfig requestOptions;
    private readonly HttpMessageInvoker client;
    private readonly SecurityHeaderForwarder st;
    private readonly PartitionedRateLimiter<HttpContext> Limiter;
    private readonly ReverseHostFinder hostFinder;
    private readonly JsonLogger logger;
    private readonly ConcurrentIPCache ipCache;
    private readonly BannedIPs bannedIPs;
    private readonly int defaultPenalty;

    public Forwarder(
        CertificateInstaller store,
        IHttpForwarder forwarder,
        ReverseHostFinder hostFinder,
        JsonLogger logger,
        ConcurrentIPCache ipCache,
        BannedIPs bannedIPs
        )
    {
        this.forwarder = forwarder;
        this.requestOptions = new ForwarderRequestConfig {
            ActivityTimeout = TimeSpan.FromSeconds(180)
        };
        this.hostFinder = hostFinder;
        this.logger = logger;
        this.ipCache = ipCache;
        this.bannedIPs = bannedIPs;
        this.defaultPenalty = int.TryParse(System.Environment.GetEnvironmentVariable("FORWARD_ERROR_PENALTY") ?? "1", out var n) ? n : 1;
        this.client = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            ConnectCallback = hostFinder.ConnectAsync
        });
        this.st = new SecurityHeaderForwarder();

        this.Limiter = this.CreateRateLimiter();
    }

    private PartitionedRateLimiter<HttpContext> CreateRateLimiter()
    {

        var readRequestRegEx = new Regex("^(GET|HEAD|OPTIONS)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var maxPenaltyPerSecond = int.TryParse(System.Environment.GetEnvironmentVariable("FORWARD_MAX_ERROR_PENALTY") ?? "60", out var n) ? n : 60;

        var noRateLimiterHeader = System.Environment.GetEnvironmentVariable("FORWARD_DISABLE_RATE_LIMITER_HEADER");
        var noRateLimiterHeaderValue = System.Environment.GetEnvironmentVariable("FORWARD_DISABLE_RATE_LIMITER_HEADER_VALUE");

        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            if (noRateLimiterHeader != null)
            {
                if (httpContext.Request.Headers.TryGetValue(noRateLimiterHeader, out var h))
                {
                    if (noRateLimiterHeaderValue == h.ToString())
                    {
                        httpContext.Items.TryAdd("no-rate-limit", "yes");
                        return RateLimitPartition.GetNoLimiter("bypass");
                    }
                }
            }

            var cacheKey = httpContext.Connection.RemoteIpAddress;

            if (maxPenaltyPerSecond == 0 || cacheKey == null || cacheKey.IsLocalOrDocker())
            {
                httpContext.Items.TryAdd("no-rate-limit", "yes");
                return RateLimitPartition.GetNoLimiter("bypass");
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

        });
    }

    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        var request = httpContext.Request;
        if (!request.IsHttps)
        {
            // send redirect...
            var url = httpContext.Request.GetDisplayUrl();
            httpContext.Response.Redirect(url.Replace("http://", "https://"));
            return;
        }

        var cacheKey = httpContext.Connection.RemoteIpAddress;
        if(cacheKey != null)
        {
            if(bannedIPs.IsBanned(cacheKey))
            {
                var response = httpContext.Response;
                response.StatusCode = 409;
                await response.WriteAsync("Too many bad requests from your computer, please try after 15 minutes");
                await response.CompleteAsync();
                return;
            }
        }

        using RateLimitLease lease = await Limiter.AcquireAsync(httpContext, permitCount: 0, httpContext.RequestAborted);

        if (!lease.IsAcquired)
        {
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status429TooManyRequests;
            await response.WriteAsync("Too many bad requests from your computer, please try after 15 minutes");
            await response.CompleteAsync();
            return;
        }

        var start = DateTime.UtcNow;

        var p = hostFinder.GetPort(request.Headers.Host!);
        if(p == null)
        {
            var response = httpContext.Response;
            RegisterStatus(httpContext, DateTime.UtcNow - start, null);
            response.StatusCode = 404;
            await response.WriteAsync("Host Not Found");
            await response.CompleteAsync();
            return;
        }


        var error = await forwarder.SendAsync(httpContext, "http://" + request.Headers.Host, client, requestOptions,st);


        Exception? exception = null;

        // Check if the proxy operation was successful
        if (error != ForwarderError.None)
        {
            var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
            if (errorFeature != null)
            {
                exception = errorFeature.Exception;
            }
        }

        RegisterStatus(httpContext, DateTime.UtcNow - start, exception);
    }

    void RegisterStatus(HttpContext context, TimeSpan ts, Exception? ex)
    {
        var cancelled = ex is TaskCanceledException || ex is OperationCanceledException;
        if (cancelled)
        {
            return;
        }

        var cacheKey = context.Connection.RemoteIpAddress;
        if (cacheKey == null)
        {
            return;
        }

        var request = context.Request;
        var response = context.Response;
        var status = response.StatusCode;
        var userAgent = request.Headers.UserAgent.ToString();
        var time = DateTime.UtcNow;
        var error = ex?.ToString();

        if (status >= 400 && !context.Items.ContainsKey("no-rate-limit"))
        {
            var penalty = this.defaultPenalty;
            if(response.Headers.TryGetValue("x-error-penalty", out var p))
            {
                if(int.TryParse(p, out var n))
                {
                    penalty = n;
                }
            }
            
            if(penalty > 0) {
                // double penalty for 404 if penalty is 1
                if (penalty == 1 && status == 404)
                {
                    penalty = 2;

                }
                var n = ipCache.GetOrUpdate(cacheKey, (x) => penalty, (x, p) => p + penalty);
                if(n >= ipCache.MaxPenalty)
                {
                    bannedIPs.Add(cacheKey);
                    try
                    {
                        var connectionLifetime = context.Features.Get<Microsoft.AspNetCore.Connections.Features.IConnectionSocketFeature>();
                        if(connectionLifetime != null)
                        {
                            logger.DebugLogger?.Log(new {
                                socket = "closed"
                            });   
                            connectionLifetime.Socket.Close();
                        }
                    } catch { }
                }
            }

            var duration = ts.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture) + "ms";
            logger.LogError(new
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

            // we will only track longer requests...
            if (ts.TotalSeconds > 5)
            {
                var duration = ts.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture) + "ms";
                logger.LogError(new
                {
                    status,
                    userAgent,
                    url = request.GetDisplayUrl(),
                    ip = context.Connection.RemoteIpAddress?.ToString(),
                    error,
                    duration
                });
            }

            // ipCache.GetOrUpdate(cacheKey, (x) => 0, (x, p) => p - 1);
            ipCache.RegisterSuccess(cacheKey);
        }
    }
}