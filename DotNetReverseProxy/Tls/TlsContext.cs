using DotNetReverseProxy.Forward;
using DotNetReverseProxy.RateLimiter;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly MemoryCache tlsCache;

    public TlsContext(CertificateStore store, BannedIPs bannedIPs, JsonLogger jsonLogger)
    {
        this.store = store;
        this.bannedIPs = bannedIPs;
        this.jsonLogger = jsonLogger;
        tlsCache = new MemoryCache(new MemoryCacheOptions { });
    }

    public ConnectionDelegate OnConnection(ConnectionDelegate next)
    {
        return Task (ConnectionContext cc) => { 
            if(cc.RemoteEndPoint is IPEndPoint ip)
            {
                if(bannedIPs.IsBanned(ip.Address))
                {
                    jsonLogger.Log(new {
                        aborted = ip.Address.ToString(),   
                    });
                    cc.Abort();
                    return next(cc);
                }
            }
            return next(cc);
        };
    }
    public async ValueTask<SslServerAuthenticationOptions> OnHandshake (TlsHandshakeCallbackContext c)
    {
        var cert = await store.GetAsync(c.ClientHelloInfo.ServerName);
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
                            SslApplicationProtocol.Http3 },
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            };
        });
        return ctx;

    }

}
