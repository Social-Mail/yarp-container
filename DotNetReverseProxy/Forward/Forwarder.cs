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
using DotNetReverseProxy.Forward;
using DotNetReverseProxy.HostLookup;
using DotNetReverseProxy.RateLimiter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using NeuroSpeech;
using NeuroSpeech.Acme;
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
                await response.WriteAsync("Too many connections...");
                await response.CompleteAsync();
                return;
            }
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
                            logger.Log(new {
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