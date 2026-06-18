using System;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetAcmeClient;
using Microsoft.Extensions.Caching.Memory;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;

namespace DotNetReverseProxy;

public class CertificateStore
{
    private readonly IMemoryCache cache;
    private readonly HttpClient httpClient;
    private readonly string? forwardPort;
    private readonly string Host;
    private readonly string accountKeyPath;
    private readonly string? UnixPort;
    private readonly int Port;
    private readonly string? ForwardDnsEndPoint;

    public CertificateStore(IMemoryCache cache, HttpClient httpClient)
    {
        this.cache = cache;
        this.httpClient = httpClient;
        this.forwardPort = System.Environment.GetEnvironmentVariable("FORWARD_PORT");
        this.Host = System.Environment.GetEnvironmentVariable("FORWARD_HOST")!;
        this.accountKeyPath = System.Environment.GetEnvironmentVariable("FORWARD_ACCOUNT_KEY_PATH") ?? "/cache/certs/account-key/";
        if(!int.TryParse(forwardPort ?? "none", out int Port))
        {
            this.UnixPort = forwardPort;
        }
        this.Port = Port;
        this.ForwardDnsEndPoint = System.Environment.GetEnvironmentVariable("FORWARD_DNS_ENDPOINT");
    }

    public async Task<PortInfo> GetPort(string hostName)
    {
        return new PortInfo
        {
            Host = Host,
            Port = Port,
            UnixPort = UnixPort
        };
    }

    internal async Task<CertificateInfo> GetCertificate(string serverName)
    {
        var apex = await this.GetApexDomain(serverName);

        var client = new AcmeClient( httpClient, AcmeUrls.letsEncrypt.staging, this.accountKeyPath);


        // we sill analyze the serverName and get the Apex Domain
        throw new NotImplementedException();
    }

    Task<(string subDomain, string domain)> GetApexDomain(string serverName)
    {
        return cache.GetOrCreateAsync<(string, string)>($"GetApexDomain:{serverName}", async (c) =>
        {
            var ruleProvider = new SimpleHttpRuleProvider();
            await ruleProvider.BuildAsync(); // Vital step to populate domain rules

            // 2. Initialize the domain parser
            var domainParser = new DomainParser(ruleProvider);

            var result = domainParser.Parse(serverName);

            c.SlidingExpiration = TimeSpan.FromMinutes(15);
            return (serverName.Substring(0, serverName.Length - result!.RegistrableDomain!.Length),result.RegistrableDomain);
        });
    }
}