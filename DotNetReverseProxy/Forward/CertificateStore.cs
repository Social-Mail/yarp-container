using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Route53.Model;
using Amazon.Runtime;
using DotNetAcmeClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;

namespace DotNetReverseProxy;

public class CertificateStore: IMiddleware
{
    private readonly IMemoryCache cache;
    private readonly HttpClient httpClient;
    private readonly string? forwardPort;
    private readonly string Host;
    private readonly string accountKeyPath;
    private readonly string? awsAccessKey;
    private readonly string? UnixPort;
    private readonly int Port;
    private readonly string? ForwardDnsEndPoint;
    private readonly string? awsAccessKeySecret;
    private readonly string? awsZoneID;
    private readonly string? awsZoneSuffix;

    private readonly string storagePath;

    public CertificateStore(IMemoryCache cache)
    {
        this.cache = cache;
        this.httpClient = new HttpClient(new SimpleConsoleLoggerHandler( new HttpClientHandler() ));
        this.forwardPort = System.Environment.GetEnvironmentVariable("FORWARD_PORT");
        this.Host = System.Environment.GetEnvironmentVariable("FORWARD_HOST")!;
        this.storagePath = System.Environment.GetEnvironmentVariable("FORWARD_CERT_STORE") ?? "/cache/certs/";
        FileEx.EnsureDirectory(this.storagePath);
        this.accountKeyPath = System.IO.Path.Join(this.storagePath, "account.key");
        this.awsAccessKey = System.Environment.GetEnvironmentVariable("AWS_ACCESS_KEY");
        this.awsAccessKeySecret = System.Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_SECRET");
        this.awsZoneID = System.Environment.GetEnvironmentVariable("AWS_ZONE_ID");
        this.awsZoneSuffix = System.Environment.GetEnvironmentVariable("AWS_ZONE_SUFFIX");

        if (this.awsZoneSuffix != null && !this.awsZoneSuffix.StartsWith("."))
        {
            this.awsZoneSuffix = "." + this.awsZoneSuffix;
        }

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

        var names = new [] { serverName };
        if (serverName.StartsWith("*"))
        {
            names = new [] { apex.domain, serverName };
        }
        await client.CreateCertificateAsync(apex.domain, names, this.SaveChallengesAsync);

        // we sill analyze the serverName and get the Apex Domain
        throw new NotImplementedException();
    }

    async Task<IAsyncDisposable> SaveChallengesAsync(AcmeChallengeGroup[] challenges, CancellationToken token)
    {
        var d = new AsyncDisposeList();

        foreach(var a in challenges)
        {
            switch(a.Type)
            {
                case "http-01":

                    foreach(var c in a.Challenges)
                    {
                        var f = GetChallengePath(c.Token);
                        await System.IO.File.WriteAllTextAsync(f, c.KeyAuthorization);
                        d.Add(async () =>
                        {
                            System.IO.File.Delete(f);
                        });
                    }

                    break;
                case "dns-01":

                    await this.SaveDnsAsync(a, d);

                    break;
            }
        }

        return d;
    }

    private async Task SaveDnsAsync(AcmeChallengeGroup a, AsyncDisposeList d)
    {
        var cred = new BasicAWSCredentials(this.awsAccessKey, this.awsAccessKeySecret);
        var c = new Amazon.Route53.AmazonRoute53Client(cred);

        var Name = $"{a.DomainName}{this.awsZoneSuffix}";
        var Type = "TXT";
        var ResourceRecords = a.Challenges.Select((a) => new ResourceRecord($"\"{ a.KeyAuthorization }\"")).ToList();

        await c.ChangeResourceRecordSetsAsync(new Amazon.Route53.Model.ChangeResourceRecordSetsRequest
        {
            HostedZoneId = this.awsZoneID,
            ChangeBatch = new Amazon.Route53.Model.ChangeBatch
            {
                Comment = "Adding AMCE Challenge",
                Changes = new System.Collections.Generic.List<Amazon.Route53.Model.Change>
                {
                    new Amazon.Route53.Model.Change
                    {
                        Action = "UPSERT",                        
                        ResourceRecordSet = new Amazon.Route53.Model.ResourceRecordSet
                        {
                            Name = Name,
                            Type = Type,
                            TTL = 60,
                            ResourceRecords = ResourceRecords
                            
                        }
                    }
                }
            }
        });

        d.Add(async () =>
        {
            await c.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = this.awsZoneID,
                ChangeBatch = new ChangeBatch
                {
                    Comment = "Deleting AMCE Challenge",
                    Changes = new System.Collections.Generic.List<Change>
                    {
                        new Change
                        {
                            Action = "DELETE",
                            ResourceRecordSet = new ResourceRecordSet
                            {
                                Name = Name,
                                Type = Type,
                                TTL = 60,
                                ResourceRecords = ResourceRecords
                            }                            
                        }
                    }
                }
            });
        });
        
    }

    Task<(string subDomain, string domain)> GetApexDomain(string serverName)
    {
        return cache.GetOrCreateAsync<(string, string)>($"GetApexDomain:{serverName}", async (c) =>
        {
            var ruleProvider = new SimpleHttpRuleProvider();
            await ruleProvider.BuildAsync(); // Vital step to populate domain rules

            // 2. Initialize the domain parser
            var domainParser = new DomainParser(ruleProvider);

            var tmpName = serverName;
            if (serverName.StartsWith("*."))
            {
                tmpName = serverName.Replace("*.", "w.");
            }

            domainParser.TryParse(tmpName, out var result);

            c.SlidingExpiration = TimeSpan.FromMinutes(15);
            return (serverName.Substring(0, serverName.Length - result!.RegistrableDomain!.Length),result.RegistrableDomain);
        });
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (request.IsHttps)
        {
            return next(context);
        }
        return SendChallenge(context);
    }

    async Task SendChallenge(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        if (!request.Path.StartsWithSegments("/.well-known/acme-challenge/"))
        {
            // redirect..
            response.Headers.Location = request.GetDisplayUrl().Replace("http://", "https://");
            response.StatusCode = 301;
            return;
        }
        var tokens = request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var file = tokens[2];
        var challengePath = GetChallengePath(file);
        if (!System.IO.File.Exists(challengePath))
        {
            response.StatusCode = 404;
            return;
        }
        var content = await System.IO.File.ReadAllTextAsync(file);
        response.StatusCode = 200;
        await response.WriteAsync(content);
    }

    private string GetChallengePath(string file)
    {
        var dir = this.storagePath + "/challenges/";
        FileEx.EnsureDirectory(dir);
        return System.IO.Path.Join(dir + file);
    }
}