using DnsClientX;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Spf;

public class SpfValidator
{

    public string Domain { get; set; }

    public SpfAddress[] IPRanges { get; set; }

    public SpfValidator()
    {
        
    }

    private List<IPNetwork> networks;

    public bool Contains(string ipAddress)
    {
        IPAddress ip = IPAddress.Parse(ipAddress);
        if(networks == null)
        {
            lock(this)
            {
                if(networks == null)
                {
                    networks = IPRanges.Select((x) => x.ToNetwork()).ToList();
                }
            }
        }
        return networks.Any((x) => x.Contains(ip));
    }

    public SpfValidator(string domain)
    {
        Domain = domain;
    }

    public async Task ResolveAsync()
    {
        var list = new List<SpfAddress>();
        await ResolveDomainAsync(this.Domain, list);
        this.IPRanges = list.ToArray();
    }

    private async Task ResolveDomainAsync(string domain, List<SpfAddress> list)
    {
        await foreach (var a in DnsResolver.ResolveAsync(domain, DnsRecordType.TXT))
        {
            var parsed = SpfParser.Parse(a);
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
                list.Add(new SpfAddress { IPv4 = m.Value, Prefix = m.Suffix });
                break;
            case "ip6":
                list.Add(new SpfAddress { IPv6 = m.Value, Prefix = m.Suffix });
                break;
            case "a":
                await foreach (var a in DnsResolver.ResolveAsync(m.Value ?? this.Domain, DnsRecordType.A))
                {
                    list.Add(SpfAddress.From(a, m.Suffix));
                }
                break;
            case "include":
                await this.ResolveDomainAsync(m.Value, list);
                break;
            case "mx":
                await foreach(var a in DnsResolver.ResolveAsync(m.Value ?? this.Domain, DnsRecordType.MX)) {
                    list.Add(SpfAddress.From(a, m.Suffix));
                }
                break;
        }
    }

    public static async Task<SpfValidator?> Fetch(string domainName)
    {
        var spf = new SpfValidator(domainName);
        await spf.ResolveAsync();
        if(spf.IPRanges.Length == 0)
        {
            return null;
        }
        return spf;
    }


}
