using DotNetReverseProxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

try
{

#pragma warning disable CA2252 // This API requires opting into preview features
    if (QuicListener.IsSupported)
    {
        Console.Out.WriteLine("Quic is available");
    }
    else
    {
        Console.Out.WriteLine("Quic is not available");
    }
#pragma warning restore CA2252 // This API requires opting into preview features

    var weakTable = new ConditionalWeakTable<object, UnixDomainSocketEndPoint>();

    MemoryCache cache = new MemoryCache(new MemoryCacheOptions { });

    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
    });

    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    // Alternatively, selectively enforce it for all categories
    builder.Logging.AddFilter(null, LogLevel.Warning); 

    builder.WebHost.ConfigureKestrel(kestrel =>
    {

        var store = kestrel.ApplicationServices.GetRequiredService<CertificateStore>();

        var tls = new TlsHandshakeCallbackOptions
        {
            OnConnection = async (c) =>
            {
                var fwd = await store.GetCertificate(c.ClientHelloInfo.ServerName);
                var ctx = cache.GetOrCreate(fwd.Cert, (ci) =>
                {
                    var xCert = X509Certificate2.CreateFromPem(fwd.Cert, fwd.Key);

                    ci.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);

                    return new SslServerAuthenticationOptions
                    {
                        ServerCertificate = xCert,
                        AllowTlsResume = true,
                        ApplicationProtocols = new List<SslApplicationProtocol> {
                            SslApplicationProtocol.Http11,
                            SslApplicationProtocol.Http2,
                            SslApplicationProtocol.Http3 },
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                    };
                });
                return ctx;
            },
        };


        var ip = new IPAddress([0, 0, 0, 0]);
        kestrel.Listen(ip, 443, portOptions =>
        {
            portOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            portOptions.UseHttps(tls);
        });

    });

    builder.Services.AddMemoryCache();
    builder.Services.AddHttpForwarder();
    builder.Services.AddSingleton<CertificateStore>();
    builder.Services.AddSingleton<Forwarder>();
    builder.Services.AddResponseCompression((options) =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();

        options.MimeTypes = ResponseCompressionDefaults.MimeTypes
            .Concat(["image/svg+xml"]);
    });

    builder.Services.AddSocialMailRateLimiter();

    var app = builder.Build();

    // Setup our own request transform class
    var requestOptions = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) };
    // app.UseHsts();
    app.UseResponseCompression();
    app.UseRouting();
    app.UseSocialMailRateLimiter();
    app.UseMiddleware<Forwarder>();

    // // When using IHttpForwarder for direct forwarding you are responsible for routing, destination discovery, load balancing, affinity, etc..
    // // For an alternate example that includes those features see BasicYarpSample.
    // app.Map("/{**catch-all}", async (HttpContext httpContext, IHttpForwarder forwarder) =>
    // {

    //     var start = DateTime.UtcNow;
    //     var forwarder = httpContext.RequestServices.GetRequiredService<Forwarder>();

    //     var error = await forwarder.SendAsync(httpContext, "http://" + httpContext.Request.Headers.Host, httpClient, requestOptions,
    //         (context, proxyRequest) =>
    //         {
    //             var responseHeaders = context.Response.Headers;
    //             responseHeaders.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");
    //             responseHeaders.TryAdd("X-Content-Type-Options", "nosniff");
    //             responseHeaders.TryAdd("X-Frame-Options", "SAMEORIGIN");
    //             responseHeaders.TryAdd("Referrer-Policy", "same-origin");

    //             // Customize the query string:
    //             var queryContext = new QueryTransformContext(context.Request);
    //             var ip = context.Connection.RemoteIpAddress;
    //             if (ip != null)
    //             {
    //                 proxyRequest.Headers.TryAddWithoutValidation("x-forwarded-for", ip.ToString());
    //             }

    //             // Assign the custom uri. Be careful about extra slashes when concatenating here. RequestUtilities.MakeDestinationAddress is a safe default.
    //             proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress("http://" + proxyRequest.Headers.Host, context.Request.Path, queryContext.QueryString);
    //             proxyRequest.Version = HttpVersion.Version11;
    //             return default;
    //         });


    //     Exception? exception = null;

    //     // Check if the proxy operation was successful
    //     if (error != ForwarderError.None)
    //     {
    //         var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
    //         if (errorFeature != null)
    //         {
    //             exception = errorFeature.Exception;
    //             if (exception != null)
    //             {
    //                 Console.WriteLine(exception.ToString());
    //             }
    //         }
    //     }

    //     httpContext.RegisterStatus(DateTime.UtcNow - start, exception);

    // });

    app.Run();



}
catch (Exception ex)
{
    Console.WriteLine(ex);
    throw new Exception("Closed", ex);
}
