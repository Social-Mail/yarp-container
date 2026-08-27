using DotNetReverseProxy.Forward;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Tls;

public class TlsContext
{
    private readonly CertificateStore store;
    private readonly MemoryCache tlsCache;

    public TlsContext(CertificateStore store)
    {
        this.store = store;
        tlsCache = new MemoryCache(new MemoryCacheOptions { });
    }

    public async ValueTask<SslServerAuthenticationOptions> OnConnection (TlsHandshakeCallbackContext c)
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
