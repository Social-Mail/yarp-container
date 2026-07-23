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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace DotNetReverseProxy;

public class Forwarder: IMiddleware
{
    private static readonly TimeSpan TrackExpiration = TimeSpan.FromMinutes(15);

    private readonly IHttpForwarder forwarder;
    private readonly ForwarderRequestConfig requestOptions;
    private readonly HttpMessageInvoker client;
    private readonly ReverseHostFinder hostFinder;
    private readonly JsonLogger logger;
    private readonly StripedCacheService stripedCache;
    private readonly int defaultPenalty;

    public Forwarder(
        CertificateInstaller store,
        IHttpForwarder forwarder,
        ReverseHostFinder hostFinder,
        JsonLogger logger,
        StripedCacheService stripedCache)
    {
        this.forwarder = forwarder;
        this.requestOptions = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) };
        this.hostFinder = hostFinder;
        this.logger = logger;
        this.stripedCache = stripedCache;
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
    }



    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {

        var start = DateTime.UtcNow;

        var error = await forwarder.SendAsync(httpContext, "http://" + httpContext.Request.Headers.Host, client, requestOptions,
            (context, proxyRequest) =>
            {
                var responseHeaders = context.Response.Headers;
                responseHeaders.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
                responseHeaders.TryAdd("X-Content-Type-Options", "nosniff");
                responseHeaders.TryAdd("X-Frame-Options", "SAMEORIGIN");
                responseHeaders.TryAdd("Referrer-Policy", "same-origin");

                // Customize the query string:
                var queryContext = new QueryTransformContext(context.Request);
                var ip = context.Connection.RemoteIpAddress;
                if (ip != null)
                {
                    proxyRequest.Headers.TryAddWithoutValidation("x-forwarded-for", ip.ToString());
                }

                // Assign the custom uri. Be careful about extra slashes when concatenating here. RequestUtilities.MakeDestinationAddress is a safe default.
                proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress("http://" + proxyRequest.Headers.Host, context.Request.Path, queryContext.QueryString);
                proxyRequest.Version = HttpVersion.Version11;
                return default;
            });


        Exception? exception = null;

        // Check if the proxy operation was successful
        if (error != ForwarderError.None)
        {
            var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
            if (errorFeature != null)
            {
                exception = errorFeature.Exception;
                if (exception != null)
                {
                    Console.WriteLine(exception.ToString());
                }
            }
        }

        RegisterStatus(httpContext, DateTime.UtcNow - start, exception);
    }

    void RegisterStatus(HttpContext context, TimeSpan ts, Exception? ex)
    {
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
            var penalty = this.defaultPenalty;
            if(context.Response.Headers.TryGetValue("x-error-penalty", out var p))
            {
                if(int.TryParse(p, out var n))
                {
                    penalty = n;
                }
            }
            
            if(penalty > 0) {
                stripedCache.Update<int?>(cacheKey, (x) => x + penalty, penalty, TrackExpiration);
            }
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
            logger.LogError(new
            {
                status,
                userAgent,
                url = request.GetDisplayUrl(),
                ip = context.Connection.RemoteIpAddress?.ToString(),
                error,
                duration
            });

            var currentCount = stripedCache.Get<int?>(cacheKey);
            if (currentCount == null)
            {
                return;
            }
            if (currentCount <= 1)
            {
                stripedCache.Remove(cacheKey);
                return;
            }
            stripedCache.Update<int?>(cacheKey, (x) => x - 1, 1, TrackExpiration);
        }
    }
}