using DnsClientX;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Spf;

public class SpfValidator
{

    public string Domain { get; set; }

    public SpfAddress[] mechanisms { get; set; }

    public SpfValidator()
    {
        
    }

    public SpfValidator(string domain)
    {
        Domain = domain;
    }

    public async Task ResolveAsync()
    {
        var list = new List<SpfAddress>();
        await ResolveDomainAsync(this.Domain, list);

    }

    private async Task ResolveDomainAsync(string domain, List<SpfAddress> list)
    {
        var host = await ClientX.QueryDns(domain, DnsRecordType.TXT, DnsEndpoint.Cloudflare);

        List<(IPAddress start, IPAddress end)> ipList = new List<(IPAddress start, IPAddress end)>();

        foreach (var a in host.Answers)
        {
            var parsed = SpfParser.Parse(a.Data);
            if (parsed != null)
            {
                var tasks = new List<Task>();
                foreach (var m in parsed)
                {
                    tasks.Add(ResolveMechanism(m, list));
                }
                if (tasks.Any())
                {
                    await Task.WhenAll(tasks);
                }
            }
        }
    }

    private async Task ResolveMechanism(SpfMechanism m, List<SpfAddress> list)
    {
        switch(m.Type)
        {
            case "ip4":
            case "ip6":

        }
    }

    public static async Task<SpfValidator> Fetch(string domainName)
    {
        var spf = new SpfValidator(domainName);
        await spf.ResolveAsync();
        return spf;
    }


}

public class SpfAddress
{
    public string IPv4 { get; set; }

    public string IPv6 { get; set; }

    public string Prefix { get; set; }
}
