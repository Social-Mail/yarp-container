using DotNetReverseProxy;
using DotNetReverseProxy.ForwardSmtp;
using DotNetReverseProxy.HostLookup;
using DotNetReverseProxy.Tls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeuroSpeech;
using NeuroSpeech.Acme;
using NeuroSpeech.Smtp;
using System;
using System.Linq;
using System.Net.Quic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

try
{

    if (!QuicListener.IsSupported) {
        Console.Out.WriteLine("Quic is not available");
    }

    var weakTable = new ConditionalWeakTable<object, UnixDomainSocketEndPoint>();

    // this cache is for TLS resumption
    // this is not certificate store
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

        kestrel.Limits.MaxRequestBodySize = 501*1024*1024;
        var tlsContext = kestrel.ApplicationServices.GetRequiredService<TlsContext>();

        var tls = new TlsHandshakeCallbackOptions
        {
            OnConnection = tlsContext.OnHandshake,
        };


        kestrel.ListenAnyIP(443, portOptions =>
        {
            portOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;

            portOptions.Use(tlsContext.OnConnection);
            portOptions.UseHttps(tls);
        });

        kestrel.ListenAnyIP(80, portOptions =>
        {
            portOptions.Protocols = HttpProtocols.Http1;
        });

    });

    builder.Services.AddMemoryCache();
    builder.Services.AddHttpForwarder();
    builder.Services.AddSingleton<JsonLogger>();
    builder.Services.AddScoped<ISmtpReceiver, ForwardSmtpReceiver>();
    builder.Services.AddSingleton<CertificateStore>();
    builder.Services.AddSingleton<TlsContext>();
    builder.Services.AddSingleton<CertificateInstaller>();
    builder.Services.AddSingleton<ReverseHostFinder>();
    builder.Services.AddSingleton<SmtpHostFinder>();
    builder.Services.AddSmtpServer();
    builder.Services.AddResponseCompression((options) =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();

        options.MimeTypes = ResponseCompressionDefaults.MimeTypes
            .Concat(["image/svg+xml"]);
    });

    // builder.Services.AddSocialMailRateLimiter();
    builder.Services.AddForwarder();

    var app = builder.Build();

    // we need to use this as soon as possible...
    app.UseCertificateInstaller();

    app.UseResponseCompression();
    app.UseRouting();
    // app.UseSocialMailRateLimiter();

    var rhf = app.Services.GetRequiredService<ReverseHostFinder>();
    await rhf.InitAsync();

    app.UseMiddleware<Forwarder>();

    var s = app.Services.GetRequiredService<SmtpServer>();
    s.DisableSpfCheck = System.Text.RegularExpressions.Regex.IsMatch(
            Environment.GetEnvironmentVariable("FORWARD_SMTP_DISABLE_SPF") ?? "",
            "(yes|true)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
         );
    s.Start();

    app.Run();

}
catch (Exception ex)
{
    Console.WriteLine(ex);

    await Task.Delay(TimeSpan.FromMinutes(1));

    throw new Exception("Closed", ex);
}
