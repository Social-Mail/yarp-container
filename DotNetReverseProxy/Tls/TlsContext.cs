using DotNetReverseProxy.Forward;
using DotNetReverseProxy.HostLookup;
using DotNetReverseProxy.RateLimiter;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
using NeuroSpeech;
using NeuroSpeech.Acme;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Tls;

public class TlsContext
{
    private readonly CertificateStore store;
    private readonly BannedIPs bannedIPs;
    private readonly JsonLogger jsonLogger;
    private readonly ReverseHostFinder hostFinder;
    private readonly MemoryCache tlsCache;

    public TlsContext(
        CertificateStore store,
        BannedIPs bannedIPs,
        JsonLogger jsonLogger,
        ReverseHostFinder hostFinder
        )
    {
        this.store = store;
        this.bannedIPs = bannedIPs;
        this.jsonLogger = jsonLogger;
        this.hostFinder = hostFinder;
        tlsCache = new MemoryCache(new MemoryCacheOptions { });
    }

    public ConnectionDelegate OnConnection(ConnectionDelegate next)
    {
        return async Task (ConnectionContext cc) => { 
            if(cc.RemoteEndPoint is IPEndPoint ip)
            {
                if(bannedIPs.IsBanned(ip.Address))
                {
                    await Task.Delay(1000);
                    jsonLogger.Log(new {
                        aborted = ip.Address.ToString(),   
                    });
                    cc.Abort();
                    await next(cc);
                    return;
                }
            }
            await next(cc);
        };
    }
    public async ValueTask<SslServerAuthenticationOptions> OnHandshake (TlsHandshakeCallbackContext c)
    {
        var serverName = c.ClientHelloInfo.ServerName;
        var cert = await store.GetAsync(serverName, hostFinder.CanServe);
        if(cert == null)
        {
            c.Connection.Abort();
            throw new ArgumentException($"Host not found {serverName}");
        }
        var ctx = tlsCache.GetOrCreate(cert.Thumbprint, (ci) =>
        {

            ci.SlidingExpiration = TimeSpan.FromMinutes(15);
            ci.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60);

            var certContext = SslStreamCertificateContext.Create(cert, additionalCertificates: null);


            return new SslServerAuthenticationOptions
            {
                ServerCertificateContext = certContext,
                AllowTlsResume = true,
                ApplicationProtocols = new List<SslApplicationProtocol> {
                    SslApplicationProtocol.Http11,
                    SslApplicationProtocol.Http2,
                    SslApplicationProtocol.Http3
                },
                EnabledSslProtocols =
                    System.Security.Authentication.SslProtocols.Tls12
                    | System.Security.Authentication.SslProtocols.Tls13
            };
        });
        return ctx!;

    }

}
