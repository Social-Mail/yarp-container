using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Route53.Model;
using Amazon.Runtime;
using DotNetAcmeClient;
using DotNetAcmeClient.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;

namespace DotNetReverseProxy;

public class CertificateInstaller: IMiddleware
{
    private readonly IMemoryCache cache;
    private readonly HttpClient httpClient;
    private readonly string accountKeyPath;
    private readonly string? awsAccessKey;
    private readonly string? awsAccessKeySecret;
    private readonly string? awsZoneID;
    private readonly string? awsZoneSuffix;

    private readonly string storagePath;

    private readonly string acmeEndPoint;
    private readonly string? acmeEAB;
    private readonly string? acmeEABHmac;

    public CertificateInstaller(IMemoryCache cache)
    {
        this.cache = cache;
        this.httpClient = new HttpClient(new SimpleConsoleLoggerHandler( new HttpClientHandler() ));
        this.storagePath = System.Environment.GetEnvironmentVariable("FORWARD_CERT_STORE") ?? "/cache/certs/";
        FileEx.EnsureDirectory(this.storagePath);
        this.accountKeyPath = System.IO.Path.Join(this.storagePath, "account.key");
        this.awsAccessKey = System.Environment.GetEnvironmentVariable("AWS_ACCESS_KEY");
        this.awsAccessKeySecret = System.Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_SECRET");
        this.awsZoneID = System.Environment.GetEnvironmentVariable("AWS_ZONE_ID");
        this.awsZoneSuffix = System.Environment.GetEnvironmentVariable("AWS_ZONE_SUFFIX");
        this.acmeEndPoint = System.Environment.GetEnvironmentVariable("ACME_END_POINT") ?? "staging";
        this.acmeEAB = System.Environment.GetEnvironmentVariable("ACME_EAB");
        this.acmeEABHmac= System.Environment.GetEnvironmentVariable("ACME_EAB_HMAC");

        switch (this.acmeEndPoint?.ToLower())
        {
            case "staging":
                this.acmeEndPoint = AcmeUrls.letsEncrypt.staging;
                break;
            case "production":
                this.acmeEndPoint = AcmeUrls.letsEncrypt.production;
                break;
        }

        if (this.awsZoneSuffix != null && !this.awsZoneSuffix.StartsWith("."))
        {
            this.awsZoneSuffix = "." + this.awsZoneSuffix;
        }
    }

    internal async Task<CertificateInfo> InstallCertificateAsync(string serverName)
    {
        var client = this.acmeEAB != null && this.acmeEABHmac != null
                ? new AcmeClient(httpClient, this.acmeEndPoint, this.acmeEAB, this.acmeEABHmac)
                : new AcmeClient( httpClient, this.acmeEndPoint, this.accountKeyPath);

        using RSA domainKey = RSA.Create(2048);
        var cert = await client.CreateCertificateAsync(domainKey, serverName, this.SaveChallengesAsync);

        string privateKeyPem = domainKey.ExportRSAPrivateKeyPem();
        return new CertificateInfo
        {
            Cert = cert,
            Key = privateKeyPem
        };
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
        var c = new Amazon.Route53.AmazonRoute53Client(cred, RegionEndpoint.USEast1);

        var Name = $"{a.DomainName}{this.awsZoneSuffix}";
        var Type = "TXT";
        var ResourceRecords = a.Challenges.Select((a1) => new ResourceRecord($"\"{ a1.KeyAuthorization }\"")).ToList();

        Console.WriteLine($"Saving {Name} - {Type}: {a.Authorization}");

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

        // we will let it propogate first..
        await Task.Delay(TimeSpan.FromSeconds(15));

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

    internal async Task GetAsync(string serverName)
    {
        throw new NotImplementedException();
    }
}