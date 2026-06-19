using DotNetReverseProxy;
using DotNetReverseProxy.Forward;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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

    // this cache is for TLS resumption
    // this is not certificate store
    MemoryCache tlsCache = new MemoryCache(new MemoryCacheOptions { });

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
                var cert = await store.GetAsync(c.ClientHelloInfo.ServerName);
                var ctx = tlsCache.GetOrCreate(cert.Thumbprint, (ci) =>
                {

                    ci.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);

                    return new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
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

        kestrel.Listen(ip, 80, portOptions =>
        {
            portOptions.Protocols = HttpProtocols.Http1;
        });

    });

    builder.Services.AddMemoryCache();
    builder.Services.AddHttpForwarder();
    builder.Services.AddSingleton<JsonLogger>();
    builder.Services.AddSingleton<CertificateStore>();
    builder.Services.AddSingleton<CertificateInstaller>();
    builder.Services.AddSingleton<ReverseHostFinder>();
    builder.Services.AddResponseCompression((options) =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();

        options.MimeTypes = ResponseCompressionDefaults.MimeTypes
            .Concat(["image/svg+xml"]);
    });

    builder.Services.AddSocialMailRateLimiter();
    builder.Services.AddSingleton<Forwarder>();

    var app = builder.Build();

    // we need to use this as soon as possible...
    app.UseMiddleware<CertificateInstaller>();

    app.UseResponseCompression();
    app.UseRouting();
    app.UseSocialMailRateLimiter();

    var rhf = app.Services.GetRequiredService<ReverseHostFinder>();
    await rhf.InitAsync();

    app.UseMiddleware<Forwarder>();

    app.Run();

}
catch (Exception ex)
{
    Console.WriteLine(ex);

    await Task.Delay(TimeSpan.FromMinutes(1));

    throw new Exception("Closed", ex);
}
