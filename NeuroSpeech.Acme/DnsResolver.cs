using DnsClientX;
using System;
using System.Collections.Generic;
using System.Text;

namespace NeuroSpeech.Acme;

internal class DnsResolver
{

    internal static DnsResolver Instance = new DnsResolver();
    private DnsMultiResolver resolver;

    public DnsResolver()
    {
        var options = new MultiResolverOptions
        {
            Strategy = MultiResolverStrategy.FirstSuccess,
            MaxParallelism = 4,
            EnableResponseCache = true,
            RespectEndpointTimeout = true,
        };

        this.resolver = new DnsMultiResolver(
            DnsResolverEndpointFactory.From(DnsEndpoint.Cloudflare | DnsEndpoint.Google)
            , options);

    }

    internal Task<DnsResponse> QueryDns(string question, DnsRecordType type, CancellationToken token = default)
    {
        return this.resolver.QueryAsync(question, type, token);
    }
}
